# Measures the Vizhi Desktop UI Automation helper on this PC, one-shot vs served (#155).
#
# One-shot is how 1.1.0 called the helper: a new process per call. Served is 1.1.1: one process
# started once, each call sent as a line. Run with the ChatGPT desktop app open and in front.
#
#   powershell -ExecutionPolicy Bypass -File measure-uia-helper.ps1 [-Exe <path>] [-Count 10]
#
# Reads only: `frontmost` and `status`. Nothing is pressed or typed.

param(
    [String]$Exe = "$env:LOCALAPPDATA\Logi\LogiPluginService\Plugins\VizhiDesktop\bin\vizhi-desktop-uia.exe",
    [Int32]$Count = 10
)

if (-not (Test-Path $Exe)) { Write-Error "helper not found: $Exe"; exit 1 }
$base = @('--require-process', '--process', 'ChatGPT', '--window', 'ChatGPT', '--window', 'Codex')

function Stats([String]$label, [Double[]]$ms) {
    $sorted = $ms | Sort-Object
    $median = $sorted[[Math]::Floor($sorted.Count / 2)]
    "{0,-22} n={1,-3} median {2,6:N0} ms   min {3,6:N0}   max {4,6:N0}" -f $label, $sorted.Count, $median, $sorted[0], $sorted[-1]
}

function OneShot([String[]]$argv) {
    $psi = New-Object Diagnostics.ProcessStartInfo $Exe
    foreach ($a in $argv) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.CreateNoWindow = $true
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($psi)
    $null = $p.StandardOutput.ReadToEnd(); $p.WaitForExit()
    $sw.Elapsed.TotalMilliseconds
}

"Helper: $Exe"
"One-shot (1.1.0 behaviour):"
Stats '  frontmost' (1..$Count | ForEach-Object { OneShot (@('frontmost') + $base) })
Stats '  status' (1..$Count | ForEach-Object { OneShot (@('status') + $base) })

"Served (1.1.1 behaviour):"
$psi = New-Object Diagnostics.ProcessStartInfo $Exe
$psi.ArgumentList.Add('serve')
$psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.CreateNoWindow = $true
$psi.StandardOutputEncoding = New-Object Text.UTF8Encoding $false
$start = [Diagnostics.Stopwatch]::StartNew()
$server = [Diagnostics.Process]::Start($psi)

function Served([String[]]$argv) {
    $line = ConvertTo-Json -Compress -InputObject @($argv)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $server.StandardInput.WriteLine($line); $server.StandardInput.Flush()
    $reply = $server.StandardOutput.ReadLine()
    if (-not $reply -or $reply -notmatch '"exit"') { throw "the helper does not serve (got: $reply)" }
    $sw.Elapsed.TotalMilliseconds
}

$first = Served (@('frontmost') + $base)
"  first call incl. start  {0,6:N0} ms" -f $start.Elapsed.TotalMilliseconds
Stats '  frontmost' (1..$Count | ForEach-Object { Served (@('frontmost') + $base) })
Stats '  status' (1..$Count | ForEach-Object { Served (@('status') + $base) })
$server.StandardInput.Close(); $null = $server.WaitForExit(3000)
"Server exited after stdin closed: $($server.HasExited)"
