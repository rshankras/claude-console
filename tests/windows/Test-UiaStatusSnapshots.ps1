# Read-only parity and timing check for full-reference versus cached-only status snapshots.
# No UI text is printed or saved. Run against a stable ChatGPT/Codex window.
param([Parameter(Mandatory=$true)][String]$Exe,[String]$OutputPath,[ValidateRange(1,20)][Int32]$Count=5)
$ErrorActionPreference='Stop'
$info=New-Object Diagnostics.ProcessStartInfo (Resolve-Path -LiteralPath $Exe).Path,'serve'
$info.UseShellExecute=$false; $info.CreateNoWindow=$true
$info.RedirectStandardInput=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
$server=[Diagnostics.Process]::Start($info)
function Request([String[]]$argv) {
 $watch=[Diagnostics.Stopwatch]::StartNew()
 $write=$server.StandardInput.WriteLineAsync((ConvertTo-Json -InputObject @($argv) -Compress))
 if(-not $write.Wait(10000)){throw 'Write timed out'}
 $flush=$server.StandardInput.FlushAsync(); if(-not $flush.Wait(10000)){throw 'Flush timed out'}
 $read=$server.StandardOutput.ReadLineAsync(); if(-not $read.Wait(10000)){throw 'Read timed out'}
 $envelope=$read.Result | ConvertFrom-Json; $reply=$envelope.out | ConvertFrom-Json
 if($envelope.exit -ne 0 -or $reply.ok -ne $true -or $reply.surface -ne $true){throw 'Status did not expose a complete surface'}
 $timing=$reply.timing; $reply.PSObject.Properties.Remove('timing')
 [PSCustomObject]@{wallMs=[Math]::Round($watch.Elapsed.TotalMilliseconds,1); scanMs=$timing.scanMs; state=($reply | ConvertTo-Json -Compress -Depth 8)}
}
try {
 $null=$server.StandardError.ReadToEndAsync()
 $ready=$server.StandardOutput.ReadLineAsync(); if(-not $ready.Wait(10000) -or $ready.Result -ne '{"ready":1}'){throw 'No readiness handshake'}
 $argv=@('status','--require-process','--process','ChatGPT','--timing','--approve','Allow once','--deny','Deny','--stop','Stop',
  '--attention','needs attention','--mode-prefix','Switch mode, current mode: ','--conv-marker','Pin chat',
  '--state-awaiting','Awaiting approval','--state-unread','Unread','--state-unread','Complete',
  '--state-running','Thinking','--state-running','Working','--idle-images','ChatGPT=1','--idle-images','Codex=2',
  '--search','Search','--changes','Changes','--changes','This branch','--changes','View changes','--changes-turn','View changes',
  '--panel-visible','Show files','--panel-visible','Hide files','--panel-mode','Codex',
  '--projects','Projects','--plugins','Plugins','--attach-files','Add files and more','--permissions','Change permissions',
  '--scheduled','Scheduled','--pull-requests','Pull requests','--explore','Explore','--quick-chat','Quick chat',
  '--voice-start','Start voice chat','--voice-start','Start new voice chat','--voice-end','Stop voice chat',
  '--copy-response','Copy response','--copy-button','Copy','--copy-completed','Copied',
  '--response-action','Fork chat from here','--response-action','Branch in new chat','--response-action','Continue in new chat',
  '--response-action','More actions','--response-action','Rate response','--response-action','Remove good response feedback',
  '--response-action','Remove bad response feedback','--assistant-heading','ChatGPT said:','--user-heading','You said:','--send-label','Send')
 $samples=New-Object 'Collections.Generic.List[Object]'
 for($i=0;$i -lt $Count;$i++) {
  $live=Request ($argv+@('--live-snapshot'))
  $cached=Request $argv
  if($live.state -cne $cached.state){throw 'Status values differ; check whether the live app changed during this pair'}
  $samples.Add([PSCustomObject]@{pair=$i+1; liveMs=$live.wallMs; cachedMs=$cached.wallMs; liveScanMs=$live.scanMs; cachedScanMs=$cached.scanMs})
 }
 $result=[PSCustomObject]@{measuredAt=[DateTime]::Now.ToString('o'); equivalentPairs=$Count; samples=@($samples.ToArray());
  liveAverageMs=[Math]::Round(($samples.liveMs | Measure-Object -Average).Average,1);
  cachedAverageMs=[Math]::Round(($samples.cachedMs | Measure-Object -Average).Average,1)}
 if($OutputPath){$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputPath -Encoding UTF8}
 $result | ConvertTo-Json -Depth 4
 $server.StandardInput.Close(); if(-not $server.WaitForExit(3000)){throw 'Server did not exit on EOF'}
} finally {if(-not $server.HasExited){$server.Kill()}; $server.Dispose()}
