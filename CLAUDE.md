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
- `src/VotschVc3.App/` — WPF UI
  - `Mvvm/` — ObservableObject, RelayCommand, AsyncRelayCommand
  - `ViewModels/` / `Views/` — dvojica na obrazovku (ShellViewModel hostí 2× ChamberViewModel)
  - `Themes/Styles.xaml`

**Pravidlo:** nová logika ide do `Core` (testovateľná), `App` je len zobrazenie a binding.

## Pravidlá pri práci
- Pred zmenou v `Core` spusti `dotnet test tests/VotschVc3.Core.Tests`.
- V repe už sú skills `.claude/skills/wpf` a `.claude/skills/wpf-ux-ui` pre XAML/MVVM vzory — riaď sa nimi, netreba ich tu opakovať.
- ⚠️ Softvér ovláda reálne zariadenie dosahujúce extrémne teploty. Zmeny v setpointoch, mapovaní kanálov, alarm limitoch alebo watchdogu rob opatrne a over na bezpečných hodnotách.
- Komora 1 = VC3 (teplota + vlhkosť), Komora 2 = VT3 (len teplota) — nezamieňaj.
- Konfigurácia a heslá sa **neukladajú do repa** — persistujú do `Dokumenty/VotschVc3/` (chambers.json, users.json, SHA-256). Necommituj testovacie IP adresy ani heslá zákazníkov.
- Po významnej zmene: záznam do `CHANGELOG.md` (Keep a Changelog, slovenčina) + zváž bump verzie (zobrazuje sa v README aj v appke).
- README a CHANGELOG sú po slovensky — drž sa toho aj v texte smerom k používateľovi.

## Časté príkazy

```
dotnet build VotschVc3.sln
dotnet test tests/VotschVc3.Core.Tests
dotnet run --project src/VotschVc3.App
```
