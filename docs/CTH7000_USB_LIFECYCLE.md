# WIKA CTH7000 USB lifecycle

## Root cause of the apparent thermometer freeze

The CTH7000 front panel is disabled while the instrument is in `SYSTEM:REMOTE`.
The previous desktop polling path entered REMOTE before `MEASURE:CHANNEL?`, but it
kept the instrument in REMOTE between polling samples. The old disposal path also
set its internal disposing flag before trying to call the public LOCAL method, so
`SYSTEM:LOCAL` could be skipped during shutdown/reconnect.

The Lab Control Bridge had a separate older implementation and could still send
legacy `READ?`, even though the active CTH7000 protocol uses the documented
channel query.

## Required sequence

Every normal temperature acquisition now follows this lifecycle as one serialized
USB operation:

```text
*IDN?                    # once per open/reconnect
SYSTEM:REMOTE
MEASURE:CHANNEL? 1       # channel A; use 2 for channel B
SYSTEM:LOCAL             # always attempted from finally
```

Serial settings remain:

- 9600 baud
- 8 data bits
- no parity
- 1 stop bit
- no flow control
- CR command terminator
- 2 ms inter-character delay

## Desktop guarantees

`VotschVc3.App/Thermometers/CTH7000Client.cs` now:

- holds one semaphore across REMOTE -> MEASURE -> LOCAL so another poll, terminal
  command or calibration read cannot interleave inside the measurement session;
- sends `SYSTEM:LOCAL` from `finally` after every measurement attempt;
- retries a failed measurement once after closing/reopening the COM port;
- clears protocol identity/state after reconnect and performs `*IDN?` again;
- sends LOCAL directly during `DisposeAsync` while the serial gate and COM handle
  are still valid instead of calling a public method that rejects operations after
  disposal has started.

Because `ReadChannelAsync` itself owns the complete lifecycle, existing polling,
manual reads and calibration reference reads all get the fix without separate UI
workarounds.

## Bridge guarantees

`VotschVc3.Agent/CTH7000Client.cs` uses the same documented lifecycle. Legacy
bridge configuration containing `readCommand: "READ?"` remains accepted only as a
compatibility alias for channel A; `READ?` is not transmitted to a CTH7000.

Recommended new configuration uses an explicit physical channel:

```json
{
  "portName": "COM4",
  "baudRate": 9600,
  "readCommand": "A"
}
```

Use `"B"` for channel B.

## On-device validation

After installing the build:

1. Connect the CTH7000 by USB and open the thermometer page.
2. Start polling at the normal interval.
3. Verify that the front-panel keys become available again between acquisitions
   instead of remaining locked in REMOTE.
4. Confirm the app log repeatedly shows the order `SYSTEM:REMOTE`,
   `MEASURE:CHANNEL? 1/2`, `SYSTEM:LOCAL`.
5. Disconnect/reconnect USB during polling and confirm the next successful read
   contains a new `*IDN?` before measurement.
6. Stop or close the application while polling and verify the CTH7000 is left in
   LOCAL mode.
7. If the dashboard Bridge is used, verify the thermometer snapshot updates with
   a real temperature and the physical unit is not left in REMOTE after a poll.

## Version

Desktop: `1.76.20`

Bridge: `1.75.4`
