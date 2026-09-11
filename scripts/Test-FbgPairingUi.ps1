$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$testDir = Join-Path ([IO.Path]::GetTempPath()) ('fbg-pairing-ui-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
$appProject = [Security.SecurityElement]::Escape((Join-Path $repo 'src/VotschVc3.App/VotschVc3.App.csproj'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
 <ItemGroup><ProjectReference Include="$appProject" /></ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $testDir 'Test.csproj'), $project)
$env:FBG_PAIRING_RENDER_DIR = $testDir
$source = @'
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VotschVc3.App.Views;
using VotschVc3.App.ViewModels;
using VotschVc3.Core.Calibration;
class Program
{
 [STAThread] static void Main()
 {
  var app = new Application();
  foreach(var name in new[]{"Styles","Icons","CrispButtonStyles"}) app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri($"/VotschVc3.App;component/Themes/{name}.xaml",UriKind.Relative)});
  CalibrationPeakRowViewModel Row(string id,double wl,CalibrationSensorMapping? saved=null)=>new(new("LOGGER","3.1",[]),new(id,1,wl),saved);
  var p1=Row("P1",1511.839); var p2=Row("P2",1512.888);
  p1.ChannelSerialNumber=p2.ChannelSerialNumber="291875/0002";
  p1.ApplyCalibrationTypeDefault("T"); p2.ApplyCalibrationTypeDefault("S"); Check(p1.Selected&&!p2.Selected,"T/S defaults");
  p1.Selected=false; p1.ApplyCalibrationTypeDefault("T"); Check(!p1.Selected,"Manual deselection");
  p2.Selected=true; p2.ApplyCalibrationTypeDefault("S"); Check(p2.Selected,"Manual selection");
  p1.ChannelSerialNumber="291875/0003"; p1.ApplyCalibrationTypeDefault("T"); Check(p1.Selected,"New SN default");
  var saved=Row("P3",1511.839,new(){ChannelSerialNumber="291875/0002",Selected=false}); saved.ApplyCalibrationTypeDefault("T"); Check(!saved.Selected,"Saved choice");
  var late=Row("P4",1511.839); late.ChannelSerialNumber="291875/0002"; late.ApplyCalibrationTypeDefault(null); Check(!late.Selected,"Unknown type"); late.ApplyCalibrationTypeDefault("T"); Check(late.Selected,"Late API default");
  var panel=new SensorPairingPanel();
  T Field<T>(string name)=>(T)panel.FindName(name);
  var metadata=new ProductionMetadata("SC-01/T · GL: 1 m","SC-01/T","104011","Ukážkový zákazník s dlhším názvom",Fbg:[new(1511.900,null,FbgType:"T"),new(1512.900,null,FbgType:"S")]);
  Field<TextBox>("SerialInput").Text="291875/00O2";
  panel.ShowIssue("Neplatný formát SN","Použi číslice vo formáte 291875/0002. Skontroluj zámeny O/0 a I/1.");
  Check(Field<Border>("IssueCard").Visibility==Visibility.Visible,"Inline error"); Render(panel,"invalid",900);
  panel.SetStage(1); Field<TextBox>("SerialInput").Text="291875/0002";
  panel.ShowIssue("Tento snímač už je priradený","SN 291875/0002 sa už nachádza v zapojení.\nKanál 3.1 · zdroj LOGGER",true);
  Check(Field<Button>("ShowDuplicate").Visibility==Visibility.Visible,"Duplicate action"); Render(panel,"duplicate",680);
  panel.SetStage(2,"291875/0002"); panel.SetLoading(true); panel.UpdateCandidates([],null);
  Check(Field<ProgressBar>("LookupProgress").IsIndeterminate,"Indeterminate loading"); Render(panel,"loading",900);
  panel.ShowMetadata(metadata); panel.UpdateCandidates([p1,p2],metadata);
  panel.ShowWavelengthComparison(FbgWavelengthComparison.Evaluate(new[]{("P1",p1.CurrentWavelengthNm),("P2",p2.CurrentWavelengthNm)},metadata.Fbg));
  var grid=Field<DataGrid>("PeakPreview"); var preview=(PairingPeakPreview)grid.Items[0];
  Check(preview.Selected&&!((PairingPeakPreview)grid.Items[1]).Selected,"Preview T/S");
  preview.Selected=false; panel.UpdateCandidates([p1,p2],metadata); Check(ReferenceEquals(preview,grid.Items[0])&&!preview.Selected,"Preview refresh choice");
  panel.ApplyPreviewSelection(p1); Check(!p1.Selected,"Preview commit");
  Check(Field<TextBlock>("CustomerValue").Text==metadata.CustomerName,"Customer visible"); Render(panel,"ready",900); Render(panel,"ready-small",680);
  panel.SetStage(1); Check(Field<Border>("MetadataCard").Visibility==Visibility.Collapsed&&!Field<ProgressBar>("LookupProgress").IsIndeterminate,"Reset cleanup");
  var window=(CalibrationWindow)RuntimeHelpers.GetUninitializedObject(typeof(CalibrationWindow));
  var vm=(CalibrationViewModel)RuntimeHelpers.GetUninitializedObject(typeof(CalibrationViewModel));
  typeof(CalibrationViewModel).GetField("<Peaks>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,new ObservableCollection<CalibrationPeakRowViewModel>{p1,p2});
  typeof(CalibrationWindow).GetField("_viewModel",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,vm);
  typeof(CalibrationWindow).GetField("_pairingPanel",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,panel);
  var arm=typeof(CalibrationWindow).GetMethod("ArmSequentialSerialV9Async",BindingFlags.Instance|BindingFlags.NonPublic)!;
  Field<TextBox>("SerialInput").Text="291875/00O2";
  ((Task)arm.Invoke(window,null)!).GetAwaiter().GetResult();
  Check(Field<Border>("IssueCard").Visibility==Visibility.Visible && Field<Button>("ShowDuplicate").Visibility==Visibility.Collapsed,"Actual invalid SN flow");
  Field<TextBox>("SerialInput").Text=p2.SerialNumber;
  ((Task)arm.Invoke(window,null)!).GetAwaiter().GetResult();
  Check(Field<Button>("ShowDuplicate").Visibility==Visibility.Visible,"Actual duplicate flow");
  Field<TextBox>("SerialInput").Text="291875A000099";
  ((Task)arm.Invoke(window,null)!).GetAwaiter().GetResult();
  Check(Field<TextBlock>("PendingSerial").Text=="291875/0099" && Field<Border>("LoadingCard").Visibility==Visibility.Collapsed,"Actual barcode/offline flow");
  var wiring=new DataGrid{AutoGenerateColumns=false,SelectionMode=DataGridSelectionMode.Single}; var col=new DataGridTextColumn(); wiring.Columns.Add(col);
  wiring.ItemsSource=new ObservableCollection<CalibrationPeakRowViewModel>{p1,p2}; wiring.SelectedItem=p2; wiring.CurrentCell=new DataGridCellInfo(p2,col);
  typeof(CalibrationWindow).GetField("_wiringGrid",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,wiring);
  var refresh=typeof(CalibrationWindow).GetMethod("RefreshWiringGridWhenSafe",BindingFlags.Instance|BindingFlags.NonPublic)!;
  for(int i=0;i<5;i++) refresh.Invoke(window,null);
  Check(ReferenceEquals(wiring.SelectedItem,p2)&&ReferenceEquals(wiring.CurrentCell.Item,p2),"Repeated refresh preserves selection/current cell");
  Console.WriteLine("PASS: WPF states/render at 680/900 DIP, T/S, manual/saved choices, late API, preview commit, repeated selection refresh.");
 }
 static void Check(bool value,string label){if(!value)throw new Exception(label);}
 static void Render(FrameworkElement view,string name,double width)
 {
  view.Width=width; view.Measure(new Size(width,double.PositiveInfinity)); view.Arrange(new Rect(0,0,width,view.DesiredSize.Height)); view.UpdateLayout(); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => {}, System.Windows.Threading.DispatcherPriority.ApplicationIdle); view.Measure(new Size(width,double.PositiveInfinity)); view.Arrange(new Rect(0,0,width,view.DesiredSize.Height)); view.UpdateLayout();
  var bitmap=new RenderTargetBitmap((int)width,(int)Math.Ceiling(view.ActualHeight),96,96,PixelFormats.Pbgra32); bitmap.Render(view);
  var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
  using var stream=System.IO.File.Create(System.IO.Path.Combine(Environment.GetEnvironmentVariable("FBG_PAIRING_RENDER_DIR")!,name+".png")); encoder.Save(stream);
 }
}
'@
[IO.File]::WriteAllText((Join-Path $testDir 'Program.cs'), $source)
$log = Join-Path $testDir 'test.log'
& dotnet run --project (Join-Path $testDir 'Test.csproj') -c Release *> $log
if ($LASTEXITCODE -ne 0) { Get-Content $log; throw "FBG pairing regression failed. Log: $log" }
Get-Content $log | Select-String '^PASS:' | ForEach-Object { $_.Line }

Write-Output "Render directory: $testDir"
