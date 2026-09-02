# 1.76.8 — 2026-09-02

## USB / WIKA CTH7000

- Added process-wide COM-port ownership using `SerialPortLease`.
- Multiple `F100Client` instances can no longer open the same COM port concurrently inside the application.
- Passive diagnostic probing uses the same ownership gate and skips a port currently used by live measurement.
- `UnauthorizedAccessException` from `SerialPort.Open()` is exposed as a dedicated busy-port condition instead of being treated as a normal communication retry.
- Manual `COMx` selection remains available; the selected port can be retried after another process releases it.
- Empty query responses are treated as a timeout/communication failure, preventing false successful measurements.
- Existing reconnect behavior for genuine USB/COM failures remains in place.
- Blocking `SerialPort` I/O continues to run off the WPF UI thread.

## Documentation

- Updated `SKILL.md` with process-wide COM ownership, busy-port handling, and regression requirements.
