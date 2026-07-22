# E-mail pre POL-EKO — MODBUS TCP zápis na SLN 115 SMART

Pripravený dopyt na výrobcu ohľadom diaľkového nastavovania teploty a spúšťania
programov cez MODBUS TCP. Pred odoslaním doplň polia označené `[...]`
(sériové číslo, firmvér, podpis).

**Kontext:** Aplikácia sa na pec pripojí (MODBUS TCP, port 502) a číta meranú
teplotu (funkcia 04, input register). Zápis setpointu / on-off (funkcia 06,
holding registre) pec odmieta MODBUS výnimkou a čítanie holding registrov
(funkcia 03) neodpovedá — verejná dokumentácia popisuje MODBUS TCP na SMART
regulátore len ako monitorovacie rozhranie. Detaily: `PolEkoRegisterMap.cs`
a `PolEkoClient.cs`.

---

**To:** info@pol-eko.com.pl

**Subject:** SLN 115 SMART — MODBUS TCP: writable registers for temperature setpoint / program start?

Dear POL-EKO Support Team,

we operate a **POL-EKO SLN 115 drying oven with the SMART controller**
(serial number: `[fill in]`, firmware version: `[fill in — shown on the
display in the Info menu]`) in our laboratory and integrate it into our own
monitoring and control software over Ethernet.

**What works:** We can connect to the oven via **MODBUS TCP on port 502** and
successfully read the measured chamber temperature using function 04
(READ INPUT REGISTERS), exactly as described in the instruction manual.

**What does not work:** Any attempt to **write** — setting the temperature
setpoint or switching the oven on/off via function 06 (WRITE SINGLE REGISTER)
to holding registers — is rejected by the controller with a MODBUS exception.
Reading holding registers (function 03) is not answered either. This matches
the manual, which describes the MODBUS TCP interface as status monitoring only.

Could you please clarify the following:

1. Is the MODBUS TCP interface on the SLN 115 SMART **read-only by design**,
   or can writing be enabled (a setting, license, or firmware update)?
2. Is there a **documented register map** for the SMART controller including
   writable registers for: temperature setpoint, heating on/off, and
   **selecting/starting a stored program**? If yes, could you send it to us?
3. If remote control over MODBUS is not possible on the SMART controller,
   is it available on the **SMART PRO** controller, and can our unit be
   upgraded?
4. Alternatively, is there a **documented API or protocol** (e.g. the one used
   by LabDesk) that third-party software may use to set the temperature or
   start a program remotely?

Our goal is simply to set the temperature setpoint and start/stop a program
from our own software, while continuing to log the measured temperature.

Thank you in advance for your help.

Best regards,

`[name]`
`[company, address]`
`[phone / e-mail]`
