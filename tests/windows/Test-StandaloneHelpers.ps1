param([string]$PublishRoot = (Join-Path $PSScriptRoot '..\..\bin\qa-fixes\win-x64'))
$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('cc-helper-smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $toolkit = Join-Path $PublishRoot 'ClaudeConsoleTools\claude-console-tools.exe'
    $shared = Test-Path -LiteralPath $toolkit
    $verbs = if ($shared) { @('manifest', 'inject', 'focus', 'voice', 'shot') } else { @('focus', 'shot') }
    foreach ($helper in $verbs) {
        $project = if ($helper -eq 'focus') { 'ClaudeConsoleFocus' } else { 'ClaudeConsoleShot' }
        $source = if ($shared) { $toolkit } else { Join-Path $PublishRoot "$project\claude-console-$helper.exe" }
        $isolated = Join-Path $scratch $helper
        New-Item -ItemType Directory -Path $isolated | Out-Null
        $exeName = if ($shared) { "claude-console-tools.exe" } else { "claude-console-$helper.exe" }
        $exe = Join-Path $isolated $exeName
        Copy-Item -LiteralPath $source -Destination $exe
        $trace = Join-Path $isolated 'host-trace.txt'
        $extract = Join-Path $isolated 'bundle'
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = $exe
        if ($shared) { $info.Arguments = $helper }
        $info.WorkingDirectory = $isolated
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardError = $true
        $info.RedirectStandardOutput = $true
        # Point every runtime search override at an empty directory. The host trace below proves
        # the runtime came from this executable, regardless of what's installed on the machine.
        $emptyRuntime = Join-Path $isolated 'no-runtime'
        New-Item -ItemType Directory -Path $emptyRuntime | Out-Null
        foreach ($key in @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_ARM64', 'DOTNET_ROOT(x86)')) {
            $info.EnvironmentVariables[$key] = $emptyRuntime
        }
        $info.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
        $info.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $extract
        $info.EnvironmentVariables['COREHOST_TRACE'] = '1'
        $info.EnvironmentVariables['COREHOST_TRACEFILE'] = $trace
        $process = [Diagnostics.Process]::Start($info)
        try {
            $stderr = $process.StandardError.ReadToEndAsync()
            $stdout = $process.StandardOutput.ReadToEndAsync()
            if (-not $process.WaitForExit(30000)) { $process.Kill(); throw "$helper startup timed out" }
            $expected = switch ($helper) { 'manifest' { 0 } 'inject' { 3 } 'voice' { 1 } default { 2 } }
            $output = $stderr.Result + $stdout.Result
            $marker = if ($helper -eq 'manifest') { 'claude-console-tools/v1 inject focus voice shot' } else { "claude-console-$helper" }
            if ($process.ExitCode -ne $expected -or $output -notmatch [regex]::Escape($marker)) {
                throw "$helper dispatch/usage failed: exit $($process.ExitCode), $output"
            }
            $hostTrace = [IO.File]::ReadAllText($trace)
            # The host says which posture it ran under. (No assertion on the extract directory:
            # a helper with no native libraries of its own never extracts anything, and that is
            # the normal shape of these helpers now.)
            if ($hostTrace -notmatch 'Executing as a self-contained app' -or
                $hostTrace -notmatch 'framework dependent=0') {
                throw "$helper tried to resolve a shared runtime"
            }
            Write-Output "PASS: $helper starts from its executable alone and uses the bundled runtime"
        } finally { $process.Dispose() }
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected scratch path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
