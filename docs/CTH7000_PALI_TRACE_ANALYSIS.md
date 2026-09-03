# WIKA CTH7000 – validated Pali trace

Production trace from 2026-09-03 confirmed that the unit on COM7 responds reliably with the Pali-compatible sequence:

1. 9600 baud, 8N1, no flow control, DTR/RTS enabled
2. CR terminator
3. 25 ms inter-character pacing
4. `SYSTEM:REMOTE`
5. wait at least 1 s
6. `MEASURE:CHANNEL? 1` → `1,24.707,"CEL"`
7. `MEASURE:CHANNEL? 2` → `2,NoProbe,"CEL"`
8. `*IDN?` → `WIKA,CTH7000,000000,V1.0,01/05/2013`
9. `SYSTEM:LOCAL`

A fresh-open `*IDN?` sent before `SYSTEM:REMOTE` with 2 ms pacing timed out with zero received bytes after 8 s. This trace therefore supersedes the previous production assumption that identification should be the first query on this hardware/firmware combination.
