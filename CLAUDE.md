# CLAUDE.md — chamber_fos_soft (VotschVc3)

Riadiaci softvér pre klimatické komory Vötsch/Weiss (kontrolér S!MPAC/SIMPAC, protokol ASCII-2) a PolEko, používaný pri kalibrácii/testovaní FBG senzorov v SYLEX. Číta aj presné teplomery ASL F100 cez USB.

## Stack
- .NET 8, WPF (`net8.0-windows`), MVVM, tmavá téma
- xUnit testy: `tests/VotschVc3.Core.Tests`
- CI: `.github/workflows/build.yml`

## Architektúra
- `src/VotschVc3.Core/` — jadro, **platform-nezávislé, testovateľné, žiadne WPF referencie**
  - `Protocol/` — Ascii2Protocol, ChamberReading, DigitalChannels
  - `Communication/` — ITransport, TcpTransport, ChamberClient, `Modbus/`, `PolEko/`
  - `Profiles/` — TestProfile, ProfileSegment, ProfileRunner, ProfileStore, ChamberConfig
  - `Recording/` — CsvRecorder, RecordingReader
  - `Security/` — User, UserStore, AuditLog
  - `Thermometers/` — F100Protocol
  - `Notifications/` — EmailNotifier, EmailSettings
  - `Calibration/` — FBG calibration orchestration, PeakLogger, production metadata abstraction and Sylex FOS API client
- `src/VotschVc3.App/` — WPF UI
  - `Mvvm/` — ObservableObject, RelayCommand, AsyncRelayCommand
  - `ViewModels/` / `Views/` — dvojica na obrazovku (ShellViewModel hostí 2× ChamberViewModel)
  - `Calibration/SylexFosCalibrationIntegration.cs` — bezpečné automatické enrichment produkčných FBG metadata cez centrálnu API
  - `Themes/Styles.xaml`

**Pravidlo:** nová logika ide do `Core` (testovateľná), `App` je len zobrazenie a binding.

## Sylex FOS API — povinná integračná architektúra

FBG calibration používa centrálnu `Sylex-FOS-API`; Chamber aplikácia **nesmie** otvárať vlastné SQL spojenia na ISYS alebo DBFOS.

Aktuálny stabilný endpoint:

```text
GET /api/v1/calibrations/fbg/context?serialNumber=XXXXXX%2FXXXX
```

Konfigurácia workstationu:

```text
SYLEX_FOS_API_URL=http://localhost:5080     # default, nastav len ak API beží inde
SYLEX_FOS_API_KEY=<raw key for chamber-fos>
```

Minimálny scope klienta `chamber-fos` je `calibrations.read`. Raw key sa nikdy necommitne ani nezapisuje do calibration JSON/logov.

Enrichment je **fail-open pre metadata, nie pre bezpečnostnú logiku**: ak API nefunguje, ProductDescription/Customer/Order ostanú ručne editovateľné a kalibrácia môže pokračovať. API chyba nikdy nesmie meniť chamber setpoint, profil, alarmy, watchdog alebo PeakLogger meranie.

Produkčné SN obsahuje `/`, preto sa prenáša ako query parameter, nie route segment. Podrobnosti: `docs/SYLEX_FOS_API.md`.

## Pravidlá pri práci
- Pred zmenou v `Core` spusti `dotnet test tests/VotschVc3.Core.Tests`.
- V repe už sú skills `.claude/skills/wpf` a `.claude/skills/wpf-ux-ui` pre XAML/MVVM vzory — riaď sa nimi, netreba ich tu opakovať.
- ⚠️ Softvér ovláda reálne zariadenie dosahujúce extrémne teploty. Zmeny v setpointoch, mapovaní kanálov, alarm limitoch alebo watchdogu rob opatrne a over na bezpečných hodnotách.
- Komora 1 = VC3 (teplota + vlhkosť), Komora 2 = VT3 (len teplota) — nezamieňaj.
- Konfigurácia a heslá sa **neukladajú do repa** — persistujú do `Dokumenty/VotschVc3/` alebo bezpečného workstation secret mechanizmu. Necommituj testovacie IP adresy, API keys ani heslá zákazníkov.
- Po významnej zmene: záznam do `CHANGELOG.md` (Keep a Changelog, slovenčina) + zváž bump verzie (zobrazuje sa v README aj v appke).
- README a CHANGELOG sú po slovensky — drž sa toho aj v texte smerom k používateľovi.

## Časté príkazy

```
dotnet build VotschVc3.sln
dotnet test tests/VotschVc3.Core.Tests
dotnet run --project src/VotschVc3.App
```
