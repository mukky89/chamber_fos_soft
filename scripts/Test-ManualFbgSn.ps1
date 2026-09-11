$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$testDir = Join-Path ([IO.Path]::GetTempPath()) ('manual-fbg-sn-' + [guid]::NewGuid().ToString('N'))
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
using VotschVc3.Core.Calibration;

class Program
{
    [STAThread] static void Main()
    {
        // Avoid the production constructor: no app settings, timers or hardware are opened.
        var vm = (CalibrationViewModel)RuntimeHelpers.GetUninitializedObject(typeof(CalibrationViewModel));
        var rows = new ObservableCollection<CalibrationPeakRowViewModel>();
        typeof(CalibrationViewModel).GetField("<Peaks>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, rows);
        var propagate = typeof(CalibrationViewModel).GetMethod("PropagateChannelSerialNumber", BindingFlags.Instance | BindingFlags.NonPublic)!;
        CalibrationPeakRowViewModel Row(string device, string channel, string peak, double wavelength, string chain = "")
        {
            var row = new CalibrationPeakRowViewModel(new(device, channel, []), new(peak, 1, wavelength),
                new() { ChainSerialNumber = chain });
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(row.ChannelSerialNumber)) propagate.Invoke(vm, [row]);
            };
            rows.Add(row);
            return row;
        }
        var p1 = Row("LOGGER", "1.3", "P1", 1530.533);
        var p2 = Row("logger", "1.3", "P2", 1531.661);
        var chain = Row("LOGGER", "1.3", "P3", 1540, "CHAIN1/0001");
        var otherChannel = Row("LOGGER", "1.4", "P1", 1530.533);
        var otherDevice = Row("OTHER", "1.3", "P1", 1530.533);
        p1.ChannelSerialNumber = "291877/0001";
        Check(p2.SerialNumber == p1.SerialNumber, "Manual SN must reach P2");
        Check(chain.ChannelSerialNumber == p1.SerialNumber && chain.SerialNumber == "CHAIN1/0001", "CHAIN override must survive");
        Check(otherChannel.SerialNumber == "" && otherDevice.SerialNumber == "", "Do not cross channel/device boundaries");
        var sensorRows = rows.Where(r => r.SerialNumber == p1.SerialNumber).ToArray();
        var types = FbgWavelengthComparison.ResolveTypes(sensorRows.Select(r => r.CurrentWavelengthNm).ToArray(),
            [new(1530.574, null, FbgType: "T"), new(1531.670, null, FbgType: "S")]);
        Check(types.SequenceEqual(new[] { "T", "S" }), "Complete channel must resolve T/S from API wavelengths");
        p2.ChannelSerialNumber = "291878/0001";
        Check(p1.SerialNumber == "291878/0001", "Editing P2 must update P1");
        p1.ChannelSerialNumber = "";
        Check(p2.SerialNumber == "" && chain.SerialNumber == "CHAIN1/0001", "Clearing shared SN must preserve CHAIN");
        typeof(CalibrationViewModel).GetField("_applyingRecoveredMappings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, true);
        p1.ChannelSerialNumber = "291877/0001";
        Check(p2.SerialNumber == "", "Loading saved per-peak mappings must not propagate halfway through restore");
        var restore = typeof(CalibrationViewModel).GetMethod("RestoreSharedChannelSerialNumbers", BindingFlags.Instance | BindingFlags.NonPublic)!;
        restore.Invoke(vm, null);
        Check(p2.SerialNumber == p1.SerialNumber && chain.SerialNumber == "CHAIN1/0001", "Previously saved partial channel SN must recover");
        p2.ChannelSerialNumber = "OTHER1/0001";
        restore.Invoke(vm, null);
        Check(p1.SerialNumber == "291877/0001" && p2.SerialNumber == "OTHER1/0001", "Conflicting saved assignments must not be guessed");
        Console.WriteLine("PASS: manual SN, recursion guard, channel/device isolation, CHAIN, T/S resolution, reverse edit, clear and restore guard.");
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
'@
[IO.File]::WriteAllText((Join-Path $testDir 'Program.cs'), $source)
$log = Join-Path $testDir 'test.log'
& dotnet run --project (Join-Path $testDir 'Test.csproj') -c Release *> $log
if ($LASTEXITCODE -ne 0) { Get-Content $log; throw "Manual SN regression failed. Log: $log" }
Get-Content $log | Select-String '^PASS:' | ForEach-Object { $_.Line }
