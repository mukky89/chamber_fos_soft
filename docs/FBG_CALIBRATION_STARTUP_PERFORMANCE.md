# FBG calibration startup performance

Desktop version: **1.76.23**

## Goal

Opening the FBG calibration workspace must show an interactive window immediately. Hardware and network discovery must not be prerequisites for rendering the UI.

## Previous startup path

The old `CalibrationWindow.Loaded` handler awaited `CalibrationViewModel.InitializeHardwareAsync()`. That method performed several expensive operations before the rest of the window initialization continued:

1. detailed USB/COM enumeration through WMI;
2. an active `CheckAsync()` against every serial port, which could open an unrelated COM device and wait for a CTH7000 timeout;
3. broad PeakLogger API discovery over local/fallback TCP ports;
4. automatic PeakLogger connection and sensor discovery.

`ThermometersViewModel` also performed a WMI query synchronously in its constructor, which runs while `CalibrationWindow` is still being created on the WPF dispatcher thread.

## New startup path

The first frame is now UI-first:

1. `ThermometersViewModel` uses `SerialPort.GetPortNames()` plus cached USB metadata only. No WMI runs in its constructor.
2. `CalibrationWindow` configures its wiring grid and strict SN validation immediately on `Loaded`.
3. The window does **not** automatically open/probe COM ports and does **not** run broad PeakLogger discovery on open.
4. After the first idle dispatcher turn, a passive detailed USB metadata refresh runs WMI on a worker thread. It may enrich entries with USB serial number/description but never transmits to the device.
5. Sylex FOS API health initialization is deferred briefly so it cannot contend with first-frame layout/input.

## Operator behavior

- Reference thermometer: choose the port and click **Načítať teplotu** when an active read is wanted.
- PeakLogger: click **Pripojiť** for the known host/port; use **Vyhľadať API** only when discovery is actually needed.
- USB refresh still performs detailed metadata enrichment asynchronously.

This also prevents opening FBG calibration from accidentally sending CTH7000 commands to every COM port, which is important while troubleshooting the reference thermometer.

## Diagnostics

The app log now writes a startup timing entry similar to:

```text
FBG kalibrácia: Okno pripravené za 180 ms (ViewModel 42 ms). Bez automatického COM probe/PeakLogger discovery.
```

If opening is still slow, compare total startup time with `ViewModel ... ms`:

- high **ViewModel** time indicates local profile/history/config loading is the next optimization target;
- low ViewModel but high total time indicates WPF/XAML layout/rendering is dominant.
