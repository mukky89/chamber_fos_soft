# Integrácia zariadení SIKA a Vötsch

Kanonický technický prehľad komunikácie, webových rozhraní, teplotných logov a
dokončovacích e-mailov. SIKA zistenia boli overené na `10.88.6.28`, TP OS
`27.41`, z Chrome HAR záznamov z 24. 8. 2026 a read-only HTTP dotazov.

## Web zariadenia

Každá karta SIKA alebo Vötsch ponúka príkaz **Web zariadenia**. Otvára
`http://<host>/` v predvolenom prehliadači. Nepoužíva komunikačný port:

- Vötsch ASCII-2 komunikuje typicky na TCP `2049`, web používa HTTP 80;
- SIKA REST API môže byť na 80 alebo 8081, web overeného zariadenia je na 80;
- POL-EKO nemá tlačidlo webu, kým nebude jeho webové rozhranie overené.

Príkaz `ChamberViewModel.OpenDeviceWebCommand` je dostupný v klasickej
nástennke, profesionálnej karte a detaile zariadenia.

## SIKA TP Premium HTTP API

SIKA používa HTTP endpointy pod `/ajax/`. Requesty jedného zariadenia musia
zostať serializované cez `SikaTpClient._ioGate`: embedded server pri paralelných
requestoch vracal sporadické 404 alebo staré odpovede.

| Účel | Endpoint |
| --- | --- |
| identifikácia | `GET /ajax/getInfoReport` |
| teplota, setpoint a gradient | `GET /ajax/getGradientInfo` |
| čítanie registra | `GET /ajax/getRegister?register=<name>` |
| zápis registra | `GET /ajax/setRegister?register=<name>&value=<value>` |
| spustenie úlohy | `GET /ajax/startCurrentTask` |
| zastavenie úlohy | `GET /ajax/stopCurrentTask` |
| zoznam logov | `GET /ajax/getTaskLog` |
| dáta logu | `GET /ajax/getTaskLogs?taskid=<ID>` |
| certifikačné dáta | `GET /ajax/getTaskLogCertData?taskid=<ID>` |

Kľúčové registre:

| Register | Význam |
| --- | --- |
| `TRset_TR` | nameraná referenčná teplota |
| `TRset_SP` | aktívny setpoint |
| `Task_SetPointList` | setpoint EasyMode úlohy |
| `System_ReglerOnOff` | regulátor 0/1 |
| `Com_ExternWriteFlag` | povolenie vzdialeného zápisu 0/1 |
| `TRset_LoggingOnOff` | stav logovania teploty |

## Remote Control

Remote Control sa zapína na SIKA zariadení. `Com_ExternWriteFlag` na overenom
zariadení vracal `1` pri povolenom ovládaní. Settings payload obsahoval aj
`RemoteOption: "Serial"`; rozhodujúci runtime signál pre zápis je register.

Aplikácia teraz:

- ponecháva read-only monitoring aj pri hodnote `0`;
- pred START, STOP, setpointom a profilom overuje flag;
- pri `0` neposiela zápis a jasne vyžiada zapnutie Remote Control na zariadení;
- po zmene na `1` obnoví ovládanie bez reštartu;
- neúspešné čítanie flagu nepovažuje za súhlas so zápisom.

Kontrola je implementovaná v `SikaTpClient`: každý setpoint, START, STOP a
mutačný príkaz zo surového terminálu číta flag tesne pred zápisom. Stav sa
zároveň obnovuje počas pollingu a ovládacie príkazy vo WPF sa deaktivujú.

## Interné logy SIKA

`GET /ajax/getTaskLog` vracia `values[]` s `ID`, názvom, typom, stavom, verziou,
Unix časmi `Start`/`End`, definíciou úlohy, setpointmi, výdržami, gradientmi,
cyklami a mapovaním kanálov. Overené zariadenie vrátilo 4 logy (ID 1–4). ID 4:

- úloha `-20TO100`;
- začiatok `2026-08-21 15:21:11 +02:00`;
- koniec `2026-08-22 04:15:20 +02:00`;
- trvanie `12:54:09`.

`GET /ajax/getTaskLogs?taskid=4` vrátil dve série po 169 732 bodov:

- `TRset_SP` – setpoint;
- `TRset_TR` – nameraná teplota.

Bod je `{ "v": <hodnota>, "t": <sekundy od začiatku> }`; odpoveď mala asi
7,9 MB. Absolútny čas je `Start + t sekúnd`. CSV:

```csv
Čas;Sekundy;Setpoint °C;Teplota SIKA °C
2026-08-21 15:21:11;0;-20;30
```

Implementácia je v `SikaTaskLogs`, `SikaTpClient.GetTaskLogsAsync` a
`GetTaskLogDataAsync`; UI je v záložke **Záznam → Interné logy SIKA**. CSV
zachová všetky body a sťahovanie nezablokuje UI. Budúci graf sa môže preriediť.
`getTaskLogCertData?taskid=4` vrátil chybu DB, hoci teplotné
série boli kompletné, preto export CSV nesmie závisieť od certifikačných dát.

## Mapa implementácie

| Funkcia | Implementácia |
| --- | --- |
| URL webu a browser príkaz | `ChamberViewModel.DeviceWebUrl`, `OpenDeviceWebCommand` |
| SIKA URL buildery a registre | `SikaRestApiProtocol` |
| serializované HTTP a Remote gate | `SikaTpClient` |
| modely/parsing/CSV interných logov | `SikaTaskLogs.cs` |
| stav Remote a príkazy logov vo WPF | `ChamberViewModel` |
| SIKA log UI | `ChamberView.xaml`, záložka **Záznam** |
| web tlačidlá | `HomeView.xaml`, `ProfessionalDeviceCard.xaml`, `ChamberView.xaml` |
| e-mailová konfigurácia a transport | `EmailSettings*`, `EmailNotifier`, `EmailSender` |
| dokončovacia HTML šablóna/graf/príloha | `ProfileCompletionEmail` |
| profilový CSV záznam | `ProfileTemperatureLog` |

Relevantné automatizované testy sú v `SikaTpClientTests` a
`ProfileCompletionEmailTests`. Pokrývajú blokovanie zápisu pri Remote Control
OFF, overené log endpointy a parsing, predvolených adresátov, HTML graf a CSV
prílohu.

## Lokálne logy aplikácie

Nezávisle od SIKA logov aplikácia zapisuje vlastné CSV:

- profilové behy: `Documents\Lab Control\Profilelog`;
- priebežné záznamy: `Documents\Lab Control\Recordings`.

Vlastný log je primárny pre behy riadené aplikáciou. SIKA log slúži na audit,
staršie merania a obnovu dát po výpadku PC.

## E-mail po dokončení profilu

Admin zapína/vypína notifikácie a upravuje adresátov aj transport. Predvolení
adresáti: `mmucka@sylex.sk; tsalat@sylex.sk; mplevka@sylex.sk`.

Podľa `sylex_fos_dashboard` sa prednostne používa Brevo HTTP API
`https://api.brevo.com/v3/smtp/email` s hlavičkou `api-key` (HTTPS/443).
Záloha je SMTP `smtp-relay.brevo.com`, port `587`, STARTTLS. Predvolený
odosielateľ je `no-reply@sylex.sk` a musí byť v Brevo overený. API kľúč,
SMTP login a SMTP key sa nezapisujú do repozitára; zadávajú sa v Admin zóne.

E-mail obsahuje zariadenie, profil/frontu, začiatok, koniec, trvanie, stav
vypnutia výkonu, graf setpoint/nameraná teplota a CSV prílohu. Implementácia je
v `ProfileCompletionEmail`, `EmailNotifier` a `EmailSender`. Chyba e-mailu ani
logu nesmie prerušiť riadenie alebo zmeniť výsledok profilu.

## Overovanie zmien

1. Nepridávať paralelné SIKA requesty.
2. Parsovanie testovať na JSON fixtures, nie živými zápismi.
3. Na živom zariadení začínať read-only endpointmi.
4. Mutačné testy robiť iba so súhlasom operátora a zapnutým Remote Control.
5. Spustiť `dotnet test VotschVc3.sln --configuration Release` a
   `dotnet build VotschVc3.sln --configuration Release`.
