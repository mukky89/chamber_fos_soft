# WIKA CTH7000 RAW USB debug

Desktop version: **1.76.22**

## Why this mode exists

The production CTH7000 client intentionally automates protocol lifecycle, retry and reconnect. That is useful for normal operation but makes a hardware/protocol failure hard to isolate because multiple actions can happen after one click.

The RAW debug mode is the opposite:

- it owns the selected COM port exclusively;
- it disconnects the normal thermometer client before taking the port;
- opening the COM port transmits **zero bytes**;
- there is no automatic `*IDN?`, `SYSTEM:REMOTE`, measurement, retry or reconnect;
- every button equals exactly one explicit operator action;
- TX and RX are shown both as visible ASCII and hexadecimal bytes;
- elapsed time, byte count and timeout state are recorded;
- serial-line variables can be changed before opening the port.

## Available serial variables

- baud: 9600 / 19200 / 38400 (start with 9600)
- 8 data bits, no parity, 1 stop bit
- flow control: none
- DTR: on/off
- RTS: on/off
- terminator: CR / CRLF / LF
- inter-character delay: 0 / 1 / 2 / 5 / 10 / **25** / 50 ms
- RX timeout: 0.5 / 1 / 2 / 4 / 8 / 12 s
- optional purge of stale RX bytes before a query

The initial settings intentionally match the current production path: `9600`, DTR on, RTS on, CR, 2 ms pacing, 8 s timeout.

## AutoOptical / Pali evidence

The imported historical calibration application in `mukky89/Auto_calibrator_Pali` contains the original WIKA serial implementation in `SensTemp/WikaTempProbe.py` and `SensTemp/TemperatureProbe.py`.

That code uses:

- 9600 baud
- 8 data bits
- no parity
- 1 stop bit
- XON/XOFF disabled
- RTS/CTS disabled
- DSR/DTR flow control disabled
- CR terminator
- **25 ms delay after every transmitted byte**
- about **666 ms wait after a command** before reading accumulated RX data
- about 1 s wait for `*IDN?`

The normal measurement connection sequence in that code is important: `connect()` opens the serial port and immediately sends `SYSTEM:REMOTE`. A normal measurement then sends `MEASURE:CHANNEL? 1` or `2`. `*IDN?` is available as a diagnostic method but is not part of the ordinary measurement cycle.

The RAW debug window therefore has a **Pali / AutoOptical preset** which sets 9600, CR, 25 ms pacing, 8 s timeout and purge-before-query. The selected DTR/RTS line state remains explicit in the UI; the old Python code disables hardware flow-control modes but does not explicitly drive those line states low.

## Where to open it

FBG calibration window → **Referenčný teplomer** → **RAW debug**.

It is intentionally disabled as an operational action during a running calibration because it must take exclusive ownership of the reference thermometer COM port.

## Test A — current production-compatible RAW test

After a physical power cycle, do not press **Načítať teplotu** first.

1. Select the real CTH7000 COM port in the calibration screen.
2. Open **RAW debug**.
3. Keep the initial serial settings.
4. Press **Otvoriť COM**.
   - The transcript must explicitly say `0 automatických TX bajtov`.
5. Press only `*IDN?`.
6. If IDN is good, press `SYSTEM:REMOTE`.
7. Press `MEASURE:CHANNEL? 1` for probe A.
8. Press `SYSTEM:LOCAL` immediately afterwards.
9. If the instrument does not recover, press **LOCAL + zavrieť COM**.

## Test B — reproduce AutoOptical / Pali behaviour

This test should be run after another physical reset so no previous communication state contaminates the result.

1. Open **RAW debug**.
2. Press **Pali / AutoOptical preset**.
3. Verify the transcript reports `9600`, `CR`, `25 ms` pacing.
4. Press **Otvoriť COM**.
5. **Do not send `*IDN?` first.**
6. Press `SYSTEM:REMOTE` once.
7. Wait at least **1 second**. The old code waited about 666 ms before returning from the command.
8. Press `MEASURE:CHANNEL? 1`.
9. Copy the entire transcript.
10. Press `SYSTEM:LOCAL` or **LOCAL + zavrieť COM**.

If Test B returns a real measurement while the 2 ms test returns 0 bytes, the production client should be changed to the proven slower pacing instead of continuing to tune parsers or retry logic.

If Test B still returns 0 bytes, continue with DTR/RTS and terminator isolation below.

## Isolation matrix

Repeat after a physical reset with one variable changed at a time:

1. Pali preset: DTR on, RTS on; CR; 25 ms.
2. DTR off, RTS off; CR; 25 ms.
3. DTR on, RTS off; CR; 25 ms.
4. DTR off, RTS on; CR; 25 ms.
5. Terminator CRLF instead of CR; 25 ms.
6. Inter-character delay 50 ms.
7. Return to 2 ms only as the production/manual comparison case.

Never change several variables at once; otherwise a successful combination does not reveal which signal/timing mattered.

## Reading the transcript

Example:

```text
08:40:11.100  TX ASCII    *IDN?<CR>
08:40:11.100  TX HEX      2A 49 44 4E 3F 0D
08:40:11.165  RX ASCII    WIKA,CTH7000,000000,V1.0,01/05/2013<CR><LF>
08:40:11.165  RX HEX      57 49 4B 41 ... 0D 0A
08:40:11.165  RESULT      RX=... B · 65 ms; timeout=False
```

A `RX TIMEOUT 0 B` is materially different from a parser error: it means no bytes reached the application during the selected timeout.

The debugger also drains a short quiet interval after the first received bytes. This lets it capture complete non-standard frames instead of assigning a late tail of one response to the next command.

## Safety / recovery controls

- **Purge RX/TX**: explicitly clears serial buffers.
- **Listen 2 s**: receives without transmitting anything; useful for detecting delayed or unsolicited data.
- **SYSTEM:LOCAL**: sends only that command and leaves the port open.
- **LOCAL + zavrieť COM**: best-effort LOCAL with the currently selected serial framing, then releases the COM port.
- **Hard close**: closes the COM port without any additional TX; use it if even sending LOCAL is undesirable during an experiment.
- Closing the debug window automatically attempts LOCAL before releasing an open port.

## Evidence from the failing chamber-fos run

The latest physical-device RAW transcript shows:

1. COM7 opens successfully and the RAW session sends no automatic bytes.
2. Three separate `*IDN?` attempts at 9600 / CR / 2 ms each time out after about 8 s with **0 received bytes**.
3. `SYSTEM:REMOTE` can be transmitted, but `MEASURE:CHANNEL? 1` still times out with **0 received bytes**.
4. Therefore the current failure happens below parsing: there is no response frame to parse.

This is why the next comparison is the historical 25 ms AutoOptical/Pali transmitter behaviour rather than another parser change.
