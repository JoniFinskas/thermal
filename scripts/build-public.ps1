param([switch]$Test,[string]$SdkPath)
$ErrorActionPreference='Stop'
$taskRepo=Split-Path -Parent $PSScriptRoot
if(!$SdkPath){$SdkPath=(Get-Command dotnet -ErrorAction Stop).Source}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1';$env:DOTNET_NOLOGO='1';$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
Push-Location $taskRepo
try {
 foreach($taskProject in @('src/Thermal.App','tests/Thermal.Public.Tests')){
  & $SdkPath restore $taskProject --locked-mode --configfile NuGet.Config --disable-build-servers -p:ThermalPublicBuild=true
  if($LASTEXITCODE-ne 0){throw 'Locked offline restore failed'}
  & $SdkPath build $taskProject -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -nodeReuse:false -p:ThermalPublicBuild=true
  if($LASTEXITCODE-ne 0){throw 'Public build failed'}
 }
 if($Test){
  & $SdkPath tests/Thermal.Public.Tests/bin/public/Release/net10.0/Thermal.Public.Tests.dll
  if($LASTEXITCODE-ne 0){throw 'Public behaviour checks failed'}
 }
}finally{Pop-Location}
