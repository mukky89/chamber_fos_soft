# Changelog

Všetky podstatné zmeny v tomto projekte. Formát vychádza z
[Keep a Changelog](https://keepachangelog.com/), verzie podľa
[SemVer](https://semver.org/lang/sk/).

## [1.7.1] – 2026-07-09

### Pridané
- **Predkonfigurovaná POL-EKO SLN 115** – zariadenie je automaticky pridané
  (IP **10.88.5.162**, port 502, MODBUS, len teplota, rozsah do 300 °C). Pridá
  sa raz (aj do existujúcich inštalácií); ak ho odstrániš, znovu sa neobjaví.
- **Grafika POL-EKO pece** – nový vektorový obrázok (nerezová skriňa s dotykovým
  displejom a teplotou) sa zobrazuje na dlaždici aj v hlavičke pre POL-EKO
  zariadenia; SVG verzia je v `assets/poleko_sln.svg`.

## [1.7.0] – 2026-07-09

### Pridané
- **Nový typ zariadenia: POL-EKO sušiareň (SLN 115) cez MODBUS TCP.** Aplikácia
  vie teraz ovládať aj POL-EKO pece so SMART regulátorom popri Vötsch komorách:
  - nová abstrakcia zariadenia (`IChamberDevice`) – Vötsch ASCII-2 aj POL-EKO
    MODBUS zdieľajú rovnaké ovládanie, profily, frontu aj náhľady,
  - vlastný **MODBUS TCP klient** (funkcie 0x03/0x04/0x06, port 502),
  - `PolEkoClient` číta meranú teplotu (input register) a setpoint/zap-vyp
    (holding registre), zápis setpointu riadi pec; ak firmware MODBUS zápis
    nepovoľuje, aplikácia to bezpečne ohlási ako chybu (nič nevykoná naslepo),
  - v **Administrácii** pri pridávaní komory je nový výber **Protokol**
    (Vötsch ASCII-2 / POL-EKO MODBUS); POL-EKO sa automaticky nastaví na
    port 502 a typ „len teplota".
  - ⚠ **Mapa registrov** (`PolEkoRegisterMap`) vychádza z verejnej POL-EKO SMART
    dokumentácie a je na jednom mieste – pred ostrým riadením ju over voči
    reálnej peci (prípadne sledovaním komunikácie LabDesk) a uprav adresy.

## [1.6.15] – 2026-07-09

### Pridané
- **Náhľad profilu na dlaždici komory** – po vybraní (alebo počas behu) testu sa
  zobrazí teplotná krivka profilu a počas behu aj zvislý ukazovateľ „teraz",
  takže vidno, v ktorom štádiu a na akej teplote sa test nachádza priamo v profile.
- **História profilov – dvojklik načíta profil** do editora (teploty aj všetky
  parametre); panel je širší a názov aj popis sa zalamujú (vidno celý text).

### Opravené
- **Zlý názov vybraného profilu** v rozbaľovacom zozname na dlaždici (zobrazoval
  sa názov typu namiesto názvu profilu) – teraz sa ukazuje názov profilu.

## [1.6.14] – 2026-07-09

### Pridané
- **Spustenie komory z uloženého profilu priamo z dlaždice** – pri výbere komory
  je rozbaľovací zoznam uložených profilov (vrátane tých z Rýchleho vytvárača)
  a ikonky **▶ spustiť · ⏸/▶ pozastaviť/pokračovať · ⏹ zastaviť**.
- **Pozastavenie a pokračovanie profilu** – bežiaci profil sa dá pozastaviť
  (testovací čas sa zmrazí, komora drží posledný setpoint) a plynulo obnoviť;
  tlačidlo je aj v detaile komory (záložka *Profil*).
- **Čas štartu a konca počas behu** – na dlaždici komory sa pri bežiacom profile
  zobrazuje „Spustené HH:mm:ss · koniec ~ HH:mm:ss".

### Opravené
- **Rýchly profil je teraz viditeľný v komore** – uložené profily sa v zozname
  komory obnovia pri návrate na hlavnú stránku a pri otvorení komory; teplotné
  profily (napr. z Rýchleho vytvárača) sú dostupné aj na komore s vlhkosťou.
- **Neviditeľný kurzor v tabuľkách profilu** – editačné bunky tabuľky segmentov
  mali čierny kurzor na tmavom podklade; kurzor je teraz biely.

## [1.6.13] – 2026-07-09

### Pridané
- **Automatický názov v Rýchlom vytváraču profilov** – názov sa generuje podľa
  vzoru z parametrov sweepu:
  `[predpona ]Sweep {od}…{do} °C · {N} bodov[ · obojsmerný][ · 2 vrcholy]`
  (N = počet rôznych teplotných bodov). Názov sa dá **ručne upraviť** (vtedy sa
  prestane prepisovať) a tlačidlom *Automaticky* sa vráti generovaný názov.
- **Predpona názvu** – voliteľné pole (napr. kód projektu/vzorky), ktoré sa
  vloží pred automaticky generovaný názov.
- **Tlačidlo „⚡ Rýchly profil“** priamo v ovládaní komory (v hlavičke detailu
  komory) otvorí rýchly vytvárač profilov.

## [1.6.12] – 2026-07-02

### Pridané
- **Import natívnych BEdit programov** (`.b01`, `.b02`, …) zo S!MPAC / SIMPATI
  editora – binárny formát bol reverzne dekódovaný z reálnych súborov:
  - teplotný **aj vlhkostný** kanál (rampy, plata, tolerancie ±x sa preskočia),
  - pri komore s vlhkosťou sa oba kanály zlúčia do jednej časovej osi,
  - rozpoznanie podľa obsahu (signatúra „BEdit"), nie podľa prípony – funguje
    cez existujúce tlačidlo *Importovať…* (filter súborov rozšírený o `*.b0*`),
  - overené na reálnych profiloch (STS11, FOSCal…): sedia teploty, plata aj
    dvojitý vrchol; import vždy pridá upozornenie, aby si profil skontroloval.

## [1.6.11] – 2026-07-02

### Pridané
- **Rýchly vytvárač profilov – dvojitý vrchol**: voliteľne vytvorí na vrchole
  dva najvyššie body a medzi nimi plato o zadaných °C nižšie (predvolene 10 °C),
  aby na vrchole prebehla zmena teploty.
- **Obnoviť teplomery** – tlačidlo ↻ vedľa výberu referenčného teplomera znovu
  vyhľadá pripojené USB COM porty (keď bol teplomer pripojený až po štarte appky).
- **Diagnostika stavu behu** – pri prvom čítaní po pripojení sa do App logu
  zapíše surová odpoveď komory (RAW, digitálny blok, štart kanál, hodnoty), aby
  sa dal presne určiť indikátor „komora beží/nečinná".

### Opravené
- **Vlhkosť sa už nezoreže** na karte komory – hodnoty teplôt/vlhkosti sa
  zalamujú (WrapPanel) a zmestia sa do rámčeka.

## [1.6.10] – 2026-07-02

### Pridané
- **Priradenie referenčného teplomera ASL F100 priamo na karte komory** – v
  hlavnom menu je výber teplomera; po priradení sa teplomer **pripojí a
  aktualizuje teplotu každé ~2 s**.
- Karta komory teraz jasne **odlišuje tri teploty**:
  - **Teplota komory** (aktuálna nameraná, biela, veľká),
  - **Nastavená (setpoint)** (žltá),
  - **Referencia F100** (zelená).

## [1.6.9] – 2026-07-02

### Opravené
- **Stav behu komory sa teraz zisťuje z reálneho stavu komory**, nie iba z toho,
  čo poslala aplikácia. Beh/nečinnosť (kontrolka aj točenie ventilátora) sa
  odvodzuje z **reportovaného „štart/system on" digitálneho kanála** v odpovedi
  na čítanie. Takže keď komoru spustil niekto iný (alebo predtým), zobrazí sa
  správne ako *bežiaca*; ak aplikácia nemá istý stav (odpoveď neobsahuje
  digitálny blok), použije sa stav podľa toho, čo appka spustila.
- **Ventilátor sa teraz naozaj točí, keď komora beží** – animácia sa spúšťa
  spoľahlivo priamo na transformácii (predtým sa za istých okolností nerozbehla).
- Popisok aktivity ukazuje aktívny setpoint z komory („Beží · setpoint … °C").

## [1.6.8] – 2026-07-02

### Pridané
- **Rýchly vytvárač profilov** (tlačidlo *Rýchly profil* v hornej lište) – vytvorí
  symetrický teplotný sweep od zadanej dolnej po hornú teplotu a späť dole:
  - zadáš **rozsah** (napr. −20 → 60 °C) a **počet medzikrokov** (napr. 7) a
    aplikácia **automaticky dopočíta** rovnomerne rozložené teploty,
  - nastavíš **dĺžku plata** a **dĺžku nábehu** (zobrazí sa aj rýchlosť °C/min),
  - vidíš **náhľad grafu**, počet segmentov a **celkový čas**,
  - **optimalizácia**: keď zadáš „skrátiť o X hodín", rovnomerne skráti všetky
    plata a prepočíta celkový čas,
  - hotový profil **uložíš do knižnice** a otvoríš v Editore profilov / spustíš
    na komore.

## [1.6.7] – 2026-07-02

### Pridané
- **Rýchle ovládanie priamo na domovskej stránke** pre každú komoru:
  - tlačidlo **Stop** (zastaví operácie – vynuluje štart kanál),
  - **preddefinované teploty −20 / 0 / 25 / 60 °C** (jedným klikom nastavia a
    spustia setpoint),
  - pole na **rýchle zadanie ľubovoľnej teploty** + tlačidlo *Nastaviť*.
- **Editovateľný názov komory** – názov na karte komory sa dá prepísať (uloží sa).
- **Indikátor behu (kontrolka) a názov profilu/aktivity** na karte – zelená
  kontrolka a text (napr. „Profil: …" alebo „Manuálny setpoint: … °C") keď
  komora beží; pri nečinnosti je kontrolka sivá.
- **Grafika komory reaguje na stav** – ventilátor sa točí len keď komora beží;
  pri nečinnosti sa zastaví a komora je „sivá".

### Zmenené
- **Predĺžený časový limit odpovede** z 3 s na 5 s – niektoré riadiace jednotky
  (a sériové brány) pomaly potvrdzujú zápis, čo sa prejavovalo občasnými
  „TimeoutException" pri nastavovaní setpointu.

## [1.6.6] – 2026-07-02

### Zmenené
- **Nová horná lišta (toolbar)** na domovskej stránke namiesto zvislého menu na
  boku – navigačné tlačidlá (Editor profilov, Teplomery, Prehliadač záznamov,
  Audit, App log, Changelog, Administrácia) sú vodorovne v hornej lište, spolu
  s verziou a prihláseným používateľom.

### Pridané
- **Zmena poradia komôr** – na každej karte komory sú šípky **◀ ▶**, ktorými sa
  komora posunie v poradí; nové poradie sa uloží.
- **Automatická detekcia nesprávneho portu** – ak riadiaca jednotka odpovedá
  uvítacím bannerom (napr. „100 OK: Portable IEC 61131-3 RT Scheduler for
  Windows CE …") namiesto ASCII-2 dát, appka to rozpozná, **nezobrazí nezmyselné
  hodnoty** a do logu zapíše jasnú nápovedu, že treba zmeniť port (ASCII-2 býva
  **2051**, ASCII-1 2050, SIMSERV 2049; staršie riadiace jednotky ASCII na 2049).
  Zároveň to zastaví neustále odpájanie/pripájanie (blikanie).

## [1.6.5] – 2026-07-01

### Pridané
- **Viac diagnostických logov okolo ovládania komory** – do *App logu* sa teraz
  zapisuje:
  - **každý ovládací príkaz na zbernici** (setpoint, stop, vendor príkazy)
    vrátane **odpovede regulátora** (`Príkaz TX: … → RX: …`); rutinné čítania
    (`$xxI`) sa nelogujú, aby log nezaplavili;
  - **zápis setpointu / stop** s adresou, štart kanálom, počtom analóg. kanálov
    a digitálnym reťazcom (na overenie správnej konfigurácie);
  - **stav povolenia ovládania** – ak má prihlásený používateľ rolu *Operátor*,
    log jasne uvedie, že ovládanie je zakázané a tlačidlá sú neaktívne;
  - **štart/dokončenie/zrušenie profilu**.

  Pomáha to diagnostikovať prípad „ku komore sa pripojím, ale neviem ju ovládať"
  (buď chýbajúce oprávnenie roly, alebo regulátor ignoruje zápis / má inú
  adresu, štart kanál či formát rámca – vidno v odpovedi RX).

## [1.6.4] – 2026-07-01

### Pridané
- **Automatické pripojenie komôr po prihlásení** – po úspešnom prihlásení sa
  všetky komory pokúsia pripojiť samé (pri neúspechu bežia na pozadí opätovné
  pokusy, ak je zapnuté automatické pripojenie).
- **Kopírovanie app logu** – tlačidlo **„Kopírovať (Ctrl+C)"** a klávesová
  skratka **Ctrl+C** v diagnostickom logu (kopíruje vybrané riadky, alebo celý
  log, do schránky vrátane hlavičky).
- **Obrazovka Administrácia** (len pre rolu Admin) – sem sa presunuli
  **Notifikácie e-mailom** a **Pridať/odobrať komoru** z domovskej stránky.

### Opravené
- **Blikanie spojenia komory** (stále odpájanie/pripájanie a zaplavenie logu
  rovnakými varovaniami „Strata spojenia"): opätovné pripojenie teraz overí
  spojenie skutočným čítaním skôr, než ho vyhlási za úspešné. Ak regulátor
  prijme TCP socket, ale neodpovedá na čítanie, komora zostane v stave „Strata
  spojenia" s jediným alarmom namiesto opakovaného blikania a e-mailov.

## [1.6.3] – 2026-06-28

### Opravené
- **Odložený štart**: kalendár (DatePicker) mal nečitateľný text na tmavej téme.
  Nahradený tématickým textovým poľom pre dátum (dd.MM.yyyy).

## [1.6.2] – 2026-06-28

### Opravené
- **Zoznam segmentov** (Ohrev/Plato…) sa teraz zobrazuje celý – editor je v
  ScrollVieweri a tabuľka ukáže všetky riadky (predtým bola orezaná na ~2 riadky).

## [1.6.1] – 2026-06-28

### Zmenené
- **Rozbaliť zoznam** segmentov teraz skryje aj históriu (pravý stĺpec), takže
  tabuľka Ohrev/Plato zaberie celú šírku. Pridané tlačidlo **„⛶ Celá obrazovka"**
  (maximalizácia okna) v záložke Profil aj v editore profilov.

## [1.6.0] – 2026-06-28

### Pridané
- **App log (diagnostika)** – globálny log štartov, chýb, kalibrácií a detailov
  appky (do súboru `app.log`) s prehľadom v aplikácii (úrovne Info/Warning/Error).
- **Zobrazenie changelogu** priamo v aplikácii (vložený CHANGELOG.md).
- **Vkladanie segmentu pred/za** vybraný (rýchly ručný mini-profil) a tlačidlo
  **„Rozbaliť zoznam"** pre väčší editovací priestor.
- **Ikona aplikácie** (taskbar + titulok).

### Zmenené
- **Nový login** – moderný dvojpanelový dizajn s animovanou grafikou a odkazom
  na changelog.

## [1.5.0] – 2026-06-27

### Pridané
- **Samostatný editor profilov (knižnica)** prístupný z home page – tvorba,
  úprava, import/export, ukladanie a načítanie profilov **bez pripojenia ku
  komore** (grafický editor, validácia, náhľad vlhkosti, história).

### Opravené
- **ComboBox** mal nečitateľný text na tmavom pozadí – nová tmavá šablóna.
- **Karty komôr** na home page boli zrazené – stránka má teraz scroll a karty
  plnú výšku.
- NullReferenceException pri štarte (poradie inicializácie v ShellViewModel).
- Štartovací projekt solution nastavený na `VotschVc3.App`.

## [1.4.0] – 2026-06-27

Ďalšia dávka inšpirovaná SIMPATI:

### Pridané
- **Užívatelia + audit trail** – prihlásenie, roly (Operátor/Supervisor/Admin),
  obmedzenie ovládania pre „len na čítanie", a log akcií operátora (CSV +
  prehľad v appke).
- **Grafický editor profilu** – ťahanie bodov teploty priamo v grafe + „smart
  checks" (validácia trvania/rozsahov).
- **Guaranteed soak (tolerancia)** – plato sa začne počítať až keď je meraná
  teplota v tolerancii cieľa.
- **Fronta testov** – viac profilov za sebou na jednej komore (pridaj aktuálny,
  spusti frontu), s priebehom naprieč frontou.

## [1.3.0] – 2026-06-27

Inšpirované Weiss **SIMPATI** (gap-analýza):

### Pridané
- **Konfigurovateľný počet komôr** – pridávanie/odoberanie komôr na home page
  (názov, typ, IP), perzistentné (predtým napevno 2). Approx. SIMPATI „viac
  systémov".
- **Prehliadač záznamov** – otvorenie uloženého CSV (komory/teplomera),
  vykreslenie do grafu a **štatistika** (min/max/priemer, počet vzoriek) na
  sériu. Approx. SIMPATI „analýza/archív dát".

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

[1.6.3]: https://github.com/mukky89/chamber_fos_soft
[1.6.2]: https://github.com/mukky89/chamber_fos_soft
[1.6.1]: https://github.com/mukky89/chamber_fos_soft
[1.6.0]: https://github.com/mukky89/chamber_fos_soft
[1.5.0]: https://github.com/mukky89/chamber_fos_soft
[1.4.0]: https://github.com/mukky89/chamber_fos_soft
[1.3.0]: https://github.com/mukky89/chamber_fos_soft
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
