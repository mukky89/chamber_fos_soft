$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$testDir = Join-Path ([IO.Path]::GetTempPath()) ('fbg-device-layout-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
$appProject = [Security.SecurityElement]::Escape((Join-Path $repo 'src/VotschVc3.App/VotschVc3.App.csproj'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
 <ItemGroup><ProjectReference Include="$appProject" /></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $testDir 'Test.csproj'), $project)
$env:FBG_LAYOUT_XAML = Join-Path $repo 'src/VotschVc3.App/Views/CalibrationWindow.xaml'
$source = @'
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
class State : INotifyPropertyChanged
{
 public event PropertyChangedEventHandler? PropertyChanged;
 public bool IsShowingDeviceLoad {get;private set;}
 public bool IsLoadingDeviceData {get;private set;}
 public void Change(bool busy,bool visible){IsLoadingDeviceData=busy;IsShowingDeviceLoad=visible;PropertyChanged?.Invoke(this,new(null));}
}
class Program
{
 [STAThread] static void Main()
 {
  var app=new Application();
  app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/VotschVc3.App;component/Themes/Styles.xaml",UriKind.Relative)});
  XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
  var xml=XDocument.Load(Environment.GetEnvironmentVariable("FBG_LAYOUT_XAML")!);
  var title=xml.Descendants(ns+"TextBlock").Single(e=>(string?)e.Attribute("Text")=="Zostava kalibrácie");
  var header=(Grid)XamlReader.Parse(title.Parent!.ToString());
  var root=new StackPanel();root.Children.Add(header);var content=new Border{Height=260};root.Children.Add(content);
  var state=new State();root.DataContext=state;
  double Measure(){root.Measure(new Size(900,1000));root.Arrange(new Rect(0,0,900,root.DesiredSize.Height));root.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);return content.TranslatePoint(new Point(),root).Y;}
  var initial=Measure();
  for(int i=0;i<10;i++){
   state.Change(true,false);Check(Measure()==initial,"Background refresh moved cards");
   Check(((StackPanel)header.Children[1]).Visibility==Visibility.Hidden,"Background progress flashed");
   state.Change(true,true);Check(Measure()==initial,"Manual loading moved cards");
   Check(((StackPanel)header.Children[1]).Visibility==Visibility.Visible,"Manual progress missing");
   state.Change(false,false);Check(Measure()==initial,"Completion moved cards");
  }
  var vm=(VotschVc3.App.ViewModels.CalibrationViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(VotschVc3.App.ViewModels.CalibrationViewModel));
  var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
  foreach(var prop in vm.GetType().GetProperties()){
   object? command=prop.PropertyType==typeof(VotschVc3.App.Mvvm.AsyncRelayCommand)?new VotschVc3.App.Mvvm.AsyncRelayCommand(()=>Task.CompletedTask):prop.PropertyType==typeof(VotschVc3.App.Mvvm.RelayCommand)?new VotschVc3.App.Mvvm.RelayCommand(()=>{}):null;
   if(command!=null)vm.GetType().GetField("<"+prop.Name+">k__BackingField",flags)?.SetValue(vm,command);
  }
  var load=vm.GetType().GetMethod("LoadDeviceDataAsync",flags)!;
  foreach(bool foreground in new[]{false,true}){
   var completion=new TaskCompletionSource();
   var task=(Task)load.Invoke(vm,new object[]{(Func<Task>)(()=>completion.Task),foreground})!;
   Check(vm.IsLoadingDeviceData&&vm.IsShowingDeviceLoad==foreground,"Loading gate/visibility mismatch");
   completion.SetResult(); task.GetAwaiter().GetResult();
   Check(!vm.IsLoadingDeviceData&&!vm.IsShowingDeviceLoad,"Loading state leaked");
   var failed=(Task)load.Invoke(vm,new object[]{(Func<Task>)(()=>Task.FromException(new Exception("test"))),foreground})!;
   try{failed.GetAwaiter().GetResult();}catch(Exception){}
   Check(!vm.IsLoadingDeviceData&&!vm.IsShowingDeviceLoad,"Failure left loading state active");
  }
  Console.WriteLine("PASS: 30 loading transitions preserve card position; background indicator hidden, manual indicator visible.");
 }
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
'@
[IO.File]::WriteAllText((Join-Path $testDir 'Program.cs'), $source)
$log = Join-Path $testDir 'test.log'
& dotnet run --project (Join-Path $testDir 'Test.csproj') -c Release *> $log
if ($LASTEXITCODE -ne 0) { Get-Content $log; throw "Device layout regression failed. Log: $log" }
Get-Content $log | Select-String '^PASS:' | ForEach-Object { $_.Line }
