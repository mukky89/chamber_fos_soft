# Lab Control Bridge — prepojenie s FOS Dashboardom

`VotschVc3.Agent` je lokálny Windows agent medzi cloudovým FOS Dashboardom a
zariadeniami v laboratórnej sieti. Z lokálneho PC otvára iba odchádzajúce HTTPS
spojenie; komory, USB/COM porty ani zdieľané disky sa nevystavujú internetu.

## Podporované funkcie

- 3 Vötsch/Weiss komory cez ASCII-2/SIMSERV TCP;
- 2 SIKA TP cez ich REST API;
- ASL F100/F150/F250 cez USB virtuálny COM port;
- živá telemetria, alarm a stav profilu;
- setpoint, START/STOP, profil start/pause/resume/stop a admin raw terminal;
- index povolených lokálnych/sieťových priečinkov;
- stiahnutie lokálneho súboru cez web a bezpečný zápis súboru z webu na disk;
- profilový runner používa rovnaké `VotschVc3.Core` ako desktopová aplikácia.

## Párovanie a spustenie

1. V Dashboarde otvor **Laboratórium FOS → Lokálny bridge**, ako admin vytvor
   agenta a jednorazovo skopíruj `lab_...` token.
2. Publikuj agenta:

   ```powershell
   dotnet publish src\VotschVc3.Agent\VotschVc3.Agent.csproj -c Release -r win-x64 --self-contained false -o publish\LabBridge
   ```

3. Skopíruj `bridge.example.json` do
   `%USERPROFILE%\Documents\Lab Control\bridge.json`, nastav `dashboardUrl` a
   vlož `agentKey`. Token nevkladaj do Gitu; server ukladá iba jeho SHA-256 hash.

## Zariadenia a bezpečné ovládanie

Predvolená konfigurácia obsahuje tri komory a dve SIKA. POL‑EKO sa nepoužíva a
v zozname nie je. Každé zariadenie má `allowControl: false`, takže agent najskôr
iba číta. Ovládanie zapni až po overení IP, portu, adresy a štartovacieho kanála:

```json
{ "id": "vc3", "host": "10.88.5.181", "port": 1080, "startChannelIndex": 1, "allowControl": true }
```

## ASL F100 / USB

Do `thermometers` pridaj explicitný COM port, aby sa po prehodení USB káblov
nezamenili referenčné teplomery:

```json
{ "id": "f100-sylex", "name": "ASL F100 Sylex", "portName": "COM6", "baudRate": 9600, "readCommand": "READ?", "serialNumber": "F100-0217", "enabled": true }
```

## Lokálne a sieťové priečinky

Web pracuje iba s aliasom a relatívnou cestou. Absolútna cesta ostáva na PC.
Každá výsledná cesta prejde cez `Path.GetFullPath` a musí zostať pod povoleným
koreňom. Reparse pointy/junctiony sa pri indexovaní neprechádzajú. UNC cesta je
podporovaná, ak má účet spúšťajúci agenta oprávnenie:

```json
{ "alias": "CalibrationNAS", "path": "\\\\fileserver\\lab\\calibration", "writable": true }
```

Zápis spustiteľných typov (`.exe`, `.dll`, `.ps1`, `.bat`, `.cmd`, `.msi`, …)
je blokovaný. Súbor sa najprv uloží do dočasného súboru a potom atomicky presunie.

## Automatické spustenie

Po publikovaní spusti ako administrátor:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-lab-bridge-task.ps1 -AgentExe "C:\LabBridge\VotschVc3.Agent.exe"
```

Vznikne Scheduled Task `Sylex Lab Control Bridge` spúšťaný po prihlásení. Daný
Windows účet musí mať prístup k COM portom a UNC cestám.

## Prevádzkové pravidlá

- Najprv testovať iba read-only.
- STOP zostáva potvrdený webovým UI a auditovaný.
- Desktop WPF a agent nemajú súčasne otvárať rovnaký COM port.
- Pred ostrým profilom overiť limity a digitálny start kanál na bezpečnej teplote.
- Pri strate Dashboardu agent nemení setpoint; lokálne bežiaci profil pokračuje.
