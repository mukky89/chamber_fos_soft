# Changelog

## [Nezverejnené]

## [1.76.3] – 2026-09-01

### Opravené
- Textové a projektové súbory používajú jednotné Windows ukončenie riadkov
  `CRLF`. Pravidlo je uložené v `.editorconfig`, takže Visual Studio už pri
  otvorení `VotschVc3.App.csproj` nezobrazuje dialóg „Inconsistent Line Endings“.

## [1.76.2] – 2026-09-01

### Zmenené
- Kalibračné okno pri otvorení automaticky vyhľadá a pripojí reálne PeakLogger
  API; simulátor už nie je predvolene zapnutý.
- Referenčný teplomer zobrazuje identifikovaný názov, výrobcu, model a úplnú
  odpoveď `*IDN?`, napríklad `WIKA CTH7000`.
- Výber kanála sondy A/B je opäť dostupný. Aplikácia kanál naďalej automaticky
  nastaví podľa vstupu, na ktorom nájde pripojenú sondu.
- Modré obrysové tlačidlá majú svetlejší a hrubší rám, aby boli na tmavom
  pozadí zreteľné.

### Opravené
- WIKA CTH7000 po otvorení USB portu dostane čas na inicializáciu a po príkaze
  `SYSTEM:REMOTE` krátku stabilizačnú pauzu. Merací timeout rešpektuje reálny
  čas konverzie približne 2,1–2,3 sekundy, takže sa odpoveď nestratí ani
  neposunie k nasledujúcemu kanálu.
- Kalibračné jednorazové čítanie už nesúťaží s automatickým pollingom o rovnakú
  sériovú odpoveď.
- Z kalibračnej obrazovky boli odstránené zastarané popisy ASL F100 a Talk Only;
  diagnostika jasne odlišuje WIKA CTH7000 od staršieho pasívneho ASL F100.
- USB identifikácia rozpozná reálne pripojené teplomery WIKA CTH7000 a pri
  meraní mapuje vstupy A/B na príkazy `MEASURE:CHANNEL? 1/2`. Predchádzajúce
  textové parametre A/B zariadenie odmietalo ako `ERR CMD`.
- Pôvodný ASL F100 po neúspešnom pasívnom čítaní už nedostáva automatické
  SCPI príkazy určené pre novšie kompatibilné modely. Aplikácia namiesto
  zavádzajúceho timeoutu vysvetlí, že na prístroji treba zapnúť
  `Menu → Options → Talk Only → On`.
- Vyhľadávanie PeakLogger API rozpozná stav, keď bežia dve okná PeakLogger, ale
  iba prvý proces vlastní pevný REST port `43122`, a zobrazí operátorovi, že
  druhá inštancia nemá samostatné API.

## [1.76.1] – 2026-08-29

### Zmenené
- Akcie v hlavičke karty zariadenia sú prehľadnejšie: zámok je vľavo, vedľa
  neho je ikonka ceruzky na nastavenie a ovládanie a vpravo zostala iba
  pomenovaná akcia „FBG Kalibrácia“.
- Odkaz na webové rozhranie je označený „WWW ↗“ a presunutý priamo vedľa IP
  adresy zariadenia, aby bolo zrejmé, kam odkaz smeruje.
- Výber kalibračného profilu používa rovnaký rozšírený náhľad ako hlavné
  zobrazenie: vyhľadávanie, graf priebehu, min/max, celkový čas, cykly, počet
  plat, teplotné úrovne a prehľad dĺžok jednotlivých plat.

### Opravené
- Odpojenie alebo vynútené opätovné pripojenie ASL F100 už nezatvára COM port
  počas blokujúceho čítania. Klient najprv bezpečne dokončí alebo časovo ukončí
  aktívne čítanie, čím sa odstránila `OperationCanceledException` zo
  `System.IO.Ports.SerialStream.ReadByte`.
- Pred každou kontrolou, vynúteným pripojením a štartom kalibrácie sa nanovo
  skenujú aktuálne Windows USB/COM porty. Výber sa obnovuje podľa USB sériového
  čísla, takže ten istý F100 zostane vybraný aj po zmene napr. z COM4 na COM9.
- USB diagnostika má bezpečný „Pasívny test F100“: bez odoslania jediného
  príkazu skúsi port otvoriť a počúva talk-only dáta pri 4800/9600/19200 bd.
  Rozlíši obsadený port, chybu ovládača, funkčný dátový prúd a stav, keď treba
  na prístroji zapnúť Menu → Options → Talk Only → On. Výsledky zapisuje aj do
  aplikačného logu.
- USB komunikácia s pôvodným ASL F100 najprv automaticky načíta jeho pasívny
  „talk-only“ dátový prúd a už po pripojení neposiela nepodporované SCPI
  príkazy. Rozpozná rámce s kanálom A/B aj 1/2; dotazovací režim zostáva ako
  fallback pre kompatibilné F150/F250 a novšie firmvéry.
- Pri odpojení sa príkaz `SYSTEM:LOCAL` posiela iba zariadeniu, ktoré už
  preukázateľne komunikovalo v query režime. Talk-only F100 už pri zatváraní
  nedostane žiadny skúšobný SCPI príkaz.
- Pri talk-only rámcoch s číslom kanála pred hodnotou sa ako teplota parsuje
  meraná hodnota, nie číslo kanála.
- Kalibračné okno má rozbaľovaciu USB diagnostiku: zoznam COM portov, USB/PnP
  stav, ovládač, hardvérové ID a vysvetlenie Windows chybového kódu. Umožňuje
  port znovu analyzovať, pridať ručne (`COMx`) a vynútiť jeho bezpečné
  zatvorenie a opätovné otvorenie v aplikácii.
- PeakLogger klient automaticky rozpozná aktuálny endpoint `/api/v1/peaks` aj
  starší `/peaks?`; dostupnosť už nekontroluje cez Swagger, ktorý môže legitímne
  vracať 404.
- Pri PeakLogger nastavení pribudol vyhľadávač viacerých súčasne spustených API.
  Paralelne prehľadá 32 portov od zadaného portu a ukáže počet API procesov,
  interrogátorov (`deviceSN`) a peakov; výber nálezu automaticky nastaví port.
- Nameraná teplota, setpoint a vlhkosť sú na dashboardovej karte oddelené do
  nenápadných metrických plôch bez farebných rámov. Farbu nesú iba výraznejšie
  hodnoty a väčšie jednotky: oranžová teplota, svetlooranžový setpoint a modrá
  vlhkosť.
- Riadok stavových odznakov aj riadok metrík majú pevnú výšku bez zalamovania.
- Live monitor automaticky pridá novo objavené PeakLogger peaky do tabuľky bez
  ručného obnovenia. Riadok bez FBG sensor SN má jemné červené pozadie a
  výrazný ľavý okraj; zvýraznenie po zadaní SN okamžite zmizne.
- Bežné FBG SN sa po naskenovaní prenesie na všetky peaky rovnakého kanála.
  Pre CHAIN zapojenie pribudol samostatný per-peak stĺpec „FBG sensor SN CHAIN“,
  ktorého hodnota má prednosť pred kanálovým SN.
- FBG SN sa priebežne ukladá s krátkym debounce, kontroluje formát
  `XXXXXX/XXXX` a duplicity. Neštandardný text zostáva povolený a uložený, ale
  riadok aj súhrn zobrazia operátorovi upozornenie.
- Kalibračná tabuľka má explicitné tmavé pozadie a svetlý text aj po ukončení
  editácie alebo strate fokusu, takže nevzniká nečitateľný sivý text na bielom.
- Profil môže obsahovať viacriadkovú poznámku zadanú v rýchlom vytvárači.
  Poznámka sa ukladá s profilom, zobrazuje sa v rozšírenom náhľade aj v
  knižnici profilov a dá sa podľa nej vyhľadávať.
- Profily majú vždy viditeľný stav pripravenosti: OK (zelený), NOK (červený),
  WIP (oranžový) alebo TBT (modrý). Staré aj nové profily majú bezpečný
  predvolený stav TBT; vysvetlenie stavov je dostupné cez „?“.
- Voliteľné upozornenie profilu sa pri výbere zobrazí výrazne so symbolom „!“.
  Vyhľadávanie podporuje aj ID profilu, stav a text upozornenia a zatvorený
  výber profilu zobrazuje ID spolu s názvom.
  Komora s vlhkosťou používa kratší odznak `T + RH` a tri užšie metrické bloky,
  takže ovládacie panely pod nimi zostávajú zarovnané s ostatnými kartami.
- Vyhľadávač PeakLogger API na `localhost` už nehádá iba úzky rozsah portov:
  preverí všetky aktívne TCP listenery z Windows a k nim rezervných 64 portov.
  Tak nájde aj ďalšiu inštanciu s dynamicky prideleným vzdialeným portom a v
  súhrne ukáže počet skontrolovaných portov.

## [1.76.0] – 2026-08-28

### Pridané
- **Meraná teplota v plnom rozlíšení regulátora (SIMSERV 11004).** ASCII-2 rámec
  `$ddI` nesie každú analógovú hodnotu v pevnom poli `0000.0`, takže cez neho
  nikdy nepríde viac ako jedno desatinné miesto – SIMPATI pritom ukazuje
  `40,0213 °C`. Aplikácia preto po každom čítaní ešte pošle SIMSERV
  `GET ACTUAL VALUE` (`11004¶ID¶1`), kde hodnota chodí ako text
  (`1¶40.0213`), a tú použije ako nameranú teplotu. Na komore s vlhkosťou sa
  rovnako doplní meraná vlhkosť (`11004¶ID¶2`).
- Presnejšia hodnota ide všade, kde sa doteraz brala z ASCII-2: na kartu, do
  grafu, do CSV záznamu aj do rozhodovania o ustálení pri behu profilu.

### Bezpečnosť a spoľahlivosť
- Prevezme sa len odpoveď so stavom `1`, ktorá nesie číslo, a len ak sa od
  hodnoty z ASCII-2 líši najviac o 1,0 (`ChamberClient.HighResolutionTolerance`).
  Väčší rozdiel znamená, že riadiaca veličina je namapovaná inde – vtedy zostáva
  v platnosti hodnota z ASCII-2, takže presnejšie čítanie nemôže podsunúť
  hodnotu z cudzieho kanála.
- Regulátor, ktorý `11004` nevie (chybový stav alebo neodpovie), sa už na dané
  spojenie nepýta znova; kanál, ktorý sa 3× po sebe nezhoduje, sa tiež prestane
  pýtať. Komora bez podpory teda stojí jeden rámec navyše, nie viac.
- Setpoint sa nedopĺňa – ten do komory posiela aplikácia, nemeria sa.
- Surová odpoveď `$ddI` sa neprepisuje. SIMSERV výmena je zvlášť a je vidieť
  v tooltipe nameranej hodnoty aj raz za spojenie v aplikačnom logu, takže sa dá
  overiť, odkiaľ číslo na obrazovke pochádza.
- Vypnúť sa to dá cez `ChamberConnectionSettings.HighResolutionRead`
  (predvolene zapnuté).

### Dokumentácia
- `docs/DEVICE_INTEGRATIONS.md`: nová kapitola „Presné meranie cez SIMSERV
  (11004)“ s rámcom, poistkami a postupom overenia cez tlačidlo SIMSERV test.

## [1.75.3] – 2026-08-28

### Zmenené
- **Červený pruh „Zamknuté / Ovládanie je uzamknuté“ na karte zmizol.** Že je
  zariadenie zamknuté, hovorí odznak „Zamknuté“ aj červený visiaci zámok v tom
  istom riadku – pruh bol tretia kópia tej istej informácie a tlačil celú kartu
  nadol. Rámik s políčkom na heslo sa zobrazí len vtedy, keď sa zariadenie
  naozaj odomyká.

### Dokumentácia
- Overené na VT3 7034: odpoveď na `$00I` je
  `0025.0 0025.0 0050.0 0000.0 0002.0 01000…`, teda **analógové hodnoty chodia
  cez ASCII-2 s jedným desatinným miestom**. Presnosť ako v SIMPATI (`24,9981 °C`)
  sa cez toto spojenie získať nedá – strop je 0,1 °C. Zapísané v
  `docs/DEVICE_INTEGRATIONS.md`.

## [1.75.2] – 2026-08-28

### Opravené
- **Odznaky na karte sa už nebijú.** Sú menšie (menší padding a písmo) a zoradené
  podľa dôležitosti — alarm, stav behu, zámok a režim (MANUÁL/PROFIL) zostanú na
  jednom riadku vedľa seba; ako prvý sa na druhý riadok zalomí typ zariadenia,
  ktorý je aj tak v názve v zátvorke a na obrázku komory.
- **Teplota sa zobrazuje presne tak, ako ju zariadenie poslalo** — už sa
  nedopisujú nuly. Ak regulátor pošle `0025.0`, na karte je `25,0`; ak pošle
  `24,9981`, zobrazí sa `24,9981`. Predchádzajúca verzia vypisovala napevno štyri
  desatinné miesta, čo z hodnoty `25,0` spravilo `25,0000` a predstieralo
  presnosť, ktorú prenos nemá.

### Pridané
- **Tooltip na nameranej teplote ukazuje surovú odpoveď komory** (RAW rámec).
  Keď sa hodnota javí iná alebo hrubšia než na displeji komory, hneď vidieť, čo
  regulátor naozaj poslal.

## [1.75.1] – 2026-08-28

### Zmenené
- **Teplota a vlhkosť sa zobrazujú na štyri desatinné miesta**, rovnako ako
  v pôvodnom SIMPATI (napr. `40,0213 °C`). Regulátor S!MPAC toľko desatinných
  miest naozaj posiela a parser ich vždy uchoval — orezávalo ich až zobrazenie.
  Referenčný ASL F100 zostáva na troch desatinných miestach podľa svojej
  špecifikácie.

### Opravené
- **Záznam teplôt do CSV už neorezáva namerané hodnoty na 0,1 °C.** Teplota,
  setpoint a vlhkosť sa do `Recordings` aj do `Profilelog` zapisujú v takom
  rozlíšení, v akom ich zariadenie hlási (koncové nuly sa nedopisujú, takže
  regulátor s jedným desatinným miestom naďalej zapíše `40,0`). Doteraz sa
  presnosť, ktorú prístroj odmeral, zahadzovala pri zápise do súboru.

## [1.75.0] – 2026-08-28

### Pridané
- **Stav zámku je vidieť vždy, aj keď je odomknuté.** Nový odznak na karte
  zariadenia: **červené „Zamknuté“** alebo **zelené „Odomknuté“**, tučným písmom.
  Ikona zámku v hlavičke karty má rovnakú farbu — zatvorený červený visiaci zámok,
  otvorený zelený. Je to vektorová ikona, nie emoji 🔒 (to sa kreslí vlastným
  farebným fontom a farbu ignoruje).

### Zmenené
- **Názov zariadenia je v rámčeku a vždy na jednom riadku** — dlhší názov sa oreže
  s „…“, celý zostáva v tooltipe. Karty tak majú rovnakú výšku hlavičky a stĺpce
  na nástenke lícujú.
- **Živé hodnoty zaberajú celú šírku karty** (predtým začínali až za obrázkom
  komory a vedľa obrázka zostávalo prázdne miesto).
- **Namerané hodnoty na tri desatinné miesta** — teplota, setpoint aj vlhkosť na
  karte, v detaile zariadenia aj na Professional karte. Číslo na obrázku komory
  zostáva na jedno desatinné miesto (viac sa naň nezmestí).
- **Tlačidlo „FBG Kalibrácia“ je výraznejšie**: akcentný rámik s podfarbením a
  novou ikonou signálu (priebeh s peakom) namiesto znaku ◈. Nový štýl
  `AccentOutlineButton` — hlasnejší ako sekundárne tlačidlá, ale stále nekonkuruje
  jedinej vyplnenej primárnej akcii v riadku.

## [1.74.0] – 2026-08-28

### Zmenené
- **Obrazovka FBG teplotnej kalibrácie je upratná do chlievikov.** Namiesto jedného
  radu voľne rozhádzaných polí sú hore tri orámované skupiny — „Profil a komora“,
  „PeakLogger (interrogátor)“ a „Referenčný teplomer ASL F100“. Tlačidlo Pripojiť
  už nesedí vo vlastnom stĺpci mriežky (čo nechávalo dieru cez pol karty), ale
  patrí k svojej skupine.
- **Ovládanie behu je jeden blok**: Spustiť / Pauza / Stop v spoločnom rámiku
  s vektorovými ikonami, oddelené od „Uložiť zapojenie“ — stop nikdy nesusedí
  s uložením.
- **Host a port PeakLoggera sa zobrazia len pri reálnom prístroji**, scenár len pri
  simulátore; referenčná teplota má veľkosť živej hodnoty, keďže je to číslo, proti
  ktorému sa celá kalibrácia meria. Graf F100 je vlastná karta, ktorá sa zobrazí až
  po vyžiadaní.
- **Prázdna tabuľka peakov má vysvetlenie**, čo má operátor spraviť (pripojiť
  PeakLogger alebo zapnúť simulátor a načítať kanály).
- **Karta zariadenia: stav je v rámčeku.** Pripojenie, „Beží · setpoint“ a SIKA
  Remote Control boli voľné riadky pod odznakmi a pôsobili ako zvyšky; teraz sú
  v rovnakom boxe ako živé hodnoty pod nimi.

## [1.73.0] – 2026-08-28

### Zmenené
- **Nové ikony — tenký obrysový set.** Všetkých 21 ikon v aplikácii je prekreslených
  ako obrysy (ťah 1,5, zaoblené konce a spoje) namiesto plných siluet, takže sú na
  tmavej téme svetlejšie a čitateľnejšie. Farbu naďalej preberajú z tlačidla, takže
  reagujú na hover aj na neaktívny stav rovnako ako text vedľa nich.
- **Prehrávacie ikony (spustiť, pauza, stop, ďalší krok) zostávajú plné** – ovládanie
  behu musí byť čitateľné ako jeden tvar aj z odstupu.
- Nový `assets/icons.svg` – prehľad celého setu s názvami, generovaný z tých istých
  path dát, ktoré používa aplikácia.

## [1.72.0] – 2026-08-28

### Pridané
- **Každý profil má jedinečný kód `P-0007`.** Prideľuje sa pri prvom uložení do
  knižnice, je jedinečný v rámci knižnice a **nemení sa pri premenovaní** profilu –
  je to identifikátor, ktorý sa dá napísať do protokolu alebo nadiktovať do
  telefónu (na rozdiel od interného GUID).
- **Kód je aj v názve súboru profilu**: `P-0007 Sweep -40…150.json`. Priečinok sa
  tak triedi v poradí, v akom profily vznikli, a z názvu súboru je hneď jasné,
  o ktorý profil ide.
- Kód sa zobrazuje vo výbere profilu na karte, v zozname profilov (aj v strome aj
  v náhľade) a v rýchlom profile pod názvom; export profilu má kód v názve súboru.

### Zmenené
- Profily uložené pred touto verziou dostanú kód automaticky pri prvom načítaní –
  číslujú sa od najstaršieho, takže `P-0001` je najstarší profil v knižnici.
- Duplikát profilu („COPY“) a prevod na SIKA formát vytvárajú nové položky
  knižnice, takže dostanú vlastný kód; úprava existujúceho profilu si ten svoj
  ponechá.

## [1.71.0] – 2026-08-28

### Zmenené
- **Každý profil je samostatný súbor.** Knižnica v `Dokumenty/Lab Control/Profiles/`
  je odteraz priečinok s jedným JSON súborom na profil, pomenovaným podľa profilu
  („Sweep -40…150.json“). Doteraz bolo všetko v jednom `profiles.json` (1,2 MB),
  z ktorého sa nedal jeden profil ani pozrieť, ani skopírovať či poslať bez
  exportu, a každé uloženie prepisovalo celý súbor.
- **Existujúca knižnica sa rozdelí automaticky** pri prvom spustení. Pôvodný
  `profiles.json` sa nemaže – zostane ako záloha `profiles.json.migrated`.
- Premenovanie profilu premenuje aj jeho súbor (starý sa nenechá ležať), dva
  profily s rovnakým názvom dostanú samostatné súbory a poškodený súbor už
  neberie so sebou celú knižnicu – preskočí sa len on.

## [1.70.1] – 2026-08-28

### Opravené
- **Zoznamy sú v tmavom režime.** Aplikácia mala vlastný štýl pre položku zoznamu,
  ale nie pre samotný zoznam, takže sa použila systémová šablóna – biely panel
  s čiernym rámikom, na ktorom svetlý text tmavej témy takmer zanikol. Najviac to
  bolo vidieť na „Posledné záznamy“ v prehliadači záznamov a na zozname interných
  logov SIKA. Zoznam má teraz rovnaké prevedenie ako textové pole (tmavé pozadie,
  jemný rámik, zaoblené rohy).

## [1.70.0] – 2026-08-28

### Pridané
- **Plánovaný čas SIKA profilu obsahuje aj čas ustálenia na teplotu.** SIKA profil
  nemá rampy – kúpeľ si na každý setpoint nabehne sám a výdrž sa začne počítať až
  keď na teplote je, takže súčet výdrží nikdy nebol skutočný čas behu. Nový odhad
  (rýchlosť ohrevu, chladenia nad 0 °C a pod 0 °C, plus pevná rezerva na
  dorovnanie) sa pripočítava v rýchlom profile, v zozname profilov, vo výbere
  profilu na karte, v plánovanom trvaní aj na časovej osi.
- **Administrácia → SIKA – odhad času ustálenia**: štyri hodnoty odhadu sa dajú
  nastaviť. Predvolené (8 / 5 / 2,5 °C/min, 5 min) sú štartovací odhad, nie údaj
  z merania konkrétnych kúpeľov.
- **Meranie skutočného času ustálenia.** Po každom dosiahnutí setpointu sa do
  aplikačného logu zapíše, ako dlho ustálenie trvalo, aký rozsah °C sa prekonal a
  akú priemernú rýchlosť (°C/min) kúpeľ dosiahol – podľa toho sa dá odhad opraviť.

## [1.69.1] – 2026-08-28

### Pridané
- **Karta zariadenia sa pri prejdení myšou rozsvieti** – rámik sa prefarbí na akcentnú
  modrú a okolo karty sa plynulo objaví jemná žiara. Pri troch komorách vedľa seba je
  hneď jasné, na ktorej je kurzor. Platí pre klasickú aj Professional kartu.
  Žiara je nakreslená vo vrstve *za* kartou, takže sa nijako nedotkne ostrosti
  nameraných hodnôt a popiskov na karte.

## [1.69.0] – 2026-08-28

### Pridané
- **Nastavenie vlhkosti v rýchlom profile.** Nový chlievik „Vlhkosť“ – zaškrtnutím
  „Riadiť vlhkosť“ dostane každý krok profilu nastavenú relatívnu vlhkosť (0–100 %,
  krok 5 %) a profil sa uloží ako „Teplota + vlhkosť“, takže ho čisto teplotná komora
  (VT3) neponúkne. Pri načítaní profilu sa vlhkosť prečíta z jeho segmentov. Pre SIKA
  je celý chlievik skrytý – kúpeľ nemá kanál vlhkosti.
- **Import a export sú v rýchlom profile**: „Nový“, „Import…“, „Hromadný import…“,
  „Import knižnice…“, „Export…“ a „Hromadný export…“ sú v hlavičke obrazovky a každé
  tlačidlo má nápovedu, čo presne robí.

### Zmenené
- **„Editor profilov“ je nahradený obrazovkou „Zoznam profilov“.** Je to už len prehľad:
  vľavo knižnica s filtrami (text, tag, zariadenie), vpravo náhľad vybraného profilu –
  ten istý graf ako v rýchlom profile (len na pozeranie, body sa neťahajú), trvanie,
  cyklovanie, zákazník/projekt, snímače a tagy. Upravuje sa tlačidlom
  „✎ Upraviť v rýchlom profile“, ktoré profil otvorí v rýchlom vytvárači.
- **Profily sa vytvárajú a upravujú na jednom mieste** – v rýchlom profile. Zoznam
  profilov ponecháva len prácu s knižnicou: duplikovanie, prevod na SIKA formát,
  mazanie a (pre admina) vymazanie celej knižnice.

## [1.68.0] – 2026-08-28

### Zmenené
- **Na obrazovku sa zmestia tri komory vedľa seba.** Karta zariadenia má 470 px
  (bolo 600), takže tri karty aj s medzerami zaberú ~1450 px namiesto ~1850 px.
- **Predvoľby teplôt sú v bloku 2 × 2** namiesto jedného radu – štyri miesta zostávajú,
  ale vedľa nich sa do toho istého riadku zmestí aj nastavenie vlastnej teploty
  a tlačidlá „Nastaviť“ a „Stop“.
- **Menší obrázok zariadenia** (108 px namiesto 128 px), aby na užšej karte zostalo viac
  miesta na názov a živé hodnoty.
- **Názov zariadenia sa zalamuje namiesto orezania** – celé „Komora 2 — Vötsch VC3 7034
  (teplota + vlhkosť)“ je čitateľné aj na užšej karte.

## [1.67.5] – 2026-08-28

### Zmenené
- **Celé rýchle ovládanie je v jednom riadku a už sa nemá ako zalomiť.** Riadok má tri
  pevné stĺpce: štyri rovnaké miesta na predvolené teploty, potom nastavenie vlastnej
  teploty a napokon tlačidlá „Nastaviť“ a „Stop“. Predtým boli predvoľby tá pružná časť
  a pri nedostatku miesta padali na druhý riadok.
- **Predvolených teplôt sú najviac štyri** (predtým až osem) – karta má pre ne presne
  štyri miesta. Zariadenie, ktoré ich malo uložených viac, si ponechá prvé štyri;
  predvolené sady pre SIKA a POL-EKO sú skrátené na štyri hodnoty.
- **Karta zariadenia má 600 px** – celý pás rýchleho ovládania potrebuje ~490 px, aby
  boli popisky tlačidiel aj hodnoty predvolieb čitateľné bez orezania.

## [1.67.4] – 2026-08-28

### Opravené
- **Hodnota vlastnej teploty sa orezávala** („2:“ namiesto „25“) – pole bolo úzke 66 px,
  teraz má 80 px, takže sa doň zmestí aj štvorznaková hodnota (−100).
- **Chýbala medzera medzi živými hodnotami a sekciou „Rýchle ovládanie“** – rámik
  s teplotami prepísal spodný okraj štýlu sekcie, hlavička karty má teraz vlastné
  odsadenie.

### Zmenené
- **Karta zariadenia má 560 px** (bolo 500). Riadok rýchleho ovládania potrebuje
  ~470 px a do pôvodnej šírky sa nezmestil – preto sa predvoľby zalamovali a hodnota
  orezávala. Karty sú v zalamovacom paneli, takže na širokej obrazovke ich vedľa seba
  je stále rovnako veľa.
- **„✕ Zrušiť profil“ je hore** pri nadpise „Testovací profil“, nie pod ovládacími
  tlačidlami.
- **„Počet cyklov“ je vedľa ovládacích tlačidiel** profilu a jeho pole je menšie
  (64 px, hodnota 1–9). Profil uložený s vyšším počtom cyklov sa naďalej načíta
  a zobrazí správne – obmedzenie platí len pre ručné zadanie.

## [1.67.3] – 2026-08-28

### Zmenené
- **Rýchle ovládanie je v jednom riadku.** Predvoľby teplôt, vlastná teplota, „Nastaviť“
  aj „Stop“ sú teraz na jednej linke – predvoľby vypĺňajú ľavú časť, hodnota a obe akcie
  sú ukotvené vpravo. Predvoľby sú tá pružná časť, takže pri užšej karte sa zalomia ony
  a akcie zostanú v riadku. „Stop“ zostáva na pravom okraji, ďaleko od „Nastaviť“.
- **Menšie prvky a medzera pod nadpisom.** Predvoľby 32 px, akcie 34 px, celý riadok je
  odsadený od nadpisu „Rýchle ovládanie“.
- **„◈ FBG Kalibrácia“ je opäť plnohodnotné tlačidlo s názvom.** Ikonkové zostali len
  zámok a odkaz na web zariadenia – ich význam nesie glyf a tooltip, takže sa celý rad
  do 500 px karty zmestí.

## [1.67.2] – 2026-08-28

### Opravené
- **Karta zariadenia sa už nezalamuje a neprelieva.** Predchádzajúca verzia dala do
  hlavičky štyri tlačidlá s popiskami, čo sa do pevných 500 px karty nezmestilo –
  „Nastaviť / ovládať“ spadlo na druhý riadok a „Stop“ pod „Nastaviť“. Sekundárne akcie
  (zámok, web, FBG kalibrácia) sú teraz ikonkové s tooltipom, popisok si necháva len
  primárna akcia, a rad sa zmestí na jeden riadok.
- **„Nastaviť“ a „Stop“ sú opäť v jednom riadku** – popisok „Vlastná teplota“ sa presunul
  nad riadok, takže sa hodnota aj obe akcie zmestia vedľa seba. Stop zostáva odtlačený
  na pravý okraj, aby sa netrafil omylom.

### Zmenené
- **Menšie ovládacie prvky.** Predvoľby teplôt 34 px (namiesto 40), tlačidlá behu
  ▶ ⏸ ⏭ ⏹ 52×44 px (namiesto 62×54) s 21 px ikonkami, jemnejšie zväčšenie pri prejdení
  myšou (1,06× namiesto 1,1×), aby sa susedné tlačidlá nedotýkali. Klikacie ciele
  zostávajú nad 28 px podľa ergonomických pravidiel.

## [1.67.1] – 2026-08-28

### Zmenené
- **Krajšie a väčšie tlačidlá rýchleho ovládania.** Predvoľby teplôt sú vyššie
  (40 px, min. 76 px široké) s farebným prechodom pri prejdení myšou a plnou akcentnou
  výplňou pri stlačení – je na nich vidieť, že sú to akcie na jedno klepnutie, nie
  hodnoty. „Nastaviť“ a „Stop“ dostali ikonky (teplomer / vypnutie) a sú vyššie (40 px).
- **Nastaviť a Stop už nie sú vedľa seba natesno** – deštruktívnu akciu oddeľuje výrazná
  medzera, aby ju operátor v rukaviciach netrafil omylom.
- **Karta zariadenia nie je natlačená.** Živé hodnoty (teplota, setpoint, rozsah) sú vo
  vlastnom rámiku oddelenom od stavových riadkov, nie nalepené hneď pod nimi.
- **Tlačidlá v hlavičke karty sa zalamujú** namiesto toho, aby sa navzájom stláčali –
  zámok sa pri užšej karte orezával na „◫ …“.

## [1.67.0] – 2026-08-28

### Pridané
- **Prevod Vötsch profilu do SIKA formátu.** V knižnici profilov pribudlo tlačidlo
  „⇄ Previesť na SIKA profil“: teploty a doby výdrže zostanú, rampy medzi nimi sa
  vynechajú (kúpeľ si na setpoint nabehne sám). Susedné platá na rovnakej teplote sa
  zlúčia do jedného, cyklovaná oblasť sa prepočíta na kratší zoznam a vlhkosť sa
  zahodí. Ukladá sa ako **nový** profil – pôvodný zostáva nezmenený.
- **Vo výbere profilu je vidieť, pre aké zariadenie profil je** (Vötsch / SIKA /
  univerzálny) – ako štítok pri každom profile v zozname aj v náhľade.
- **Tlačidlo „✎ Upraviť v rýchlom profile“** priamo v náhľade výberu profilu – otvorí
  vybraný profil v rýchlom vytvárači, takže sa nemusí znovu hľadať v knižnici.
- **Prepočet minút na hodiny** pri všetkých časových poliach rýchleho vytvárača a ako
  stĺpec „≈ hodiny“ v tabuľke segmentov. Je to **iba vizuálna pomôcka** – ukladá sa
  naďalej hodnota v minútach.
- **Predvoľby typov snímačov.** Pole „Snímače“ v rýchlom vytvárači aj v knižnici ponúka
  katalóg SYLEX snímačov (DTP-01, TP-01, SAT-0x, SWA-0x, STS-xx, DSS-0x, …). Príslušenstvo
  rady S-line (Scan, Switch, Splitter, Comp, Battery Pack) v zozname zámerne nie je –
  profil sa preň nerobí. Typy, ktoré už používajú uložené profily, sa do zoznamu pridajú
  automaticky, takže nový typ netreba čakať na novú verziu.

### Zmenené
- **Časová os (timeline) je predvolene skrytá** a zobrazí sa tlačidlom v jej hlavičke;
  voľba sa pamätá. Existujúcim inštaláciám sa nastavenie raz zresetuje, ďalšia voľba
  operátora už platí.
- **Teplota komory, setpoint a rozsah sú vyššie na karte** – presunuli sa k názvu
  zariadenia vedľa obrázka komory, kde bolo prázdne miesto, takže karta ušetrí celú
  jednu sekciu.
- **Ľavý panel rýchleho vytvárača je rozdelený do kategórií** (Zariadenie, Profil,
  Zaradenie profilu, Režim a parametre, Časy krokov, Nábeh a koniec, Cyklovanie,
  Optimalizácia) – každá vo vlastnom rámiku namiesto jedného dlhého zoznamu polí.

### Opravené
- **„Dĺžka plata“ v postupnosti teplôt sa neprejavila na bodoch.** Pole sa správalo len
  ako predvoľba pre novo pridaný bod, takže po nastavení 70 min zostali všetky body na
  30 min. Teraz zmena tejto hodnoty prepíše dĺžku plata **všetkým bodom** postupnosti;
  jednotlivý bod sa dá potom stále upraviť zvlášť.

## [1.66.0] – 2026-08-28

### Pridané
- **Profily majú typ zariadenia – Vötsch alebo SIKA.** Rýchly vytvárač profilov sa
  na začiatku pýta, pre aké zariadenie profil vzniká. Pri SIKA sa negenerujú žiadne
  rampy: kúpeľ si na setpoint nabehne sám, takže profil je len zoznam **teplôt
  s dobou výdrže (dwell)**. Pri Vötschi zostáva pôvodné správanie (nábeh + plato).
- **Profily sa filtrujú podľa zariadenia.** Karta komory aj okno FBG kalibrácie
  ponúkajú len profily svojho typu, takže sa dva rôzne druhy profilov nemiešajú.
  V knižnici profilov pribudol filter aj políčko „Zariadenie“. Profily uložené
  predtým zostávajú *univerzálne* a ponúkajú sa všade – nič zo starej knižnice
  nezmizne.
- **Tlačidlo „FBG kalibrácia“ je pri každom zariadení** (karta na hlavnej obrazovke,
  Professional karta aj obrazovka „Nastaviť / ovládať“) a otvorí kalibračné okno
  rovno s tým zariadením. Plávajúce tlačidlo v rohu hlavnej obrazovky sa zrušilo.
- **SIKA: „Zapnúť cez sieť“.** Remote Control sa dá skúsiť zapnúť priamo zo softvéru
  (`Com_ExternWriteFlag = 1`) namiesto chodenia k displeju prístroja. Výsledok sa
  overí spätným čítaním registra – firmvér, ktorý to dovolí len z panela, je
  ohlásený a nič sa netvári ako úspech.

### Zmenené
- **SIKA Remote Control je vidieť vždy**, nielen keď je vypnutý: zelený stav =
  zápisy prejdú, oranžový = iba monitoring.
- **Manuál a profil sa navzájom vypínajú.** Kým beží profil, manuálne ovládanie je
  neaktívne; kým je zariadenie riadené manuálne, sú neaktívne ovládače profilu.
  V oboch prípadoch je na karte napísané, čo treba zastaviť.
- **Zastavenie profilu vynuluje čas aj graf** – progres, odpočet, krok, značka
  „teraz“ aj živý graf začínajú od nuly, nezostáva na karte časová os predošlého behu.
- **Výber profilu na karte je cez celú šírku** okna, takže je vidieť podstatne viac
  z názvu profilu; ovládacie tlačidlá sa presunuli pod neho.
- **Väčšie a krajšie tlačidlá behu** (▶ ⏸ ⏭ ⏹) – vektorové ikony namiesto znakov,
  výraznejšie farebné odlíšenie a hover.
- **Rýchle spustenie profilu sa zrušilo.** Profil sa spúšťa výberom v zozname a
  tlačidlom ▶; „✕ Zrušiť profil“ zostáva pri ovládacích tlačidlách.
- **Ľavý panel rýchleho vytvárača je širší a dá sa potiahnuť** na požadovanú šírku.

### Opravené
- **Rozbalovacie zoznamy ukazovali názov triedy namiesto hodnoty** (napr.
  `VotschVc3.Core.Profiles.TestProfile`). Tmavá šablóna `ComboBox` neprepájala
  `ItemTemplateSelector`, ktorým WPF implementuje `DisplayMemberPath`, takže
  zavretý zoznam padal na `ToString()`. Týkalo sa to všetkých zoznamov v appke,
  najviac bolo vidno v okne FBG kalibrácie.
- **FBG kalibrácia hlásila chybu bindingu na `PortName`.** `Run.Text` je v WPF
  štandardne obojsmerný binding, takže sa viazal na read-only property.
- **Zatvorenie okna FBG kalibrácie padalo** s `Cannot set Visibility to Visible or
  call Show, ShowDialog, Close … while a Window is closing`. Skutočné zatvorenie sa
  teraz odloží cez dispatcher a chyba pri uvoľňovaní zariadení okno nezablokuje.
- **„Prehľad plat“ v náhľade profilu nemal scrollbar** – dlhý profil pretekal mimo
  okna.
- **Nedostupné zariadenie už nevyskakuje ako Windows notifikácia.** Časový limit
  pri pripájaní, nedostupná IP alebo spadnutý socket sa zapíšu do stavového riadka
  karty a do app logu, ale netlačia sa na plochu – automatické znovupripájanie to
  skúša ďalej, takže jedna vypnutá komora predtým vypisovala tú istú hlášku dokola.
  Skutočná strata spojenia počas behu zostáva alarmom.

## [1.65.0] – 2026-08-26

### Pridané
- **Nastavenia e-mailu sa dajú dať do premenných prostredia** – a tie prežijú nový
  build aj preinštalovanie appky. Prázdne pole si hodnotu vezme z premennej,
  vyplnené pole má prednosť:
  `BREVO_API_KEY`, `EMAIL_SENDER`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`,
  `EMAIL_PASSWORD`. Sú to tie isté názvy, aké používa FOS Dashboard, takže sa
  nastavujú raz pre obe aplikácie a žiadny kľúč ani heslo nemusí ísť do nastavení
  aplikácie (ani do repozitára).
- Panel v administrácii píše, **ktoré hodnoty prišli z premenných prostredia**, aby
  prázdne políčko nevyzeralo ako nenastavené.
- Nový návod [`docs/EMAIL_NOTIFIKACIE.md`](docs/EMAIL_NOTIFIKACIE.md): čo je povinné
  pre ktorý spôsob odosielania, príkazy `setx`, a najčastejšia príčina neodoslaného
  e-mailu (neoverený odosielateľ v Brevo).

### Zmenené
- **Odosielateľ (from) už nemá napevno predvolené `no-reply@sylex.sk`.** Musí sedieť
  s adresou overenou v Brevo, inak Brevo odmietne odoslanie aj so správnym kľúčom –
  zlá predvoľba to len maskovala. Nová inštalácia si adresu vezme z `EMAIL_SENDER`.
- Chybové hlášky pri odosielaní hovoria aj to, ktorá premenná prostredia
  by problém vyriešila.

## [1.64.0] – 2026-08-26

### Opravené
- **Cyklovalo sa aj záverečné ustálenie na 25 °C.** Karta komory si cyklovanú časť
  profilu odhadovala zo segmentov a vedela odlúpnuť len koncovú rampu – nie dvojicu
  *rampa + hodinové plato*, ktorou rýchly vytvárač profil ukončuje. Pri 2 cykloch
  tak bežala stabilizácia dvakrát, graf ju mal v cyklovanom pásme a celkový čas bol
  nafúknutý. Teraz sa berie **cyklovaná oblasť uložená v samotnom profile**
  (rýchly vytvárač ju zapisuje) a heuristika navyše rozpozná aj záverečnú dvojicu.
  Po zmene počtu cyklov sedí pásmo v grafe, celkový čas aj odhad konca.

### Pridané
- **Potvrdenie pri zásahu do bežiaceho testu.** Pozastavenie, preskočenie plata aj
  zastavenie profilu sa najprv opýtajú a povedia, čo to spraví (⏭ ukončí prebiehajúce
  plato, ⏹ ukončí beh a vypne výkon). Automatické zastavenie pri alarme sa
  nepýta – to musí prejsť aj keď pri komore nikto nestojí.
- **E-mail: panel povie, čo ešte chýba.** Namiesto hľadania metódou „Poslať test“
  je pod nastavením veta typu „⚠ Chýba: API kľúč“. Kontrolujú sa len polia, ktoré
  zvolený spôsob naozaj používa – v režime **BrevoApi** sa SMTP používateľ a heslo
  nepoužívajú vôbec.
- **API kľúč sa dá nechať mimo aplikácie.** Keď je pole prázdne, kľúč sa načíta zo
  systémovej premennej `BREVO_API_KEY`. Do repozitára sa tak nikdy nedostane.

### Zmenené
- **Rad profilov a odložený štart sú na spodku karty a zbalené.** Rozbalia sa
  tlačidlom „▾ Rad profilov a odložený štart“ – predtým boli medzi výberom profilu
  a grafom a odtláčali dole to, čo operátor sleduje.
- **Väčšie ovládacie tlačidlá behu** (▶ ⏸ ⏭ ⏹): 56×48 namiesto 44×40, väčší glyf
  a font so správnymi symbolmi.
- **Väčšie okno výberu profilu** – 560–820 px široké a 640 px vysoké namiesto
  340×400, takže sa dlhé názvy profilov zmestia a netreba scrollovať cez skupiny.

## [1.63.0] – 2026-08-26

### Opravené
- **Priblíženie sa zastavilo na plate alebo rampe.** Limit priblíženia bol 200×,
  takže na viacdňovom profile sa dalo zísť len na ~20-minútový výrez – a po
  narazení na strop koliesko prepadlo do stránky, ktorá sa pod rukami odrolovala.
  Limit je teraz 2000× (do jednej rampy sa dá priblížiť naplno) a **kým je graf
  priblížený, koliesko patrí grafu** – stránka sa už neposunie. Pri celom
  rozsahu koliesko naďalej roluje stránku ako predtým.
- **Koliesko funguje aj nad krivkou, bublinou či pásmom výdrže.** Obsluha je
  teraz na celom grafe a „tunelovaná“ (PreviewMouseWheel), takže ju nemôže
  pohltiť nič, čo je pod kurzorom; dekoratívne prvky grafu navyše myš vôbec
  nechytajú.
- **Zväčšený graf profilu (⛶ Zväčšiť) ukazuje detaily.** Doteraz z neho vypadli
  podfarbené výdrže, body zlomu, zvýraznenie kroku s jeho dĺžkou aj cyklované
  pásmo – čiže presne to, kvôli čomu sa otvára. To isté platí pre fullscreen
  ostatných grafov.

### Zmenené
- **Os teploty ide vždy od minima po maximum celého záznamu.** Pri priblížení sa
  už nepreškáluje na viditeľný výrez – na plate to ukazovalo rovnú čiaru
  uprostred osi 59…61 °C a nedalo sa povedať, kde má profil skutočné maximum
  a minimum. (Ruší to preškálovanie zavedené v 1.58.)
- **Mriežka a popisy času aj v grafoch na hlavnej stránke.** Zvislé čiary na
  čitateľnom kroku (štvrťhodiny / hodiny / dni podľa výrezu) s časom pod každou;
  predtým boli popísané len oba konce osi. Štítok priblíženia hlási aj to, ktorý
  úsek je zobrazený.

## [1.62.0] – 2026-08-26

### Zmenené
- **Rýchly vytvárač sa otvára prázdny.** Po otvorení obrazovky (aj z karty komory)
  začínaš vždy na predvolenom profile **-20…60 °C, 7 medzikrokov** – dovtedy sa
  vrátil rozrobený profil z minula, ktorý sa potom nechtiac uložil cez ten
  načítaný. Existujúci profil si vyberieš v „Načítať existujúci profil“.
- **Pri načítaní profilu sa appka opýta, či prepísať pôvodný.** „Prepísať pôvodný“
  ho pri uložení aktualizuje, „Vytvoriť nový“ ho nechá nedotknutý a uloží nový
  profil (aj keď má rovnaký názov).
- **Odstránené tlačidlo „Editovať profil“.** Komplexná úprava profilov sa robí
  priamo v Rýchlom vytváraču.
- **Priblíženie v grafoch na hlavnej stránke je rovnaké ako v rýchlom profile** –
  tlačidlá ＋ / － / ⤢ sú vpravo hore (predtým prekrývali popis časovej osi dole),
  legenda sa presunula vľavo hore a popis cyklu na spodok pásma.

### Opravené
- **Názov sa po zmene načítaného profilu aktualizuje.** Keď má profil automaticky
  generovaný názov, po načítaní sa generovanie zapne späť (aj s pôvodnou
  predponou), takže zmena rozsahu, plata či cyklov názov prepíše. Ručne napísaný
  názov sa naďalej nechá tak.
- **Náhľad profilu ukazuje pri prechode myšou, či ide o nábeh alebo plato.**
  Krok pod kurzorom sa zvýrazní cez celú výšku grafu a bublina píše napr.
  „↗ Rampa (ohrev) na 60 °C · dĺžka 30 min“ alebo „→ Výdrž (plato) 60 °C ·
  dĺžka 1 h 40 min“; pri cyklovaní aj to, do ktorého opakovania krok patrí.
- **Os teploty je orezaná na profil.** Profil -40…120 °C sa kreslil na osi
  od -100 do 300 °C a krivka sa tlačila do spodnej tretiny grafu. Os už nie je
  natvrdo na štyroch dielikoch – `NiceAxis.Scale` vyberie krok aj počet čiar tak,
  aby zostali okrúhle popisy a čo najmenej prázdna. Platí pre náhľad profilu aj
  pre grafy na hlavnej stránke.

## [1.61.1] – 2026-08-26

### Zmenené
- **Kratšie automatické názvy profilov.** Názov už nevypisuje všetky teploty –
  vždy uvedie len **pokrytý rozsah a počet bodov** (napr. `-20…60 °C · 13 teplôt`)
  a za tým ostatné parametre: plato, rampu, nábeh, koncové plato, cyklovanie
  a celkový čas. Platí to rovnako pre sweep aj pre postupnosť teplôt, takže
  názvy sú krátke a navzájom porovnateľné.
- Jednotlivé teploty zostávajú vo **vete nad náhľadom profilu** (tam sa vypíšu
  spojené šípkou a pri dlhej postupnosti sa skrátia na začiatok … koniec).

## [1.61.0] – 2026-08-26

### Opravené
- **Načítanie existujúceho profilu v Rýchlom vytváraču.** Vybraný profil sa teraz
  načíta **hneď po výbere** – dovtedy sa nič nedialo, kým si nestlačil samostatné
  tlačidlo, takže to vyzeralo, že výber profilu nefunguje a profil sa nedá
  editovať. Tlačidlo zostáva ako „↺ Načítať znovu (zahodiť úpravy)".
- **Načítaný profil sa zobrazí presne tak, ako bol uložený.** Predtým sa hneď po
  načítaní prekreslil z generátora (jedna spoločná dĺžka rampy, jedno koncové
  plato), takže ručne robený alebo importovaný profil vyzeral inak, než čo bolo
  v knižnici. Segmenty načítaného profilu platia, kým naozaj nezmeníš parameter.
  Aj počty segmentov a celkové časy pod grafom sa teraz počítajú z toho, čo je
  v grafe.
- **Generovanie názvu profilu.** Teploty sa spájali spojovníkom, takže postupnosť
  so zápornými hodnotami vyšla ako nečitateľné `-20--10-0-20-40-…`. Teplotné body
  sa teraz spájajú šípkou (`-20→-10→0→20`) a dlhá postupnosť sa skráti na
  začiatok … koniec s uvedeným počtom bodov a pokrytým rozsahom.
- **Popis profilu pri rôznych nastaveniach** (hlavne v režime „Postupnosť teplôt“).
  Názov aj veta nad náhľadom teraz vždy uvádzajú dĺžku plata (pri rôznych dĺžkach
  rozsah „30 min–1 h“), dĺžku rampy, úvodný nábeh, koncové bezpečnostné plato,
  cyklovanie aj celkový čas. Obe sa skladajú z tých istých pravidiel
  (`Core/Profiles/QuickProfileNaming`) a sú pokryté testami, takže si navzájom
  neodporujú.
- **Tlačidlá na presúvanie bodov postupnosti.** Boli hneď vedľa ▲/▼ číselných polí
  a nedali sa od nich rozoznať; poradové číslo bodu sa po presune neaktualizovalo
  (WPF neprepočítava `AlternationIndex` po presune), takže to vyzeralo, že sa nič
  nestalo. Presun je teraz vľavo pri poradovom čísle (↑ / ↓), číslo sa prepočíta
  a na kraji zoznamu sú tlačidlá zošednuté.

### Zmenené
- **Cyklovanie sa v grafoch konečne ukazuje ako beh.** V grafe profilu (rýchly
  vytvárač aj editor knižnice) sa cyklované telo vykreslí na časovej osi
  **toľkokrát, koľkokrát naozaj pobeží** – dovtedy sa kreslil len jeden priebeh
  s podfarbeným úsekom, hoci celkový čas hlásil násobok. Každé opakovanie má
  vlastné podfarbenie, oddeľovač a číslo (⟲ 2/4). Ťahať sa dá prvý priebeh,
  ostatné ho kopírujú. Rovnako je rozdelené a očíslované cyklované pásmo v grafe
  profilu na hlavnej stránke.
- **Čitateľnejší graf profilu pri veľa krokoch.** Pribudla popísaná časová os
  (mriežka po štvrťhodinách/hodinách/dňoch podľa výrezu), os teploty sa
  zaokrúhľuje na „pekné" hodnoty (namiesto 69,6 / 44,8 / −29,6 °C) a plochy
  výdrže sú jemne podfarbené, takže rampy a plata sa rozoznajú na prvý pohľad.
  Body na úpravu, ktoré by sa prekrývali, sa nekreslia a graf napíše, koľko ich
  je skrytých – po priblížení sa objavia.
- **Jednoduchšie priblíženie grafov.** V grafe profilu aj vo všetkých ostatných
  grafoch pribudli tlačidlá **＋ / － / ⤢** priamo v grafe (netreba koliesko),
  **Shift + koliesko** posúva časovú os a **mini-mapa pod grafom sa dá chytiť
  a ťahať** – na viacdňovom profile je to podstatne rýchlejšie než posúvanie
  ťahaním krivky.

## [1.60.0] – 2026-08-26

### Pridané
- **Zvýraznenie kroku pod kurzorom v grafe profilu.** Keď prejdeš myšou po krivke,
  celý segment (rampa alebo plato) sa podfarbí červeným pásmom cez celú výšku
  grafu a ohraničí sa – je jasné, ku ktorému kroku odčítaná hodnota patrí.
- Bublina pri kurzore okrem teploty a času ukazuje aj **typ kroku a jeho
  naprogramovanú dĺžku**: „→ Výdrž (plato) · dĺžka 45 min".

### Zmenené
- **Doladené priblíženie grafov.** Koliesko reaguje podľa toho, o koľko sa naozaj
  otočilo (jedna zarážka = 1,4×, touchpad plynulo namiesto skokov), pri
  priblížení sa ukazuje kurzor posunu a mini-mapa výrezu je hrubšia a čitateľnejšia.
- **Os hodnôt sa zaokrúhľuje na „pekné" hodnoty** (`Core/Charting/NiceAxis`):
  namiesto popisov 68 / 75,3 / 82,7 / 90 / 97,3 °C sú to 60 / 70 / 80 / 90 / 100 °C
  a os pri posúvaní priblíženého grafu neposkakuje. Pri úzkom výreze (napr. plato
  s kolísaním 0,5 °C) si zachová rozlíšenie.

### Opravené
- Veľký tooltip nad grafom profilu na hlavnej stránke prekrýval krivku aj
  odčítanie pod kurzorom – presunutý na popis „Graf profilu" nad grafom.

## [1.59.0] – 2026-08-26

### Pridané
- **Vždy viditeľný aktuálny krok profilu.** Počas behu sa na hlavnej stránke,
  v profesionálnom dashboarde aj v detaile komory ukazuje, či práve beží
  **nábeh (rampa)** alebo **výdrž (plato)**, na akú teplotu, **ako dlho krok trvá**
  a **koľko do konca kroku zostáva** – napr.
  „→ Plato 85,0 °C · krok trvá 30 min · zostáva 12:34".
- Odpočet kroku beží každú sekundu, nezávisle od intervalu hlásení bežca profilu.
- Pri garantovanej výdrži, kým komora dobieha na teplotu, sa ukazuje
  „⏳ čaká na ustálenie" – odpočet kroku začne až po dosiahnutí teploty.
  Pozastavený profil ukazuje „⏸ pozastavené" namiesto bežiaceho odpočtu.
- Pri rampe je rozlíšený smer: „↗ Rampa (ohrev)" / „↘ Rampa (chladenie)".
  Na SIKA, kde je každý krok skok a ustálenie, sa krok ukazuje ako plato.

## [1.58.0] – 2026-08-26

### Pridané
- **Priblíženie kolieskom myši aj v grafe profilu na hlavnej stránke** a vo
  všetkých ostatných grafoch (`ChartView`): živá teplota/vlhkosť, prehliadač
  záznamov, teplomery. Zoom ide okolo kurzora, **ťahanie** posúva časovú os,
  **dvojklik** vráti celý rozsah; aktuálny výrez ukazuje mini-mapa a štítok.
- Pri priblížení sa **os hodnôt preškáluje na viditeľný výrez**, takže na plate
  vidno aj malé kolísanie namiesto rovnej čiary uprostred celého rozsahu.
- Výrez je uložený v jednotkách dát (minúty), takže **živý graf sa pri pribúdaní
  meraní neposúva** pod rukami – zostane tam, kam si sa priblížil.

### Zmenené
- Logika výrezu časovej osi je v `Core` (`Charting/TimeAxisViewport`) a je pokrytá
  testami; editor profilu aj `ChartView` používajú tú istú implementáciu.

## [1.57.0] – 2026-08-26

### Pridané
- **Priblíženie grafu kolieskom myši.** V náhľade profilu (rýchly profil, knižnica
  profilov aj detail komory) koliesko myši približuje časovú os okolo kurzora –
  dlhé profily už nie sú stlačené do pár pixelov na segment. Keď je graf
  priblížený, **ťahaním prázdnej plochy** sa posúvaš po profile a **dvojklik**
  vráti celý profil. Aktuálny výrez ukazuje mini-mapa pod grafom a štítok
  „🔍 2,4× · 12 min – 45 min".
- Ťahanie bodov (teplota/trvanie) aj odčítanie hodnôt pod kurzorom fungujú
  rovnako aj v priblíženom pohľade. Keď už nie je čo približovať, koliesko
  normálne roluje stránku pod grafom.

## [1.56.1] – 2026-08-26

### Opravené
- **Build už nezlyháva na zamknutom `VotschVc3.Agent.exe`.** Bridge Agent beží ako
  samostatný proces na pozadí, takže si držal zamknutý vlastný `.exe`/`.dll`
  a `dotnet build` / F5 končil chybou *„The process cannot access the file …
  because it is being used by another process"* pri kopírovaní do
  `bin\…\LabBridge`. Oba projekty (`VotschVc3.App` aj `VotschVc3.Agent`) teraz
  pred prepísaním výstupu ukončia bežiaceho agenta cez
  `build/Stop-BridgeAgent.ps1`.
- Skript zámerne ukončí **iba proces spustený presne z daného build výstupu** –
  agenta nainštalovaného inde (produkčná inštalácia, naplánovaná úloha z iného
  priečinka) nechá bežať. Ak sa proces ukončiť nedá, iba to ohlási a build
  pokračuje ďalej.

## [1.56.0] – 2026-08-25

### Opravené
- **Bridge používa aktuálne nastavenia desktopovej aplikácie.** Pri štarte načíta
  `Documents\\Lab Control\\chambers.json` a pre tri komory a dve SIKA prevezme
  reálny názov, IP, port, adresu a mapovanie kanálov. POL-EKO ignoruje.
- **Synchronizácia profilovej knižnice do Dashboardu.** Heartbeat prenáša profily
  z `Profiles\\profiles.json` vrátane segmentov, cyklovania, rampy, guaranteed
  soak a tolerancií.
- Zachovaná je nová observabilita agenta cez `bridge-status.json`; neúspešný
  Dashboard endpoint sa ďalej zobrazuje ako reálna chyba, nie ako online stav.

## [1.55.1] – 2026-08-25

### Odstránené
- **Mŕtvy druhý mechanizmus obnovy behu.** `ProfileRunState` a `ProfileRunStateStore`
  (ukladanie do `runstate.json`) zostali v repe ako pozostatok súbežnej implementácie,
  ktorá sa do aplikácie nikdy nezapojila – produkčný kód ide výhradne cez
  `ProfileRunCheckpoint` / `ProfileRunCheckpointStore`. Odkazovali na ne už len testy.
  Dve súbežné „stavy behu" v tom istom priečinku boli presne to, čo pri zlučovaní
  vetiev spôsobovalo zámeny, takže odchádzajú.
- Z `ProfileResumeTests` odstránené tri testy, ktoré overovali len tento mŕtvy pár.
  Tri testy obnovy samotného `ProfileRunner` zostávajú nedotknuté.

## [1.55.0] – 2026-08-25

### Pridané
- **Preskočenie plata počas behu.** Nové tlačidlo „⏭“ (na karte zariadenia, v
  profesionálnom dashboarde aj v detaile komory) ukončí plato, ktoré práve beží,
  a beh pokračuje ďalším nábehom a platom v poradí. Zvyšok profilu sa nemení –
  skráti sa iba toto jedno čakanie. Akcia sa zapisuje do auditu.
- Preskočiť sa dá aj čakanie na **garantovanú výdrž**, keď komora cieľovú teplotu
  nedosiahne a beh by inak čakal donekonečna.
- ⚠️ Počas **nábehu (rampy) je tlačidlo neaktívne** zámerne: skrátenie rampy by
  nechalo ďalší segment zapísať svoju cieľovú teplotu okamžite, teda skok
  namiesto riadeného nábehu. Požiadavka podaná počas rampy sa zahodí, nikdy sa
  neprenesie na nasledujúce plato. Na SIKA, kde je každý krok skok a ustálenie,
  je tlačidlo aktívne pri každom segmente.

## 1.54.9

- Obnovená rampa pokračuje z presného uloženého setpointu: checkpoint teraz
  uchováva pôvodnú štartovaciu teplotu a vlhkosť segmentu, takže sa rampa po
  reštarte neprepočíta od aktuálne nameranej teploty komory.
- Po obnove sa do ďalšieho checkpointu zapisuje celkový uplynutý čas segmentu,
  nie iba čas od reštartu. Staršie checkpointy odvodia začiatok rampy z
  predchádzajúceho segmentu profilu, ak je to možné.

## 1.54.8

- Opravené balenie Bridge Agenta pri spustení cez F5: do `LabBridge` sa teraz
  kopíruje kompletný výstup vrátane `VotschVc3.Agent.dll`, `.deps.json` a
  `.runtimeconfig.json`, nie iba nefunkčný EXE host.
- Desktopová aplikácia uprednostní kompletný Agent v `LabBridge`; proces už
  neskončí pred vytvorením `bridge-status.json`.

## 1.54.7

- Zostavenie/spustenie desktopovej aplikácie teraz automaticky zostaví aj samostatný
  `VotschVc3.Agent`, takže Bridge funguje aj pri spustení iba `VotschVc3.App` cez F5.
- Hľadanie agenta kontroluje výstupy Debug aj Release. Chýbajúca naplánovaná úloha
  Windows preto už neblokuje lokálne spustenie existujúceho agenta.

## 1.54.6

- Desktopová aplikácia pri prvom spustení sama vytvorí
  `Documents\Lab Control\bridge.json` zo zabudovaného bezpečného vzoru; vytvorenie
  konfigurácie už nezávisí od úspešného štartu samostatného agenta.
- Po spustení naplánovanej úlohy sa overí skutočný proces agenta. Ak úloha síce
  vráti úspech, ale agent nenabehne, aplikácia skúsi lokálny executable.

## 1.54.5

- Prerušený profil sa po spustení aplikácie a úspešnom pripojení zariadenia
  automaticky obnoví od posledného uloženého segmentu, cyklu a času v segmente.
- Pred automatickým pokračovaním sa vykoná živé čítanie zariadenia. Pri SIKA sa
  profil spustí iba so zapnutým Remote Control; pri chybe zostáva kontrolný bod
  zachovaný a znovu sa zobrazí možnosť pokračovať.

## 1.54.4

- FOS Dashboard Bridge Agent sa teraz automaticky spustí pri otvorení desktopovej
  aplikácie. Aplikácia najprv overí, či agent už nebeží, aby nevytvorila duplicitný
  proces.
- Preferuje sa nainštalovaná naplánovaná úloha Windows; pri vývoji alebo prenosnej
  inštalácii sa aplikácia pokúsi nájsť a spustiť `VotschVc3.Agent.exe` priamo.

Všetky podstatné zmeny v tomto projekte. Formát vychádza z
[Keep a Changelog](https://keepachangelog.com/), verzie podľa
[SemVer](https://semver.org/lang/sk/).

## [1.54.3] – 2026-08-25

### Opravené
- Prerušený profil sa načíta automaticky už pri štarte aplikácie, aj kým sa
  zariadenie ešte pripája. Karta okamžite zobrazí jeho názov, segmenty a graf.
- Po potvrdení obnovy sa presný profil z checkpointu načíta do editora a graf
  zostane viditeľný počas pokračujúceho behu.

## [1.54.2] – 2026-08-25

### Pridané
- Administrácia zobrazuje živý stav FOS Dashboard Bridge: online/offline,
  cieľovú URL, čas heartbeat-u, verziu agenta, stav konfigurácie a poslednú chybu.
- Tlačidlá na obnovenie stavu, spustenie naplánovanej úlohy Bridge a otvorenie
  priečinka s `bridge.json`.
- Bridge zapisuje bezpečný lokálny stav do `Dokumenty/Lab Control/bridge-status.json`.

## [1.54.1] – 2026-08-25

### Opravené
- **E-mailové notifikácie podľa FOS Dashboard.** Pribudol natívny Brevo HTTP
  API transport cez HTTPS/443 so správnou hlavičkou `api-key` a Brevo payloadom.
- Predvolený odosielateľ je `no-reply@sylex.sk`; staré prázdne nastavenia sa
  automaticky doplnia. SMTP zrozumiteľne overí odosielateľa, login a SMTP key.

## [1.54.0] – 2026-08-25

### Pridané / zmenené
- **Odkaz na webové rozhranie zariadenia.** Karty zariadení aj detail komory majú
  tlačidlo „🌐 Web“, ktoré otvorí vstavanú stránku prístroja (`http://<IP>/`)
  v predvolenom prehliadači. Komunikačný port (ASCII-2, REST-API) sa zámerne
  nepoužíva – operátorské rozhranie beží na štandardnom HTTP porte.
- **Interné SIKA merania a ich export.** Na karte Záznam pribudol zoznam meraní
  uložených priamo v prístroji (`getTaskLog`) a stiahnutie vybraného záznamu
  (`getTaskLogs?taskid=…`) do kompletného CSV – bez stráty jediného bodu,
  podvzorkuje sa iba graf v UI.
- **Ochrana Remote Control.** Kým `Com_ExternWriteFlag` nepotvrdí zapnutý Remote
  Control na prístroji, ovládanie SIKA je zablokované (setpoint, START/STOP,
  zápis registra) a na karte je to vidieť; monitoring beží ďalej. Nečitateľný
  príznak sa nikdy nepovažuje za povolenie. Kontrola je v oboch vrstvách –
  zakázané príkazy v UI a čerstvé overenie v Core tesne pred každým zápisom.
- **Dokončovací e-mail profilu prepracovaný.** Viac adresátov (oddelených
  bodkočiarkou alebo čiarkou), predvoľby pre Brevo SMTP, HTML šablóna s grafom
  teploty a CSV log v prílohe. Zlyhanie e-mailu alebo logu nikdy nepreruší
  ovládanie komory ani nezmení výsledok dokončeného profilu.
- Zdokumentované overené SIKA endpointy v `docs/DEVICE_INTEGRATIONS.md`
  a nový projektový skill `chamber-device-integrations`.

## [1.53.1] – 2026-08-25

### Opravené
- **Vrátené opravy pripojenia na SIKA, ktoré sa stratili pri včerajšom zlučovaní
  vetiev.** Vetva `sika-connection-temperature` bola do `main` zlúčená, ale
  konflikt v `SikaTpClient.cs` sa vyriešil v prospech druhej strany, takže
  samotné opravy v kóde neskončili. Prenesené sú teraz na aktuálny (novší,
  `setRegister`) zápis setpointu:
  - **Obídenie systémovej HTTP proxy** (`SocketsHttpHandler.UseProxy = false`).
    Toto bola hlavná príčina „pripojenie nefunguje“: s firemnou proxy na PC
    zomreli požiadavky na lokálnu IP kúpeľa v proxy, kým zariadenia po surovom
    TCP fungovali ďalej.
  - **Test pripojenia cez lacný `getRegister`** namiesto `getInfoReport`, ktorého
    generovanie na prístroji trvalo dlhšie než timeout pripojenia. Ak niečo na
    porte odpovie, ale nie je to REST-API, hlási sa to zrozumiteľne (skontroluj
    port 8081 / povolenie REST-API) namiesto tichého zlyhania.
  - **Opakovanie pri výpadku** (3 pokusy po 350 ms) pre jednorazové príkazy –
    pripojenie, zápis setpointu, START/STOP. Vstavaný webserver kúpeľa občas
    odpovie sporadickým 404, jeden zádrhel už nezhodí celú operáciu. Živé
    načítavanie sa neopakuje, to sa aj tak opýta znova.
  - **`Connection: close`** pri každej požiadavke (keep-alive proti vstavanému
    serveru vracal staré odpovede).
  - **Overenie zapísaného setpointu** spätným čítaním `TRset_SP`. Prístroj vie
    zápis potvrdiť a napriek tomu ho ignorovať (ručný režim, prebiehajúca
    kalibrácia, zakázané vzdialené ovládanie) – doteraz to operátor videl len
    tak, že sa kúpeľ nerozbehol. Teraz sa to ohlási ako chyba.
  - Zrozumiteľné slovenské hlášky pri odmietnutom spojení, nedostupnom prístroji
    a nepreložiteľnej adrese.
- Ponechaný je novší, na prístroji overený spôsob zápisu setpointu
  (`Task_SetPointList` + `TRset_SP` cez `setRegister`) aj funkčné `StopAsync`
  (`stopCurrentTask` + `System_ReglerOnOff=0`) – tie sú novšie než stratená vetva.

## [1.53.0] – 2026-08-25

### Opravené
- **Načítanie existujúceho profilu do rýchleho vytvárača.** Načítaný profil sa už
  neprevádza vždy na plochý zoznam bodov – nová analýza `QuickProfileShape`
  rozpozná symetrický sweep a doplní jeho skutočné parametre (rozsah od–do, počet
  krokov a krok v °C, dĺžku plata a rampy, dvojitý vrchol, spiatočnú vetvu).
  Profil sa tak dá znova upravovať v režime „Sweep (rozsah)“, nie iba po bodoch.
- **Nábeh a bezpečnostné ukončenie sa pri načítaní nestrácajú.** Úvodná rampa a
  záverečné plato na bezpečnej teplote sa rozpoznajú a zapnú späť na svojich
  prepínačoch, takže opätovné uloženie vytvorí ten istý profil (predtým sa dĺžka
  úvodnej rampy pri uložení stratila).
- Profily, ktoré sweep nie sú (rôzne dĺžky plat, iba klesajúce), sa naďalej načítajú
  ako postupnosť teplôt so zachovanou dĺžkou plata pri každom bode.

### Pridané
- **Skupina „🕘 Najnovšie“ vo výbere profilu.** Výber testovacieho profilu (na karte
  komory aj v rýchlom vytváraču) má hore rozbalenú skupinu s ôsmimi naposledy
  vytvorenými alebo upravenými profilmi. Dátum poslednej úpravy sa ukladá do
  knižnice (`UpdatedAt`), staršie profily sa radia podľa dátumu vytvorenia.

## [1.52.0] – 2026-08-24

### Integrované
- **Zlúčené všetky zostávajúce vzdialené vývojové vetvy.** História vetiev je
  obsiahnutá v `main`; konflikty zachovávajú novšie implementácie a aktuálnu
  zostavu troch komôr a dvoch SIKA zariadení.
- **Obnovenie rozbehnutého profilu po páde alebo výpadku.** Checkpoint uchováva
  profil, cyklus, segment a uplynutý čas. Starší aj novší formát pozície behu sú
  spätne kompatibilné a obnovená výdrž neopakuje už dokončený guaranteed soak.
- **Fullscreen grafy a branding.** Grafy komory a teplomerov možno otvoriť v
  samostatnom maximalizovanom okne; doplnené sú uložené SVG/PNG Sylex assety.
- **Optimalizácia knižnice profilov a Ganttu.** Prenesené boli nekonfliktné
  optimalizácie uloženia, cyklických profilov, časovej osi a navigácie.

### Bezpečnosť konfigurácie
- POL-EKO zostáva v aktuálnej prevádzkovej konfigurácii skryté a bridge ho
  nepublikuje ani nepripája; predvolená flotila zostáva 3 komory + 2 SIKA.

## [1.51.0] – 2026-08-24

### Pridané
- **Lab Control Bridge pre FOS Dashboard.** Nový Windows agent používa existujúce
  jadro Chamber FOS Soft na živé čítanie a bezpečné ovládanie troch komôr a dvoch
  SIKA zariadení, ASL F100 cez USB/COM, spúšťanie a riadenie profilov a odosielanie
  telemetrie do webového Dashboardu cez odchádzajúce HTTPS spojenie.
- **Bezpečný prístup webu k lokálnym a sieťovým priečinkom.** Agent indexuje iba
  explicitne povolené korene, podporuje obojsmerný prenos súborov, blokuje path
  traversal, reparse pointy a zápis spustiteľných súborov. Absolútne lokálne cesty
  sa do webu neposielajú.
- **Inštalačný návod a automatické spustenie.** Pribudol vzor `bridge.json`, detailná
  prevádzková dokumentácia a PowerShell skript na vytvorenie Windows Scheduled Task.

## [1.50.0] – 2026-08-19

### Pridané / zmenené
- **Rýchly vytvárač profilov – živý náhľad pri písaní.** Číselné polia (Od/Do,
  dĺžka plata/nábehu, počet cyklov, ...) teraz aktualizujú graf a súhrn priamo
  počas písania platnej hodnoty (nie až po opustení poľa) – neúplný zápis
  (napr. „-" alebo „22,") sa jednoducho ignoruje, kým ho nedopíšeš. Enter
  vždy vynúti aplikovanie a zarovnanie zapísanej hodnoty ako poistka.
- **Postupnosť teplôt – vlastná dĺžka plata pre každý bod.** Namiesto jedného
  textového poľa so zdieľanou dĺžkou plata je postupnosť teraz zoznam
  editovateľných bodov (teplota + vlastná dĺžka plata), s tlačidlami na
  pridanie bodu, rýchle hromadné pridanie (oddelené bodkočiarkou), presun
  hore/dole a odobratie. Rampa medzi bodmi zostáva zdieľaná.
- **Načítanie existujúceho profilu – presné dáta.** Pri načítaní profilu na
  úpravu sa dĺžka plata každého bodu teraz rekonštruuje z jeho skutočného
  segmentu namiesto toho, aby sa všetkým bodom priradila jedna spoločná
  dĺžka (podľa prvého nájdeného plata) – graf aj zoznam bodov po načítaní
  presne zodpovedajú uloženému profilu, aj keď mal rôzne dĺžky plata.

## [1.49.0] – 2026-08-19

### Pridané / zmenené
- **Rýchly vytvárač profilov – vylepšenia editora.**
  - Prepnutie zo Sweepu na Postupnosť teplôt teraz do textového poľa zapíše
    aktuálne teploty, na ktoré je sweep nastavený, namiesto predvolenej
    (nesúvisiacej) ukážkovej postupnosti.
  - Číselné polia (Od/Do °C, dĺžka nábehu/plata, ...) teraz akceptujú desatinnú
    čiarku aj bodku (napr. „22,5" aj „22.5").
  - Zákazník a Projekt sa dajú vybrať zo zoznamu už použitých hodnôt (alebo
    napísať nové) – predtým to boli len prázdne textové polia.
  - Snímače a Tagy: opravená neviditeľnosť písaného textu vo vstupnom poli
    (rovnaký problém, aký mal predtým NumericStepper) a pole na písanie novej
    hodnoty je teraz širšie.
  - Náhľad profilu (graf) teraz pri prejdení myšou nad krivkou zobrazí
    aktuálnu teplotu a čas v danom bode.

### Opravené
- **Graf profilu pri behu s cyklovaním.** Počas skutočného behu profilu graf na
  dashboarde vždy zobrazoval iba jeden priebeh (bez ohľadu na nastavený počet
  cyklov) – operátor tak nevidel, že profil má cyklovať. Graf teraz pri behu
  aj pred jeho spustením vykresľuje celý cyklovaný priebeh (telo ×počet
  cyklov) a ukazovateľ „Teraz" správne postupuje naprieč všetkými cyklami.

## [1.48.0] – 2026-08-18

### Pridané / zmenené
- **Automatické obnovenie profilu po výpadku prúdu / páde aplikácie.** Beh profilu
  sa priebežne ukladá na disk (segment, cyklus, uplynutý čas, zvyšok fronty). Po
  ďalšom úspešnom pripojení komory appka rozpozná prerušený beh a ponúkne operátorovi
  obnovenie presne od miesta prerušenia (vrátane fronty nasledujúcich profilov);
  segment, ktorý práve čakal na dosiahnutie teploty (guaranteed soak), sa po obnovení
  vždy znovu overí voči reálne nameranej hodnote namiesto slepého pokračovania podľa
  uloženého času. Nová voľba na karte komory (záložka Bezpečnosť) „Po výpadku prúdu
  ponúknuť obnovenie prerušeného profilu" (predvolene zapnuté) umožňuje funkciu pre
  dané zariadenie vypnúť. Explicitné zastavenie profilu (tlačidlo Stop) checkpoint
  vždy zmaže – ponuka na obnovenie sa zobrazí iba pri skutočnom neplánovanom prerušení.

## [1.47.1] – 2026-08-17

### Opravené
- **Rýchly profil – načítanie existujúceho profilu.** Zoznam profilov na výber sa
  načítaval iba raz pri štarte aplikácie, takže profily pridané/zmazané/premenované
  v Editore profilov (alebo v inej relácii) sa v Rýchlom profile neobjavili, kým sa
  aplikácia nereštartovala. Zoznam sa teraz obnoví vždy pri otvorení panela Rýchly
  profil, rovnako ako v Editore profilov.

### Pridané / zmenené
- **Rýchly profil – vyhľadávanie a kategórie pri výbere profilu.** Jednoduchý
  rozbaľovací zoznam v „Načítať existujúci profil" je nahradený rovnakým
  vyhľadávacím výberom profilov ako v Editore profilov a na karte komory: textové
  hľadanie podľa názvu/snímača/zákazníka/projektu/tagu a stromová štruktúra
  zoskupená podľa zákazníka/snímača.

## [1.47.0] – 2026-08-17

### Pridané / zmenené
- **SIKA – krok a výdrž pre celý profil, nielen výdrže.** Garantovaná výdrž (v1.45.0)
  teraz platí pre **každý krok profilu** na SIKA zariadeniach, vrátane rámp: namiesto
  postupného rampovania sa cieľová teplota nastaví naraz, počká sa kým ju kúpeľ
  skutočne dosiahne, a **až potom** sa počíta nastavený čas kroku, kým profil postúpi
  na ďalšiu teplotu.
- **Robustnejšie ukončenie profilu.** Vypnutie výkonu po dokončení/zastavení profilu
  teraz skúša opakovane (3×) a ak zlyhá aj tak, zobrazí viditeľný alarm (nielen záznam
  do logu) – operátor tak vidí, že komora môže stále aktívne regulovať.
- **Rýchly profil – postupnosť teplôt.** Nový režim, kde sa profil zadá ako zoznam
  teplôt oddelený bodkočiarkou (napr. `0;20;30;60;30;20;0`) so zdieľanou dĺžkou
  rampy/plata; vygenerujú sa striedavo rampy a platá.
  **Load & Edit** – existujúci profil z knižnice sa dá načítať priamo do Rýchleho
  profilu na úpravu a opätovné uloženie (aj po premenovaní).
  **Editovateľný graf náhľadu** – bod v grafe sa dá ťahať zvisle (teplota) aj
  vodorovne (trvanie segmentu), rovnako ako v plnom Editore profilov.
- **Vždy zapnutý priebežný záznam teplôt.** Záznam sa už nemusí ručne spúšťať –
  pri každom pripojení komory (aj automatickom znovupripojení) sa automaticky
  otvorí nový záznam v `Lab Control\Recordings`, takže aj bežná manuálna prevádzka
  mimo profilu je zachytená. Zastaví sa pri odpojení a znova naštartuje pri
  ďalšom pripojení.
- **Prehliadač záznamov – zoznam posledných záznamov.** Namiesto len „Otvoriť CSV…"
  cez Windows dialóg teraz vidno priamo v aplikácii zoznam posledných záznamov –
  behy profilov aj priebežné záznamy – zoradené podľa času.

## [1.46.0] – 2026-08-16

### Pridané
- **Nový „Profesionálny" režim ovládania klimatických komôr.** Alternatívny,
  kompaktný dashboard vedľa pôvodného (teraz „Klasický") rozloženia — hustejší
  grid kariet zariadení, horný stavový panel (počet zariadení / aktívnych behov /
  upozornení), **alarm center**, znovupoužitá časová os (Gantt) a odkaz „Detail →"
  na pôvodnú, plnú obrazovku zariadenia (pripojenie, graf, editor profilov, admin…).
  Karty sú **capability-based**: vlhkosť sa zobrazí len pri zariadeniach, ktoré ju
  podporujú (Vötsch s vlhkosťou), a všetky tlačidlá volajú tie isté príkazy ako
  klasické UI (Stop, Nastaviť teplotu, Spustiť/Pauza/Stop profilu) – žiadne
  duplicitné ani fingované ovládanie.
- **Prepínač v Administrácia → „Vzhľad a ovládanie zariadení":** `Klasické` /
  `Profesionálne` / `Kompaktné` rozhranie (`UiSettings.ControlMode`, predvolene
  **Klasické** – existujúci používatelia nevidia žiadnu zmenu, kým si nový režim
  admin sám nezapne). „Kompaktné" použije pôvodné karty, len vždy zmenšené.
  Pridané aj: potvrdenie pred STOP a pred spustením profilu (platí len pre
  profesionálny dashboard) a prepínač alarm centra.
- Bočný panel profesionálneho dashboardu sa dá zbaliť/rozbaliť (uložené).

## [1.45.0] – 2026-08-04

### Pridané / zmenené
- **SIKA – garantovaná výdrž (soak): najprv dosiahnuť teplotu, potom počítať čas.**
  Na zariadeniach SIKA sa teraz pri **každej výdrži** najprv nastaví cieľová teplota
  a **počká sa, kým ju kúpeľ skutočne dosiahne** (s malou toleranciou), a **až potom**
  sa začne odpočítavať nastavený čas výdrže. Tým je čas na danej teplote presný a
  nezávislý od toho, ako dlho trvá nábeh. Predtým to platilo len pre segmenty s ručne
  zapnutým „Soak"; teraz je to pre SIKA automatické pre všetky výdrže (segment s
  vlastným „Soak" si ponechá svoju toleranciu).
- **Tolerancia dosiahnutia teploty (SIKA) je nastaviteľná** v **Admin → „SIKA –
  garantovaná výdrž"** (0,1…10 °C, predvolene **0,3 °C** pre presné ustálenie).
  Uložené v `UiSettings.SikaSoakToleranceC`. Počas čakania na teplotu stav ukazuje
  „⏳ Soak".

## [1.44.0] – 2026-08-04

### Pridané
- **Priečinky s dátami priamo z menu.** V bočnom menu pribudla sekcia
  **PRIEČINKY** s tlačidlami, ktoré otvoria priečinok v prieskumníkovi Windows:
  **Profily** (`Lab Control\Profiles`), **Záznamy teplôt** (`Lab Control\Profilelog`),
  **Logy aplikácie** (`Lab Control\App log`) a **Všetky dáta** (koreň `Lab Control`).

### Zmenené
- **Sušiareň POL-EKO je predvolene skrytá.** Laboratórium ju bežne nepoužíva,
  takže sa už predvolene **nezobrazuje na nástenke ani na časovej osi a
  automaticky sa nepripája**. Zapnúť sa dá v **Admin → Rozloženie nástenky →
  „Zobraziť sušiareň POL-EKO"** – vtedy sa objaví a pripojí. Nastavenie je
  uložené (`UiSettings.ShowPolEko`).

## [1.43.0] – 2026-08-04

### Pridané / zmenené
- **Interval zápisu záznamu teplôt profilu (predvolene 30 s, nastaviteľný).**
  Doteraz sa riadok do CSV záznamu zapisoval pri každom cykle merania (rádovo
  každých pár sekúnd), čo zbytočne nafukovalo súbory. Teraz sa počas behu profilu
  zapisuje **najviac raz za 30 sekúnd**. Hodnota sa dá zmeniť v **Admin →
  „Záznam teplôt profilu" → Interval zápisu do súboru (sekundy)** (1…3600 s),
  platí pre všetky komory a zmena sa prejaví okamžite aj počas bežiaceho profilu.
  Nastavenie je uložené (`UiSettings.ProfileLogIntervalSeconds`).

## [1.42.1] – 2026-08-04

### Opravené
- **CSV exporty – čísla sa už neberú ako dátumy v Exceli.** Hodnoty sa zapisovali
  s desatinnou **bodkou** (`25.4`), no oddeľovač stĺpcov je bodkočiarka (`;`).
  V slovenskom Exceli je desatinný oddeľovač **čiarka**, takže `25.4` sa načítalo
  ako dátum „25. apríl". Teraz sa čísla zapisujú s **čiarkou** (`25,4`), čo Excel
  správne rozpozná ako číslo. Týka sa všetkých CSV záznamov (teplotné logy
  profilov, záznamy komôr aj teplomery ASL F100). Formát je centralizovaný v
  novej triede `CsvFormat`; prehliadač záznamov v aplikácii číta obe podoby
  (bodku aj čiarku), takže staršie súbory sa naďalej otvoria.

## [1.42.0] – 2026-08-04

### Zmenené
- **Denné rozdelenie záznamu aplikácie (log).** Doteraz sa všetko zapisovalo do
  jediného súboru `app.log`, ktorý neobmedzene rástol (u používateľa dosiahol
  ~100 MB). Teraz sa píše **jeden súbor na deň** v prehľadnej štruktúre:
  `Lab Control\App log\<rok-mesiac>\<rok-mesiac-deň>.log`
  (napr. `App log\2026-08\2026-08-04.log`). Súbor sa automaticky mení o polnoci,
  takže žiadny log nerastie donekonečna. Prehliadač logu v aplikácii načíta
  najnovšie záznamy naprieč dennými súbormi (od najnovšieho dňa).
- **Starý `app.log` sa už nemigruje.** Pôvodný obrovský monolitický log sa
  vedome nekopíruje do novej štruktúry (je to presne to, čo denné rozdelenie
  nahrádza); ostáva v pôvodnom priečinku ako záloha.

## [1.41.0] – 2026-08-04

### Zmenené
- **Nové úložisko dát v `Dokumenty\Lab Control`.** Všetky dáta aplikácie sa
  presunuli z pôvodného `Dokumenty\VotschVc3` do prehľadnej štruktúry:
  - `Lab Control\Profiles` – **profily** (`profiles.json` + pribalené predvolené
    profily). Pri prvom spustení sa priečinok vytvorí a naplnia sa predvolené
    profily; novo vytvorené profily sa ukladajú sem.
  - `Lab Control\App log` – **záznamy aplikácie** (`app.log`).
  - `Lab Control\Profilelog` – **teplotné záznamy z bežiacich profilov** (CSV
    per spustenie).
  - Ostatné nastavenia (komory, používatelia, e-mail, audit, UI) sú v koreni
    `Lab Control`.
- **Jednorazová migrácia.** Pri prvom spustení novej verzie sa dáta z pôvodného
  `Dokumenty\VotschVc3` **skopírujú** do novej štruktúry (pôvodný priečinok
  ostáva ako záloha), takže existujúca knižnica profilov a nastavenia sa
  nestratia. Cesty sú centralizované v novej triede `AppPaths`.


### Pridané / zmenené
- **Zlúčenie vetvy profilovej knižnice a rýchleho vytvárača s hlavnou vetvou.**
  Vývoj sa po verzii 1.24.1 rozvetvil na dve línie – jedna pridávala SIKA
  protokol a graf (verzie 1.25–1.27 nižšie), druhá stavala profilovú knižnicu
  a rýchly vytvárač. Táto verzia obe línie spája. Z knižničnej vetvy pribúda:
  - **Vyhľadávanie + stromový výber profilu** na hlavnom paneli (podľa názvu,
    snímača, zákazníka, projektu alebo tagu) namiesto plochej rozbaľovačky.
  - **Rýchly vytvárač profilov:** voliteľný **koncový nábeh na 25 °C**,
    vypnutie výkonu po dokončení, plnošírkové/ukotvené tlačidlá (bez orezania),
    zákazník/projekt, snímače a **tagy** (chip editor), **cyklovanie** (počet
    cyklov, len telo profilu).
  - **Profilová knižnica:** pribalených **226 reálnych profilov** (seed z BEdit),
    stromová knižnica s filtrovaním a rozbaľovaním, hromadný import/export s
    progresom, import/export knižnice do JSON, štandardizácia názvov.
  - **Graf:** prepočet minút na hodiny/dni v hover bubline a na osi, cyklovaný
    úsek v grafe rýchleho vytvárača.
  - Nové okná/komponenty: `BulkImportWindow`, `ProfilePicker`, `TagEditor`,
    `PasswordDialog`, `MarkdownText`; Core: `ProfileNaming`, `ProfileStandardizer`,
    `ProfileFile` + testy.
- Do tejto verzie sú zahrnuté aj všetky zmeny hlavnej vetvy uvedené nižšie
  (SIKA START/STOP a `setRegister` protokol, overené rozsahy, oprava CI,
  graf rampa/výdrž s čítaním teploty myšou, predvolená IP SIKA Sylex
  `10.88.5.226`).

## [1.27.1] – 2026-08-04

### Zmenené
- **SIKA Sylex – predvolená IP zmenená na `10.88.5.226`** (predtým `10.88.5.81`).
  Reset-marker SIKA komôr posunutý na `v3`, aby sa nová predvolená IP prejavila
  aj na inštaláciách, ktoré už mali starší (`v2`) reset. Manuálne úpravy IP po
  tomto resete ostávajú rešpektované.

## [1.27.0] – 2026-08-03

### Pridané / zmenené
- **Graf profilu – rozlíšenie rampy a výdrže + čítanie teploty myšou.** Náhľad
  profilu na Domove aj v **rýchlom vytváraní profilov (sweep)** teraz vizuálne
  odlišuje jednotlivé fázy testu:
  - **Podfarbené stĺpce = výdrž (plato)** – ploché úseky, kde komora drží
    teplotu, sú jemne podfarbené; nepodfarbené šikmé úseky sú **rampy (nábeh)**.
  - **Body na krivke** označujú hranice segmentov (kde sa mení fáza).
  - **Prejdenie myšou po krivke** ukáže teplotu a čas v danom bode a k tomu
    štítok, či ide o **↗ rampu (ohrev)**, **↘ rampu (chladenie)** alebo
    **→ výdrž (plato)**.

  Rozlíšenie je odvodené priamo z geometrie krivky (ploché vs. šikmé úseky),
  takže funguje pre naimportované aj sweep-om vygenerované profily. Zapína sa
  novým `ShowStages` na `ChartView`; živé grafy teploty/vlhkosti ostávajú
  bez zmeny.

## [1.26.2] – 2026-07-28

### Opravené
- **CI (.NET Core Desktop) build.** Workflow `dotnet-desktop.yml` mal nevyplnené
  placeholdery z GitHub šablóny (`Solution_Name: your-solution-name`) a kroky na
  podpisovanie/MSIX balenie, ktoré projekt nemá – build padal na `MSB1009`.
  Prepísaný na reálny `dotnet restore/build/test` solution `VotschVc3.sln` na
  `windows-latest` (Debug aj Release) a publikovanie spustiteľného buildu appky
  ako artefakt.

## [1.26.1] – 2026-07-28

### Opravené
- **SIKA – START sa spustí PRED nastavením teploty** (ako pri zapnutí profilu).
  `startCurrentTask` úlohu znova načíta (reload), takže setpoint zapísaný pred
  štartom by sa zahodil. `WriteSetpointsAsync` preto teraz – keď je regulátor
  vypnutý a je požiadané o štart – najprv spustí START (`startCurrentTask` +
  `System_ReglerOnOff=1`) a **až potom** zapíše setpoint (`Task_SetPointList` +
  `TRset_SP`). Ak už zariadenie beží, urobí len zápis setpointu.

## [1.26.0] – 2026-07-27

### Pridané / zmenené
- **SIKA – START/STOP ovládanie regulátora (overený protokol).** Podľa reálneho
  záznamu z prístroja **SIKA Sylex (TP3M165E.2)**: nastavený setpoint sa prejaví
  až keď beží úloha. `WriteSetpointsAsync` teraz po zápise setpointu – ak je
  regulátor vypnutý a je požiadané o štart – spustí zariadenie rovnako ako
  tlačidlo **START** vo webovom rozhraní: `ajax/startCurrentTask` a následne
  `setRegister?register=System_ReglerOnOff&value=1`. Ak už beží, stačí samotný
  zápis setpointu (žiadny zbytočný re-štart).
- **SIKA – `StopAsync` reálne zastaví zariadenie.** Namiesto pôvodnej výnimky
  „nepodporované" teraz vykoná overenú STOP sekvenciu: `ajax/stopCurrentTask`
  a následne `setRegister?register=System_ReglerOnOff&value=0`.
- **SIKA – overené teplotné rozsahy.** SIKA **Sylex** −50…+165 °C (z prístroja:
  `getShells` AbsolutMin/AbsolutMax, `getGradientInfo` MaxTemp), SIKA **PolyTech**
  −60…+200 °C (z `getInfoReport`). Rozsahy nastavené explicitne per-zariadenie.

## [1.25.0] – 2026-07-27

### Pridané / zmenené
- **SIKA – zápis teploty cez `setRegister` (opravený protokol).** Podľa reálneho
  záznamu komunikácie z prístroja **SIKA Sylex (TP3M165E.2)** sa setpoint
  nastavuje cez `ajax/setRegister?register=TRset_SP&value=…` (a súčasne
  `Task_SetPointList`), nie cez pôvodný `setSP`. `SikaTpClient.WriteSetpointsAsync`
  teraz zapisuje presne tak, ako to robí webové rozhranie prístroja. Externý zápis
  vyžaduje `Com_ExternWriteFlag = 1`; zap/vyp regulátora drží register
  `System_ReglerOnOff`.
- **SIKA – čítanie cez `getGradientInfo` a podpora portu 80.** Podľa záznamu z
  prístroja **SIKA PolyTech (TP37200E.2)** teraz `ReadAsync` uprednostní jedno
  volanie `ajax/getGradientInfo` (referenčná teplota `TR` aj setpoint `SP` naraz)
  namiesto dvoch `getRegister` volaní. Ak zariadenie endpoint nepozná (staršie
  firmware, HTTP 404), automaticky sa vráti k čítaniu po registroch.
- **SIKA PolyTech – správna konfigurácia.** Prístroj `10.88.6.28` je model
  **TP37200E.2**, odpovedá na REST-API na **porte 80** (nie 8081) a má rozsah
  **−60…+200 °C**; štítok a rozsah upravené podľa jeho `getInfoReport`.

## [1.24.1] – 2026-07-19

### Opravené
- **Prekrývajúce sa tlačidlá** v Rýchlom vytváračovi (Nový / Vymazať) – teraz
  sú v samostatných stĺpcoch a neprekrývajú sa ani pri užšom okne.
- **Prihlasovacie okno** už nezobrazuje nápovedu s predvolenými účtami
  (admin/operator) – odstránené.

## [1.24.0] – 2026-07-19

### Pridané
- **Vlastné vektorové ikony (XAML).** Nová knižnica `Themes/Icons.xaml` s
  ostrými, tému-rešpektujúcimi ikonami (prefarbia sa podľa tlačidla, ostré pri
  každom DPI). Nasadené na hlavné menu vľavo (Editor, Rýchly profil, Teplomery,
  Záznamy, Audit, App log, Changelog, Administrácia, Odhlásiť, Ukončiť) a na
  kľúčové tlačidlá (Editor profilov: Obnoviť/Nový/Import/Export; Rýchly profil:
  Uložiť/Editovať). Knižnica je pripravená na rozšírenie do zvyšku appky.

## [1.23.0] – 2026-07-19

### Zmenené / opravené
- **Mazanie profilov cez potvrdzovacie okno.** V Editore profilov aj v Rýchlom
  vytváračovi sa pri mazaní zobrazí tmavé potvrdzovacie okno „Naozaj vymazať?"
  (namiesto dvojkliku), takže mazanie vybraného profilu funguje jednoznačne.
- **Rýchly vytvárač – krajšie usporiadanie tlačidiel.** Primárne akcie (Uložiť,
  Editovať) v jednom riadku, oddeľovač a pod ním Nový vľavo / Vymazať vpravo.
- **Rýchle spustenie na karte zariadenia už nezobrazuje všetky profily.**
  Zobrazia sa iba profily, ktoré admin výslovne pridá cez „✎ Upraviť"; keď nie
  je pridaný žiadny, ukáže sa nápoveda.
- **Upozornenia aj ako popup notifikácie.** Teplota/vlhkosť mimo rozsahu a chyby
  operácií sa teraz hlásia aj cez systémové popup (tray) notifikácie so zvukom,
  nielen v stavovom riadku (alarmy fungovali takto už predtým).

## [1.22.0] – 2026-07-19

### Pridané
- **Rýchly profil – potvrdenie po uložení:** po uložení sa zobrazí výrazná zelená
  hláška s názvom profilu a počtom segmentov.
- **Rýchly profil – Nový / Vymazať / Editovať profil:** tlačidlá „Nový / začať
  odznova" (vynuluje parametre), „Vymazať z knižnice" (s potvrdením) a
  **„✎ Editovať profil"**, ktoré profil uloží a **presunie do Editora profilov**.
- **Log teplôt pre každý profil:** počas behu profilu sa do
  `Dokumenty\VotschVc3\profil-logy\` ukladá CSV so **setpointom a nameranou
  teplotou komory** (pri klimakomorách aj vlhkosť), jeden súbor na spustenie.
- **Krajšie okno na ukončenie aplikácie:** namiesto systémového dialógu sa
  zobrazí tmavé okno v štýle aplikácie s možnosťami **Ukončiť / Skryť do tray /
  Zrušiť**. Opravená aj zle zobrazená ikona tlačidla „Ukončiť aplikáciu".

## [1.21.0] – 2026-07-19

### Pridané
- **Časová os zariadení:** hustejšie hodinové značky na osi X a druhý riadok s
  názvom dňa a dátumom (napr. „Ne 20.07.", „Po 21.07."), aby bola viacdňová os
  čitateľná.
- **Rýchly vytvárač profilov:**
  - Všetky číselné polia majú teraz krokovadlo (▲/▼) so správne viditeľnou
    hodnotou vnútri.
  - **Dvojitý vrchol** je zapnutý ako predvolený.
  - Nová predvolená možnosť **„Nábeh z aktuálnej teploty"** – na začiatku sa
    pridá rampa z aktuálnej (predvolene 25 °C) na prvú teplotu za 60 min.
  - Okrem „od–do" sa dá zadať aj **požadovaný krok teploty** medzi min a max;
    počet krokov sa dopočíta a profil sa vygeneruje.
- **Editor profilov:**
  - Zoznam profilov sa **obnoví pri každom vstupe** a pribudlo tlačidlo **↻ Obnoviť**.
  - **Duplikovať profil** – vytvorí kópiu s príponou „COPY" v názve.
  - **Prepínanie medzi profilmi na jeden klik** (výber = načítanie do editora).
  - **Graf je hore a „zamrznutý"** (neroluje) a je vyšší, aby sa lepšie ovládal
    a bolo vidno, ako profil vzniká.
- **Bezpečnostný zámok automaticky** – pri spustení rýchleho/testovacieho
  profilu aj pri manuálnom nastavení sa zariadenie zamkne (pred zmenou alebo
  zastavením ho treba odomknúť).
- **Krajší progress bar** (zaoblený, farebný, s percentami) na karte aj úplne
  hore pri zariadení; k dokončeniu profilu sa dopĺňa **názov dňa**.
- **Info o dokončení profilu** (deň, dátum, čas a odpočet) je aj v hlavnom
  info hore, hneď pod názvom profilu.
- **Zatvorenie do oznamovacej oblasti (tray):** krížik okno len skryje a
  aplikácia beží ďalej. Ukončiť sa dá iba tlačidlom **⏻ Ukončiť aplikáciu**
  (alebo z menu tray ikony) – vždy s potvrdzovacou otázkou.

## [1.20.1] – 2026-07-17

### Opravené
- **Poriadok v SIKA zariadeniach.** Predošlé buildy nechali duplicitné /
  nekonzistentne pomenované SIKA kúpele („Sika Sylex", „Sylex Sika"…).
  Jednorazová oprava odstráni všetky SIKA REST-API záznamy a vytvorí presne
  dva správne: **SIKA Sylex** (`10.88.5.81`) a **SIKA PolyTech** (`10.88.6.28`),
  s rozsahom -50…+165 °C a štítkom (Sylex má reálne údaje zo systémovej
  informácie prístroja). Sú to bežné zariadenia – IP sa dá zmeniť, dajú sa
  odobrať. Pevné poradie: … Sušiareň, SIKA Sylex, SIKA PolyTech.

## [1.20.0] – 2026-07-17

### Opravené
- **Build padal** kvôli tmavému štýlu kalendára – `CalendarButton` nemá
  vlastnosť `IsSelected` (použitá `HasSelectedDays`). Aplikácia sa opäť
  skompiluje.

### Pridané
- **Povolený teplotný rozsah pre každé zariadenie sa teraz vynucuje.** Pri
  nastavovaní teploty (aj rýchle predvoľby) sa hodnota mimo rozsahu zariadenia
  `[TempMin…TempMax]` odmietne so zrozumným hlásením a nič sa nepošle. Rovnako
  pri vlhkosti. Predvolené rozsahy: SIKA -50…+165 °C, POL-EKO 0…+300 °C,
  Vötsch -45…+190 °C (editovateľné pri každom zariadení).
- **Sylex SIKA s reálnymi parametrami zo štítku zariadenia** (TP3M165E.2, s/n
  2219005, HW 001927, SW 28.17 / FW V 1.15, kalibrácia 2022-05-09 → 2025-05-09,
  rozsah -50…+165 °C). Doplní sa jednorazovo aj do existujúcej inštalácie
  (ak „Sylex SIKA" existuje, len sa doplní štítok a rozsah). Ide o bežné
  zariadenie – IP sa dá zmeniť a dá sa odobrať.

## [1.19.0] – 2026-07-17

### Pridané
- **Skrytie časovej osi:** na karte „Časová os zariadení" pribudlo tlačidlo
  **▾ Skryť / ▸ Zobraziť** – časová os sa dá schovať a ušetriť miesto na
  obrazovke. Voľba sa ukladá.

### Zmenené
- **Odstránené predvolené zariadenia Sylex SIKA a Polytech SIKA.** Už sa
  automaticky nepridávajú ani im nie je „na tvrdo" vnucovaná IP – SIKA kúpele
  sú teraz bežné zariadenia, ktoré admin pridá/odoberie/prepíše ručne. Existujúce
  sa nemažú automaticky (mohli by byť práve v používaní) – odober ich v
  nastaveniach zariadenia, ak ich nechceš.

### Opravené
- **Kalendár odloženého štartu je v tmavom režime** – tmavé pozadie, svetlý
  text, akcentové zvýraznenie dnešného/vybraného dňa (predtým systémový svetlý
  kalendár).
- **Hodiny/minúty odloženého štartu sa opäť nezobrazovali:** NumericStepper má
  teraz vlastný, izolovaný vzhľad textového poľa nezávislý od globálneho štýlu
  (ktorý v niektorých vnorených rozloženiach nechal pole prázdne), plus mierne
  širšie polia.

## [1.18.0] – 2026-07-17

### Pridané
- **Druhý kalibračný kúpeľ „Sylex SIKA"** (SIKA REST-API) na IP `10.88.5.81`.
  Obe SIKA zariadenia (Polytech `10.88.6.28`, Sylex `10.88.5.81`) majú IP
  nastavenú **„na tvrdo"** – pri každom štarte sa IP (a REST-API port)
  prepíše na správnu hodnotu, aj keby ju niekto medzitým zmenil, a chýbajúce
  zariadenie sa doplní.

- **Zrušenie rýchlo spusteného profilu:** v sekcii „Rýchle spustenie profilu"
  pribudlo tlačidlo **„✕ Zrušiť profil"** – zastaví bežiaci profil (ak beží) a
  odoberie ho z karty, takže už nezostane svietiť ako testovací profil.
  Tlačidlo je aktívne len keď je čo rušiť.
- **Zámok zariadenia (bezpečnosť):** tlačidlom **🔒 Zamknúť / 🔓 Odomknúť** na
  karte (aj v nastaveniach zariadenia) sa dá zariadenie uzamknúť – **všetky
  ovládacie tlačidlá** (teplota, predvoľby, profily, štart/stop…) sa
  zablokujú, aby počas behu profilu alebo temperovania nedošlo k neúmyselnému
  stlačeniu. Odomknutie sa dá **voliteľne chrániť heslom** (nastavuje admin v
  nastaveniach zariadenia); bez hesla sa odomkne jedným klikom. Stav zámku sa
  ukladá pre každé zariadenie.
- **Kompaktný režim nástenky** (Administrácia → Rozloženie nástenky): zmenší
  karty, grafiku a text, aby sa na obrazovku zmestilo viac zariadení.
  Nastaviteľné, dá sa kedykoľvek vypnúť a vrátiť sa k pôvodnému zobrazeniu.

### Zmenené
- **Pevné poradie zariadení „na tvrdo":** Komora 1, Komora 2, Komora 3,
  Sušiareň, Sylex SIKA, Polytech SIKA. Uplatní sa pri každom štarte (má
  prednosť pred ručným preusporiadaním po reštarte).

### Opravené
- **Odložený štart – neviditeľné hodiny/minúty:** polia času (NumericStepper)
  boli v zakázanom (sivom) kontajneri, takže ich vlastný TextBox sa vykreslil
  prázdny a po zaškrtnutí sa text neobnovil. Sekcia je teraz vždy aktívna a
  keď je „Odložený štart" vypnutý, len sa stlmí (nedá sa klikať) – hodnoty
  hodín a minút sú tak vždy viditeľné.

## [1.17.0] – 2026-07-17

### Pridané
- **Polytech SIKA** (kalibračný kúpeľ, REST-API) na IP `10.88.6.28` a
  **Komora 3 - FOI** (klimatická komora teplota + vlhkosť, rovnaký Vötsch
  ASCII-2 protokol ako Komora 1/2) na IP `10.88.5.233` – pridané ako nové
  predvolené zariadenia. Jednorazová migrácia ich doplní aj do už bežiacich
  inštalácií (podľa IP, takže sa nepridajú duplicitne, ak si ich niekto medzitým
  premenoval alebo odstránil).
- **Kopírovať** tlačidlo v „Surový terminál" – skopíruje celý zobrazený
  TX/RX log do schránky.

### Zmenené
- **„Vyčistiť" v Surovom termináli** teraz vymaže aj výsledok diagnostiky
  (getInfoReport, MODBUS sken a pod.), nielen TX/RX log.
- **SIKA grafika:** čierny predný panel je teraz výrazne tmavší a kontrastnejší
  keď je zariadenie online, a vybledne do sivej keď nie je – jasnejší signál,
  že kúpeľ naozaj komunikuje (predtým vyzeral rovnako v oboch stavoch).

### Opravené
- **SIKA REST-API klient teraz serializuje všetky HTTP požiadavky** (živé
  čítanie, zápis setpointu, surový terminál) tak, ako to už robia ASCII-2 aj
  MODBUS klienti. Bez tohto zámku vedela paralelná požiadavka (napr. `setSP`
  počas prebiehajúceho pollingu) na embedded webserveri kúpeľa skončiť
  náhodným 404 alebo neaplikovaným zápisom – to vyzeralo, akoby nastavenie
  teploty nemalo na reálne zariadenie žiadny vplyv.

## [1.16.0] – 2026-07-16

### Pridané
- **Časová os zariadení (Gantt) na vrchu hlavného prehľadu:** riadok pre každé
  zariadenie s pruhom od štartu bežiaceho profilu po jeho odhadovaný koniec
  (odložený štart sa kreslí ako priesvitný plánovaný pruh). Zariadenie zapnuté
  **manuálne bez profilu** má otvorený pruh s **∞** – beží „do nekonečna",
  kým sa nevypne. Čiarkovaná zvislá čiara ukazuje „teraz", os sa
  automaticky prispôsobuje rozsahu časov.
- **Rad profilov na karte zariadenia:** až **3 uložené profily** sa dajú
  pridať do radu a spustiť za sebou – po skončení jedného sa hneď spustí
  ďalší. Karta ukazuje poradie, celkové trvanie radu a stav `x/3`; časová os
  zobrazuje celý rad ako jeden pruh od–do.

### Zmenené
- **SIKA TP Premium má novú grafiku podľa fotky reálneho prístroja:**
  červené telo s čiernym predným panelom, rukoväť, ohnutá referenčná sonda
  v kalibračnej šachte, dotykový displej s trendovým grafom a živou teplotou,
  konektor s káblom, USB porty a kolískový vypínač s poistkovým štítkom.
  Počas behu pulzuje koncový bod krivky na displeji; v pokoji je prístroj
  stmavený. SVG verzia je v `assets/sika_thermal_bath.svg`.

## [1.15.0] – 2026-07-15

### Pridané
- **Nový typ zariadenia: SIKA TP Premium (kalibračný kúpeľ / dry block)** cez
  HTTP REST-API (port 8081). Meraná teplota a setpoint sa čítajú príkazom
  `getRegister`, zápis cez `setSP`; v záložke „Surový terminál" pribudla
  diagnostika `getInfoReport` / `getCalibrationStatus`. Kúpeľ nemá vzdialené
  vypnutie (REST-API ho neponúka) – beží nepretržite.
- **Nová vektorová grafika zariadenia** pre SIKA TP Premium – červená skrinka
  s rotujúcim ventilátorom v mriežke na vrchu (animácia sa zastaví, keď
  zariadenie nie je aktívne), podľa vzhľadu reálneho prístroja.

## [1.14.0] – 2026-07-14

### Pridané
- **Správa používateľov v Administrácii:** vytváranie používateľov, priradenie
  a zmena rolí (Operátor / Supervisor / Admin) aj mazanie (nedá sa odstrániť
  posledný admin ani práve prihlásený používateľ).
- **Šípky ▲▼ (jemné +/−)** pri poli „Vlastná teplota" (krok 1 °C) a pri čase
  odloženého štartu (hodiny / minúty).
- **Odložený štart:** dátum sa vyberá z **kalendára** a čas cez **hodiny:minúty**.

### Opravené
- **Checkbox** – fajka sa zobrazovala priveľká a nezarovnaná; nahradená čistou
  vektorovou fajkou, ktorá je dobre viditeľná.

## [1.13.0] – 2026-07-14

### Zmenené
- **Aplikácia sa premenovala na „Riadenie laboratórnych zariadení"** (titulok
  okna, prihlasovacia obrazovka, bočný panel, notifikácie).
- **Prihlasovacia obrazovka:** čistejší nadpis „Prihlásenie" (bez emoji),
  odznak „LAB CONTROL", hero titulok podľa nového názvu.
- **Názvy zariadení nastavené natvrdo:** „Komora 1 — Vötsch VT3 7034 (teplota)",
  „Komora 2 — Vötsch VC3 7034 (teplota + vlhkosť)", „Sušiareň — POL-EKO SLN 115
  (teplota)". Existujúce inštalácie sa jednorazovo premenujú podľa IP; potom
  môže admin názov ľubovoľne meniť.
- **„Nastaviť / ovládať" je teraz hore na karte** ako výrazné tlačidlo.
- **Premenovanie a odobratie zariadenia** sa presunulo z karty do nastavení
  zariadenia (záložka „Zariadenie" → „Nastavenie zariadenia").

### Pridané
- **Odložený štart priamo na nástenke** pre každé zariadenie – zapni a zadaj
  dátum/čas; spustenie profilu (▶ alebo rýchle tlačidlo) sa naplánuje.

### Odstránené
- Nadpis „Vyber komoru" a popis „Obe komory môžu byť…" z hlavného zobrazenia.

## [1.12.0] – 2026-07-14

### Pridané
- **Editovateľné rýchle spustenie profilov.** Admin cez „✎ Upraviť" vyberie
  z existujúcich profilov, ktoré sa zobrazia ako rýchle tlačidlá (pridať cez
  výber + „Pridať", odobrať cez ✕). Výber sa ukladá pre každé zariadenie.
  Prázdny výber = zobrazia sa všetky profily.

### Opravené
- **Celý názov profilu** na rýchlych tlačidlách (predtým sa orezával „…").
- **Neviditeľný text v poli „Vlastná teplota"** – zadaná hodnota sa teraz
  zobrazuje celá (opravené zvislé zarovnanie pri pevnej výške poľa).

## [1.11.0] – 2026-07-14

### Pridané
- **Rýchle tlačidlá na profily** na karte komory: pod výberom profilu je rad
  tlačidiel s uloženými profilmi – **jedno kliknutie profil načíta a spustí**.
  Platí pre komory aj sušiareň (POL-EKO).

### Zmenené
- **Predvolené rýchle ovládanie pre sušiareň (POL-EKO)** je teraz
  **0, 25, 50, 60, 80, 120, 150, 250 °C**. Existujúca sušiareň so starou
  štvorhodnotovou predvoľbou sa pri štarte automaticky povýši na novú sadu
  (vlastné upravené predvoľby zostávajú nedotknuté).
- **Zjednotené veľkosti** v riadku „Vlastná teplota" – pole, „Nastaviť" aj
  „Stop" majú rovnakú výšku (a tlačidlá rovnakú minimálnu šírku).

## [1.10.0] – 2026-07-14

### Zmenené
- **Nový vzhľad hlavnej stránky.** Horné menu „Riadenie klim. komôr" sa
  presunulo do **bočného panela (sidebar)** vľavo; navigácia je teraz zvislý
  zoznam a používateľ + odhlásenie sú pripnuté dole.
- **Preusporiadané panely na karte komory:** poradie je teraz **Teplota →
  Rýchle ovládanie → Testovací profil** (testovací profil je na konci). Popis
  komory je na **jednom riadku** (dlhý názov sa oreže s tooltipom).
- **Pripojenie sa presunulo do nastavení komory.** IP adresa / port,
  Pripojiť/Odpojiť aj referenčný teplomer sú teraz cez „Nastaviť / ovládať →"
  (a IP/port aj v Administrácii); karta tak nie je preplnená. Stav pripojenia
  (guľôčka + IP) zostáva v hlavičke karty.
- **Rýchle ovládanie prehľadnejšie:** predvoľby ako čipy, vlastná teplota
  a „Nastaviť" v samostatnom rámiku, tlačidlo **Stop** oddelené vpravo.
- **Modernejšie tlačidlá testovacieho profilu** (▶ ⏸ ⏹): jemné farebné
  podfarbenie v pokoji, výraznejší hover so žiarou a farebné odlíšenie.

### Pridané
- **Ventilátor v grafike sušiarne (POL-EKO)** – točí sa, keď zariadenie beží,
  a zastaví sa v nečinnosti, rovnako ako pri komorách Vötsch.
- **Prepínač „Povoliť presúvanie komôr" v Administrácii.** Šípky ◀ ▶ na
  kartách sú **predvolene skryté**; admin ich zobrazí len keď potrebuje zmeniť
  poradie komôr.

## [1.9.4] – 2026-07-13

### Pridané
- **Graf profilu na hlavnej stránke je väčší** (118 → 210 px) a má **hover
  odčítanie**: keď prejdeš myšou po krivke, ukáže sa zvislý zameriavač, bod a
  bublina s **teplotou a časom** v danom mieste.
- **Živý časový odpočet** bežiaceho profilu („Zostáva MM:SS", resp. H:MM:SS),
  aktualizovaný každú sekundu, pod progress barom.
- **Výrazný odznak režimu** hore na karte: **PROFIL** (beží profil) alebo
  **MANUÁL** (manuálne nastavená teplota), skrytý keď je komora nečinná.

## [1.9.3] – 2026-07-13

### Pridané
- **MODBUS sken registrov (POL-EKO)** – nové tlačidlo v Surovom termináli
  prečíta holding (FC03) aj input (FC04) registre 0–63 a vypíše hodnoty. Sprav
  sken počas behu programu (aj z inej appky) a raz bez neho a porovnaj, ktorý
  register sa zmenil – tak nájdeme register bežiaceho programu/segmentu.

### Zmenené
- **Stop bežiaceho profilu úplne vypne výkon komory.** Predtým Stop len ukončil
  plán; teraz po zastavení pošle StopAsync (stop programu + štart kanál OFF),
  takže komora prestane hriať/chladiť.

## [1.9.2] – 2026-07-13

### Opravené
- **Štart kanál späť na index 1** (v 1.9.1 bol omylom 0). SIMSERV kanál 0
  digitálny výstup nenastavil. Rozhoduje tvrdý dôkaz: pri ručne spustenej komore
  digitálna diagnostika ukázala nastavený **bit 1**, a `SET DIGITALOUT` kanál N
  zodpovedá bitu N – takže štart komory je **kanál 1**. Reseed marker `v6`.
  (Ak by na inej komore štart sedel na inom bite, zisti ho cez „Prečítať
  digitálne" pri bežiacej komore a nastav „Štart kanál index".)

## [1.9.1] – 2026-07-13

### Opravené
- **Štart išiel na nesprávny digitálny kanál.** Panel komory ukazuje, že „Start"
  je prvý digitálny výstup (**index 0**), nie index 1/2. Terminál potvrdil, že
  `SET DIGITALOUT` kanál N zodpovedá rovnakému bitu N v ASCII odpovedi (kanál 2 →
  bit 2). Predvolený „Štart kanál index" opravený na **0** a SIMSERV štart kanál
  = index (bez +1), takže sa zapína práve kanál „Start". Reseed marker zdvihnutý
  na `v5`, aby sa oprava raz automaticky použila. (Setpoint sa zapisoval správne
  už predtým – problém bol len v tom, že sa nezapol správny štart kanál.)
- **Teplotná komora už neposiela kanál vlhkosti.** `SET NOMINAL VALUE` na kanál 2
  (vlhkosť) vracal na teplotnej komore `-8` (kanál neexistuje) – appka teraz na
  teplotné komory posiela iba teplotu.

## [1.9.0] – 2026-07-13

### Pridané
- **Rýchle ovládanie – teploty v rámčekoch (chip) s hover efektom.** Predvoľby
  teplôt sú teraz orámované boxy (nový štýl `PresetChip`); po prejdení myšou sa
  zvýrazní okraj (accent), po stlačení sa vyplnia. Väčší, jasnejší cieľ pre
  operátora (aj v rukaviciach).
- **Vyskakujúca spätná väzba akcií.** Po každej akcii (Nastaviť, predvoľba, Stop,
  Pripojiť/Odpojiť, štart profilu) sa v karte komory zobrazí banner „✔ Nastavené
  30 °C · štart ZAPNUTÝ", „⏹ Stop – výkon VYPNUTÝ" a pod., aby operátor vždy
  vedel, čo sa stalo a čo je zapnuté. Banner sám zmizne po ~4,5 s.

### Zmenené
- **Stop teraz úplne vypne výkon komory.** Namiesto len vynulovania štart kanála
  Stop cez SIMSERV najprv zastaví prípadný bežiaci program (`SET STOPZPGPRG
  19015`) a potom zhodí štart kanál (`SET DIGITALOUT 14001 = 0`). Pri POL-EKO
  zapíše on/off register na 0. Setpoint sa nemení (zapamätá sa na ďalší štart).

## [1.8.10] – 2026-07-13

### Pridané
- **„Program info" (SIMSERV)** – nové tlačidlo v Surovom termináli prečíta živý
  stav regulátora: prevádzkový režim (`10010`), stav (`10012`), či beží program
  (`19062`), názov programu (`19031`) a detaily (`19064`). Funguje bez ohľadu na
  to, kto komoru ovláda – naša appka, iná appka, alebo program spustený priamo
  na paneli komory. Slúži na overenie, ktoré z týchto príkazov daný regulátor
  podporuje (niektoré môžu vrátiť napr. `-5` = neznámy príkaz).

## [1.8.9] – 2026-07-13

### Zmenené
- **Riadenie Vötsch/Simpac ide teraz cez SIMSERV** (nie ASCII-2 `$01E`).
  Test na VT3 7034 potvrdil, že komora zápis cez `$01E` ignoruje, ale SIMSERV
  príkazy prijíma (odpoveď „1"): `11001¶1¶1¶30.0` (setpoint) aj `14001¶1¶1¶1`
  (štart). Zápis setpointu (Nastaviť), štart aj stop teraz appka posiela ako
  SIMSERV `SET NOMINAL VALUE (11001)` pre každý kanál a `SET DIGITALOUT (14001)`
  pre štart kanál. **Čítanie ostáva cez ASCII-2 `$01I`** (jedným rámcom, rýchle).
  Štart kanál pre SIMSERV = „Štart kanál index" + 1 (SIMSERV čísluje kanály od 1);
  ak by komora nenaskočila, priprav „Štart kanál index" (0 = SIMSERV kanál 1).
  POL-EKO (MODBUS) sa to netýka.

## [1.8.8] – 2026-07-13

### Opravené
- **Pád „ItemsControl is inconsistent with its items source"** v Surovom
  termináli. Auto-scroll (`ScrollIntoView`) sa volal synchrónne priamo v
  obsluhe `CollectionChanged`, čo vynútilo layout a znovu-vstúpilo do
  generátora položiek počas jeho aktualizácie – padalo to pri rýchlom prílive
  riadkov (napr. „SIMSERV test", ktorý pošle viac rámcov naraz). Scroll sa teraz
  odloží cez dispatcher (Background priorita), takže sa zbehne až po dokončení
  zmeny kolekcie.

## [1.8.7] – 2026-07-13

### Pridané
- **SIMSERV protokol (Simpac) – prvý krok: test.** Komora Vötsch odpovedá na
  čítanie (`$01I`), ale zápis setpointu cez ASCII-2 (`$01E`) ignoruje. Podľa
  Simpati manuálu sa Simpac riadi cez SIMSERV funkčné príkazy
  (`FunkciaNo ¶ Simpati-ID ¶ …`, oddeľovač `¶` = ASCII 182, ukončené CR):
  napr. `SET NOMINAL VALUE 11001`, `SET DIGITALOUT 14001`,
  `GET ACTUAL VALUE 11004`. Pridaný kodek `SimservProtocol` + tlačidlo
  **„SIMSERV test"** v Surovom termináli, ktoré pošle funkčné príkazy a ukáže
  odpoveď komory – takto zistíme, či komora SIMSERV na danom porte podporuje.
  Tlačidlá **„SIMSERV setpoint / štart"** vložia príslušný príkaz do terminálu.
  (ASCII-2 a MODBUS ostávajú nezmenené.)

### Opravené
- **TCP prenos posiela znaky ako Latin-1** namiesto ASCII, aby prešiel
  oddeľovač SIMSERV `¶` (0xB6). Pre ASCII-2 (znaky < 128) sa nič nemení.

## [1.8.6] – 2026-07-13

### Opravené
- **Prehodená nameraná teplota a setpoint (Vötsch)** – regulátor S!MPAC vracia
  v odpovedi na čítanie pre každý kanál poradie „setpoint, nameraná hodnota",
  no appka to čítala opačne. Prejavilo sa to tak, že po zadaní setpointu −20 °C
  ukazovala „Teplota komory −20 °C" a „setpoint" nameranú hodnotu. Parser to
  teraz normalizuje na „nameraná, setpoint", takže karta aj graf ukazujú
  správne hodnoty (platí pre teplotu aj vlhkosť). POL-EKO ide inou cestou a
  ostáva nezmenené.

## [1.8.5] – 2026-07-13

### Opravené
- **Nesprávna predvolená IP komôr 1 a 2** – seed dáta mali podsieť `10.88.1.x`
  (`10.88.1.175` / `10.88.1.180`), reálne sú komory na `10.88.5.x`
  (`10.88.5.175` / `10.88.5.180`, port 2049). Prejavilo sa to len ak sa
  `chambers.json` zmazal/obnovil – komory potom hlásili
  `TimeoutException: Timed out connecting to 10.88.1.x…`. IP opravené a
  reseed marker zdvihnutý na `v4`, takže sa správne lab IP raz automaticky
  aplikujú pri najbližšom spustení. (POL-EKO `10.88.5.162:502` bol vždy
  správne.)

  > Pozn.: jednorazový reseed prepíše komory na predvolený lab layout
  > (názvy, IP, štart kanál #1). Ak máš vlastné úpravy, po spustení ich
  > prípadne znova zadaj.

## [1.8.4] – 2026-07-13

### Opravené
- **Predvolený štart kanál Vötsch je teraz #1** (nie #0). Diagnostika „Prečítať
  digitálne" na VT3 7034 potvrdila, že pri ručne spustenej komore je nastavený
  bit s indexom **1** (`01000000…`), pri vypnutej žiadny. Doteraz appka
  zapisovala štart na kanál #0, takže sa setpoint zapísal, ale komora
  nenaskočila na výkon. Nové a novopridané Vötsch komory majú „Štart kanál
  index" predvyplnený na 1; POL-EKO (MODBUS) sa to netýka. **Existujúce uložené
  komory:** nastav v záložke „Pripojenie a live" pole „Štart kanál index" na
  **1** a ulož (alebo zmaž `chambers.json` pre obnovu predvolieb).

## [1.8.3] – 2026-07-10

### Zmenené
- **Názov komory sa upravuje len cez ✎ (admin)** – inak sa zobrazuje celý názov
  (už sa neoreže). Úpravu názvu, IP adresy aj portu a presúvanie/odoberanie
  komôr vidí a robí len admin; operátorovi sú polia len na čítanie a šípky
  ◀ ▶ skryté.

### Opravené
- **Checkbox – viditeľná fajka** – vlastný glyph nahradený spoľahlivým „✔"
  (predtým sa fajka nevykreslila).

### Pridané
- **Diagnostika „Prečítať digitálne"** (Vötsch) – prečíta digitálne kanály
  komory a vypíše, ktoré bity sú nastavené. Takto nájdeš správny **štart /
  'condition on' kanál**: spusti komoru ručne na paneli, klikni Prečítať a bit,
  ktorý je 1, zadaj do „Štart kanál index". (Rieši prípad: setpoint sa zapíše,
  ale komora sa nezapne na výkon.)

## [1.8.2] – 2026-07-10

### Opravené
- **Build zlyhal (WPF XAML)** – animácie tlačidiel z 1.8.0 používali neplatný
  `Setter TargetName="Sc"` (na `ScaleTransform`) a `Setter.Value="{Binding …}"`,
  ktoré WPF nepodporuje (chyby MC4111 a „Binding cannot be set on Value"). Scale
  na stlačenie odstránený (hover animácia ostáva), farebná výplň ▶⏸⏹ pri prejdení
  myšou riešená cez prekryvnú vrstvu. Aplikácia sa teraz zostaví.

## [1.8.1] – 2026-07-10

### Pridané
- **Karta „Zariadenie" s údajmi zo štítku** – v detaile komory nová záložka
  s údajmi z typového štítku (typ, sériové číslo, zákazka, rok, chladivá,
  napájanie, výkon/prúd, kalibrácie, poznámky). Predvyplnené pre VT3 7034
  a VC3 7034 z fotiek štítkov; editovateľné a ukladajú sa.
- **Diagnostika nastavenia teploty (Vötsch)** – v záložke „Surový terminál"
  nový panel:
  - **Spustiť test zápisu** zapíše skúšobný setpoint a hneď ho prečíta späť;
    ak sa nezmenil, vypíše najpravdepodobnejšie príčiny (komora nie je
    v režime diaľkového/PC ovládania, zlý štart kanál, adresa, počet kanálov,
    terminátor). TX/RX rámce idú do App logu.
  - tlačidlá **Čítať / Zápis + štart / Stop** vložia presný ASCII-2 rámec do
    terminálu na ručné odoslanie a sledovanie odpovede.

## [1.8.0] – 2026-07-10

### Pridané
- **Editovateľné predvoľby rýchleho ovládania pre každé zariadenie** (admin) –
  tlačidlo „✎ Upraviť predvoľby" na karte umožní adminovi zadať vlastné teploty
  (napr. 60, 105, 150, 250). Ukladajú sa per zariadenie.
- **Rozsah teploty a vlhkosti na hlavnej stránke** – karta zobrazuje
  „Rozsah: −45…190 °C · 0…100 %rv" (z limitov zariadenia).
- **Stav zariadenia „Aktívna / Neaktívna"** – jasný odznak na karte: zelená
  „Aktívna", keď beží nejaká nastavená teplota (profil alebo manuál), inak
  sivá „Neaktívna".

### Zmenené
- **Predkonfigurované tri zariadenia** (jednorazovo sa nastavia):
  Komora 1 = Vötsch VT3 7034 (teplota, 10.88.1.175:2049), Komora 2 = Vötsch
  VC3 7034 (teplota + vlhkosť, 10.88.1.180:2049), Komora 3 = POL-EKO SLN 115
  (10.88.5.162:502). IP adresy aj porty sa zapamätajú (port Vötsch je 2049,
  dá sa zmeniť).
- **Krajšie rozmiestnenie hlavnej stránky** – karta komory prepracovaná do
  prehľadných sekcií (živé hodnoty + rozsah, pripojenie + referencia, testovací
  profil + náhľad, rýchle ovládanie); širšia karta, zarovnané prvky.
- **Krajšie tlačidlá + animácie** – jemná animácia zväčšenia pri prejdení myšou
  a stlačení na všetkých tlačidlách.
- **Farebné animované play/pause/stop** – ▶ zelené, ⏸ oranžové, ⏹ červené, pri
  prejdení myšou sa vyfarbia a zväčšia.

## [1.7.8] – 2026-07-10

### Opravené
- **Krížik ✕ appku naozaj zavrie** – ikona notifikácií v oznamovacej oblasti
  po zavretí okna držala proces „v lište", takže zavretie vyzeralo ako
  minimalizácia. Ikona sa teraz odstráni hneď pri zatváraní a aplikácia sa
  garantovane ukončí (`ShutdownMode=OnMainWindowClose` + explicitný Shutdown).
- **Biely textový kurzor aj v poli hesla** – `PasswordBox` na prihlasovacej
  obrazovke nie je TextBox, takže mal stále čierny (neviditeľný) kurzor;
  pridaný globálny štýl s bielym kurzorom. Výber textu je teraz zvýraznený
  akcentovou farbou vo všetkých vstupoch.

### Zmenené
- **Aplikácia sa spúšťa maximalizovaná** (na celú obrazovku).

## [1.7.7] – 2026-07-09

### Pridané
- **Notifikácie na ploche a zvuk** – pri dokončení profilu/fronty a pri každom
  novom alarme zaznie zvuk, zobrazí sa bublina v oznamovacej oblasti Windows
  (na Win 10/11 ako toast) a ikona na paneli úloh bliká, kým appku neotvoríš.
  Operátor tak nemusí sledovať monitor; dopĺňa existujúci e-mail.
- **Dvojkrokové odobratie komory** – prvý klik na ✕ zmení tlačidlo na
  „✕ Naozaj?", druhý klik do 4 sekúnd komoru odoberie (vrátane konfigurácie);
  inak sa tlačidlo samo vráti. Omylom už komoru nezmažeš.

## [1.7.6] – 2026-07-09

### Pridané
- **Dvojkrokové mazanie profilov** – prvý klik na „Zmazať" zmení tlačidlo na
  „Naozaj zmazať?" a druhý klik do 3 sekúnd potvrdí; inak sa tlačidlo samo
  vráti. Platí v histórii komory aj v editore profilov; zmena výberu
  potvrdenie zruší. Omylom už profil nezmažeš.

## [1.7.5] – 2026-07-09

### Zmenené (dizajn podľa wpf-ux-ui)
- **Prázdne stavy zoznamov** – prázdny zoznam už nie je prázdna plocha, ale
  nápoveda čo spraviť: história profilov („ulož tlačidlom Uložiť aktuálny"),
  fronta testov, terminál a zoznam teplomerov (nový štýl `ListWithEmptyHint`).
- **Panel Uložené profily v editore profilov rozšírený** (290 → 380 px) –
  parita s históriou v komore, celé názvy a popisy sú čitateľné.

## [1.7.4] – 2026-07-09

### Zmenené (dizajn podľa wpf-ux-ui)
- **Tmavé tooltips** – systémový svetložltý ToolTip nahradený tmavým so
  zaoblením a zalamovaním (tooltips používame všade, konečne ladia s témou).
- **Štíhle tmavé scrollbary** – namiesto hrubých systémových; ťahaný palec
  sa zvýrazní akcentovou farbou.
- **Tmavý CheckBox** – vlastný glyph (tmavý box, akcentová výplň s bielou
  fajkou), hover a klávesnicový focus.
- **Výber v zoznamoch** – akcentový obrys namiesto systémovo-modrej výplne
  (história profilov, teplomery, fronta).
- **Klávesnicový focus na tlačidlách** je viditeľný (akcentový rámik).
- **Jednotné metriky na dlaždici** – nové štýly `MetricSmall`/`MetricSub`
  namiesto ad-hoc veľkostí písma (teplota, vlhkosť, setpoint, referencia).
- **Stop a Odpojiť na dlaždici sú červené** (`DangerButton`) – konzistentne
  s detailom komory (pravidlo: nebezpečné akcie vždy odlíšené).

## [1.7.3] – 2026-07-09

### Zmenené
- **UI audit podľa dizajn systému (wpf-ux-ui skill)** – do témy pridané
  sémantické tokeny `OkBrush`/`WarnBrush`/`ErrorBrush`; všetky natvrdo zadané
  hex farby vo views nahradené tokenmi (setpoint, referenčný teplomer,
  ▶⏸⏹ ikonky, sekundárne texty). Výnimkou ostávajú dekoratívne gradienty
  LoginView a ilustrácie komôr (označené komentárom).

### Opravené
- **Prehliadač záznamov – tabuľka štatistík je read-only** (bunky sa nedali
  zmysluplne editovať a mali neviditeľný kurzor).

## [1.7.2] – 2026-07-09

### Opravené
- **Ukladanie profilov už nevytvára duplikáty** – „Uložiť" prepíše profil
  s rovnakým názvom (komora, editor profilov aj rýchly vytvárač); stav hlási
  „uložený" vs. „aktualizovaný".
- **Validácia teploty podľa zariadenia** – POL-EKO sušiareň 0…300 °C,
  Vötsch komora −80…200 °C (predtým natvrdo −80…200 pre všetko).
- **Ukazovateľ „teraz"** v náhľade profilu sa pri štarte nového behu resetuje.
- **Pauza počas odloženého štartu** – tlačidlo ⏸ vysvetlí, že profil ešte
  nebeží, namiesto tichého ignorovania.

### Zmenené
- **POL-EKO detail komory** – skryté ASCII-2 polia (analóg. kanály, štart
  kanál, terminátor), MODBUS nápoveda a predvolený HEX príkaz v termináli,
  tooltip na adresu (MODBUS unit ID).
- **Rýchle predvoľby teploty na dlaždici podľa zariadenia** – sušiareň:
  60/105/150/250 °C; komora: −20/0/25/60 °C.
- **Editor profilov (knižnica)** – dvojklik načíta profil do editora, názvy
  a popisy sa zalamujú (ako v komore).

### Pridané
- `docs/NAVRHY.md` – prioritizované návrhy nových modulov (simulátor
  zariadenia, PDF report testu, kalibračný modul, sken MODBUS registrov,
  štítky profilov, REST/MQTT monitoring, perzistentná fronta, lokalizácia).

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
