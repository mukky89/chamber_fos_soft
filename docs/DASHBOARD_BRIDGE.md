# Lab Control Bridge v2 — prepojenie s FOS Dashboardom

`VotschVc3.Agent` je lokálny Windows agent medzi FOS Dashboardom a zariadeniami v laboratórnej sieti. Z lokálneho PC otvára iba odchádzajúce HTTPS spojenie. Dashboard ani webový prehliadač sa nikdy nepripájajú priamo na IP adresy komôr, COM porty ani zdieľané disky.

## Architektúra a source of truth

Bridge v2 používa jednoznačný model vlastníctva dát:

- `chambers.json` v desktopovej aplikácii je source of truth pre konfiguráciu komôr;
- adresár profilov spravovaný `ProfileStore` je source of truth pre profily;
- `bridge.json` obsahuje iba nastavenie spojenia s Dashboardom, bezpečnostné prepínače `AllowControl`, priečinky, teplomery a ďalšie bridge nastavenia;
- Dashboard drží synchronizovanú cache a zmeny zapisuje späť cez príkazy Bridge v2;
- `LockPasswordHash` zostáva iba lokálne a nikdy sa neposiela do Dashboardu.

Pri každom heartbeate agent publikuje `contractVersion = 2`, živé zariadenia, kompletné profily, konfigurácie komôr, nesekretné bridge nastavenia a revízie profilov/komôr.

## Funkcie Bridge v2

- živá teplota, setpoint, RH, RH setpoint, alarmy a stav profilu;
- START/STOP, zmena setpointu a profil start/pause/resume/stop;
- úplná synchronizácia profilov vrátane segmentov, cyklov, metadata, validácie a poznámok;
- vytvorenie, úprava a zmazanie profilu z Dashboardu s uložením do desktopového `ProfileStore`;
- synchronizácia konfigurácie komôr;
- úprava konfigurácie komory z Dashboardu s uložením cez `ChamberConfigStore`;
- nové komory vytvorené v desktop aplikácii sa automaticky objavia v Bridge bez duplicitného ručného zápisu;
- konfiguráciu komory nie je možné meniť počas bežiaceho profilu;
- po bezpečnej zmene konfigurácie agent znovu vytvorí lokálne device runtime spojenia;
- retry doručovania príkazov, ak sa agent preruší po prevzatí príkazu;
- index povolených lokálnych/sieťových priečinkov a bezpečný prenos súborov;
- ASL F100/F150/F250 cez USB virtuálny COM port.

## Príkazy Bridge v2

Dashboard môže zaradiť tieto príkazy:

- `device.setpoint`
- `device.humidity`
- `device.start`
- `device.stop`
- `profile.start`
- `profile.pause`
- `profile.resume`
- `profile.stop`
- `profile.upsert`
- `profile.delete`
- `chamber.upsert`
- `chamber.delete`
- `agent.settings`
- `raw.command` — iba administrátor

Konfiguračné operácie komory a agenta sú na Dashboarde admin-only. Bežné ovládanie zariadenia a profilov zostáva dostupné autentifikovanému používateľovi podľa existujúcich oprávnení.

## Párovanie

1. V Dashboarde otvor **FOS Laboratórium → Lokálny bridge**.
2. Ako administrátor vytvor agenta a jednorazovo skopíruj `lab_...` token.
3. Token vlož do lokálneho `bridge.json` ako `agentKey`.
4. Token nikdy nevkladaj do Gitu. Dashboard ukladá iba jeho SHA-256 hash.
5. `dashboardUrl` musí byť HTTPS URL dostupná z firemnej siete/VPN.

Príklad minimálnej časti `bridge.json`:

```json
{
  "dashboardUrl": "https://dashboard.example.internal",
  "agentKey": "lab_..."
}
```

## Bezpečné ovládanie komôr

Pre každú komoru sa zachováva samostatný `AllowControl`. Nové zariadenie je predvolene read-only (`false`). Po overení IP, portu, protokolu, adresy a kanálov možno ovládanie zapnúť cez Dashboard alebo lokálnu bridge konfiguráciu.

Odporúčaný prvý test je iba čítanie telemetrie. Až potom povoliť ovládanie a otestovať malú bezpečnú zmenu setpointu.

## ASL F100 / USB

Do `thermometers` nastav explicitný COM port a podľa možnosti sériové číslo, aby sa po prehodení USB káblov nezamenili referenčné teplomery:

```json
{
  "id": "f100-sylex",
  "name": "ASL F100 Sylex",
  "portName": "COM6",
  "baudRate": 9600,
  "readCommand": "READ?",
  "serialNumber": "F100-0217",
  "enabled": true
}
```

## Lokálne a sieťové priečinky

Web pracuje iba s aliasom a relatívnou cestou. Absolútna cesta ostáva na lokálnom PC. Výsledná cesta prejde cez `Path.GetFullPath` a musí zostať pod povoleným koreňom. Reparse pointy/junctiony sa pri indexovaní neprechádzajú. UNC je podporované, ak má účet spúšťajúci agenta oprávnenie.

```json
{
  "alias": "CalibrationNAS",
  "path": "\\\\fileserver\\lab\\calibration",
  "writable": true
}
```

Zápis spustiteľných typov (`.exe`, `.dll`, `.ps1`, `.bat`, `.cmd`, `.msi`, …) je blokovaný. Súbor sa najprv uloží do dočasného súboru a potom atomicky presunie.

## Build agenta

Na Windows PC s .NET SDK:

```powershell
git checkout main
git pull --ff-only

dotnet restore
dotnet build -c Release
dotnet test -c Release

dotnet publish src\VotschVc3.Agent\VotschVc3.Agent.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o C:\LabBridge
```

Ak cieľový PC nemá vhodný .NET runtime, publikuj self-contained build:

```powershell
dotnet publish src\VotschVc3.Agent\VotschVc3.Agent.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o C:\LabBridge
```

## Automatické spustenie

Po publikovaní spusti PowerShell ako administrátor:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-lab-bridge-task.ps1 -AgentExe "C:\LabBridge\VotschVc3.Agent.exe"
```

Vznikne Scheduled Task `Sylex Lab Control Bridge` spúšťaný po prihlásení. Windows účet musí mať prístup k COM portom a UNC cestám.

## Stav v desktopovej aplikácii

Desktopová aplikácia sa pokúsi Bridge Agent spustiť, ak ešte nebeží. Preferuje naplánovanú úlohu `Sylex Lab Control Bridge`; inak hľadá `VotschVc3.Agent.exe` vedľa aplikácie, v `LabBridge` alebo vo vývojovom výstupe.

V **Administrácia → Prepojenie s FOS Dashboardom** sa zobrazuje proces agenta, prijatie heartbeat-u, cieľová URL, čas posledného stavu, verzia a posledná chyba. Agent zapisuje lokálny stav atomicky do `Dokumenty\Lab Control\bridge-status.json`.

Dashboard Bridge v2 používa kompaktný snapshot endpoint a Laboratory Control Center obnovuje živý stav približne každé 2 sekundy.

## Acceptance pre-flight na Windows

Pred povolením `AllowControl` spusti na laboratórnom PC:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify-dashboard-bridge-v2.ps1
```

Skript bez vypísania `agentKey` skontroluje:

- `bridge.json` a jeho JSON formát;
- HTTPS Dashboard URL a sieťovú/TLS/HTTP dosiahnuteľnosť;
- prítomnosť pairing tokenu iba podľa formátu, bez zobrazenia hodnoty;
- lokálne cesty ku konfigurácii komôr a profilovej knižnici;
- Scheduled Task `Sylex Lab Control Bridge` a existenciu jeho executable;
- `bridge-status.json`, `Running`, `DashboardReachable`, čerstvosť statusu a posledného heartbeat-u;
- reportovanú verziu agenta.

Ak skončí `FAIL > 0`, remote control ešte nepovoľuj. `PRE-FLIGHT READY` znamená, že infraštruktúrna časť je pripravená na manuálnu kontrolu `contractVersion=2`, live dát a bezpečný test jednej komory.

## Rollout — body 1 až 11

### 1. Nasadiť Dashboard Bridge v2

Najprv nasadiť `sylex_fos_dashboard` s backendom a Laboratory Control Center v2. Starý agent môže počas tejto chvíle zostať pripojený, ale nové konfiguračné funkcie sa aktivujú až pri `contractVersion >= 2`.

### 2. Overiť Dashboard server

Po deployi skontrolovať, že aplikácia štartuje bez chyby a existujú endpointy `/api/laboratory/snapshot`, `/profiles` a `/chambers`.

### 3. Aktualizovať `chamber_fos_soft`

Na laboratórnom Windows PC aktualizovať aplikáciu/agent na Bridge v2 a publikovať `VotschVc3.Agent`.

### 4. Zachovať pairing

Pred výmenou build-u zálohovať lokálne `bridge.json`. Zachovať existujúci `agentKey`; nie je potrebné vytvárať nový token, ak sa nemení agent.

### 5. Spustiť Bridge v2 read-only

Po prvom štarte ponechať `AllowControl=false` pre všetky komory. Overiť, že Dashboard ukazuje agenta ako online a `contractVersion = 2`.

### 6. Overiť synchronizáciu živých dát

V Dashboarde overiť pre každú komoru aktuálnu teplotu, setpoint, RH/RH setpoint podľa typu komory, alarm a stav zariadenia. Hodnoty musia korešpondovať s desktop aplikáciou/komorou.

### 7. Overiť synchronizáciu profilov

Porovnať počet/názvy profilov s desktop profilovou knižnicou. Z Dashboardu vytvoriť bezpečný testovací profil, počkať na dokončenie príkazu a overiť, že sa objaví aj lokálne. Profil potom upraviť a zmazať z Dashboardu a overiť rovnaký výsledok lokálne.

### 8. Overiť konfiguráciu komôr

Na bezpečnom zariadení upraviť neprevádzkový parameter, napr. názov alebo polling interval. Overiť zápis do `chambers.json`, nový heartbeat a zobrazenie zmeny späť na Dashboarde. `LockPasswordHash` sa nesmie objaviť v sieťovom payload-e ani Dashboard DB.

### 9. Povoliť remote control jednej komory

Až po overení správneho `deviceId`, IP, portu, protokolu a bezpečnostných limitov nastaviť `AllowControl=true` iba pre jednu testovaciu komoru.

### 10. Funkčný ovládací test

Na komore bez kritického testu vykonať malú bezpečnú zmenu setpointu, napr. o 1 °C v rámci schválených limitov. Overiť command status `queued → delivered/running → completed`, reakciu fyzickej komory a spätnú telemetriu. START/STOP profilového testu skúšať až po tomto kroku.

### 11. Akceptačný end-to-end test a ostré povolenie

Pred ostrou prevádzkou potvrdiť:

- agent je stabilne online;
- `contractVersion = 2`;
- live hodnoty sedia s lokálnou aplikáciou;
- profily sa obojsmerne zapisujú a nemažú nesprávne;
- konfigurácia komory sa obojsmerne synchronizuje;
- remote konfigurácia je admin-only;
- `AllowControl` je povolený iba na schválených zariadeniach;
- príkaz po dočasnom výpadku agenta nie je stratený a vie sa retry-nuť;
- lokálny profil pri strate Dashboardu pokračuje bez automatickej zmeny setpointu;
- STOP a alarm správanie bolo overené podľa lokálnych bezpečnostných pravidiel;
- po reštarte Windows sa Scheduled Task a heartbeat obnovia automaticky.

Až po úspešnom bode 11 povoľ `AllowControl` na ďalších produkčných komorách.

## Prevádzkové pravidlá

- Dashboard/browser sa nikdy nepripája priamo na 10.88.x.x adresy komôr.
- Najprv read-only, potom jedna testovacia komora, až následne ďalšie zariadenia.
- Desktop WPF a samostatný agent nemajú súčasne otvárať rovnaký COM port.
- Pred ostrým profilom over limity a digitálny start kanál na bezpečnej teplote.
- Pri strate Dashboardu agent nemení setpoint; lokálne bežiaci profil pokračuje.
- `bridge.json`, `agentKey` a lokálne lock hash hodnoty necommitovať do repozitára.
