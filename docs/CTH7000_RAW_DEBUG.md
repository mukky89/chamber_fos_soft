# WIKA CTH7000 RAW USB debug

Desktop version: **1.76.21**

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
- inter-character delay: 0 / 1 / 2 / 5 / 10 ms
- RX timeout: 0.5 / 1 / 2 / 4 / 8 / 12 s
- optional purge of stale RX bytes before a query

The initial settings intentionally match the current production path: `9600`, DTR on, RTS on, CR, 2 ms pacing, 8 s timeout.

## Where to open it

FBG calibration window → **Referenčný teplomer** → **RAW debug**.

It is intentionally disabled as an operational action during a running calibration because it must take exclusive ownership of the reference thermometer COM port.

## First test after a physical power cycle

Do not press **Načítať teplotu** first. Open RAW debug and use this sequence manually:

1. Select the real CTH7000 COM port in the calibration screen.
2. Open **RAW debug**.
3. Keep the initial serial settings.
4. Press **Otvoriť COM**.
   - The transcript must explicitly say `0 automatických TX bajtov`.
   - Check that the physical thermometer still responds to its front-panel keys.
5. Press only `*IDN?`.
   - Copy the complete ASCII + HEX transcript.
   - If there is no RX at all, stop here. Do not test REMOTE yet.
6. If IDN is good, press `SYSTEM:REMOTE`.
   - Note whether the display indicates remote mode and whether keys are disabled.
7. Press `MEASURE:CHANNEL? 1` for probe A.
   - Record whether RX arrives, how many bytes arrive and after how many milliseconds.
8. Press `SYSTEM:LOCAL` immediately afterwards.
9. If the instrument does not recover, press **LOCAL + zavrieť COM**.

## Isolation matrix

If `*IDN?` works with the initial settings but measurement still locks the unit, repeat after a physical reset with one variable changed at a time:

1. DTR off, RTS off; CR; 2 ms.
2. DTR on, RTS off; CR; 2 ms.
3. DTR off, RTS on; CR; 2 ms.
4. Terminator CRLF instead of CR.
5. Inter-character delay 5 ms.
6. Inter-character delay 10 ms.

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

## Evidence that motivated RAW mode

Application logs from the failing hardware show three important states:

1. `*IDN?` has successfully returned a real CTH7000 identity in earlier attempts.
2. `SYSTEM:REMOTE` was then sent and `MEASURE:CHANNEL? 1/2` could time out with zero measurement response.
3. In at least one retry, an IDN frame arrived late enough to be consumed while a measurement command was waiting, proving that command/response timing must be observed directly rather than inferred through the normal retry layer.

The first goal of RAW mode is therefore not to guess a new production protocol. It is to produce one deterministic transcript from a physical CTH7000 so the production path can be corrected from measured behavior.
