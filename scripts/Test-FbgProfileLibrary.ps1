$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$testDir = Join-Path ([IO.Path]::GetTempPath()) ('fbg-library-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
$appProject = [Security.SecurityElement]::Escape((Join-Path $repo 'src/VotschVc3.App/VotschVc3.App.csproj'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
 <ItemGroup><ProjectReference Include="$appProject" /></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $testDir 'Test.csproj'), $project)
$source = @'
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Profiles;
class Program
{
 [STAThread] static void Main()
 {
  var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"fbg-profiles-"+Guid.NewGuid().ToString("N"));
  var store=new ProfileStore(directory);
  var original=new TestProfile{Name="Original",DeviceKind=ProfileDeviceKind.Votsch};store.Save(original);
  var vm=(CalibrationViewModel)RuntimeHelpers.GetUninitializedObject(typeof(CalibrationViewModel));
  void Set(string name,object value)=>typeof(CalibrationViewModel).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,value);
  Set("_profileStore",store);Set("_selectedProfile",original);Set("_selectedChamber",new CalibrationChamberOption(new(){Protocol=ChamberProtocol.VotschAscii2}));
  var list=new ObservableCollection<TestProfile>{original};Set("<Profiles>k__BackingField",list);
  var peaks=new ObservableCollection<CalibrationPeakRowViewModel>();Set("<Peaks>k__BackingField",peaks);
  var peak=new CalibrationPeakRowViewModel(new("LOGGER","1.2",[]),new("P1",1,1511),null){ChannelSerialNumber="291875/0002",Selected=true};peaks.Add(peak);
  var quick=new TestProfile{Name="Quick profile",DeviceKind=ProfileDeviceKind.Votsch,ExecutionMode=ProfileExecutionMode.TemperatureCalibration};
  store.Save(quick);store.Save(new(){Name="Other device",DeviceKind=ProfileDeviceKind.Sika});store.Save(new(){Name="Archived",IsArchived=true});
  vm.RefreshProfileLibraryAsync().GetAwaiter().GetResult();
  Check(list.Any(p=>p.Id==quick.Id),"New quick profile missing");
  Check(list.Count==2,"Device/archive filtering changed");
  Check(ReferenceEquals(vm.SelectedProfile,original)&&ReferenceEquals(peaks[0],peak)&&peak.Selected&&peak.ChannelSerialNumber=="291875/0002","Refresh changed active profile/wiring");
  vm.RefreshProfileLibraryAsync().GetAwaiter().GetResult();Check(list.Count==2,"Repeated open duplicated profiles");
  Set("_isRunning",true);store.Save(new(){Name="Added while running",DeviceKind=ProfileDeviceKind.Votsch});vm.RefreshProfileLibraryAsync().GetAwaiter().GetResult();Check(list.Count==2,"Running calibration changed library");
  Set("_isRunning",false);vm.RefreshProfileLibraryAsync().GetAwaiter().GetResult();Check(list.Count==3,"Library did not refresh after run");
  Console.WriteLine("PASS: new Quick Profile reload, device/archive filter, repeated open, active selection/wiring retained, running guard.");
 }
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
'@
[IO.File]::WriteAllText((Join-Path $testDir 'Program.cs'), $source)
$log = Join-Path $testDir 'test.log'
& dotnet run --project (Join-Path $testDir 'Test.csproj') -c Release *> $log
if ($LASTEXITCODE -ne 0) { Get-Content $log; throw "Profile library regression failed. Log: $log" }
Get-Content $log | Select-String '^PASS:' | ForEach-Object { $_.Line }
