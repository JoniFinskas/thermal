param([Parameter(Mandatory=$true)][string]$RuntimeRoot,[Parameter(Mandatory=$true)][string]$OutputRoot,[switch]$PublicCandidate=$true)
$ErrorActionPreference='Stop'
$taskRepo=Split-Path -Parent $PSScriptRoot
$taskArtifacts=[IO.Path]::GetFullPath((Join-Path $taskRepo 'artifacts'))
$taskOutput=[IO.Path]::GetFullPath($OutputRoot)
if(!$taskOutput.StartsWith($taskArtifacts+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)-or (Test-Path -LiteralPath $taskOutput)){throw 'Use a fresh directory strictly inside artifacts/.'}
for($taskAncestor=Split-Path -Parent $taskOutput;Test-Path -LiteralPath $taskAncestor;$taskAncestor=Split-Path -Parent $taskAncestor){if((Get-Item -LiteralPath $taskAncestor -Force).Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'Linked output ancestor'};if([IO.Path]::GetPathRoot($taskAncestor)-eq $taskAncestor){break}}
$taskRuntime=[IO.Path]::GetFullPath($RuntimeRoot)
if(!$PublicCandidate){throw 'This packager only supports the public opt-in build.'}
$taskDotnet=(Get-Command dotnet -ErrorAction Stop).Source
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:DOTNET_NOLOGO='1';$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false';$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false';$env:DOTNET_CLI_HOME=Join-Path $taskRepo 'work/dotnet-home'
function Invoke-CandidateTool([string]$Exe,[string[]]$Arguments,[int]$Seconds=180){
 $taskStart=[Diagnostics.ProcessStartInfo]::new($Exe);foreach($taskArg in $Arguments){$taskStart.ArgumentList.Add($taskArg)}
 $taskStart.WorkingDirectory=$taskRepo;$taskStart.UseShellExecute=$false;$taskStart.CreateNoWindow=$true;$taskStart.RedirectStandardOutput=$true;$taskStart.RedirectStandardError=$true
 $taskChild=[Diagnostics.Process]::Start($taskStart)
 try{$taskChild.PriorityClass='BelowNormal';$taskOut=$taskChild.StandardOutput.ReadToEndAsync();$taskErr=$taskChild.StandardError.ReadToEndAsync();if(!$taskChild.WaitForExit($Seconds*1000)){$taskChild.Kill($true);[void]$taskChild.WaitForExit(5000);throw 'Owned package command deadline'};if(!$taskOut.Wait(2000)-or !$taskErr.Wait(2000)-or $taskChild.ExitCode-ne 0){throw ('Package command failed: '+$taskErr.Result)};return $taskOut.Result}finally{if(!$taskChild.HasExited){$taskChild.Kill($true);[void]$taskChild.WaitForExit(5000)};$taskChild.Dispose()}
}
$taskProfile=@(('-p:ThermalPublicBuild='+$PublicCandidate.IsPresent.ToString().ToLowerInvariant()),'-p:AppHostDotNetSearch=AppRelative')
# Publish to a new directory only. No restore/download or system-wide runtime installation.
[void](Invoke-CandidateTool $taskDotnet (@('publish','src/Thermal.App','-c','Release','--no-restore','--disable-build-servers','-m:1','-p:UseSharedCompilation=false','-p:DebugType=None','-nodeReuse:false','--self-contained','false','-o',$taskOutput)+$taskProfile))
$taskRuntimeFiles=[Collections.Generic.List[object]]::new()
foreach($taskRelative in @('dotnet.exe','LICENSE.txt','ThirdPartyNotices.txt')){$taskRuntimeFiles.Add([pscustomobject]@{Source=(Join-Path $taskRuntime $taskRelative);Relative=$taskRelative})}
foreach($taskRelative in @('host/fxr/10.0.13','shared/Microsoft.NETCore.App/10.0.13','shared/Microsoft.WindowsDesktop.App/10.0.13')){
 $taskDirectory=Get-Item -LiteralPath (Join-Path $taskRuntime $taskRelative) -Force
 if($taskDirectory.Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'Linked runtime root'}
 foreach($taskItem in Get-ChildItem -LiteralPath $taskDirectory.FullName -Recurse -Force){if($taskItem.Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'Linked runtime input'};if(!$taskItem.PSIsContainer){$taskRuntimeFiles.Add([pscustomobject]@{Source=$taskItem.FullName;Relative=[IO.Path]::GetRelativePath($taskRuntime,$taskItem.FullName)})}}
}
foreach($taskFile in $taskRuntimeFiles){$taskItem=Get-Item -LiteralPath $taskFile.Source -Force;if($taskItem.Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'Linked runtime file'};$taskTarget=Join-Path (Join-Path $taskOutput '.NET') $taskFile.Relative;[void][IO.Directory]::CreateDirectory((Split-Path -Parent $taskTarget));[IO.File]::Copy($taskFile.Source,$taskTarget,$false);if((Get-FileHash -LiteralPath $taskTarget).Hash-ne (Get-FileHash -LiteralPath $taskFile.Source).Hash){throw 'Private runtime copy mismatch'}}
[IO.File]::Copy((Join-Path $taskRepo 'LICENSE'),(Join-Path $taskOutput 'LICENSE.txt'),$false)
$taskDescribe=Invoke-CandidateTool (Join-Path $taskOutput 'Thermal.exe') @('--describe') 20 | ConvertFrom-Json
$taskExpected=[IO.Path]::GetFullPath((Join-Path $taskOutput '.NET/shared/Microsoft.NETCore.App/10.0.13'))
if($taskDescribe.RuntimeVersion-ne '10.0.13'-or ![IO.Path]::GetFullPath($taskDescribe.RuntimeDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar).Equals($taskExpected,[StringComparison]::OrdinalIgnoreCase)-or $taskDescribe.CpuDriverAccessAllowed-or $taskDescribe.ServiceActivationAllowed-or $taskDescribe.AcerLiveAllowed-or $taskDescribe.PublicBuild-ne $PublicCandidate.IsPresent-or $taskDescribe.AcerAutomaticRequest-eq $PublicCandidate.IsPresent){throw 'Candidate runtime or immutable admission differs'}
# Reject local build paths even when the separate PDB file was excluded.
foreach($taskOwnAssembly in @('Thermal.exe','Thermal.dll','Thermal.Core.dll')){
 $taskBytes=[IO.File]::ReadAllBytes((Join-Path $taskOutput $taskOwnAssembly))
 $taskViews=@([Text.Encoding]::UTF8.GetString($taskBytes),[Text.Encoding]::Unicode.GetString($taskBytes))
 if($taskBytes.Length-gt 1){$taskViews+=([Text.Encoding]::Unicode.GetString($taskBytes,1,$taskBytes.Length-1))}
 foreach($taskPrivateRoot in @($taskRepo,[Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile))){
  if(!$taskPrivateRoot){continue}
  foreach($taskSpelling in @($taskPrivateRoot,$taskPrivateRoot.Replace('\','/'))){foreach($taskView in $taskViews){if($taskView.IndexOf($taskSpelling,[StringComparison]::OrdinalIgnoreCase)-ge 0){throw ('Private build path in '+$taskOwnAssembly)}}}
 }
}
$taskFiles=@(foreach($taskFile in Get-ChildItem -LiteralPath $taskOutput -Recurse -File){if($taskFile.Extension-in @('.sys','.inf','.cat','.pfx','.key','.pem','.log','.dmp','.pdb')-or $taskFile.Name-like '.env*'){throw 'Forbidden portable payload entry'};[ordered]@{Path=[IO.Path]::GetRelativePath($taskOutput,$taskFile.FullName).Replace('\','/');Bytes=$taskFile.Length;Sha256=(Get-FileHash -LiteralPath $taskFile.FullName).Hash.ToLowerInvariant()}})
$taskReceipt=[ordered]@{Kind='ThermalDriverFreePortableCandidate';PublicBuild=$PublicCandidate.IsPresent;AcerAutomaticRequest=$taskDescribe.AcerAutomaticRequest;PublicReleaseApproved=$false;InstallationPerformed=$false;Runtime='10.0.13';RuntimeFiles=$taskRuntimeFiles.Count;DriverIncluded=$false;SensorModulesIncluded=$false;CpuDriverAccessAllowed=$false;Files=$taskFiles}
[IO.File]::WriteAllText((Join-Path $taskOutput 'candidate-manifest.json'),($taskReceipt|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
Write-Output ('Driver-free portable candidate prepared; '+$taskFiles.Count+' files, '+$taskRuntimeFiles.Count+' runtime copies verified. No installation, service activation or hardware read.')
