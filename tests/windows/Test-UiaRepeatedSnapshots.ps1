# Read-only soak across the persistent worker's periodic COM-wrapper collection.
# Records timings and counts only; no app text is printed or saved.
param([Parameter(Mandatory=$true)][String]$Exe,[String]$OutputPath,[ValidateRange(55,500)][Int32]$Count=60)
$ErrorActionPreference='Stop'
$info=New-Object Diagnostics.ProcessStartInfo (Resolve-Path -LiteralPath $Exe).Path,'serve'
$info.UseShellExecute=$false; $info.CreateNoWindow=$true
$info.RedirectStandardInput=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
$server=[Diagnostics.Process]::Start($info)
try {
 $null=$server.StandardError.ReadToEndAsync()
 $ready=$server.StandardOutput.ReadLineAsync()
 if(-not $ready.Wait(10000) -or $ready.Result -ne '{"ready":1}'){throw 'No readiness handshake'}
 $samples=[Collections.Generic.List[object]]::new()
 for($i=0;$i -lt $Count;$i++) {
  $watch=[Diagnostics.Stopwatch]::StartNew()
  $write=$server.StandardInput.WriteLineAsync('["status","--require-process","--process","ChatGPT","--timing"]')
  if(-not $write.Wait(10000)){throw 'Write timed out'}
  $flush=$server.StandardInput.FlushAsync(); if(-not $flush.Wait(10000)){throw 'Flush timed out'}
  $read=$server.StandardOutput.ReadLineAsync(); if(-not $read.Wait(10000)){throw 'Read timed out'}
  $envelope=$read.Result | ConvertFrom-Json; $reply=$envelope.out | ConvertFrom-Json
  if($envelope.exit -ne 0 -or -not $reply.ok -or -not $reply.surface){throw 'Incomplete or unavailable surface'}
  $samples.Add([PSCustomObject]@{request=$i+1; wallMs=[Math]::Round($watch.Elapsed.TotalMilliseconds,1);
    scanMs=$reply.timing.scanMs; scans=$reply.timing.scans})
  Start-Sleep -Milliseconds 200
 }
 $ordered=@($samples.wallMs | Sort-Object)
 $result=[PSCustomObject]@{measuredAt=[DateTime]::Now.ToString('o'); helperSha256=(Get-FileHash -LiteralPath $Exe -Algorithm SHA256).Hash;
   count=$Count; medianMs=$ordered[[Int32][Math]::Floor($Count/2)]; maxMs=$ordered[-1]; over3000ms=@($samples | Where-Object {$_.wallMs -ge 3000}).Count;
   samples=$samples.ToArray()}
 if($OutputPath){$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8}
 $result | Select-Object count,medianMs,maxMs,over3000ms | ConvertTo-Json
 $server.StandardInput.Close(); if(-not $server.WaitForExit(3000)){throw 'Server remained alive after EOF'}
} finally {if(-not $server.HasExited){$server.Kill()}; $server.Dispose()}
