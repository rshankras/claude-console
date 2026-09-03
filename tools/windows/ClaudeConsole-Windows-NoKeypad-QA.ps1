[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [string]$ExpectedSha256 = "464b1608bc2ec2a5bbfa1ed8e6f8a95a614d57502966780d2ddd558fa300d854",

    [switch]$TestMicrophone,
    [switch]$TestClaudeDiscovery
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Write-Pass([string]$Message) {
    Write-Host "PASS  $Message" -ForegroundColor Green
}

function Write-Info([string]$Message) {
    Write-Host "INFO  $Message" -ForegroundColor Cyan
}

function Start-HookWithOpenInput(
    [string]$HookPath,
    [string]$Arguments,
    [string]$IsolatedTemp
) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $HookPath
    $start.Arguments = $Arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables["TEMP"] = $IsolatedTemp
    $start.EnvironmentVariables["TMP"] = $IsolatedTemp

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    if (-not $process.Start()) {
        throw "Could not start $HookPath $Arguments"
    }

    return $process
}

function Wait-Hook(
    [System.Diagnostics.Process]$Process,
    [int]$TimeoutMs,
    [string]$Label
) {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    if (-not $Process.WaitForExit($TimeoutMs)) {
        try { $Process.Kill() } catch { }
        throw "$Label did not exit within $TimeoutMs ms"
    }
    $Process.WaitForExit()
    $timer.Stop()

    $stdout = $Process.StandardOutput.ReadToEnd()
    $stderr = $Process.StandardError.ReadToEnd()
    $exitCode = $Process.ExitCode
    try { $Process.StandardInput.Close() } catch { }
    $Process.Dispose()

    if ($exitCode -ne 0) {
        throw "$Label exited $exitCode. stderr: $stderr"
    }

    Write-Pass "$Label exited cleanly in $($timer.ElapsedMilliseconds) ms with its input pipe deliberately left open"
    return $stdout
}

if ($env:OS -ne "Windows_NT") {
    throw "Run this script on Windows."
}

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$actualSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedPackage).Hash.ToLowerInvariant()
if ($actualSha -ne $ExpectedSha256.ToLowerInvariant()) {
    throw "Wrong package. Expected SHA-256 $ExpectedSha256 but found $actualSha"
}
Write-Pass "package SHA-256 matches the final 91921cb build"

$qaRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ClaudeConsole-NoKeypad-QA-{0}-{1}" -f $PID, [DateTime]::UtcNow.Ticks)
$unpackRoot = Join-Path $qaRoot "package"
$isolatedTemp = Join-Path $qaRoot "isolated-temp"
$zipPath = Join-Path $qaRoot "ClaudeConsole.zip"

New-Item -ItemType Directory -Path $unpackRoot -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedTemp -Force | Out-Null

try {
    Copy-Item -LiteralPath $resolvedPackage -Destination $zipPath
    Expand-Archive -LiteralPath $zipPath -DestinationPath $unpackRoot -Force

    $hook = Join-Path $unpackRoot "bin\claude-console-hook.exe"
    $voice = Join-Path $unpackRoot "bin\claude-console-voice.exe"
    $inject = Join-Path $unpackRoot "bin\claude-console-inject.exe"
    foreach ($required in @($hook, $voice, $inject)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Package is missing $required"
        }
    }
    Write-Pass "Windows hook, voice, and injection helpers are present"

    $installedHook = Join-Path $env:LOCALAPPDATA "Logi\LogiPluginService\Plugins\ClaudeConsole\bin\claude-console-hook.exe"
    if (Test-Path -LiteralPath $installedHook -PathType Leaf) {
        $packedHookSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $hook).Hash
        $installedHookSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $installedHook).Hash
        if ($packedHookSha -ne $installedHookSha) {
            throw "The installed hook does not match this package. Reinstall the .lplug4 before testing."
        }
        Write-Pass "installed #57 helper is byte-for-byte identical to the package"
    }
    else {
        Write-Host "WARN  Installed helper was not found. The package stress test will still run, but the Options+ install is not verified." -ForegroundColor Yellow
    }

    $process = Start-HookWithOpenInput $hook "statusline" $isolatedTemp
    [void](Wait-Hook $process 5000 "statusline bounded-input check")

    $process = Start-HookWithOpenInput $hook "activity permission" $isolatedTemp
    [void](Wait-Hook $process 5000 "PermissionRequest bounded-input check")
    $activityState = Join-Path $isolatedTemp "claude-console\activity\shared.json"
    if (-not (Test-Path -LiteralPath $activityState -PathType Leaf)) {
        throw "The activity helper exited but did not write isolated shared state."
    }
    Write-Pass "PermissionRequest helper wrote state in the isolated temp root"

    $process = Start-HookWithOpenInput $hook "codex SessionStart" $isolatedTemp
    $codexOutput = Wait-Hook $process 5000 "Codex hook bounded-input check"
    if ($codexOutput.Trim() -ne "{}") {
        throw "Codex hook output was '$codexOutput', expected '{}'."
    }
    $codexState = Join-Path $isolatedTemp "codex-console\sessions\shared.json"
    if (-not (Test-Path -LiteralPath $codexState -PathType Leaf)) {
        throw "The Codex helper exited but did not write isolated shared state."
    }
    Write-Pass "Codex hook wrote state and returned its non-breaking {} contract"

    Write-Info "starting 24 concurrent hook processes with every input pipe held open"
    $stress = New-Object System.Collections.ArrayList
    for ($i = 0; $i -lt 24; $i++) {
        [void]$stress.Add((Start-HookWithOpenInput $hook "statusline" $isolatedTemp))
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    foreach ($process in $stress) {
        $remaining = [int][Math]::Max(1, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        if (-not $process.WaitForExit($remaining)) {
            foreach ($owned in $stress) {
                try { if (-not $owned.HasExited) { $owned.Kill() } } catch { }
            }
            throw "At least one of the 24 concurrent hooks survived past the 5-second test limit."
        }
    }

    $badExit = @($stress | Where-Object { $_.ExitCode -ne 0 })
    foreach ($process in $stress) {
        try { $process.StandardInput.Close() } catch { }
        $process.Dispose()
    }
    if ($badExit.Count -ne 0) {
        throw "$($badExit.Count) concurrent hooks exited nonzero."
    }
    Write-Pass "all 24 concurrent hooks exited within 5 seconds; no test-owned process piled up"

    if ($TestMicrophone) {
        Write-Info "running the one-second Windows microphone self-test"
        & $voice selftest
        if ($LASTEXITCODE -ne 0) {
            throw "Windows voice self-test failed with exit code $LASTEXITCODE."
        }
        Write-Pass "Windows microphone helper self-test completed"
    }

    if ($TestClaudeDiscovery) {
        Write-Info "running Claude session discovery; keep a native Claude Code session open"
        & $inject selftest
        if ($LASTEXITCODE -ne 0) {
            throw "Claude session discovery found no usable session (exit $LASTEXITCODE)."
        }
        Write-Pass "Windows injection helper discovered at least one candidate session"
    }

    Write-Host ""
    Write-Host "RESULT: PASS — #57 no-keypad Windows helper checks" -ForegroundColor Green
    Write-Host "The keypad-only #58 answer faces and #61 navigation notice still require Logitech hardware."
}
finally {
    if (Test-Path -LiteralPath $qaRoot) {
        Remove-Item -LiteralPath $qaRoot -Recurse -Force
    }
}
