$ErrorActionPreference='Stop'
$PSModuleAutoLoadingPreference='None'
if($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or -not [Environment]::Is64BitProcess){exit 2}
$win=[Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
$modules=[IO.Path]::Combine($win,'System32\WindowsPowerShell\v1.0\Modules')
$env:PSModulePath=$modules
Import-Module ([IO.Path]::Combine($modules,'Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1'))
if(-not $fixture){
 $principal=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
 if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){exit 3}
}
if($parentId -lt 1 -or $nonce -notmatch '^[a-f0-9]{32}$'){exit 4}
$parent=[Diagnostics.Process]::GetProcessById($parentId)
$me=[Diagnostics.Process]::GetCurrentProcess()
if($parent.HasExited -or $parent.SessionId -ne $me.SessionId){exit 4}
# Reflection.Emit defines only fixed native identity calls and watchdog callbacks.
# No Add-Type compiler, temporary assembly or script file is used by this host.
$an=[Reflection.AssemblyName]::new('ThermalLocalIdentity')
$ab=[AppDomain]::CurrentDomain.DefineDynamicAssembly($an,[Reflection.Emit.AssemblyBuilderAccess]::Run)
$mb=$ab.DefineDynamicModule('m');$tb=$mb.DefineType('Identity',385)
foreach($name in @('GetNamedPipeServerProcessId','GetNamedPipeServerSessionId')){
 $m=$tb.DefinePInvokeMethod($name,'kernel32.dll',8214,[Reflection.CallingConventions]::Standard,[bool],@([Microsoft.Win32.SafeHandles.SafePipeHandle],[uint32].MakeByRefType()),[Runtime.InteropServices.CallingConvention]::Winapi,[Runtime.InteropServices.CharSet]::Unicode)
 $m.SetImplementationFlags([Reflection.MethodImplAttributes]::PreserveSig)
}
$m=$tb.DefinePInvokeMethod('QueryFullProcessImageNameW','kernel32.dll',8214,[Reflection.CallingConventions]::Standard,[bool],@([Microsoft.Win32.SafeHandles.SafeProcessHandle],[uint32],[Text.StringBuilder],[uint32].MakeByRefType()),[Runtime.InteropServices.CallingConvention]::Winapi,[Runtime.InteropServices.CharSet]::Unicode)
$m.SetImplementationFlags([Reflection.MethodImplAttributes]::PreserveSig)
$pf=$tb.DefineField('Parent',[Diagnostics.Process],22)
$exitMethod=$tb.DefinePInvokeMethod('TerminateProcess','kernel32.dll',8214,[Reflection.CallingConventions]::Standard,[bool],@([IntPtr],[uint32]),[Runtime.InteropServices.CallingConvention]::Winapi,[Runtime.InteropServices.CharSet]::Unicode)
$exitMethod.SetImplementationFlags([Reflection.MethodImplAttributes]::PreserveSig)
foreach($name in @('Exit','CheckParent')){
 $m=$tb.DefineMethod($name,22,[void],@([object]));$il=$m.GetILGenerator()
 if($name -eq 'CheckParent'){
  $done=$il.DefineLabel();$il.Emit([Reflection.Emit.OpCodes]::Ldsfld,$pf)
  $il.Emit([Reflection.Emit.OpCodes]::Callvirt,[Diagnostics.Process].GetProperty('HasExited').GetGetMethod())
  $il.Emit([Reflection.Emit.OpCodes]::Brfalse_S,$done)
 }
 # -1 is the current-process pseudo handle: timers can terminate ONLY this reader.
 # Avoid managed shutdown/finalizers delaying a watchdog behind a blocked WMI call.
 $il.Emit([Reflection.Emit.OpCodes]::Ldc_I4_M1);$il.Emit([Reflection.Emit.OpCodes]::Conv_I)
 $il.Emit([Reflection.Emit.OpCodes]::Ldc_I4,7);$il.Emit([Reflection.Emit.OpCodes]::Call,$exitMethod);$il.Emit([Reflection.Emit.OpCodes]::Pop)
 if($name -eq 'CheckParent'){$il.MarkLabel($done)}
 $il.Emit([Reflection.Emit.OpCodes]::Ret)
}
$native=$tb.CreateType();$native.GetField('Parent').SetValue($null,$parent)
$exitCallback=[Delegate]::CreateDelegate([Threading.TimerCallback],$native.GetMethod('Exit'))
$parentCallback=[Delegate]::CreateDelegate([Threading.TimerCallback],$native.GetMethod('CheckParent'))
$operation=[Threading.Timer]::new($exitCallback,$null,15000,-1)
$parentWatch=[Threading.Timer]::new($parentCallback,$null,1000,1000)
$lifetime=[Threading.Timer]::new($exitCallback,$null,14400000,-1)
$pipe=$null
try{
 $image=[Text.StringBuilder]::new(32768);[uint32]$length=32768
 if(-not $native::QueryFullProcessImageNameW($parent.SafeHandle,0,$image,[ref]$length) -or -not [string]::Equals($image.ToString(),$parentImage,[StringComparison]::OrdinalIgnoreCase)){exit 4}
 # Identification prevents a normal-user pipe server impersonating an admin.
 $pipe=[IO.Pipes.NamedPipeClientStream]::new('.','thermal-acer-'+$nonce,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::None,[Security.Principal.TokenImpersonationLevel]::Identification)
 $pipe.Connect(5000);[uint32]$peer=0;[uint32]$session=0
 if(-not $native::GetNamedPipeServerProcessId($pipe.SafePipeHandle,[ref]$peer) -or $peer -ne $parentId -or -not $native::GetNamedPipeServerSessionId($pipe.SafePipeHandle,[ref]$session) -or $session -ne $me.SessionId){exit 4}
 $instance=$null
 if(-not $fixture){
  Import-Module ([IO.Path]::Combine($modules,'CimCmdlets\CimCmdlets.psd1'))
  $system=@(Get-CimInstance -Namespace root/cimv2 -ClassName Win32_ComputerSystem -Property Manufacturer,Model -OperationTimeoutSec 5)
  $bios=@(Get-CimInstance -Namespace root/cimv2 -ClassName Win32_BIOS -Property SMBIOSBIOSVersion -OperationTimeoutSec 5)
  if($system.Count -ne 1 -or $bios.Count -ne 1 -or $system[0].Manufacturer -ine 'Acer' -or $system[0].Model -ine 'Predator PHN16-72' -or $bios[0].SMBIOSBIOSVersion -cne 'V1.11'){exit 5}
  $schema=Get-CimClass -Namespace root/WMI -ClassName AcerGamingFunction -OperationTimeoutSec 5
  $method=$schema.CimClassMethods['GetGamingSysInfo']
  if([guid]$schema.CimClassQualifiers['GUID'].Value -ne [guid]'7A4DDFE7-5B5D-40B4-8595-4408E0CC7F56' -or $null -eq $method -or [int]$method.Qualifiers['WmiMethodId'].Value -ne 5 -or $method.Parameters.Count -ne 2 -or $method.Parameters['gmInput'].CimType.ToString() -cne 'UInt32' -or $method.Parameters['gmOutput'].CimType.ToString() -cne 'UInt64'){exit 5}
  $instances=@(Get-CimInstance -Namespace root/WMI -ClassName AcerGamingFunction -OperationTimeoutSec 5)
  if($instances.Count -ne 1 -or -not $instances[0].Active){exit 5};$instance=$instances[0]
 }
 $last=[Diagnostics.Stopwatch]::StartNew();$first=$true;$reads=0
 while($true){
  $null=$operation.Change(12000,-1);$command=$pipe.ReadByte()
  if($command -eq -1 -or $command -eq 3){break}
  if($command -eq 2){$pipe.WriteByte(2);$pipe.Flush();continue}
  if($command -ne 1 -or (-not $first -and $last.ElapsedMilliseconds -lt 2000)){exit 6}
  if($first -and -not $fixture){
   $null=$operation.Change(5000,-1)
   $caps=[uint64](Invoke-CimMethod -InputObject $instance -MethodName GetGamingSysInfo -Arguments @{gmInput=[uint32]0} -OperationTimeoutSec 4).gmOutput
   if(($caps -band 255) -ne 0 -or ((($caps -shr 24) -band 1) -eq 0)){exit 5}
  }
  $first=$false;$last.Restart();$null=$operation.Change(5000,-1)
  if($fixture){$value=65+($reads % 2)}else{
   $word=[uint64](Invoke-CimMethod -InputObject $instance -MethodName GetGamingSysInfo -Arguments @{gmInput=[uint32]257} -OperationTimeoutSec 4).gmOutput
   $value=[int](($word -shr 8) -band 65535)
   if(($word -band 255) -ne 0 -or $value -lt 1 -or $value -gt 125){exit 6}
  }
  $reads++;$detail='Local CPU firmware reading'
  if($fixture){$detail='SYNTHETIC LOCAL - reads='+$reads}
  $unsupported=@{Value=$null;State=2}
  $frame=@{At=[DateTimeOffset]::UtcNow.ToString('o');CpuTemperature=@{Value=$value;State=0};CpuFan=$unsupported;GpuFan=$unsupported;GpuTemperature=$unsupported;Detail=$detail}
  $bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $frame -Depth 4 -Compress))
  if($bytes.Length -gt 4096){exit 6}
  $header=[BitConverter]::GetBytes([int]$bytes.Length)
  $pipe.Write($header,0,4);$pipe.Write($bytes,0,$bytes.Length);$pipe.Flush()
 }
}catch{exit 8}finally{
 if($null -ne $pipe){$pipe.Dispose()};$operation.Dispose();$parentWatch.Dispose();$lifetime.Dispose();$parent.Dispose();$me.Dispose()
}
