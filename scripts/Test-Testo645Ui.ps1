$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$testDir = Join-Path ([IO.Path]::GetTempPath()) ('testo-ui-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
$appProject = [Security.SecurityElement]::Escape((Join-Path $repo 'src/VotschVc3.App/VotschVc3.App.csproj'))
[IO.File]::WriteAllText((Join-Path $testDir 'Test.csproj'), @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="$appProject" /></ItemGroup></Project>
"@)
$source = @'
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VotschVc3.App.ViewModels;
using VotschVc3.App.Views;
using VotschVc3.App.Mvvm;
using VotschVc3.App.Converters;
using VotschVc3.Core.Calibration;
using VotschVc3.Core.Thermometers;

class Program
{
    [STAThread] static void Main()
    {
        var app = new Application();
        foreach (var resource in new[] { "Styles", "Icons", "CrispButtonStyles" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/VotschVc3.App;component/Themes/{resource}.xaml", UriKind.Relative) });
        app.Resources["BoolToVisibility"] = new BoolToVisibilityConverter();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await Run(); Console.WriteLine("PASS: Testo UI, fake transport/API, locks, recording, stale data, restore, COM lease, rendering."); }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        Dispatcher.Run();
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static string LiveText(string path) { using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)); return reader.ReadToEnd(); }
    static async Task Until(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("UI test condition"); await Task.Delay(20); }
    }
    static async Task Command(AsyncRelayCommand command)
    {
        Check(command.CanExecute(null), "Command disabled"); command.Execute(null);
        await Until(() => !command.IsRunning);
    }
    static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "testo-ui-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fake = new FakeSerial(); var api = new FakeApi(); var id = Guid.NewGuid();
        await using var vm = new ExternalHumidityViewModel(id, root, _ => fake, () => api) { ChamberName = "Simulated chamber - long device name for layout verification" };
        await vm.InitializeAsync();
        Check(!vm.Enabled && !vm.ConnectCommand.CanExecute(null) && fake.Opens == 0, "Disabled feature opened hardware");
        vm.Enabled = true; vm.Port = "FAKE_COM"; vm.IntervalSeconds = 1; vm.MaxAgeSeconds = 1;
        vm.ApiHost = "simulation"; vm.ApiPort = 15000; vm.UsePeaks = true;
        await Command(vm.LoadPeaksCommand); Check(vm.Peaks.Count == 2, vm.Status);
        vm.Peaks[0].Selected = true; vm.Peaks[1].Selected = true;
        await Command(vm.ConnectCommand); await Until(() => vm.HumidityText.Contains("56"));
        Check(!vm.CanConfigure, "Source configuration not locked");
        vm.Port = "SHOULD_NOT_CHANGE"; Check(vm.Port == "FAKE_COM", "COM changed while running");
        string output = Path.Combine(root, "recording.txt"); vm.SelectFile(output, false);
        vm.SelectFile(Path.Combine(root, "missing-directory", "error.txt"), false);
        await Command(vm.StartLogCommand); Check(!vm.IsLogging && vm.LogStatus.StartsWith("CHYBA") && vm.IsRunning, "Write error did not stay isolated");
        vm.SelectFile(output, false);
        await Command(vm.StartLogCommand); Check(vm.IsLogging, vm.LogStatus);
        vm.SelectFile(Path.Combine(root, "wrong.txt"), false); Check(vm.OutputPath == output, "Log path changed mid-session");
        await Until(() => LiveText(output).Contains("1533"));
        api.Fail = true; await Until(() => vm.Status.Contains("API_ERROR"));
        Check(vm.HumidityText.Contains("56"), "API failure discarded valid Testo reading");
        fake.Timeout = true; await Until(() => vm.HumidityText.StartsWith("\u2014"));
        await Until(() => vm.Status.Contains("TIMEOUT"));
        await Command(vm.StopLogCommand); await vm.DisconnectAsync();
        Check(fake.Disposals == 1 && !vm.IsRunning, "Transport disposal");
        string text = File.ReadAllText(output);
        Check(text.Contains("API_ERROR") && text.Contains("NA"), "Errors or missing values not logged");
        await using var restored = new ExternalHumidityViewModel(id, root, _ => new FakeSerial(), () => new FakeApi());
        await restored.InitializeAsync();
        Check(restored.Enabled && restored.Port == "FAKE_COM" && restored.Peaks.Count == 2 && !restored.IsRunning, "Settings/restart");

        // Shared physical port lease, without opening any physical port.
        var leaseType = typeof(ExternalHumidityViewModel).Assembly.GetType("VotschVc3.App.Thermometers.SerialPortLease")!;
        var acquire = leaseType.GetMethod("TryAcquire", BindingFlags.Static | BindingFlags.Public)!;
        object?[] first = ["TEST_FAKE_PORT", null]; object?[] second = ["test_fake_port", null];
        Check((bool)acquire.Invoke(null, first)!, "first lease");
        Check(!(bool)acquire.Invoke(null, second)!, "same COM opened twice");
        ((IDisposable)first[1]!).Dispose(); Check((bool)acquire.Invoke(null, second)!, "lease not released");
        ((IDisposable)second[1]!).Dispose();

        var errors = new StringWriter(); var trace = new TextWriterTraceListener(errors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var window = new ExternalHumidityWindow(vm);
        // Render the window content offscreen, without launching App startup or physical devices.
        var content = (FrameworkElement)window.Content; window.Content = null; content.DataContext = vm;
        ((ScrollViewer)content).Background = (Brush)Application.Current.FindResource("BackgroundBrush");
        // An off-screen presentation source makes ChartView.IsVisible true, without activating a window.
        var host = new Window { Content = content, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Width = 760, Height = 580 };
        host.Show();
        foreach (int width in new[] { 760, 1800 })
        {
            int height = width == 760 ? 580 : 1000;
            host.Width = width; host.Height = height;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(root, $"testo-{width}.png"); using var stream = File.Create(path); encoder.Save(stream);
            Console.WriteLine(path);
            ((ScrollViewer)content).ScrollToEnd(); content.UpdateLayout();
            var bottom = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bottom.Render(content);
            var bottomEncoder = new PngBitmapEncoder(); bottomEncoder.Frames.Add(BitmapFrame.Create(bottom));
            string bottomPath = Path.Combine(root, $"testo-{width}-bottom.png"); using var bottomStream = File.Create(bottomPath); bottomEncoder.Save(bottomStream);
            Console.WriteLine(bottomPath);
            ((ScrollViewer)content).ScrollToTop(); content.UpdateLayout();
        }
        trace.Flush(); Check(string.IsNullOrWhiteSpace(errors.ToString()), errors.ToString());
        PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
        host.Close(); window.Close();
    }
    sealed class FakeSerial : ITesto645Transport
    {
        public int Opens, Disposals; public volatile bool Timeout;
        public void Open() => Opens++;
        public void ClearInput() { }
        public void Write(byte[] request) { }
        public int Read(byte[] buffer)
        {
            if (Timeout) { Thread.Sleep(100); throw new TimeoutException(); }
            var frame = new byte[29]; frame[0] = 0x21; frame[4] = 1; frame[14] = 2; frame[15] = 55; frame[20] = 234;
            frame.CopyTo(buffer, 0); return frame.Length;
        }
        public void Dispose() => Disposals++;
    }
    sealed class FakeApi : IPeakLoggerClient
    {
        public bool Fail; public bool IsConnected { get; private set; }
        public DateTimeOffset? LastDataTimestamp => DateTimeOffset.Now;
        public Task ConnectAsync(PeakLoggerSettings s, CancellationToken t = default) { IsConnected = true; return Task.CompletedTask; }
        public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
        public Task<IReadOnlyList<PeakLoggerSensor>> DiscoverSensorsAsync(CancellationToken t = default) => Task.FromResult<IReadOnlyList<PeakLoggerSensor>>([new("SIM", "1.1", [new("P1", 1, 1533)]), new("SIM", "2.1", [new("P2", 2, 1544)])]);
        public Task<IReadOnlyList<PeakLoggerMeasurement>> ReadMeasurementsAsync(CancellationToken t = default)
        {
            if (Fail) throw new IOException("simulated API loss");
            return Task.FromResult<IReadOnlyList<PeakLoggerMeasurement>>([new(DateTimeOffset.Now, "SIM", "1.1", "P1", 1, 1533)]);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
'@
[IO.File]::WriteAllText((Join-Path $testDir 'Program.cs'), $source)
dotnet run --project (Join-Path $testDir 'Test.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Testo UI verification failed.' }
