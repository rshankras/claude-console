# Read-only Windows UIA measurements. Keep ChatGPT open; no presses or typing.
# Works in Windows PowerShell 5.1. Times only successful calls, and kills stalled children.
param(
    [String]$Exe = "$env:LOCALAPPDATA\Logi\LogiPluginService\Plugins\VizhiDesktop\bin\vizhi-desktop-uia.exe",
    [ValidateRange(1, 1000)][Int32]$Count = 10,
    [ValidateRange(100, 120000)][Int32]$TimeoutMs = 15000,
    [String]$OutputPath = ''
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Exe)) { throw "helper not found: $Exe" }
$Exe = (Resolve-Path -LiteralPath $Exe).Path
$base = @('--require-process', '--process', 'ChatGPT', '--window', 'ChatGPT', '--window', 'Codex')
$rows = New-Object 'Collections.Generic.List[Object]'

function New-Helper([String]$arguments, [Boolean]$inputPipe = $false) {
    $psi = New-Object Diagnostics.ProcessStartInfo $Exe, $arguments
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $inputPipe; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = New-Object Text.UTF8Encoding $false
    $process = [Diagnostics.Process]::Start($psi)
    $null = $process.StandardError.ReadToEndAsync()
    return $process
}
function Stop-Helper($process) {
    if (-not $process) { return }
    try { if (-not $process.HasExited) { $process.Kill(); $null = $process.WaitForExit(3000) } }
    finally { $process.Dispose() }
}
function Check-Result([String]$text, [String]$verb) {
    $result = ConvertFrom-Json -InputObject $text
    if ($result.ok -ne $true) { throw "$verb returned a helper error; timing would not measure a successful operation" }
}
function Stats([String]$label, [Double[]]$ms) {
    $sorted = @($ms | Sort-Object)
    $middle = [Int32][Math]::Floor($sorted.Count / 2)
    $median = $sorted[$middle]
    if ($sorted.Count % 2 -eq 0) { $median = ($sorted[$middle - 1] + $median) / 2 }
    $row = [PSCustomObject]@{ operation = $label; count = $sorted.Count; medianMs = [Math]::Round($median, 1);
        p90Ms = [Math]::Round($sorted[[Math]::Ceiling($sorted.Count * 0.9) - 1], 1);
        minMs = [Math]::Round($sorted[0], 1); maxMs = [Math]::Round($sorted[-1], 1) }
    $rows.Add($row)
    "{0,-22} n={1,-3} median {2,7:N1} ms   p90 {3,7:N1}   max {4,7:N1}" -f $label, $row.count, $row.medianMs, $row.p90Ms, $row.maxMs
}
function OneShot([String[]]$argv) {
    $process = $null
    try {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        # Every argument here is fixed and has no spaces; the executable path uses FileName.
        $process = New-Helper ($argv -join ' ')
        $output = $process.StandardOutput.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMs) -or -not $output.Wait($TimeoutMs)) { throw 'one-shot timed out' }
        $ms = $watch.Elapsed.TotalMilliseconds
        if ($process.ExitCode -ne 0) { throw "one-shot $($argv[0]) failed" }
        Check-Result $output.Result $argv[0]
        return $ms
    } finally { Stop-Helper $process }
}
function Served([String[]]$argv) {
    $line = ConvertTo-Json -Compress -InputObject @($argv)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $write = $server.StandardInput.WriteLineAsync($line)
    if (-not $write.Wait($TimeoutMs)) { throw 'request write timed out' }
    $flush = $server.StandardInput.FlushAsync()
    if (-not $flush.Wait($TimeoutMs)) { throw 'request flush timed out' }
    $read = $server.StandardOutput.ReadLineAsync()
    if (-not $read.Wait($TimeoutMs)) { throw 'response timed out' }
    $ms = $watch.Elapsed.TotalMilliseconds
    if (-not $read.Result) { throw 'helper exited without a response' }
    $reply = ConvertFrom-Json -InputObject $read.Result
    if ($reply.exit -ne 0 -or $null -eq $reply.out) { throw "served $($argv[0]) failed" }
    Check-Result $reply.out $argv[0]
    return $ms
}

"Helper: $Exe"
Stats 'one-shot frontmost' (1..$Count | ForEach-Object { OneShot (@('frontmost') + $base) })
Stats 'one-shot status' (1..$Count | ForEach-Object { OneShot (@('status') + $base) })
$server = $null
try {
    $start = [Diagnostics.Stopwatch]::StartNew()
    $server = New-Helper 'serve' $true
    $ready = $server.StandardOutput.ReadLineAsync()
    if (-not $ready.Wait($TimeoutMs) -or $ready.Result -ne '{"ready":1}') { throw 'helper did not announce serve readiness' }
    $cold = Served (@('frontmost') + $base)
    $startup = $start.Elapsed.TotalMilliseconds
    "First call including start: {0:N1} ms" -f $startup
    Stats 'served frontmost' (1..$Count | ForEach-Object { Served (@('frontmost') + $base) })
    Stats 'served status' (1..$Count | ForEach-Object { Served (@('status') + $base) })
    $server.StandardInput.Close()
    $exited = $server.WaitForExit(3000)
    "Server exited after stdin closed: $exited"
    if (-not $exited) { throw 'helper remained alive after stdin closed' }
    if ($OutputPath) {
        [PSCustomObject]@{ measuredAt = [DateTime]::UtcNow.ToString('o'); helper = $Exe;
            helperSha256 = (Get-FileHash -LiteralPath $Exe -Algorithm SHA256).Hash;
            startupMs = [Math]::Round($startup, 1); exitedOnEof = $exited; results = @($rows.ToArray()) } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    }
} finally { Stop-Helper $server }
