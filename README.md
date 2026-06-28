# Vötsch — riadiaci softvér klimatických komôr

Moderná **WPF (.NET 8) MVVM** aplikácia na ovládanie klimatických komôr
**Vötsch / Weiss** (kontrolér S!MPAC / SIMPAC) cez **Ethernet** pomocou textového
**ASCII-2** protokolu.

Súčasťou je aj **odčítavanie presných teplomerov ASL F100** cez USB.

**Verzia: 1.6.2** — história zmien je v [CHANGELOG.md](CHANGELOG.md). Verzia sa
zobrazuje aj v aplikácii (home page a titulok okna).

Prihlásenie (predvolené): **admin / admin** (plný prístup), **operator / operator**
(len čítanie). Heslá sú v `Dokumenty/VotschVc3/users.json` (SHA-256).

Podporuje **dve komory naraz** (každá s vlastnou IP adresou, obe môžu byť
pripojené a bežať súčasne). **Home page slúži ako dashboard** – ukazuje živé
hodnoty, stav profilu (progress) a alarm oboch komôr naraz. Konfigurácia komôr
(IP, port, mapovanie kanálov, alarm limity) sa **automaticky ukladá** a obnoví
po reštarte (`Dokumenty/VotschVc3/chambers.json`).

- **Komora 1** — teplota **+ vlhkosť** (typ VC3),
- **Komora 2** — **iba teplota** (typ VT3).

Umožňuje:

- **home page** s výberom komory, ktorá sa má nastavovať/ovládať,
- **animovaná vektorová grafika** komory s rotujúcim ventilátorom (podľa VT³ 7060),
- pripojiť sa ku každej komore cez TCP/IP nezávisle,
- **čítať** namerané hodnoty (teplota, vlhkosť, digitálne kanály) v reálnom čase,
- **nastavovať** setpointy teploty a vlhkosti (vlhkosť len pre komoru, ktorá ju má),
- ovládať **digitálne kanály** (vrátane štartovacieho „system on"),
- **vizuálny editor profilov** – skladanie segmentov (rampy a plata) z prvkov,
  s **náhľadovým grafom** profilu (celý beh vrátane cyklov),
- **živý graf** teploty a vlhkosti v reálnom čase (meraná hodnota vs. setpoint),
- **história profilov** (ukladanie / načítanie / mazanie, perzistentné v JSON),
- **import aj export** profilov (CSV pre Vötsch/Excel, JSON),
- **viac cyklov naraz**, **odložený štart** (naplánovaný čas) a **výpočet času**
  (celkové trvanie, odhad konca behu),
- **e-mail upozornenie** po dokončení profilu (SMTP alebo HTTP API),
- **bezpečnosť**: alarmy na limity teploty/vlhkosti, **watchdog** so stratou
  spojenia, **auto-stop** profilu a **auto-reconnect**, e-mail pri alarme,
- **zaznamenávať** priebeh do CSV s časovou pečiatkou,
- posielať **ľubovoľné príkazy** cez surový terminál (na kalibráciu a vendor
  príkazy ako programy či hodiny).

Samostatný vektorový obrázok komory je aj v [`assets/chamber.svg`](assets/chamber.svg).

> ⚠️ **Bezpečnosť:** softvér ovláda reálne zariadenie, ktoré môže dosahovať
> extrémne teploty. Pred ostrým nasadením si over správanie na bezpečných
> hodnotách a skontroluj mapovanie kanálov (viď *Kalibrácia*).

---

## Architektúra

```
VotschVc3.sln
├─ assets/
│  └─ chamber.svg               ← samostatná SVG grafika komory (rotujúci ventilátor)
├─ src/
│  ├─ VotschVc3.Core/           ← jadro, platform-nezávislé (net8.0), testovateľné
│  │  ├─ Protocol/              Ascii2Protocol, ChamberReading, DigitalChannels
│  │  ├─ Communication/         ITransport, TcpTransport, ChamberClient, settings
│  │  ├─ Profiles/              ProfileSegment, TestProfile, ProfileRunner,
│  │  │                         ChamberKind, ProfileStore (história)
│  │  └─ Recording/             CsvRecorder
│  └─ VotschVc3.App/            ← WPF UI (net8.0-windows), MVVM, tmavá téma
│     ├─ Mvvm/                  ObservableObject, RelayCommand(<T>), AsyncRelayCommand
│     ├─ ViewModels/            ShellViewModel, ChamberViewModel, SegmentViewModel
│     ├─ Views/                 MainWindow, HomeView, ChamberView, ChamberGraphic
│     ├─ Themes/                Styles.xaml
│     └─ Converters/
└─ tests/
   └─ VotschVc3.Core.Tests/     ← xUnit testy protokolu, profilov a klienta
```

`ShellViewModel` hostí dve `ChamberViewModel` inštancie (každá s vlastným
`ChamberClient` a spojením) a navigáciu medzi **home page** (`HomeView`) a
**detailom komory** (`ChamberView`). `ChamberGraphic` je škálovateľná vektorová
grafika s animovaným ventilátorom. História profilov sa ukladá cez `ProfileStore`
do `Dokumenty/VotschVc3/profiles.json`.

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

> **Spustenie vo Visual Studio:** štartovací projekt musí byť **`VotschVc3.App`**
> (nie `VotschVc3.Core` — to je knižnica a nedá sa spustiť priamo). Ak dostaneš
> chybu *„A project with an Output Type of Class Library cannot be started
> directly"*, klikni pravým na `VotschVc3.App` → **Set as Startup Project**.

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

## Import originálnych Vötsch / SIMPATI profilov

Tlačidlo **„Importovať Vötsch profil…"** v záložke *Profil* načíta profil zo
súboru. Keďže natívny SIMPATI program (databáza) nemá verejne dokumentovaný
formát, import cieli na to, čo sa reálne dá vymeniť:

- **CSV / textový export zo SIMPATI alebo Excelu** (oddeľovač `;`, tab alebo `,`),
  v dvoch tvaroch, ktoré sa rozpoznajú automaticky:
  - **tabuľka segmentov** – riadok = krok s trvaním a cieľovou teplotou/vlhkosťou
    (+ voliteľný stĺpec Rampa/Halten),
  - **časová os setpointov** – riadok = bod s kumulatívnym časom; medzi bodmi sa
    vytvoria rampové segmenty.
- vlastné **`.json`** profily aplikácie (spätný import).

Rozpozná nemecké desatinné čiarky (`60,0`) aj časy `hh:mm:ss`. Pre čisto teplotnú
komoru sa vlhkosť automaticky ignoruje. Hlavičkové stĺpce sa mapujú podľa
kľúčových slov (`Dauer/Duration/Zeit/Time`, `Temperatur/Temperature`,
`Feuchte/Humidity/rF`, `Art/Ramp`). Vzorový súbor:
[`assets/sample_profile.csv`](assets/sample_profile.csv).

> Tip: v SIMPATI exportuj program/tabuľku do CSV (alebo si ho prepíš do Excelu a
> ulož ako CSV) – tento súbor potom naimportuješ priamo do editora.

## Export, odložený štart a e-mail

- **Export profilu** (tlačidlo *Export…* v záložke *Profil*) uloží aktuálny
  profil do **CSV** (kompatibilné so SIMPATI/Excel a s vlastným importom) alebo
  do **JSON** podľa prípony.
- **Odložený štart** – zapni *Odložený štart*, zadaj dátum a čas (`HH:mm`);
  profil sa spustí v naplánovaný čas, dovtedy beží odpočet. Výpočet konca behu
  to zohľadní.
- **E-mail upozornenie** – na home page v karte *Notifikácie e-mailom* zapni
  posielanie, zadaj adresáta a vyber spôsob:
  - **SMTP** (host, port, SSL, login) – univerzálne, cez `System.Net.Mail`;
  - **HTTP API** (endpoint + voliteľný Bearer kľúč) – POST JSON
    `{ to, from, subject, text }`; sem zadáš váš dbfood endpoint. Formát tela
    prípadne uprav v `HttpEmailSender`.

  Po dokončení profilu sa odošle e-mail s názvom komory, profilu a časom.
  Nastavenia sa ukladajú do `Dokumenty/VotschVc3/email.json`.

## Bezpečnosť (alarmy, watchdog, auto-reconnect)

Záložka **Bezpečnosť** na detaile komory:

- **Alarmy na limity** – zadaj min/max teploty (a vlhkosti); pri prekročení sa
  spustí alarm (červený indikátor v hlavičke + stav), pošle e-mail a voliteľne
  zastaví bežiaci profil.
- **Watchdog spojenia** – po 3 neúspešných čítaniach sa spojenie považuje za
  stratené: alarm, voliteľný auto-stop profilu, e-mail.
- **Auto-reconnect** – po výpadku sa appka automaticky znovu pripája (exponenciálny
  backoff do 30 s) a obnoví polling.

E-mail pri alarme sa odošle, ak sú notifikácie nastavené na home page.

## Teplomery ASL F100 (USB)

Tlačidlo **„Teplomery ASL F100 →"** na home page otvorí správu presných
teplomerov **ASL F100** (a príbuzných F150/F250), ktoré sa pripájajú cez **USB
ako virtuálny COM port**.

- **Enumerácia portov so sériovým číslom** – keďže máš viac rovnakých kusov,
  každý sa zobrazí ako `COMx · <sériové číslo> (popis)`; vyberáš podľa portu
  alebo S/N. Tlačidlo *Obnoviť zoznam* znova prehľadá USB.
- **Viacero teplomerov naraz** – každý má vlastné spojenie, živú teplotu, graf a
  **CSV záznam**.
- **Referenčné meranie pri komore** – ku komore priradíš F100 ako externú
  referenciu; v live zobrazení vidíš referenčnú teplotu a **odchýlku**
  (komora − referencia), ktorá sa zapíše aj do CSV záznamu komory.
- **Komunikácia**: 9600 8N1 (voliteľne 4800/19200), príkazy zakončené **CR**,
  1–2 ms medzi znakmi. `*IDN?` na identifikáciu; **príkaz čítania je
  konfigurovateľný** (default `READ?`), lebo sa medzi firmvérmi líši.
- **SCPI terminál** na kalibráciu a ladenie (napr. `UNITS C`, `CHANNEL A`,
  `MODE REMOTE` – presnú syntax over voči svojmu kusu).

> Pozn.: parsovanie hodnoty/jednotky je v `F100Protocol` (jadro, testované);
> sériová komunikácia (`System.IO.Ports`) a enumerácia USB sériových čísiel
> (WMI) sú vo WPF projekte – fungujú na Windowse.

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
