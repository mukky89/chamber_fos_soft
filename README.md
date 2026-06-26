# Vötsch VC3 — riadiaci softvér klimatickej komory

Moderná **WPF (.NET 8) MVVM** aplikácia na ovládanie klimatickej komory
**Vötsch / Weiss VC3** (kontrolér S!MPAC / SIMPAC) cez **Ethernet** pomocou
textového **ASCII-2** protokolu.

Umožňuje:

- pripojiť sa ku komore cez TCP/IP,
- **čítať** namerané hodnoty (teplota, vlhkosť, digitálne kanály) v reálnom čase,
- **nastavovať** setpointy teploty a vlhkosti,
- ovládať **digitálne kanály** (vrátane štartovacieho „system on"),
- spúšťať **teplotné/vlhkostné profily** s rampami a platami (plateaus) –
  riadené z PC, takže nezávisia od programovej pamäte komory,
- **zaznamenávať** priebeh do CSV s časovou pečiatkou,
- posielať **ľubovoľné príkazy** cez surový terminál (na kalibráciu a vendor
  príkazy ako programy či hodiny).

> ⚠️ **Bezpečnosť:** softvér ovláda reálne zariadenie, ktoré môže dosahovať
> extrémne teploty. Pred ostrým nasadením si over správanie na bezpečných
> hodnotách a skontroluj mapovanie kanálov (viď *Kalibrácia*).

---

## Architektúra

```
VotschVc3.sln
├─ src/
│  ├─ VotschVc3.Core/           ← jadro, platform-nezávislé (net8.0), testovateľné
│  │  ├─ Protocol/              Ascii2Protocol, ChamberReading, DigitalChannels
│  │  ├─ Communication/         ITransport, TcpTransport, ChamberClient, settings
│  │  ├─ Profiles/              ProfileSegment, TestProfile, ProfileRunner
│  │  └─ Recording/             CsvRecorder
│  └─ VotschVc3.App/            ← WPF UI (net8.0-windows), MVVM, tmavá téma
│     ├─ Mvvm/                  ObservableObject, RelayCommand, AsyncRelayCommand
│     ├─ ViewModels/            MainViewModel, SegmentViewModel
│     ├─ Views/                 MainWindow
│     ├─ Themes/                Styles.xaml
│     └─ Converters/
└─ tests/
   └─ VotschVc3.Core.Tests/     ← xUnit testy protokolu, profilov a klienta
```

Logika protokolu je oddelená od UI – `VotschVc3.Core` nemá žiadne závislosti na
WPF, dá sa použiť aj z konzolovej appky, služby alebo testov. Aplikácia nemá
žiadne externé NuGet závislosti (MVVM je ručne písané), takže sa zostaví „out of
the box".

---

## Zostavenie a spustenie

Potrebuješ **Windows** (WPF) a **.NET 8 SDK**.

```powershell
# v koreňovom adresári repozitára
dotnet restore
dotnet build -c Release

# spustenie aplikácie
dotnet run -c Release --project src/VotschVc3.App

# testy jadra
dotnet test
```

Alebo otvor `VotschVc3.sln` vo Visual Studio 2022 a spusti projekt
`VotschVc3.App`.

> Jadro (`VotschVc3.Core`) a testy sa dajú zostaviť aj na Linuxe/macOS; samotná
> WPF aplikácia `VotschVc3.App` sa zostaví a spustí len na Windowse.

---

## ASCII-2 protokol (zhrnutie)

Rámec požiadavky:

```
$ dd C <parametre> <terminátor>
│  │  │     │           └ terminátor, prednastavene CR (\r)
│  │  │     └ parametre oddelené medzerou (závisia od príkazu)
│  │  └ príkaz: I = čítaj, E = zapíš setpointy
│  └ 2-miestna adresa komory (napr. „01")
└ štartovací znak '$'
```

| Príkaz | Význam | Príklad |
|--------|--------|---------|
| `I` | čítanie nameraných + nastavených hodnôt | `$01I\r` |
| `E` | zápis setpointov + 32 digitálnych kanálov | `$01E 0050.0 0000.0 0000.0 0000.0 0000.0 0000.0 10000000000000000000000000000000\r` |

- **Analógové hodnoty** majú pevný formát `0050.0` (50,0 °C). Záporné si
  zachovajú šírku: `-040.0` (−40,0 °C).
- **Digitálne kanály** sa prenášajú ako blok **32 znakov** `0`/`1`. Štartovací
  („system on") kanál musí byť `1`, inak komora setpoint nebude regulovať.
- **Odpoveď** na `$ddI` obsahuje pre každý analógový kanál nameranú a nastavenú
  hodnotu a na konci digitálny blok. Dekodér je tolerantný – vyberá všetky
  desatinné čísla a binárny blok bez ohľadu na echo adresy či medzery.

**Ethernet:** pevný TCP port **1080** (niekde **2049**), max 5 súčasných
spojení. Port aj adresu vieš zmeniť v záložke *Connection*.

### Zdroje k protokolu

- NI Forum – [Vötsch VCL 7010 ASCII-2 interface protocol](https://forums.ni.com/t5/Instrument-Control-GPIB-Serial/V%C3%B6tsch-VCL-7010-ASCII-2-interface-protocol/td-p/3137671)
- NI Forum – [Communication to Vötsch oven](https://forums.ni.com/t5/Instrument-Control-GPIB-Serial/Communication-to-V%C3%B6tsch-oven/td-p/3716321)
- [CTS ASCII Interface protocol](https://www.cts-umweltsimulation.de/downloads/dokumentation/en/ASCII_Interfaceprotocol.pdf) (takmer identický protokol)
- Manuály VC3/VT3 (Fisher Scientific): [VOE042](https://assets.fishersci.com/TFS-Assets/CCG/EU/Voetsch-Industrietechnik/manuals/VOE042_EN%20CLIMATE%20IN%20PERFECTION%20VT3%20VC3.pdf),
  [VOE025](https://assets.fishersci.com/TFS-Assets/CCG/EU/Voetsch-Industrietechnik/manuals/VOE025_EN%20CONSTANT%20CLIMATE%20CABINET%20VC3%2000.pdf)
- Weiss [S!MPATI](http://weiss-na.com/wp-content/uploads/Simpati_4.50_user_guide.pdf) (referenčný softvér výrobcu)

---

## Kalibrácia voči konkrétnej komore

Počet kanálov, číslo portu a **mapovanie 32 digitálnych bitov sa líši kus od
kusu**. Postup overenia:

1. V záložke **Connection** zadaj IP a port, klikni **Connect**.
2. V záložke **Raw terminal** pošli `$01I` a pozri si odpoveď.
   - Spočítaj desatinné hodnoty → koľko je analógových kanálov (nastav
     *Analog channels*).
   - Identifikuj poradie: zvyčajne `teplota_meraná teplota_set vlhkosť_meraná
     vlhkosť_set …`.
3. Zisti, ktorý **digitálny bit** je „štart". Pošli `$01E …` s rôznymi bitmi a
   sleduj, kedy komora nabehne. Index nastav do *Start channel index*.
4. Ak komora vyžaduje `\r\n`, prepni *Frame terminator* na `CR LF`.

Surový terminál loguje **TX/RX** každého rámca, takže mapovanie spoľahlivo
odladíš.

---

## Profily (rampy a plata)

Záložka **Profile** umožňuje zadať postupnosť segmentov:

- **Ramp** – lineárny prechod z aktuálnej hodnoty na cieľovú za daný čas.
- **Plato (hold)** – udržiavanie cieľovej hodnoty po danú dobu.

Profil vykonáva PC (`ProfileRunner`): v pevnom intervale prepočíta interpolovaný
setpoint a zapíše ho príkazom `$ddE` so zapnutým štart kanálom. Tým je nezávislý
od programovej pamäte komory a funguje rovnako na ľubovoľnom kuse. Možno ho
opakovať v cykloch.

---

## Záznam dát

Záložka **Recording** zapisuje každé odčítané meranie do CSV
(`Timestamp;Temperature;TemperatureSetpoint;Humidity;HumiditySetpoint;Digital;Raw`).
Frekvenciu vzorkovania určuje interval pollingu v záložke *Connection*.

---

## Licencia / zodpovednosť

Softvér je poskytnutý „tak ako je". Overuj na bezpečných hodnotách – riadiš
reálne zariadenie schopné extrémnych teplôt.
