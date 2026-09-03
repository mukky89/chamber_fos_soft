# FBG calibration startup acceptance checks

Version: **1.76.23**

Use these checks on the production workstation after installing the build.

1. From the dashboard press the FBG calibration button for a chamber.
2. The calibration window should become visible and interactive without waiting for thermometer or PeakLogger timeouts.
3. Do not press any thermometer button. Verify that opening the workspace alone does not put the WIKA CTH7000 into REMOTE mode.
4. The reference thermometer port list should appear immediately from the Windows COM-port list. USB serial/description may be enriched shortly afterwards by the passive background WMI scan.
5. PeakLogger must not run broad API discovery automatically. Use **Pripojiť** for the configured host/port or **Vyhľadať API** only when discovery is needed.
6. Sylex FOS API status may update shortly after the window appears, but the UI must remain usable while it does.
7. In the app log find the entry:

```text
FBG kalibrácia: Okno pripravené za X ms (ViewModel Y ms). Bez automatického COM probe/PeakLogger discovery.
```

Interpretation:

- large `Y` means local ViewModel construction (history/profile/config reads) is the next optimization target;
- small `Y` but large `X` means XAML/layout/rendering is the remaining startup cost.

8. Select the CTH7000 port and press **Načítať teplotu**. The calibration workspace keeps thermometer polling disabled so this remains an explicit one-shot operation.
9. Close and reopen the same chamber calibration workspace. The existing hidden window is reused and should appear essentially immediately.
