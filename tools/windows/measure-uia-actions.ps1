# Compare full-tree conversation resolution with the narrow query on the same live window.
# Read-only unless -Invoke is explicitly set. No prompt is typed and no message is sent.
# Results contain only timings and query outcomes, never the title or UI text.
param(
    [Parameter(Mandatory = $true)][String]$Exe,
    [Parameter(Mandatory = $true)][String]$TitleFile,
    [ValidateRange(1, 100)][Int32]$Count = 10,
    [switch]$Invoke,
    [String]$OutputPath = ''
)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path -LiteralPath $Exe).Path
$title = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $TitleFile).Path)
if ([String]::IsNullOrWhiteSpace($title)) { throw 'TitleFile must contain an exact visible chat title' }
$base = @('press', '--require-process', '--process', 'ChatGPT', '--label', $title, '--conversation', 'Pin chat', '--timing')
$samples = New-Object 'Collections.Generic.List[Object]'
$server = $null

function Request([String[]]$argv) {
    # Framework ProcessStartInfo (PowerShell 5.1) has no StandardInputEncoding. ASCII JSON
    # escapes preserve every Unicode title independently of the console's input code page.
    $line = [regex]::Replace((ConvertTo-Json -Compress -InputObject @($argv)), '[^\x00-\x7f]', {
        param($match) '\u{0:x4}' -f [Int32][Char]$match.Value[0]
    })
    $write = $server.StandardInput.WriteLineAsync($line)
    if (-not $write.Wait(15000)) { throw 'Request write timed out' }
    $flush = $server.StandardInput.FlushAsync()
    if (-not $flush.Wait(15000)) { throw 'Request flush timed out' }
    $read = $server.StandardOutput.ReadLineAsync()
    if (-not $read.Wait(15000) -or -not $read.Result) { throw 'Helper timed out or exited' }
    $envelope = ConvertFrom-Json -InputObject $read.Result
    $reply = ConvertFrom-Json -InputObject $envelope.out
    if ($envelope.exit -ne 0 -or $reply.ok -ne $true) { throw "Request refused: $($reply.error). No action will be repeated automatically." }
    return $reply
}
try {
    $info = New-Object Diagnostics.ProcessStartInfo $Exe, 'serve'
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = New-Object Text.UTF8Encoding $false
    $startup = [Diagnostics.Stopwatch]::StartNew()
    $server = [Diagnostics.Process]::Start($info)
    $null = $server.StandardError.ReadToEndAsync()
    $ready = $server.StandardOutput.ReadLineAsync()
    if (-not $ready.Wait(15000) -or $ready.Result -ne '{"ready":1}') { throw 'No serve handshake' }
    $startupMs = $startup.Elapsed.TotalMilliseconds
    # Warm both resolution paths without pressing anything.
    $null = Request ($base + @('--dry', '--full-scan'))
    $null = Request ($base + @('--dry', '--focus-after'))
    for ($i = 0; $i -lt $Count; $i++) {
        foreach ($mode in @('full', 'query')) {
            $argv = $base + $(if ($mode -eq 'full') { @('--full-scan') } else { @('--focus-after') })
            if (-not $Invoke) { $argv += '--dry' }
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $reply = Request $argv
            $focusMs = $reply.timing.focusMs
            if ($Invoke -and $mode -eq 'full') {
                $focus = Request @('focus', '--require-process', '--process', 'ChatGPT', '--timing')
                $focusMs += $focus.timing.totalMs
            }
            if ($Invoke -and $mode -eq 'query' -and $reply.focused -ne $true) { throw 'Chat opened, but Windows refused focus; stopping measurement' }
            $samples.Add([PSCustomObject]@{ mode = $mode; wallMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 1);
                scanMs = $reply.timing.scanMs; invokeMs = $reply.timing.invokeMs; focusMs = $focusMs;
                path = $reply.timing.scanPath; query = $reply.timing.queryDetail })
        }
    }
    $summary = foreach ($mode in @('full', 'query')) {
        $group = @($samples | Where-Object { $_.mode -eq $mode })
        $ordered = @($group.wallMs | Sort-Object)
        $middle = [Int32][Math]::Floor($ordered.Count / 2)
        $median = $ordered[$middle]
        if ($ordered.Count % 2 -eq 0) { $median = ($ordered[$middle - 1] + $median) / 2 }
        [PSCustomObject]@{ mode = $mode; count = $group.Count; medianMs = [Math]::Round($median, 1);
            p90Ms = $ordered[[Math]::Ceiling($ordered.Count * 0.9) - 1]; maxMs = $ordered[-1];
            fallbacks = @($group | Where-Object { $_.path -eq 'full-fallback' }).Count }
    }
    $summary | Format-Table -AutoSize
    $server.StandardInput.Close()
    if (-not $server.WaitForExit(3000)) { throw 'Helper failed to exit after stdin closed' }
    if ($OutputPath) {
        [PSCustomObject]@{ measuredAt = [DateTime]::UtcNow.ToString('o'); invoked = [Boolean]$Invoke;
            helperSha256 = (Get-FileHash -LiteralPath $Exe -Algorithm SHA256).Hash;
            startupMs = [Math]::Round($startupMs, 1); results = @($summary); samples = @($samples.ToArray()); exitedOnEof = $true } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    }
} finally {
    if ($server) {
        if (-not $server.HasExited) { $server.Kill(); $null = $server.WaitForExit(3000) }
        $server.Dispose()
    }
}
