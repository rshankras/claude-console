# Compare the panel control view with the raw view. Focuses the app, but never invokes an
# opener or edits app content. All panel requests use --dry. No UI text is saved.
param([Parameter(Mandatory=$true)][String]$Exe,[String]$OutputPath,[ValidateRange(1,20)][Int32]$Count=5)
$ErrorActionPreference='Stop'
$info=New-Object Diagnostics.ProcessStartInfo (Resolve-Path -LiteralPath $Exe).Path,'serve'
$info.UseShellExecute=$false; $info.CreateNoWindow=$true
$info.RedirectStandardInput=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
$server=[Diagnostics.Process]::Start($info)
function Request([String[]]$argv) {
 $watch=[Diagnostics.Stopwatch]::StartNew()
 $write=$server.StandardInput.WriteLineAsync((ConvertTo-Json -InputObject @($argv) -Compress)); if(-not $write.Wait(12000)){throw 'Write timed out'}
 $flush=$server.StandardInput.FlushAsync(); if(-not $flush.Wait(12000)){throw 'Flush timed out'}
 $read=$server.StandardOutput.ReadLineAsync(); if(-not $read.Wait(12000)){throw 'Read timed out'}
 $envelope=$read.Result | ConvertFrom-Json; $reply=$envelope.out | ConvertFrom-Json
 $timing=$reply.timing; $reply.PSObject.Properties.Remove('timing')
 [PSCustomObject]@{wallMs=[Math]::Round($watch.Elapsed.TotalMilliseconds,1); timing=$timing; reply=$reply; state=($reply | ConvertTo-Json -Compress -Depth 5)}
}
try {
 $null=$server.StandardError.ReadToEndAsync()
 $ready=$server.StandardOutput.ReadLineAsync(); if(-not $ready.Wait(10000) -or $ready.Result -ne '{"ready":1}'){throw 'No readiness handshake'}
 $focus=Request @('focus','--require-process','--process','ChatGPT'); if(-not $focus.reply.ok){throw 'Could not focus app'}
 $argv=@('open-panel','--require-process','--process','ChatGPT','--timing','--dry','--audit-panel-view','--expect-mode','Codex',
  '--mode-prefix','Switch mode, current mode: ','--panel-open','Changes','--panel-open','This branch','--panel-open','View changes',
  '--panel-open-turn','View changes','--panel-visible','Show files','--panel-visible','Hide files','--panel-tab','Changes','--conv-marker','Pin chat')
 $samples=[Collections.Generic.List[object]]::new()
 for($i=0;$i -lt $Count;$i++) {
  if($i % 2 -eq 0){$raw=Request ($argv+@('--full-scan')); $controls=Request $argv}
  else {$controls=Request $argv; $raw=Request ($argv+@('--full-scan'))}
  if(-not $raw.reply.ok -or -not $controls.reply.ok){throw ('Panel was unavailable: '+$raw.reply.error+' / '+$controls.reply.error)}
  if($raw.state -cne $controls.state){throw 'Panel outcomes differ; check whether the app changed during the pair'}
  if($controls.reply.contextFingerprint.Length -ne 64){throw 'Panel context was not audited'}
  if($controls.timing.scanPath -ne 'panel-controls'){throw 'Control view was not used'}
  $samples.Add([PSCustomObject]@{pair=$i+1; rawMs=$raw.wallMs; controlsMs=$controls.wallMs; rawScans=$raw.timing.scans; controlScans=$controls.timing.scans})
 }
 $wrong=@($argv); $wrong[[Array]::IndexOf($wrong,'--expect-mode')+1]='ChatGPT'
 $raw=Request ($wrong+@('--full-scan')); $controls=Request $wrong
 if($raw.reply.error -ne 'mode-changed' -or $raw.state -cne $controls.state){throw 'Wrong-mode refusal changed'}
 $result=[PSCustomObject]@{measuredAt=[DateTime]::Now.ToString('o');equivalentPairs=$Count;wrongModeRefused=$true;samples=$samples.ToArray()}
 if($OutputPath){$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8}
 $result | ConvertTo-Json -Depth 8
 $server.StandardInput.Close(); if(-not $server.WaitForExit(3000)){throw 'Server remained alive after EOF'}
} finally {if(-not $server.HasExited){$server.Kill()}; $server.Dispose()}
