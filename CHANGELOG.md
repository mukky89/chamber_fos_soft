# Changelog

Všetky podstatné zmeny v tomto projekte. Formát vychádza z
[Keep a Changelog](https://keepachangelog.com/), verzie podľa
[SemVer](https://semver.org/lang/sk/).

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
