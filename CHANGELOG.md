# Changelog

Všetky podstatné zmeny v tomto projekte. Formát vychádza z
[Keep a Changelog](https://keepachangelog.com/), verzie podľa
[SemVer](https://semver.org/lang/sk/).

## [1.2.0] – 2026-06-27

### Pridané
- **CSV záznam z teplomerov** ASL F100 (Timestamp;Temperature;Unit;Raw) s
  výberom súboru a počítadlom riadkov.
- **Referenčný teplomer pri komore** – ku komore sa dá priradiť F100 ako externá
  referencia; v live zobrazení sa ukáže referenčná teplota a **odchýlka**
  (komora − referencia).

### Zmenené
- CSV záznam komory rozšírený o stĺpce **Reference** a **Deviation** (pre
  kalibračné záznamy oproti F100).

## [1.1.0] – 2026-06-27

### Pridané
- **Teplomery ASL F100** cez USB (virtuálny COM port): enumerácia portov so
  **sériovým číslom** (rozlíšenie viacerých rovnakých kusov), pripojenie a
  **súčasné čítanie viacerých** teplomerov naraz.
- Pre každý teplomer: živá teplota, graf priebehu, `*IDN?` identifikácia,
  konfigurovateľný príkaz čítania (default `READ?`), baud, interval a
  **SCPI terminál** na kalibráciu.
- Jadro: `F100Protocol` (parsovanie hodnoty a jednotky) + testy.

## [1.0.0] – 2026-06-27

### Pridané
- **Changelog** a **zobrazenie verzie** v aplikácii (home page + titulok okna).

## [0.7.0] – 2026-06-27

### Pridané
- **Dashboard oboch komôr** na home page – živé hodnoty, progress bežiaceho
  profilu a ALARM chip pre obe komory naraz.
- **Perzistencia konfigurácie komôr** (IP, port, mapovanie kanálov, alarm limity)
  do `Dokumenty/VotschVc3/chambers.json`; obnova po reštarte, automatické
  ukladanie zmien (debounced) aj pri zatvorení.

## [0.6.0] – 2026-06-27

### Pridané
- **Bezpečnosť**: alarmy na limity teploty/vlhkosti, **watchdog** straty spojenia,
  **auto-stop** bežiaceho profilu, **auto-reconnect** s exponenciálnym backoffom.
- E-mail upozornenie pri novom alarme; ALARM indikátor v hlavičke komory.

## [0.5.0] – 2026-06-27

### Pridané
- **Export profilu** do CSV (kompatibilný s importom) a JSON.
- **E-mail notifikácie** po dokončení profilu – **SMTP** alebo **HTTP API**
  (napr. dbfood endpoint), s testovacím tlačidlom a perzistentnými nastaveniami.
- **Odložený štart** profilu (naplánovaný čas) so živým odpočtom.

### Zmenené
- Prepracovaný dizajn tlačidiel (accent glow, stlačený stav, ghost variant).

## [0.4.0] – 2026-06-27

### Pridané
- **Živý graf** teploty a vlhkosti (meraná hodnota vs. setpoint).
- **Náhľad profilu** v editore (rampy a plata vrátane cyklov).
- Znovupoužiteľný vektorový graf `ChartView` (bez externých závislostí).

## [0.3.0] – 2026-06-27

### Pridané
- **Import originálnych Vötsch / SIMPATI profilov** – CSV (tabuľka segmentov aj
  časová os setpointov) a vlastný JSON; nemecké desatinné čiarky a `hh:mm:ss`.

## [0.2.0] – 2026-06-27

### Pridané
- **Dve komory naraz** s nezávislými spojeniami; **home page** s výberom komory.
- Rozlíšenie **teplota + vlhkosť** (VC3) vs. **iba teplota** (VT3).
- **Animovaná vektorová grafika** komory s rotujúcim ventilátorom (+ `assets/chamber.svg`).
- **Vizuálny editor profilov**, **história profilov**, **viac cyklov**,
  **výpočet času** (trvanie a odhad konca).

## [0.1.0] – 2026-06-27

### Pridané
- Jadro **ASCII-2 protokolu** (čítanie/zápis, 32 digitálnych kanálov, tolerantný parser).
- TCP komunikácia (port 1080), `ChamberClient`, **PC-side profilový engine**
  (rampy a plata), CSV záznam.
- WPF (.NET 8) MVVM aplikácia: pripojenie, live monitoring, manuálne setpointy,
  profil, záznam, surový terminál; tmavá téma; jednotkové testy jadra.

[1.2.0]: https://github.com/mukky89/chamber_fos_soft
[1.1.0]: https://github.com/mukky89/chamber_fos_soft
[1.0.0]: https://github.com/mukky89/chamber_fos_soft
[0.7.0]: https://github.com/mukky89/chamber_fos_soft
[0.6.0]: https://github.com/mukky89/chamber_fos_soft
[0.5.0]: https://github.com/mukky89/chamber_fos_soft
[0.4.0]: https://github.com/mukky89/chamber_fos_soft
[0.3.0]: https://github.com/mukky89/chamber_fos_soft
[0.2.0]: https://github.com/mukky89/chamber_fos_soft
[0.1.0]: https://github.com/mukky89/chamber_fos_soft
