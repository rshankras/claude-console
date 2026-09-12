# Run the Claude Console test suite on Windows: the C# suite (which includes the hook exe contract
# tests) plus the standalone-helper smoke. The bash and Python suites under tests/scripts are
# macOS-only; they are named below so their absence is visible, never silent.
#
#   powershell -ExecutionPolicy Bypass -File tests\run-all.ps1
#   powershell -ExecutionPolicy Bypass -File tests\run-all.ps1 -Dotnet C:\dotnet10\dotnet.exe -Strict
#
# Safe to run any time: the test project compiles the engine from source and never writes the dev
# .link, so the installed plugin is untouched. Like run-all.sh it leaves a canary in the live IPC
# root and fingerprints the live settings.json, to prove no test strayed out of its temp home.
#
# -Strict fails the run when the helper smoke has nothing to test (for a release or CI machine,
# where "not published yet" is itself a failure).
param(
    [string]$Dotnet = '',
    [string]$HelpersRoot = '',
    [switch]$Strict
)

$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot
$status = 0

if (-not $Dotnet) {
    if ($env:DOTNET_HOST_PATH -and (Test-Path $env:DOTNET_HOST_PATH)) { $Dotnet = $env:DOTNET_HOST_PATH } else { $Dotnet = 'dotnet' }
}

# The grid code DELETES state files for sessions it judges dead. Tests must drive a throwaway root,
# never the live one. Leave a canary in the live root and check it survives.
$liveRoot = Join-Path ([IO.Path]::GetTempPath()) 'claude-console'
$canary = Join-Path $liveRoot '.suite-canary'
$canaryPlaced = $false
if (Test-Path $liveRoot) {
    try { [IO.File]::WriteAllText($canary, ''); $canaryPlaced = $true } catch { }
}

# Same idea for the user's Claude Code settings: a test that touches the REAL settings.json is a
# test that forgot TempHome. Fingerprint before, compare after.
$settings = Join-Path $env:USERPROFILE '.claude\settings.json'
$optOut = Join-Path $env:USERPROFILE '.claude\claude-console\no-autowire'
function Get-Fingerprint([string]$path) {
    if (Test-Path $path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash } else { 'absent' }
}
$settingsBefore = Get-Fingerprint $settings
$optOutBefore = Test-Path $optOut

Write-Host '> C# unit tests (the first run publishes the hook exe for its contract tests)'
& $Dotnet test (Join-Path $repo 'tests\ClaudeConsolePlugin.Tests.csproj') --nologo
if ($LASTEXITCODE -ne 0) { $status = 1 }

Write-Host ''
Write-Host '> Windows helper smoke (each helper must start from its executable alone)'
$smoke = Join-Path $repo 'tests\windows\Test-StandaloneHelpers.ps1'
$staged = Join-Path ([IO.Path]::GetTempPath()) ('cc-helpers-' + [Guid]::NewGuid().ToString('N'))
$missing = @()
$proj = 'ClaudeConsoleTools'; $exe = 'claude-console-tools.exe'
if ($HelpersRoot) { $src = Join-Path $HelpersRoot "$proj\$exe" } else { $src = Join-Path $repo "tools\windows\$proj\publish-win-x64\$exe" }
if (Test-Path $src) {
    New-Item -ItemType Directory -Force (Join-Path $staged $proj) | Out-Null
    Copy-Item -LiteralPath $src -Destination (Join-Path $staged "$proj\$exe")
} else { $missing += $src }
if ($missing.Count -eq 0) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $smoke -PublishRoot $staged
    if ($LASTEXITCODE -ne 0) { $status = 1 }
    Remove-Item -Recurse -Force -LiteralPath $staged -ErrorAction SilentlyContinue
} else {
    Write-Host '  SKIP no published helpers to smoke-test. Missing:'
    foreach ($m in $missing) { Write-Host "       $m" }
    Write-Host '       Publish them (what tools/windows/build-windows-payload.sh does), then rerun:'
    Write-Host '       dotnet publish tools\windows\ClaudeConsoleTools -c Release -r win-x64 -o tools\windows\ClaudeConsoleTools\publish-win-x64'
    Write-Host '       or pass -HelpersRoot <dir> laid out as <Project>\<exe>.'
    if ($Strict) { Write-Host '  FAIL -Strict: the helper smoke must run on this machine'; $status = 1 }
}

Write-Host ''
Write-Host '> bridge script tests, codex hook tests, concurrent uninstall cleanup'
Write-Host '  SKIP the bash and Python suites under tests\scripts run on macOS only.'
Write-Host '       The Windows half of the bridge-script contract is WindowsHookContractTests, run above.'

Write-Host ''
Write-Host '> live settings.json not touched'
$settingsAfter = Get-Fingerprint $settings
$optOutAfter = Test-Path $optOut
$leftovers = @(Get-ChildItem -Path (Join-Path $env:USERPROFILE '.claude') -Filter 'settings.json.cc.*.tmp' -ErrorAction SilentlyContinue)
if ($settingsBefore -ne $settingsAfter) {
    Write-Host "  FAIL a test rewrote the LIVE $settings - tests must use TempHome (BridgeManager.HomeOverride)."
    $status = 1
} elseif ($optOutBefore -ne $optOutAfter) {
    Write-Host "  FAIL a test changed the LIVE opt-out marker ($optOut) - tests must use TempHome."
    $status = 1
} elseif ($leftovers.Count -gt 0) {
    Write-Host '  FAIL a settings.json temp file was left behind under ~\.claude'
    $status = 1
} else {
    Write-Host "  ok   $settings and the opt-out marker are as they were"
}

Write-Host ''
Write-Host '> live IPC root not wiped'
if (-not $canaryPlaced) {
    Write-Host '  skip no live IPC root on this machine - nothing a test could destroy'
} elseif (Test-Path $canary) {
    Remove-Item -LiteralPath $canary -ErrorAction SilentlyContinue
    Write-Host "  ok   $liveRoot survived the test run"
} else {
    Write-Host "  FAIL a suite deleted the LIVE IPC root ($liveRoot) - that wipes running sessions' state."
    Write-Host '       Tests must use an injected temp root - see SessionRegistryTests.'
    $status = 1
}

Write-Host ''
if ($status -eq 0) { Write-Host 'all suites passed' } else { Write-Host 'some suites failed' }
exit $status
