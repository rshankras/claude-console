# Native smoke for the packaged Windows UIA helper. Only ping/frontmost and a refused status
# request are sent; this never presses controls, changes focus, or reads conversation text.
param([Parameter(Mandatory = $true)][String]$Exe)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path -LiteralPath $Exe).Path
$children = New-Object 'Collections.Generic.List[Object]'
function Send($process, [String]$line) {
    $write = $process.StandardInput.WriteLineAsync($line)
    if (-not $write.Wait(5000)) { throw 'Write timed out' }
    $flush = $process.StandardInput.FlushAsync()
    if (-not $flush.Wait(5000)) { throw 'Flush timed out' }
    $read = $process.StandardOutput.ReadLineAsync()
    if (-not $read.Wait(5000) -or -not $read.Result) { throw 'Read timed out or helper exited' }
    return (ConvertFrom-Json -InputObject $read.Result)
}
try {
    foreach ($lane in @('poll', 'keys', 'foreground')) {
        $verb = if ($lane -eq 'foreground') { 'serve-win32' } else { 'serve' }
        $info = New-Object Diagnostics.ProcessStartInfo $Exe, $verb
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($info)
        $children.Add([PSCustomObject]@{ lane = $lane; process = $process })
        $null = $process.StandardError.ReadToEndAsync()
        $ready = $process.StandardOutput.ReadLineAsync()
        if (-not $ready.Wait(10000) -or $ready.Result -ne '{"ready":1}') { throw "$lane did not announce readiness" }
    }
    if (@($children | ForEach-Object { $_.process.Id } | Select-Object -Unique).Count -ne 3) { throw 'Lanes are not isolated' }
    foreach ($child in $children) {
        $process = $child.process
        if ((Send $process '["ping"]').exit -ne 0) { throw "$($child.lane) ping failed" }
        if ($child.lane -eq 'foreground') {
            $refusal = Send $process '["status"]'
            if ($refusal.exit -eq 0 -or (ConvertFrom-Json -InputObject $refusal.out).error -ne 'bad-request') {
                throw 'Win32-only lane accepted a UIA verb'
            }
            if ((Send $process '["frontmost","--process","ChatGPT"]').exit -ne 0) { throw 'Win32 foreground read failed' }
        }
        $hasUia = @($process.Modules | Where-Object { $_.ModuleName -eq 'UIAutomationCore.dll' }).Count -gt 0
        if ($hasUia -ne ($child.lane -ne 'foreground')) { throw "$($child.lane) UIA isolation failed" }
        "PASS: $($child.lane) serves requests in its own process; UIA loaded = $hasUia"
    }
    foreach ($child in $children) {
        $child.process.StandardInput.Close()
        if (-not $child.process.WaitForExit(3000)) { throw "$($child.lane) remained alive after EOF" }
    }
    'PASS: all three servers exited after stdin closed'
} finally {
    foreach ($child in $children) {
        try { if (-not $child.process.HasExited) { $child.process.Kill(); $null = $child.process.WaitForExit(3000) } }
        finally { $child.process.Dispose() }
    }
}
