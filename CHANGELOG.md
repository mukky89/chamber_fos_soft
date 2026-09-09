# Changelog

## [1.76.298] – 2026-09-09

### Opravené
- Stavové štítky kalibrácie sú jednotne po slovensky: Splnené, Prebieha, Čaká, Zastavené a Monitorovanie. Preložené sú aj celkový stav behu a popis výsledkových e-mailov v administrácii.

## [1.76.297] – 2026-09-08

### Pridané
- Rýchly profil má režim Profil na žíhanie: samostatne nastaviteľný nábeh, jedno plato a výstupná rampa. Režim zostáva zachovaný pri uložení a opätovnom otvorení.
- V rade profilov výstupná rampa žíhania prejde na prvú teplotu nasledujúceho profilu bez zmeny profilu v knižnici. Samostatné žíhanie používa zadanú výstupnú teplotu.

## [1.76.296] – 2026-09-08

### Opravené
- Dlhé názvy profilov v rade už nevytláčajú odstránenie mimo karty. Každý riadok má viditeľnú ikonu koša; celý názov je dostupný v tooltipe.
- Pridané tlačidlo Vyprázdniť rad, ktoré odstráni položky z radu bez vymazania uložených profilov.

## [1.76.295] – 2026-09-08

### Pridané
- Počas celého behu aplikácie, aj v oznamovacej oblasti, sa vyžaduje zapnutý systém a displej. Aktivita sa obnovuje každých 10 sekúnd nulovým pohybom myši bez kliknutia.
- Pri ukončení sa požiadavka uvoľní; nastavenia Windows sa trvalo nemenia. Ručné zamknutie, vynútené systémové pravidlá, výpadok napájania a siete zostávajú mimo kontroly aplikácie. Odmietnutie požiadavky Windows sa zapíše do logu.

## [1.76.294] – 2026-09-08

### Opravené
- Nastavenia vstupnej stability komory sa prenesú z administrácie aj do starších uložených zapojení pri načítaní, opätovnom otvorení a pred novým behom.
- Prenáša sa zapnutie, tolerancia, časové okno, rozsah a drift. Vlastné úpravy zapojenia sa zachovajú, kým sa tieto administrátorské predvoľby nezmenia. Rozbehnuté a obnovované behy sa automaticky nemenia.

## [1.76.293] – 2026-09-08

### Zmenené
- Administrátorské predvoľby FBG sú rozdelené do piatich farebne odlíšených kategórií: nábeh teploty, ustálenie komory, referencia WIKA, stabilita FBG a odber/záznam dát.
- Každá kategória má stručné vysvetlenie a zalamovanie polí podľa šírky okna. Spoločný timeout je označený ako Limit komora + WIKA; nastavené hodnoty, tooltipy a spoločné uloženie zostávajú zachované.

## [1.76.292] – 2026-09-08

### Zmenené
- Otázniky pri administrátorských predvoľbách FBG nahradili modré vektorové informačné ikony. Vysvetlenie sa zobrazí po prejdení myšou bez dialógu; zachovaný je aktuálny výpočet času odberu podľa nastavených hodnôt.

## [1.76.291] – 2026-09-08

### Opravené
- FBG checkpoint vzniká už pred prvým pohybom komory, obnovuje sa pri zmene fázy a priebežne podľa živých aktualizácií s intervalom zápisu 15 sekúnd. Hotové plata, zapojenie a parametre zostávajú zachované; rozpracované plato sa po obnove stabilizuje a meria nanovo.
- Checkpoint aj súhrn behu sa zapisujú cez dočasný súbor s flush na disk a atomickou výmenou. Poškodený hlavný súbor sa načíta z predchádzajúcej záložnej kópie.
- Ukončenie aplikácie počká na uloženie, ukončenie FBG behu a zatvorenie zariadení pred zavretím hlavného okna.
- Po štarte a prihlásení sa otvoria pracoviská s checkpointom a vyberie sa príslušný profil. Pokračovanie vyžaduje kontrolu hardvéru a tlačidlo Pokračovať; výpadok neznamená automatický príkaz komore.

## [1.76.290] – 2026-09-08

### Zmenené
- Kalibračný plán používa farebné stavové štítky s vektorovými ikonami a slovenskými názvami. Výsledky naďalej rozlišujú úspešné, neúspešné a nepotvrdené body.

## [1.76.289] – 2026-09-08

### Zmenené
- Odpočítavací pruh vo všetkých upozorneniach je hrubší (8 namiesto 3 pixelov), aby bol lepšie viditeľný.

## [1.76.288] – 2026-09-08

### Opravené
- Logo Sylex je pribalené ako WPF resource a spoľahlivo sa načíta v bočnom paneli aj pri prihlásení.
- Logo nad názvom aplikácie je väčšie a zachováva pôvodný pomer strán.

## [1.76.287] – 2026-09-08

### Opravené
- Karta komory zobrazuje skutočnú odchýlku, rozsah, drift a dĺžku okna vstupnej stability s limitmi, farebným vyhodnotením a vysvetleniami.
- Rozlišuje vypnutú kontrolu, chýbajúce údaje, čakanie a splnenie. Po potvrdení uvádza čas a uchované hodnoty vstupnej kontroly; regulácia komory sa nemení.

## [1.76.286] – 2026-09-08

### Opravené

- Zoom ovládanie grafu má vlastný riadok mimo vykresľovanej plochy. Zvislá čiara Teraz je plná, výrazne modrá; kurzor sa v pásme 10 px prichytí k čiare a zobrazí skutočnú teplotu komory aj setpoint.

## [1.76.285] – 2026-09-08

### Opravené

- Štítky peakov rozlišujú čakanie na teplotu, prebiehajúcu stabilizáciu FBG a čakanie finálneho odberu na stabilitu FBG. Ukončené nepotvrdené peaky sa už nezobrazujú ako stále čakajúce alebo stabilizujúce. Dlhšie štítky sa zalamujú.

## [1.76.284] – 2026-09-08

### Zmenené

- Opravená aktivácia náhrady ikon v reálnych oknách a popupoch; doplnený opakovateľný WPF smoke test tém, väzieb a vykreslenia.
- Zjednotené farebné vektorové ikony stavov: zelené splnenie/štart, oranžové upozornenie/čakanie, červená chyba/stop a modré informácie/úpravy.
- Operátorské hlásenia používajú priame vykresľovanie ikon; spoločné zaškrtávacie políčka sú zelené a knižnica ikon tlačidiel používa farby podľa významu.

## [1.76.283] – 2026-09-08

### Opravené

- Pole Dôvod rozhodnutia už neblokuje všeobecný zámok vstupov bežiacej kalibrácie. Počas rozhodovania zostáva editovateľné; jeho povinnosť a odomknutie akcií sú jasne vysvetlené.

## [1.76.282] – 2026-09-08

### Zmenené

- Otvoriť súbory na FBG karte otvára sieťovú repliku konkrétneho behu (G: / rok / mesiac / beh). Chýbajúca konfigurácia alebo nedostupná sieť sa vysvetlí upozornením; tlačidlo nevytvára prázdne priečinky.

## [1.76.281] – 2026-09-08

### Zmenené

- FBG karta má pod odhadom konca rovnaký kompaktný progressbar s percentami ako profil komory. Odhady dokončenia FBG aj profilu používajú jednotnú vektorovú ikonu hodín.

## [1.76.280] – 2026-09-08

### Pridané

- Editovateľná vstupná stabilita komory pred WIKA v administrácii a FBG: ±0,5 °C, okno 120 s, rozsah 0,5 °C, drift 0,1 °C/min. Odporúčanie vychádza zo 4 behov a 92 plat.
- WIKA sa číta a loguje, ale jej stabilné okno začne až po splnení komory. Čakanie používa existujúci ohraničený teplotný timeout. Historické checkpointy a zapojenia sa nemenia automaticky.

## [1.76.279] – 2026-09-08

### Zmenené

- Kompaktnejší panel Rýchle ovládanie: menšie medzery a predvoľby, odstránený samostatný nadpis vlastnej teploty a časovač so stavom v spoločnom riadku.

## [1.76.278] – 2026-09-08

### Zmenené

- Hlásenie stabilizácie pomenúva vypršaný časový limit, pravidlo operátorského dohľadu a nesplnený stabilný čas.
- Textové fajky v zobrazovanom texte aplikácie nahrádzajú jednotné zelené vektorové ikony so zachovaním živých dátových väzieb.

## [1.76.277] – 2026-09-08

### Pridané

- Záverečné overenie pri 25 °C zobrazuje pre každý SN, peak a model skutočnú teplotu vypočítanú z kontrolnej λ pomocou koeficientov. Hodnoty sú v CSV, XLSX, histórii a záverečnom e-maile spolu s WIKA, odchýlkou a výsledkom overenia.

## [1.76.276] – 2026-09-08

### Pridané

- Každý nový FBG beh uloží samostatný zapojenie.xlsx s jedným prehľadným hárkom, filtrami, sériovými číslami, kanálmi a výberom peakov. Súbor sa replikuje spolu s behom; obnovenie zachová pôvodné zapojenie.

## [1.76.275] – 2026-09-08

### Zmenené

- Hlavička FBG kalibrácie zobrazuje stav automatického ukladania zapojenia: Ukladá sa…, Uložené o HH:mm:ss alebo Chyba uloženia. Farebný indikátor a vysvetlenie zobrazia aj príčinu chyby.
- Ručné uloženie zostáva dostupné cez malú ikonu. Chyby automatického ukladania sa zachytia a zostanú viditeľné; oneskorené zrušené požiadavky sa neukladajú.

## [1.76.274] – 2026-09-08

### Pridané

- Tlačidlo Ukončiť a začať odznova zastaví kalibráciu a po potvrdenom STOP komory zruší checkpoint. Nasledujúce spustenie začne od prvého bodu s novým ID.
- Zapojenie a nastavenia zostanú pripravené; pôvodný beh a merania zostanú v histórii označené ako ukončené operátorom. Pri chybe STOP alebo uloženia sa pokračovanie nemaže. Akcia vyžaduje potvrdenie v aplikácii.

## [1.76.273] – 2026-09-08

### Zmenené

- Po vyčerpaní stabilizácie wavelength sa vždy spustí ohraničený odber finálnych vzoriek aj bez stabilného FBG. Nestabilita wavelength už sama nezastaví vyhodnotenie ani pri operátorskom dohľade. Platné namerané dáta sa zahrnú do koeficientov s označením problému a zachovaním diagnostiky.
- Každý senzor s výsledkami má vyhodnotenie; pri nedostatku rôznych teplotných bodov alebo singulárnom modeli vznikne explicitný N/A výsledok s dôvodom, bez vymyslených koeficientov.
- Po plánovaných bodoch vždy nasleduje návrat na 25 °C, temperovanie a nezávislý kontrolný odber. Koeficienty predpovedia λ pri nameranej teplote WIKA; zaznamená sa rozdiel v pm a chyba prepočítanej teploty. Kontrolné dáta sa nezahŕňajú do výpočtu koeficientov.
- História, CSV a XLSX koeficientov obsahujú výsledok kontroly, WIKA, meranú a vypočítanú λ, odchýlky a príčinu problému. Neúplná alebo nestabilná kontrola sa neoznačí ako PASS. Limit teplotnej chyby používa existujúcu toleranciu modelu.
- Časový odhad a pomocníky vysvetľujú dodatočný odber. Chýbajúca WIKA, strata komunikácie, explicitný STOP a bezpečnostné blokovania zostávajú odlíšené od samotnej nestability wavelength.

## [1.76.272] – 2026-09-08

### Zmenené

- Operátorský dohľad má výraznú zlatú vektorovú ikonu štítu s výkričníkom v zaoblenom poli. Nahrádza textové výstražné znaky; stav zostáva čitateľný a zalamuje sa podľa šírky.

## [1.76.271] – 2026-09-08

### Pridané

- Referenčný teplomer v FBG kalibrácii má tlačidlo Odpojiť teplomer. Zastaví polling a samostatný záznam teplomera, počká na prebiehajúce čítanie, vráti WIKA do LOCAL a uvoľní COM port.
- Ručné odpojenie sa pamätá podľa portu aj USB identity vrátane reštartu aplikácie a obnovy zoznamu portov. Automatické čítanie ho neobnoví; Načítať teplotu alebo explicitné pripojenie ho zruší.
- Priradenie ku komore zostáva uložené, živá teplota sa vymaže a stav pripojenia sa aktualizuje priamo z teplomera. Odpojenie referencie používanej bežiacou kalibráciou je zablokované.

## [1.76.270] – 2026-09-08

### Opravené

- Upozornenia sa umiestňujú podľa skutočnej viditeľnej polohy okna a pracovnej plochy jeho monitora s prepočtom DPI. Maximalizované alebo čiastočne odsunuté okno už neposúva popup mimo obrazovky.
- Šírka a výška upozornenia sa prispôsobia dostupnému priestoru; dlhý obsah sa dá posúvať. Staršie upozornenia bez miesta sa zatvoria namiesto umiestnenia mimo obrazovky.

## [1.76.269] – 2026-09-08

### Opravené

- Stav FBG kalibrácie a operátorského dohľadu má v karte samostatný riadok pod hlavičkou LIVE. Dlhý text sa zalamuje podľa šírky karty a neprekrýva názov ani ďalšie údaje.

## [1.76.268] – 2026-09-08

### Opravené

- Vyhľadávanie v zapojení FBG má krátky, celý viditeľný pomocný text „Hľadať v zapojení…“ a vyhradené miesto pre tlačidlo vymazania. Úplný zoznam prehľadávaných údajov zostáva v popise po prejdení myšou.

## [1.76.267] – 2026-09-08

### Opravené

- Tabuľka zapojenia FBG používa jemné súvislé modré zvýraznenie výberu bez hrubých rámčekov okolo každej bunky. Aktívnu bunku označuje tenká modrá spodná čiara namiesto červeného rámu.
- Tenké zvislé deliace čiary jasne oddeľujú stĺpce. Zachované editovanie, validácia, ukladanie, virtualizácia a zmrazené stĺpce.

## [1.76.266] – 2026-09-08

### Zmenené

- Upozornenia aplikácie používajú farebné vektorové ikony: červený alarm, žltý výstražný trojuholník, zelené potvrdenie a modrú informáciu v zaoblenom farebnom poli.
- Pokojnejšie tmavé pozadie zvýrazňuje symbol a text. Kopírovanie a zatvorenie majú jednotné vektorové ikony s prístupnými názvami; duplicitné úvodné textové symboly sa nezobrazujú v nadpise.
- Zachované časovanie, kopírovanie celého upozornenia, zvuky, filtrovanie a potláčanie duplicít.

## [1.76.265] – 2026-09-08

### Zmenené

- Vzdušnejšie rýchle ovládanie komory: kompaktné teplotné predvoľby zvýraznia potvrdený setpoint, väčšie pole vlastnej teploty a tlačidlo Nastaviť dopĺňa oddelený červený Stop.
- Automatické vypnutie má samostatný spodný riadok s časom dostupným po zapnutí a stavom Bez časového limitu. Úprava predvolieb je dostupná cez ceruzku s vysvetlením.
- Zachované existujúce príkazy, oprávnenia, blokovanie počas profilu a FBG kalibrácie aj spätná väzba operácií.

## [1.76.264] – 2026-09-08

### Zmenené

- Vstavané predvoľby FBG kalibrácie zodpovedajú schváleným nastaveniam: zapnutá rampa 2 °C/min, stabilný čas WIKA 10 minút a interval odberu FBG 30 s.
- Zachované predvoľby: tolerancia 1 °C; WIKA drift 0,03 °C/min, rozsah 0,03 °C a StdDev 0,01 °C; timeout teploty aj peaku 60 minút; trace log 1 s; 50 stabilizačných a 50 finálnych vzoriek; FBG rozsah 5 pm, StdDev 1,5 pm a drift 1 pm/min.
- Uložené administrátorské nastavenia a nastavenia existujúcich či obnovených behov sa automaticky neprepisujú.

## [1.76.263] – 2026-09-08

### Pridané

- Administrácia obsahuje viditeľné otázniky pri všetkých 15 predvolených parametroch FBG kalibrácie aj pri plynulej rampe. Kliknutie otvorí vysvetlenie významu, jednotiek, stability, timeoutov a platnosti predvolieb.
- Pomocník odberu, počtov vzoriek a timeoutu peaku vypočíta orientačný čas z práve nastavených hodnôt. Odlišuje priebežný trace log od vzoriek kalibrácie a upozorňuje na dodatočný čas ustálenia a opakovaní.

## [1.76.262] – 2026-09-08

### Pridané

- Redundantné ukladanie FBG kalibrácií: priebežný zápis na lokálny disk PC a predvolene aj sieťová kópia v produkčnom priečinku G:. Obe kópie zachovávajú štruktúru rok / mesiac / beh.
- Jeden pracovník na pozadí synchronizuje zmenené súbory predvolene každých 10 sekúnd. Výpadok siete neblokuje lokálne meranie; trvalý front obnoví kopírovanie po návrate siete aj po reštarte aplikácie. Rozpracovaná sieťová kópia sa nahrádza cez dočasný súbor.
- Administrácia obsahuje lokálnu a sieťovú cestu, prepínač sieťovej kópie pre nové behy, interval, kontrolu zápisu, ručnú synchronizáciu a priebežný stav čakajúcich kópií. Lokálna cesta je povinná a nesmie byť sieťová ani vnorená do druhého úložiska.
- Cesty rozbehnutého behu sa nemenia po úprave predvolieb. História uprednostní lokálnu kópiu, nezobrazuje duplicity a nájde aj staršie cesty z frontu. Starý beh uložený iba na sieti sa pred obnovením skopíruje lokálne.
- Panel dát FBG kalibrácie zobrazuje obe cesty a stav synchronizácie. Zálohujú sa trace log, raw samples, diagnostika, výsledky a reporty.

## [1.76.261] – 2026-09-08

### Zmenené

- Nové FBG kalibračné behy sa ukladajú do G:\Projekty\FOS\1_Production\C_Calibration_data\10_Calibration_automatic\01_Temperature, priamo pod rok a slovenský názov mesiaca (napr. 2026\09_September\ID_behu).
- Zaradenie určuje dátum spustenia; beh zostáva v rovnakom priečinku aj po prechode do ďalšieho mesiaca alebo roka. Trace log, raw samples, výsledky aj reporty sú spolu.
- História, obnova behov a otváranie priečinkov rozumejú mesačnej štruktúre a naďalej nájdu pôvodné lokálne behy. Zapojenia a checkpointy zostávajú lokálne.
- Nedostupný alebo nezapisovateľný cieľ zobrazí chybu s cestou a nepovolí spustenie nového behu bez uloženia dát.

## [1.76.260] – 2026-09-08

### Zmenené

- Spoločná e-mailová šablóna používa výrazné farebné ikony: sirénu pri alarme, výstražný trojuholník pri upozornení, zelené potvrdenie pri dokončení a modrú informáciu. Ikony majú vlastnú väčšiu plochu a písma pre farebné emoji; textový názov závažnosti zostáva čitateľný.
- E-maily operátorského dohľadu a zastavenej kalibrácie dostávajú výstražnú alebo alarmovú ikonu namiesto informačného označenia. Farebné symboly sa pridávajú aj do predmetu správ bez zdvojenia pôvodnej ikony. Rovnaký vzhľad používajú náhľady v administrácii.

## [1.76.259] – 2026-09-08

### Pridané

- Voliteľný operátorský dohľad pre FBG beh: výrazné označenie v okne aj na karte komory, pomocník „?“ a červený panel s dôvodom problému a odpočtom.
- Timeout WIKA/FBG, chyba komunikácie, strata peaku, zastarané dáta, rozpad finálneho meracieho okna alebo nepotvrdená teplotná odozva vyžiadajú rozhodnutie a jeden e-mail pre danú udalosť. Pred štartom sa kontroluje zapnutie e-mailov kalibrácie a adresát.
- Operátor môže so zapísaným dôvodom predĺžiť čas o 15/30 minút, zopakovať meranie, preskočiť nepotvrdený bod alebo ukončiť beh. Pri strate komunikácie sú dostupné nový pokus a STOP. Po zásahu sa WIKA a rozpracované okná overujú odznova; pôvodné surové dáta zostávajú zachované.
- Bez rozhodnutia do 30 minút sa beh riadene ukončí a odošle STOP komore. Nepotvrdený STOP sa hlási ako chyba. Čakanie neblokuje zapnutý trace log; režim, požiadavka a rozhodnutia sa ukladajú do výsledkov.
- Nový pokus po chybnej teplotnej odozve uchová pôvodný výsledok medzi nahradenými pokusmi. Preskočenie ani uložená výnimka z validácie v režime dohľadu neoznačia nepotvrdený výsledok za úspešný.

## [1.76.258] – 2026-09-08

### Opravené

- Pri každom otvorení alebo opätovnom zobrazení FBG kalibrácie sa zopakuje health kontrola Sylex FOS API. Súbežné otvorenia nespúšťajú duplicitné kontroly.
- Nedostupné API zobrazí červenú chybu s adresou, dôvodom a vysvetlením, že automatické dopĺňanie tabuľky nefunguje. Kontrola beží na pozadí bez blokovania okna a bez e-mailov za jednotlivé SN.

## [1.76.257] – 2026-09-08

### Zmenené

- Predvolený interval priebežného wavelength trace logu je 1 s. Administrácia umožňuje nastaviť jeho interval samostatne od odberu pre stabilitu. Každý nový beh preberá uložený interval z administrácie aj pre existujúce zapojenie; obnovené a aktívne behy si zachovajú svoje nastavenia.

## [1.76.256] – 2026-09-08

### Opravené

- Správa o dokončení kalibračného bodu odovzdáva dashboardu skutočné výsledky všetkých senzorov. Úspešné body sa počas živého behu už nesprávne neoznačia ako nepotvrdené so stabilitou 0/N len kvôli prázdnemu zoznamu v stavovej správe. Uložené namerané výsledky sa nemenia.

## [1.76.255] – 2026-09-08

### Zmenené

- Karta prebiehajúcej FBG kalibrácie a jej detail zobrazujú samostatne úspešné, nepotvrdené/neúspešné a zostávajúce body. Aktuálne rozpracovaný bod patrí medzi zostávajúce; ukončenie bez stability sa nepočíta ako úspech.

## [1.76.254] – 2026-09-08

### Opravené

- FBG už neposiela samostatný e-mail ani zvukový alarm za každý neustálený senzor, ak beh automaticky pokračuje. Priebežné upozornenia zostávajú v histórii a záverečnom reporte.
- Okamžité oznámenie a jeden e-mail vzniknú až pri skutočnom zastavení behu alebo požiadavke na zásah operátora. Záverečný súhrn zostáva zachovaný.

## [1.76.253] – 2026-09-08

### Opravené

- Kalibračný plán rozlišuje úspešné, nepotvrdené a neúspešné body namiesto mätúceho DONE. Zobrazuje počet senzorov s potvrdenou stabilitou aj pri obnovení starších výsledkov. Súhrnné karty neoznačujú nepotvrdenú stabilitu za úspech; percentá vyjadrujú ukončené body.

## [1.76.252] – 2026-09-08

### Opravené

- FBG časový limit sa počíta z počtu stabilizačných a finálnych vzoriek a skutočného intervalu odberu s 20 % rezervou. Uložený timeout je minimálny základ, pevný strop zostáva 90 min od prvého začiatku FBG fázy.
- Predĺženie po 10 min je možné iba pri pokroku merania alebo zlepšení celých stabilizačných okien. Reset, zmena nastavení a strata stability WIKA nevynulujú časový strop.
- Po vyčerpaní limitu sa už nespúšťa dlhé náhradné meranie. Neúspešný peak sa označí a použije sa nastavená politika pokračovania alebo zastavenia. Upozornenia, zostávajúci čas, nastavenia a pomoc pod otáznikmi vysvetľujú aktuálne pravidlá.

## [1.76.251] – 2026-09-07

### Zmenené

- Vo FBG live prehľade pribudla otázka „?“ pri karte „Aktuálny limit plata“.
  Vysvetľuje, čo znamená základný limit, priebeh automatických predĺžení a prečo
  sa rozlišuje „uplynulo“ a „zostáva“ počas čakania na stabilitu WIKA.

## [1.76.250] – 2026-09-07

### Opravené

- Každá nová FBG kalibrácia načíta interval odberu z uložených predvolieb Admin zóny aj pri existujúcom zapojení. Interval sa aktualizuje pri načítaní profilu aj pred štartom; ostatné nastavenia zapojenia zostávajú zachované. Rozbehnuté a obnovené behy si ponechávajú svoj pôvodný interval.
- Nastavenia FBG vysvetľujú pôvod intervalu a zobrazujú ho len na čítanie. Interval pre nové behy sa upravuje v Admin zóne.
- V kalibračnom workspaci sa tlačidlo Spustiť kalibráciu v režime simulátora už nedá zakázať nefunkčným stavom pripojenia PeakLoggera; ak simulátor beží a klient ešte nie je vytvorený, spustenie ho automaticky inicializuje.

## [1.76.249] – 2026-09-07

### Zmenené

- Rýchle ovládanie má menšie tlačidlá a číselné vstupy s jednotnou výškou 28 px a menšími medzerami. Ostatné číselné vstupy zachovávajú predvolenú výšku 36 px.

## [1.76.248] – 2026-09-07

### Opravené

- Neaktívna komora po pripojení nezobrazuje historický setpoint regulátora. Teplotný aj vlhkostný setpoint sa zobrazia až po potvrdenom behu alebo spustení z aplikácie; bez aktívneho nastavenia zostáva pomlčka.

## [1.76.247] – 2026-09-07

### Zmenené

- Teplotná poistka je malý nenápadný údaj vedľa rozsahu, bez rámčeka. Kliknutie na údaj s ceruzkou otvorí úpravu limitov; aktivovaná poistka zostáva zvýraznená červenou.

## [1.76.246] – 2026-09-07

### Opravené

- Číselné polia modalu teplotnej poistky majú zvislo vycentrovaný text bez orezania pri kompaktnej výške.

## [1.76.245] – 2026-09-07

### Zmenené

- Rozsah zariadenia a stav teplotnej poistky sú pri pripojení. Tlačidlo Upraviť otvorí modal s validáciou oboch limitov a možnosťou zrušiť zmeny.
- Rýchle ovládanie má kompaktnejší časovač, predvoľby v jednom riadku a jednotnú výšku vstupov a tlačidiel.

## [1.76.244] – 2026-09-07

### Odstránené

- Z kalibračného prehľadu bolo odstránené tlačidlo `+30 min na ustálenie` aj celý mechanizmus ručného predlžovania časového limitu stability. Automatické predĺženie stability zostáva bez zmeny.

## [1.76.243] – 2026-09-07

### Zmenené

- Tabuľka kalibračných koeficientov v záverečnom e-maile zobrazuje každé výrobné SN presne v jednom riadku. Pre jednotlivé kombinácie kanála, peaku a typu kalibrácie automaticky vytvorí samostatné stĺpce s koeficientmi a výsledkami bez opakovania SN.

## [1.76.242] – 2026-09-07

### Odstránené

- Z celej aplikácie bolo odstránené automatické jemné dorovnávanie setpointu komory podľa WIKA vrátane ovládania, uložených volieb, stavovej diagnostiky a regulačnej logiky. WIKA zostáva autoritatívnou referenciou na vyhodnotenie stability, ale aplikácia podľa nej setpoint komory neupravuje.

## [1.76.241] – 2026-09-07

### Opravené

- Po odpojení komory alebo strate komunikácie sa na karte zariadenia už nezobrazujú posledné namerané hodnoty teploty, setpointu, vlhkosti ani referencie. Konfigurácia zariadenia a bezpečnostné limity zostávajú zachované.

## [1.76.240] – 2026-09-07

### Zmenené

- Nové priečinky FBG kalibrácií začínajú čitateľným ID kalibrácie zobrazeným v aplikácii, napríklad 02-2026-09-06__..., takže ich možno jednoznačne priradiť ku konkrétnemu behu priamo v Prieskumníkovi. Otváranie súborov, história, e-mailové prílohy aj obnovenie behu používajú spoločný resolver a naďalej podporujú staršie priečinky pomenované iba interným GUID.

## [1.76.239] – 2026-09-07

### Opravené

- Po dokončení FBG kalibračných bodov aplikácia nastaví záverečnú teplotu 25 °C a spustí pevný 60-minútový čas temperovania. Čas sa už nevynuluje pri kolísaní teploty ani nečaká na stabilitu; po jeho uplynutí aplikácia odošle zariadeniu STOP a vypne jeho výkon.

## [1.76.238] – 2026-09-07

### Zmenené

- CSV a Excel súbory vytvárané pri FBG kalibrácii používajú slovenské zobrazenie čísel s desatinnou čiarkou. Týka sa to súhrnu, koeficientov, surových vzoriek, priebehu vlnovej dĺžky aj reportov kalibračných bodov; čítanie starších CSV s desatinnou bodkou zostáva podporované.

## [1.76.237] – 2026-09-07

### Pridané

- V hlavnom menu pribudla samostatná záložka `FBG kalibrácie`. Zobrazuje všetky uložené kalibračné behy, ich profil, komoru, stav a počet snímačov; pri vybranej kalibrácii sprístupní jej priečinok a podrobnú tabuľku výsledkov jednotlivých snímačov vrátane citlivosti, chyby, R², výsledku a stavu stability.

## [1.76.236] – 2026-09-07

### Opravené

- Kontrola dostupnosti Sylex FOS API po prechodnom timeoute opakuje požiadavku a používa systémový heartbeat ako záložný endpoint. Prvé úspešné načítanie výrobných metadata navyše okamžite nahradí starý stav `nedostupné` stavom `dostupné`, takže aplikácia už nezobrazuje nepravdivé upozornenie pri funkčnom API.

## [1.76.235] – 2026-09-06

### Opravené

- Po úspešnom zastavení ľubovoľnej komory, sušiarne alebo kúpeľa sa zobrazený teplotný aj vlhkostný setpoint vymaže na `—`. Historická cieľová hodnota, ktorú niektoré regulátory vracajú aj v stave STOP, sa už na karte nezobrazí ako aktívne nastavenie; pri zlyhaní STOP zostane hodnota zachovaná spolu s alarmom.

## [1.76.234] – 2026-09-06

### Pridané

- Administrátor môže v časti `Rozloženie nástenky` nastaviť ID existujúceho programu POL-EKO `FOS LAB`. Hodnota sa trvalo uloží a používa sa pri úprave, spustení aj overení ďalšieho manuálneho setpointu bez pevnej závislosti od programu 11.

## [1.76.233] – 2026-09-06

### Opravené

- POL-EKO po úspešnom zápise a potvrdenom spustení programu `FOS LAB` toleruje chybnú odpoveď `DATA_CORRUPTED` z nepodporovaného katalógu `GET_PROGRAMS`. Karta preto správne zobrazí nastavenú teplotu a stav `Aktívna`; ostatné chyby a skutočný nesúlad teploty naďalej spustenie zamietnu.

## [1.76.232] – 2026-09-06

### Opravené

- Program POL-EKO `FOS LAB` odosiela cieľovú teplotu aj ochranné limity priamo v stupňoch Celzia podľa reálneho správania SLN 115. Hodnota 20 °C sa odošle ako `20`, nie `200`, ktoré zariadenie interpretovalo ako 200 °C a preto nesprávne kúrilo.
- Spätné overenie uloženého programu používa rovnaké priame jednotky.

## [1.76.231] – 2026-09-06

### Opravené

- Po zbalení grafu USB referenčnej teploty sa jeho skrytý riadok už neroztiahne cez voľnú výšku okna. Záložky `Nastavenia`, `Zapojenie`, `Prehľad` a ostatné sa zobrazia hneď pod hornou lištou bez veľkej prázdnej medzery.

## [1.76.230] – 2026-09-06

### Opravené

- Ručne prepísaná hodnota v číselnom editore už nezruší WPF dátovú väzbu. Tlačidlo `Nastaviť` preto odošle presne zobrazenú teplotu (napr. 20 °C), nie predchádzajúcu hodnotu 25 °C; rovnaká oprava platí aj pre ostatné číselné polia a ich šípky.

## [1.76.229] – 2026-09-06

### Zmenené

- Projektové a release inštrukcie vyžadujú tokenovo úsporné overovanie: úspešné testy a Release build vracajú iba stručný súhrn, zatiaľ čo podrobné logy sa čítajú iba pri zlyhaní alebo relevantnom varovaní. Povinné overenie pred vydaním zostáva zachované.

## [1.76.228] – 2026-09-06

### Opravené

- Ochranné teploty programu POL-EKO `FOS LAB` sa teraz odosielajú v správnych desatinách stupňa Celzia. Nastavenie rozsahu `-45 až 190 °C` sa teda uloží ako `-450 až 1900`, takže zariadenie pri setpointe 20 °C nesprávne nevyhlási alarm `OVER_TEMP_PROTECTION`.

## [1.76.227] – 2026-09-06

### Opravené
- Program POL-EKO „FOS LAB“ sa pri manuálnom nastavení vytvára z aktuálneho setpointu a teplotnej poistky nastavenej na karte namiesto pevných neplatných limitov `50/50`. Po spustení aplikácia načíta katalóg programov, overí skutočne uloženú teplotu a pri nesúlade program bezpečne zastaví; lokálny časovač naďalej nezávisle vypína výkon po zvolenom čase.

## [1.76.226] – 2026-09-06

### Opravené
- POL-EKO LabDesk má pri spustení programu FOS LAB primeraný čas na oneskorenú odpoveď `LAUNCH_BY_ID`. Pri skutočnom timeoute sa znečistené spojenie zahodí, po opätovnom pripojení sa overí reálny stav programu a príkaz sa zopakuje iba vtedy, ak sa nespustil; oneskorená odpoveď sa už nepriradí nasledujúcemu `GET_STATUS`.

## [1.76.225] – 2026-09-06

### Opravené
- Po zbalení veľkého grafu WIKA už jeho pracovná plocha nenecháva nad kartami kalibračného prehľadu veľké prázdne miesto. Riadok kariet sa prispôsobí obsahu a zostane zarovnaný hneď pod hornými ovládacími prvkami.

## [1.76.224] – 2026-09-06

### Opravené
- Aktualizácia teploty WIKA počas FBG kalibrácie sa teraz vždy prenesie na WPF UI vlákno. Čítanie referencie na pracovnom vlákne už nezastaví nový ani obnovený beh chybou „The calling thread cannot access this object because a different thread owns it“.

## [1.76.223] – 2026-09-06

### Zmenené
- Projektový `SKILL.md` a GitHub `README.md` boli zosúladené s aktuálnou implementáciou FBG kalibrácie, WIKA CTH7000, obnovovania checkpointov, živého ladenia stability, tabuľkového zapojenia a manuálneho ovládania POL-EKO cez program FOS LAB.

## [1.76.222] – 2026-09-06

### Opravené
- Zmena teploty aktívnej sušiarne POL-EKO cez tlačidlo „Nastaviť“ teraz automaticky počká na potvrdené zastavenie programu FOS LAB a na uvoľnenie programu pre editáciu. Dočasnú odpoveď `GENERAL_ERROR` pri aktualizácii zopakuje, takže operátor už nemusí najskôr ručne stlačiť Stop.

## [1.76.221] – 2026-09-06

### Opravené
- Pokračovanie obnovenej FBG kalibrácie už v prvom cykle zbytočne nečíta komoru druhýkrát po odčítaní WIKA. Použije poslednú čerstvú hodnotu komory, takže komunikácia po kliknutí na „Pokračovať od plata“ nezlyhá na duplicitnom prístupe.
- Pôvodná príčina prípadného zlyhania kalibračného runnera sa zapíše do diagnostiky ešte pred zatvorením súboru a zobrazí sa operátorovi; chyba sa už neprekryje zápisom do zatvoreného logu.

## [1.76.220] – 2026-09-06

### Opravené
- Veľký graf „Priebeh USB referenčnej teploty“ je po otvorení alebo obnovení FBG kalibrácie opäť predvolene zbalený. Zobrazí sa iba na výslovnú požiadavku operátora, takže neposúva karty a prehľad mimo úvodnej obrazovky.

## [1.76.219] – 2026-09-06

### Opravené
- Manuálne nastavenie teploty sušiarne POL-EKO používa existujúci vyhradený program „FOS LAB“ s ID `11` namiesto neexistujúceho programu `99`; po aktualizácii naďalej overí, že zariadenie skutočne spustilo správny program.

## [1.76.218] – 2026-09-06

### Opravené
- Karta „Prehľad“ FBG kalibrácie už omylom neskrýva celý pracovný panel s kartami. Pri aktívnej alebo obnovenej kalibrácii zostáva kompletný prehľad viditeľný pod voliteľným grafom WIKA.

## [1.76.217] – 2026-09-06

### Zmenené
- Prihlasovacie údaje zariadenia POL-EKO sa načítavajú z lokálnych používateľských nastavení Windows namiesto pevného hesla v zdrojovom kóde; heslo zostáva v komunikačnom logu maskované.

### Opravené
- Ovládacie príkazy POL-EKO môžu použiť skutočné poverenie používateľa zariadenia, takže nie sú odmietnuté stavom `UNAUTHORIZED` iba preto, že účet LabDesk používa iné heslo.

## [1.76.216] – 2026-09-06

### Zmenené
- Predvolené limity stabilného okna WIKA sú sprísnené na rozsah `0,030 °C`, smerodajnú odchýlku `0,010 °C` a drift `0,030 °C/min`.
- Stabilita WIKA súčasne kontroluje lineárny drift celého okna aj krátkodobý drift posledných 120 sekúnd; rozhoduje prísnejšia hodnota.

### Opravené
- Drift WIKA sa znovu počíta lineárnou regresiou zo skutočných časov a teplôt každej novej vzorky. Rastúca alebo klesajúca referencia už nezostane nesprávne zelená s hodnotou `0,000 °C/min`.

## [1.76.215] – 2026-09-06

### Pridané
- Obnovená kalibrácia ponúka potvrdenú akciu „Použiť aktuálne predvoľby“, ktorá prenesie administrátorské nastavenia do checkpointu, zachová dokončené plata a zmenu zapíše do histórie behu.
- Pokračovanie z checkpointu je dostupné až po načítaní PeakLoggera a platného živého merania pôvodne použitej WIKA; stav oboch podmienok je viditeľný priamo v pracovnom priestore.

### Opravené
- Obnovenie už nemožno spustiť iba s načítaným zapojením bez dostupnej referencie, ak pôvodný kalibračný beh používal WIKA CTH7000.

## [1.76.214] – 2026-09-06

### Zmenené
- Stavy peakov v kompaktných zoznamoch sú zobrazené ako farebné chipy: meranie modrou, hotovo zelenou a stabilizácia, čakanie alebo upozornenie oranžovou.
- Farebné rozlíšenie sa používa konzistentne v prehľade stability aj finálneho merania.

## [1.76.213] – 2026-09-06

### Zmenené
- Záchranné finálne meranie po neúspešnej stabilizácii vytvára každú výslednú vzorku ako priemer troch po sebe idúcich surových odberov. Pri 50 výsledných vzorkách sa preto odoberie 150 surových hodnôt.
- Živý stav zobrazuje počet hotových priemerovaných vzoriek aj rozpracovanú trojicu; auditné upozornenie pri SN zostáva zachované.

## [1.76.212] – 2026-09-06

### Zmenené
- Peak, ktorý do timeoutu nesplní stabilizačné podmienky, pri politike pokračovania dokončí celé finálne vzorkovanie a jeho dáta sa uložia a použijú na záverečný výpočet kalibrácie.
- Takýto peak zostáva označený upozornením na problém so stabilizáciou v živom prehľade, histórii, CSV, Exceli a záverečnom e-maile; výsledok kalibračného modelu PASS/FAIL zostáva vyhodnotený samostatne.

## [1.76.211] – 2026-09-06

### Pridané
- Kliknutie na konkrétny SN v hornom zozname stability alebo finálneho merania presunie zobrazenie na zodpovedajúci detailný graf a zvýrazní jeho kartu.
- Rovnaké otvorenie detailu je dostupné klávesmi Enter a medzerník.

## [1.76.210] – 2026-09-06

### Opravené
- Súhrn karty FBG peaky už nezamieňa úspešnú stabilizáciu s úplne dokončeným meraním. Samostatne uvádza počet peakov, ktoré prešli stabilitou, práve zbierajú finálne vzorky a sú úplne dokončené.

## [1.76.209] – 2026-09-06

### Pridané
- Karta FBG peaky v živom prehľade zobrazuje aktuálne podmienky stability: počet vzoriek, maximálny range, smerodajnú odchýlku a drift.
- Zobrazené tolerancie sa obnovujú podľa aktuálnych nastavení kalibrácie.

## [1.76.208] – 2026-09-06

### Opravené
- Obnovenie alebo opätovné pripojenie PeakLoggera už nezobrazí falošné hlásenie „pribudol nový riadok“, ak zostali rovnaké kanály a Peak ID.
- Zmena dočasného alebo API identifikátora interrogátora sa nepovažuje za zmenu fyzickej topológie; skutočne pridaný alebo odstránený kanál/peak sa naďalej rozpozná.

## [1.76.207] – 2026-09-06

### Opravené
- Výber v tabuľke Zapojenie zostáva po kliknutí zvýraznený modrou farbou cez celý riadok; jednotlivé inicializačné vrstvy už neprepínajú tabuľku medzi výberom bunky a riadka.
- Jednokliková editácia povolených buniek a automatické presunutie na nový peak zostávajú zachované.

## [1.76.206] – 2026-09-06

### Pridané
- Počas spustenej FBG kalibrácie možno meniť toleranciu, stabilný čas, rozsah, smerodajnú odchýlku a drift WIKA aj počet a limity stabilizačných vzoriek FBG.
- Zmenené limity sa okamžite prenesú do aktívneho plata, bezpečne vynulujú rozpracované stabilizačné a finálne meracie okná a zapíšu sa s pôvodnými aj novými hodnotami do auditu behu.

### Zmenené
- Parametre riadenia komory, interval odberu, timeouty, finálne meranie, filtrovanie a validačná politika zostávajú počas behu uzamknuté.

## [1.76.205] – 2026-09-06

### Zmenené
- Predvolená maximálna tolerancia driftu referenčnej teploty WIKA bola nastavená na 0,030 °C/min pre nové kalibrácie.

## [1.76.204] – 2026-09-06

### Zmenené
- Automatické jemné dorovnávanie setpointu komory podľa WIKA je predvolene vypnuté. Pri prvom načítaní novej verzie sa vypne aj pôvodná predvoľba zariadení; operátor ho môže pred spustením kalibrácie ručne zapnúť.
- Pomocník a popisy nastavenia teraz zodpovedajú vypnutej predvoľbe.

## [1.76.203] – 2026-09-06

### Zmenené
- Predvolený interval odberu FBG pre každú novú kalibráciu bol nastavený na 10 sekúnd. Existujúce uložené a rozbehnuté kalibrácie si ponechávajú vlastný interval.

## [1.76.202] – 2026-09-06

### Opravené
- Ovládacie príkazy POL-EKO LabDesk používajú správne prihlasovacie údaje `admin`; sušiareň už neodmietne STOP, uloženie programu 99 ani jeho spustenie odpoveďou `UNAUTHORIZED` pre prázdne heslo.
- Heslo POL-EKO sa v diagnostickom TX zázname maskuje a nezapisuje sa do aplikačného logu.

## [1.76.201] – 2026-09-06

### Zmenené
- Predvolená teplotná tolerancia WIKA pre nové FBG kalibrácie bola nastavená na ±1,0 °C. Ostatné sprísnené podmienky súvislej stability, rozsahu, smerodajnej odchýlky a driftu zostávajú zachované.

### Opravené
- Otvorenie kalibračného okna už nezlyhá na chýbajúcom WPF zdroji `SuccessBrush`; indikátor pripojeného PeakLoggera používa existujúci jednotný zelený zdroj aplikácie.

## [1.76.200] – 2026-09-06

### Opravené
- Stabilita referencie WIKA sa už nezbiera bodovým skóre počas pomalého rastu alebo poklesu. Vyžaduje jedno neprerušené okno, v ktorom každá vzorka spĺňa toleranciu a celé okno spĺňa limit rozsahu, smerodajnej odchýlky aj lineárneho driftu.
- Každé prekročenie podmienky a každý vykonaný krok automatického dorovnania setpointu vynuluje stabilný čas; staršie vzorky sa nepoužijú na otvorenie FBG fázy.
- Nové predvolené nastavenia sú: tolerancia ±0,2 °C, súvislá stabilita 5 min, drift najviac 0,02 °C/min, rozsah WIKA najviac 0,1 °C a smerodajná odchýlka najviac 0,03 °C. Rozsah a smerodajnú odchýlku možno nastaviť aj v administrácii a v nastavení kalibrácie.
- Live diagnostika, pomocník a Excel protokol zobrazujú nové WIKA kritériá; záložná verzia bola zvýšená na 1.76.200.

## [1.76.199] – 2026-09-06

### Zmenené
- Zapojenie FBG bolo vizuálne prerobené podľa schváleného referenčného návrhu: kompaktná lišta vyhľadávania a filtrov s počítadlami, 14 stĺpcov v rovnakom poradí, jemné tmavé riadky bez modrých blokov a úzky stavový pás PeakLoggera.
- Tabuľka sa prispôsobí skutočnému počtu peakov a rastie najviac po 16 viditeľných riadkov; pri menšom počte už nevytvára veľkú prázdnu plochu.
- Odstránené boli ovládacie prvky a druhý spodný stavový panel, ktoré neboli súčasťou návrhu; záložná verzia bola zvýšená na 1.76.199.

## [1.76.198] – 2026-09-06

### Opravené
- Stabilizácia každého FBG peaku už nezostane na zavádzajúcom stave 50/50, keď naplnené okno prekročí povolený range, smerodajnú odchýlku alebo drift. Nevyhovujúce okno sa zahodí, počítadlo sa vráti na 0 a začne sa nový čistý stabilizačný pokus.
- Diagnostika a pomocník pracovného postupu zobrazujú presný dôvod resetu; pôvodné vzorky zostávajú iba v auditnom raw zázname a nepoužijú sa na potvrdenie stability.
- Záložná verzia bola zvýšená na 1.76.198.

## [1.76.197] – 2026-09-06

### Opravené
- Návrh tabuľky Zapojenie bol dorovnaný na definitívnu sadu 14 údajových polí a výberový stĺpec bez dodatočného stĺpca timeoutu. Všetky stĺpce majú pevné návrhové šírky, takže sa ich veľkosť nemení podľa okna ani dĺžky obsahu.
- Obnovené boli plné názvy hlavičiek a presné kompaktné rozmery: riadok 35 px, hlavička 42 px, šestnásť viditeľných pracovných riadkov a tri desatinné miesta meraní.
- Záložná verzia bola zvýšená na 1.76.197.

## [1.76.196] – 2026-09-06

### Zmenené
- Zapojenie FBG bolo prerobené podľa schváleného plnošírkového tabuľkového návrhu bez bočného detailu. Všetky produkčné údaje zostávajú v riadkoch, prvých šesť stĺpcov je zmrazených a zvyšok je dostupný vodorovným posunom.
- Pribudlo okamžité vyhľadávanie, filtre Všetky/Vybrané/Chyby a súhrn počtu vybraných peakov a kanálov.
- Riadky majú jednotnú výšku 35 px, merania jednotný font a tri desatinné miesta. Výrazné modré rámiky skupín nahradili jemné deliace čiary; editovacie ceruzky a ochrana rozpracovanej bunky zostali zachované.
- Záložná verzia bola zvýšená na 1.76.196.

## [1.76.195] – 2026-09-06

### Zmenené
- Predvolený základný limit čakania na ustálenie referenčnej teploty pre nové FBG kalibrácie bol zvýšený z 30 minút na 1 hodinu. Rovnakú hodinovú hodnotu používa aj úvodný stav živého kalibračného prehľadu.
- Už uložené zapojenia a práve prebiehajúce kalibrácie si zachovávajú vlastnú uloženú hodnotu; globálnu predvoľbu možno naďalej spravovať v administrátorskej zóne.
- Záložná verzia bola zvýšená na 1.76.195.

## [1.76.194] – 2026-09-06

### Pridané
- V diagnostike POL-EKO pribudlo tlačidlo „Načítať všetky programy“. Bez zmeny stavu zariadenia načíta cez `GET_PROGRAMS` kompletný katalóg a zobrazí počet aj všetky polia programov vrátane ID, názvu a segmentov.

### Opravené
- Existujúce tlačidlo diagnostiky POL-EKO teraz správne používa priamy klient zariadenia aj pri zapnutej nadradenej teplotnej poistke.
- Záložná verzia bola zvýšená na 1.76.194.

## [1.76.193] – 2026-09-06

### Opravené
- Manuálne nastavenie teploty POL-EKO už nepoužíva nepodporovaný príkaz `GET_NEXT_PROGRAM_ID`. Aplikácia používa jediný vyhradený nekonečný program s ID 99, ktorý podľa potreby uloží alebo aktualizuje, spustí a overí cez stav sušiarne.
- Pred nastavením novej manuálnej teploty sa zastaví predchádzajúci program. Teplotná poistka zostáva nadradená zápisu a manuálny časovač po uplynutí naďalej fyzicky zastaví program sušiarne.
- Záložná verzia bola zvýšená na 1.76.193.

## [1.76.192] – 2026-09-06

### Opravené
- Číselné polia minimálnej a maximálnej teplotnej poistky aj časovača vypnutia už nie sú natlačené do výšky menšej než ich ovládací prvok. Hodnoty a obe šípky sú celé viditeľné.
- Záložná verzia bola zvýšená na 1.76.192.

## [1.76.191] – 2026-09-06

### Opravené
- Jemné dorovnávanie podľa WIKA už nepridáva korekciu každých 10 sekúnd bez ohľadu na tepelnú zotrvačnosť. Po každom kroku čaká 2 minúty na odozvu komory a ďalší krok povolí až po vyhodnotení 30-sekundového trendu WIKA.
- Korekcia sa nevykoná, kým sa WIKA pohybuje rýchlejšie než povolený stabilný drift. Tým sa odstraňuje kumulovanie biasu a rozkmitanie teploty okolo cieľa.
- Živá WIKA karta zobrazuje vykonaný krok, aktuálny bias, čakanie na odozvu, zber trendu aj čakanie na spomalenie WIKA.

### Zmenené
- Existujúce nastavenia zariadení sa migrujú na bezpečnú odozvu 120 sekúnd a pozorovacie okno 30 sekúnd. Limity jedného kroku ±0,30 °C a celkového biasu ±3,0 °C zostali zachované.
- Záložná verzia bola zvýšená na 1.76.191.

## [1.76.190] – 2026-09-06

### Pridané
- Živá WIKA karta počas aktívneho jemného dorovnávania výrazne zobrazí stav „DOROVNÁVANIE PREBIEHA“, aktuálny setpoint komory a celkovú korekciu (bias).

### Opravené
- Text pomoci pri WIKA dorovnávaní teraz správne uvádza, že funkcia je predvolene zapnutá.
- Záložná verzia bola zvýšená na 1.76.190.

## [1.76.189] – 2026-09-06

### Zmenené
- Bezpečne obmedzené jemné dorovnávanie setpointu komory podľa WIKA je predvolene zapnuté pre nové FBG kalibrácie.
- Existujúce nastavenia zariadení z predchádzajúcej verzie sa jednorazovo prenastavia na nový zapnutý default; po migrácii môže operátor funkciu pred spustením kalibrácie opäť vypnúť.
- Texty pomoci teraz vysvetľujú nový default. Limity zostávajú bez zmeny: krok najviac 0,30 °C za 10 s a celková korekcia najviac ±3,0 °C.
- Záložná verzia bola zvýšená na 1.76.189.

## [1.76.188] – 2026-09-06

### Opravené
- WIKA graf v živom prehľade je explicitne priradený ku komore svojej kalibrácie. Pri dvoch alebo viacerých súbežných kalibráciách sa už nevymaže pre nejednoznačný výber aktívneho behu.
- Aktuálna WIKA hodnota, detailný graf a kompaktný graf stabilného času teraz používajú rovnakú identitu komory.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.188.

## [1.76.187] – 2026-09-06

### Opravené
- Hodnoty teploty, setpointu, vlhkosti a WIKA referencie na kartách zariadení používajú pevne rovnakú veľkosť písma; dlhšia hodnota už nezmenší font oproti ostatným bunkám.
- Číselné hodnoty na hlavnom paneli sa zobrazujú jednotne na tri desatinné miesta.

### Zmenené
- Bunky živých hodnôt majú kompaktnejšiu výšku, menší zvislý padding a menšiu spodnú medzeru, aby panel zobrazil viac obsahu bez straty čitateľnosti.
- Záložná verzia bola zvýšená na 1.76.187.

## [1.76.186] – 2026-09-06

### Pridané
- Administrácia obsahuje nové centrum `Notifikácie` s vysvetlením všetkých piatich e-mailových udalostí, samostatným zapnutím každého typu a testovacím e-mailom pre každý typ.
- Pribudol prepínateľný náhľad jednotnej HTML šablóny s ukážkovým predmetom, obsahom a presným popisom okamihu odoslania.
- Plávajúce hlásenia sa dajú spravovať podľa závažnosti `info`, `úspech`, `warning` a `critical`, vrátane vlastného času zobrazenia od 2 do 120 sekúnd.

### Opravené
- Alarm zariadenia, ukončenie profilu, varovanie kalibrácie, výsledok kalibrácie a rozdiel WIKA–komora rešpektujú vlastný e-mailový prepínač; vypnutie jedného typu už nevypne ostatné.
- Kalibračná obrazovka si pred odoslaním načíta čerstvo uložené notifikačné nastavenia, takže zmeny v administrácii platia bez reštartu aplikácie.
- Vypnutie e-mailu o rozdiele WIKA–komora už nevypína samotnú bezpečnostnú kontrolu a lokálne upozornenie.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.186.

## [1.76.185] – 2026-09-06

### Opravené
- POL-EKO LabDesk parser prijíma aktuálny formát `TEMPERATURE_MAIN` ako priamu číselnú hodnotu a už neoznačuje platnú odpoveď sušiarne ako stratu spojenia.
- Zachovaná zostala kompatibilita so starším vnoreným formátom `valueProbe`/`value` aj textovou číselnou hodnotou; neplatné a nekonečné hodnoty sa naďalej odmietnu.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.185.

## [1.76.184] – 2026-09-06

### Opravené
- Karta rýchleho ovládania má responzívne dvojriadkové rozloženie teplotnej poistky; minimálna a maximálna hodnota, jednotky aj informácia o profile sa už neorezávajú.
- Časovač manuálneho ovládania používa kompaktnejší popis a širšie číselné pole, aby boli celé hodnoty aj stav viditeľné na úzkej karte zariadenia.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.184.

## [1.76.183] – 2026-09-06

### Opravené
- Diagnostický App log pri príchode nových záznamov už neposúva riadky, ktoré operátor práve číta po odscrollovaní od začiatku zoznamu.
- Ručný výber a pozícia v logu zostávajú nezávislé od automaticky vloženého prvého riadku; na začiatku zoznamu sa nové udalosti naďalej zobrazujú okamžite.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.183.

## [1.76.182] – 2026-09-06

### Opravené
- Strata stability počas finálneho merania už nespotrebuje jediný spoločný limit: prvý reset s rozpracovanými vzorkami povolí jeden nový kompletný pokus.
- Čas nového pokusu sa vypočíta z reálne nameranej rýchlosti vzoriek a zahŕňa celé stabilizačné aj finálne okno s rezervou; ďalšie restarty limit nepredlžujú donekonečna.
- Diagnostika a živý prehľad uvádzajú presný počet zahodených finálnych vzoriek aj prekročené kritérium (range, StdDev alebo drift).

### Zmenené
- Záložná verzia bola zvýšená na 1.76.182.

## [1.76.181] – 2026-09-06

### Zmenené
- Všetky prechodné upozornenia používajú jednotné plávajúce karty v pravom hornom rohu aplikácie; viac udalostí sa skladá pod seba.
- Karta má farebnú závažnosť, odpočet s časovým pruhom, tlačidlo kopírovania a krížik na okamžité zatvorenie.
- Zelená označuje úspech, modrá informáciu, oranžová upozornenie a červená kritickú chybu.
- Systémové Windows bubliny a duplicitný textový alarm v hornej lište detailu zariadenia boli odstránené; stav alarmu zariadenia zostáva viditeľný na jeho karte.
- Záložná verzia bola zvýšená na 1.76.181.

## [1.76.180] – 2026-09-06

### Pridané
- Dokončovací e-mail FBG kalibrácie obsahuje prehľadnú tabuľku SN, kanála, peaku, typu kalibrácie, λTref, citlivosti, koeficientov, maximálnej chyby, R² a výsledku PASS/FAIL.
- E-mail prikladá `calibration-coefficients.xlsx` a `calibration-coefficients.csv` samostatne popri súhrnnom CSV a kompletnom ZIP archíve behu.

### Zachované
- Excel a CSV koeficientov zostávajú uložené aj v lokálnom priečinku kalibračného behu.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.180.

## [1.76.179] – 2026-09-06

### Opravené
- Obnovenie kalibrácie už nevyvolá falošné hlásenie o zmene zapojenia, keď PeakLogger v pomocnom zozname peakov dočasne vynechá sériové číslo interrogátora.
- Automatická obnova tabuľky sa spustí až po dvoch zhodných kontrolách skutočne zmenenej topológie; zmena kanála alebo peaku sa naďalej rozpozná.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.179.

## [1.76.178] – 2026-09-06

### Pridané
- Administrácia obsahuje spoločné predvolené nastavenia stability FBG kalibrácie vrátane tolerancie a driftu komory, časov, počtov vzoriek a limitov vlnovej dĺžky.
- Predvoľby sa ukladajú do samostatného súboru `fbg-calibration-defaults.json` a automaticky sa skopírujú do každého nového kalibračného zapojenia.

### Zachované
- Existujúce uložené zapojenia a rozbehnuté kalibrácie si ponechávajú svoje vlastné nastavenia a zmena administrátorskej predvoľby ich spätne neprepíše.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.178.

## [1.76.177] – 2026-09-06

### Pridané
- Každý FBG peak má samostatne vypočítané všetky tri podporované teplotné kalibrácie: `FBGS · s1/s2`, `2nd · ABC` a `3rd · ABCD`.
- História, CSV aj Excel zobrazujú typ kalibrácie a samostatné stĺpce s1, s2, A, B, C a D.
- Existujúce exporty z predchádzajúceho formátu sa pri načítaní dokončeného behu automaticky prepočítajú a nahradia úplným formátom.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.177.

## [1.76.176] – 2026-09-06

### Pridané
- Dokončená FBG teplotná kalibrácia vypočíta pre každý peak λTref, citlivosť, koeficienty A–D, maximálnu chybu, toleranciu, R² a výsledok PASS/FAIL podľa algoritmu aplikácie Auto_calibrator_Pali.
- Pre rozsah do 100 °C sa používa kvadratická a nad 100 °C kubická regresia; referenčná teplota je štandardne 22,5 °C.
- Koeficienty sa zobrazujú pri vybranom behu v histórii a ukladajú do `calibration-coefficients.csv`, `calibration-coefficients.xlsx` aj `summary.json`.
- História automaticky dopočíta a vytvorí exporty aj pre existujúce dokončené behy, ktoré majú aspoň tri rôzne referenčné teploty.
- Pribudol samostatný export koeficientov vybraného behu do CSV.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.176.

## [1.76.175] – 2026-09-06

### Opravené
- Pri zamknutom zariadení už nemožno meniť minimálny ani maximálny limit teplotnej poistky.
- Tlačidlo `Upraviť predvoľby` rešpektuje zámok zariadenia a otvorený editor sa pri zamknutí automaticky zatvorí.
- Záložná verzia bola zvýšená na 1.76.175.

## [1.76.174] – 2026-09-06

### Opravené
- Informácia o stave `Remote Control` na karte SIKA má správne vnútorné okraje a pri užšej karte sa zalomí bez orezania.
- Akcia na zapnutie vzdialeného ovládania používa kompaktné tlačidlo, takže text stavu má dostatok priestoru.
- Záložná verzia bola zvýšená na 1.76.174.

## [1.76.173] – 2026-09-06

### Opravené
- Odhad konca FBG kalibrácie zahŕňa aj staršie neustálené pláta, ktoré aplikácia odložila a po prejdení ostatných bodov sa k nim ešte vráti.
- Zostávajúci čas riadených nábehov sa počíta podľa skutočného poradia nedokončených a odložených bodov.
- Záložná verzia bola zvýšená na 1.76.173.

## [1.76.172] – 2026-09-06

### Opravené
- Dlhší odznak typu zariadenia, napríklad `Sušiareň`, sa pri nedostatku miesta presunie celý na ďalší riadok a zachová si správny vnútorný okraj.
- Záložná verzia bola zvýšená na 1.76.172.

## [1.76.171] – 2026-09-06

### Opravené
- Odznaky stavu na karte komory sú kompaktnejšie, takže označenie `T+RH` zostáva celé a nie je orezané ani počas FBG kalibrácie.
- Záložná verzia bola zvýšená na 1.76.171.

## [1.76.170] – 2026-09-06

### Pridané
- Manuálne rýchle ovládanie môže bežať bez časového limitu alebo s nastaviteľným časovačom v minútach.
- Po uplynutí časovača aplikácia bezpečne vypne výkon komory, zobrazí výsledok operátorovi a udalosť zaznamená.
- Počas časovaného manuálneho behu sa priamo na karte zariadenia zobrazuje zostávajúci čas.
- Voľba časovača a jeho trvanie sa ukladajú samostatne pre každú komoru.
- Časová os zariadení zobrazuje časovaný manuálny beh s konkrétnym plánovaným koncom; nekonečný beh zostáva označený symbolom ∞.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.170.

## [1.76.169] – 2026-09-06

### Zmenené
- Teplotná poistka v rýchlom ovládaní je menšia a umiestnená v samostatnom kompaktnom rámčeku.
- Popis automatických limitov profilu zostal dostupný priamo v riadku aj v nápovede bez zmeny bezpečnostnej logiky.
- Záložná verzia bola zvýšená na 1.76.169.

## [1.76.168] – 2026-09-06

### Pridané
- SIKA TP+ podporuje odomknutie vzdialeného ovládania PIN-om, voliteľný gradient a automatické vyhľadanie HTTP/AJAX API na portoch 80, 8080, 8081, 8082 a 8085.
- Hlavná karta pravidelne zobrazuje stav regulátora, aktuálny gradient, zostávajúci čas a interné teplotné limity; diagnostika navyše načíta chyby zariadenia.
- Diagnostika umožňuje načítať interné úlohy, shells, testovacie objekty a bežiacu úlohu a spustiť úlohu z JSON podľa rozhrania zariadenia.
- Pribudla servisná sériová komunikácia SIKA 2400 8N1 s CRLF pre identifikáciu, čítanie teploty a setpointu a zápis setpointu.

### Opravené
- SIKA diagnostické a logovacie príkazy teraz správne používajú vnútorného klienta aj za nadradenou teplotnou poistkou.
- STOP sa po odoslaní overí spätným čítaním regulátora a pri nezastavení vyhlási zásah operátora.
- Remote PIN sa v diagnostike maskuje a neukladá sa do konfiguračného súboru.
- Záložná verzia bola zvýšená na 1.76.168.

## [1.76.167] – 2026-09-06

### Opravené
- Číselné hodnoty na karte `Nastavenia stability` sa už vertikálne neorezávajú na pomlčky a zostávajú čitateľné aj počas zamknutého bežiaceho alebo obnoveného behu.
- Vnútorné odsadenie vstupov bolo prispôsobené spoločnej šablóne WPF TextBox bez zmeny uložených kalibračných nastavení.
- Záložná verzia bola zvýšená na 1.76.167.

## [1.76.166] – 2026-09-06

### Opravené
- Všetky textové zobrazenia nameranej teploty, setpointu a vlhkosti na hlavnej, detailnej aj profesionálnej karte zariadenia používajú explicitne jednosmerné WPF väzby.
- Chyba pri pokuse zapisovať do vlastnosti `MeasuredTemperature` iba na čítanie je odstránená aj z metrík a výpočtu odchýlky, nielen z grafických náhľadov zariadení.
- Záložná verzia bola zvýšená na 1.76.166.

## [1.76.165] – 2026-09-06

### Opravené
- Grafické náhľady zariadení POL-EKO a SIKA teraz čítajú nameranú teplotu výslovne iba jednosmerne.
- Odstránená bola neočakávaná chyba WPF, ktorá sa pokúšala zapisovať do vlastnosti `MeasuredTemperature` určenej iba na čítanie.
- Záložná verzia bola zvýšená na 1.76.165.

## [1.76.164] – 2026-09-06

### Pridané
- POL-EKO SLN 115 teraz komunikuje rovnakou TCP/IP cestou ako LabDesk: Eneter duplex, AES heslo zariadenia, port 56506 a JSON RPC.
- Čítanie aktuálneho stavu cez `GET_STATUS`, spustenie cieľovej teploty cez programový segment a `LAUNCH_BY_ID` a bezpečné zastavenie cez `STOP`.
- Protokolové testy overujú názvy JSON polí, prihlásenie, škálovanie teploty ×10 a bodkované polia rampy.

### Zmenené
- Existujúca konfigurácia POL-EKO na starom porte 502 sa pri prvom štarte automaticky presunie na 56506 a sušiareň sa zobrazí na hlavnom paneli.
- Bridge už POL-EKO nevynecháva a diagnostika zobrazuje odpovede LabDesk `GET_STATUS` a `GET_CONFIG` namiesto registračného skenu.
- Záložná verzia bola zvýšená na 1.76.164.

## [1.76.163] – 2026-09-06

### Pridané
- Pribudla trvalo aktívna teplotná poistka nadradená manuálnemu ovládaniu, testovacím profilom aj FBG kalibrácii.
- Pri prekročení nastaveného minima alebo maxima poistka odošle fyzický STOP komore, zablokuje ďalšie setpointy, ukončí riadenie a zobrazí výrazný alarm s výsledkom vypnutia.
- Výber profilu automaticky nastaví bezpečnostné limity podľa najnižšej a najvyššej teploty profilu s rezervou 5 °C; operátor ich môže upraviť ručne.
- Aktuálny rozsah poistky je viditeľný na klasickej aj profesionálnej karte zariadenia a v detaile bezpečnosti.
- Limity poistky sa ukladajú samostatne pre každú komoru a zdieľajú sa aj so samostatným FBG kalibračným oknom.

### Zmenené
- Záložná verzia bola zvýšená na 1.76.163.

## [1.76.162] – 2026-09-06

### Opravené
- Časová os zariadení rozpozná, že komoru vlastní aktívna FBG kalibrácia, a už ju nesprávne nezobrazuje ako manuálny beh.
- Pruh FBG kalibrácie používa rovnaký odhad začiatku a konca ako kalibračný dashboard vrátane zostávajúcich plat, riadených nábehov a záverečného temperovania pri 25 °C.
- Ak kalibrácia ešte nemá spoľahlivý odhad konca, časová os zobrazí `FBG · ETA neurčitá` s otáznikom a vysvetlením namiesto nepravdivého nekonečného manuálneho behu.
- Legenda časovej osi rozlišuje profil, FBG kalibráciu a manuálny beh vlastnými farbami.
- Záložná verzia bola zvýšená na 1.76.162.

## [1.76.161] – 2026-09-06

### Opravené
- Jednotky `°C` a `%` sú na kartách komôr súčasťou rovnakého textu ako nameraná hodnota, takže majú rovnakú veľkosť, farbu a zarovnanie.
- Hodnota a jednotka sa pri nedostatku miesta zmenšujú spoločne; `°C` sa už pri dlhšej teplote neodreže ani nestratí.
- Rovnaké zobrazenie sa používa na hlavnom dashboarde, profesionálnej karte aj v detaile komory.
- Záložná verzia bola zvýšená na 1.76.161.

## [1.76.160] – 2026-09-06

### Opravené
- Kompaktný štítok komory s podporou teploty a vlhkosti používa kratší zápis `T+RH`, menšie vnútorné odsadenie a mierne menšie písmo, takže sa zmestí celý do jedného riadka aj počas FBG kalibrácie.
- Úplný význam štítku zostáva dostupný v nápovede po prejdení myšou.
- Záložná verzia bola zvýšená na 1.76.160.

## [1.76.159] – 2026-09-06

### Pridané
- Po dokončení všetkých vybraných kalibračných bodov sa komora vždy vráti na 25 °C a výrobky sa temperujú najmenej jednu súvislú hodinu v pásme 25 ± 0,5 °C.
- Záverečné temperovanie je v pláne, pracovnom postupe, odhade času a histórii behu zobrazené ako samostatný krok bez merania WIKA a FBG a bez vytvorenia kalibračného bodu.
- Pri opustení povoleného teplotného pásma sa súvislý čas temperovania začne počítať odznova; rozpracovaný krok sa ukladá do checkpointu a po obnovení sa bezpečne zopakuje.

### Opravené
- Zrušenie plata v kalibračnom pláne okamžite aktualizuje počet bodov, poradie, progress aj odhadovaný čas; odškrtnuté plato sa nekalibruje ani neuloží medzi výsledky.
- Záložná verzia bola zvýšená na 1.76.159.

## [1.76.158] – 2026-09-06

### Opravené
- Pri prechode na nové kalibračné plato sa živé grafy komory, referencie WIKA, stabilného času a FBG peakov okamžite vyčistia a ich časová os začne znovu od nuly.
- Reset sa vykoná aj pri zmene cieľovej teploty počas prechodového stavu, aj keby ešte runner dočasne hlásil rovnaký index plata.
- Historické a auditné dáta predchádzajúceho plata zostávajú zachované v súboroch behu a nových reportoch.
- Záložná verzia bola zvýšená na 1.76.158.

## [1.76.157] – 2026-09-06

### Pridané
- Po dokončení každého kalibračného plata sa pri výsledku PASS aj FAIL automaticky vytvorí prehľadný Excel s výsledkami, limitmi a auditnými dátami.
- Ku každému bodu sa ukladajú samostatné PNG grafy stabilizácie FBG peakov, finálnych vzoriek vlnovej dĺžky a stabilnej referenčnej teploty WIKA.
- Excel obsahuje vložené grafy, prehľad výsledkov jednotlivých peakov, dostupné finálne vzorky a teplotné dáta; neúspešné výsledky zachovávajú dôvod zlyhania.
- Záverečný ZIP archív odosielaný e-mailom zahŕňa aj podpriečinky s novými reportmi a obrázkami.
- Záložná verzia bola zvýšená na 1.76.157.

## [1.76.156] – 2026-09-06

### Opravené
- Grafy FBG peakov majú pre os vlnovej dĺžky v nanometroch viac priestoru, takže presné hodnoty aj jednotka `nm` zostávajú v jednom riadku.
- Zalamovanie textu popisov osí je explicitne vypnuté; teplotné a ostatné grafy si zachovávajú pôvodnú kompaktnú šírku osi.
- Záložná verzia bola zvýšená na 1.76.156.

## [1.76.155] – 2026-09-06

### Zmenené
- Kompaktný graf WIKA sa pri začiatku narastania stabilného času automaticky nastaví na začiatok aktuálneho stabilného okna, takže predchádzajúci teplotný nábeh už nestláča mierku grafu.
- Krivka stabilného času po resete začína od nuly a ďalej plynulo zobrazuje aktuálne zbierané skóre; úplné namerané dáta zostávajú zachované pre históriu a audit.
- Záložná verzia bola zvýšená na 1.76.155.

## [1.76.154] – 2026-09-06

### Pridané
- Vedľa stavu „Posledná vzorka WIKA“ pribudla klikateľná nápoveda, ktorá vysvetľuje pôvod hodnoty, jej pravidelné obnovovanie a použitie pri výpočte odchýlky, driftu a stabilného času.
- Nápoveda upozorňuje, že jedna vzorka sama nepotvrdzuje stabilitu a že bez novej platnej vzorky sa FBG stabilizácia nespustí.
- Záložná verzia bola zvýšená na 1.76.154.

## [1.76.153] – 2026-09-06

### Pridané
- Karta WIKA počas čakania zobrazuje aktuálny celkový limit ustálenia pre dané plato, uplynutý čas a zostávajúci čas.
- Samostatný rozpis ukazuje základný limit, použité automatické predĺženie z povoleného maxima a ručne pridaný čas; údaje sa priebežne aktualizujú.
- Záložná verzia bola zvýšená na 1.76.153.

## [1.76.152] – 2026-09-05

### Opravené
- Formulár „Nastavenia stability“ po obnovení kalibrácie znovu načíta aktuálne hodnoty zo setupu alebo checkpointu pri vytvorení aj pri návrate na túto kartu.
- Uložené limity kalibrácie sa už v zamknutom formulári nezobrazia ako prázdne pomlčky; rozhodovacia logika bežiacej kalibrácie zostáva nezmenená.
- Záložná verzia bola zvýšená na 1.76.152.

## [1.76.151] – 2026-09-05

### Pridané
- Živá karta FBG kalibrácie na hlavnom prehľade zobrazuje odhadovaný čas ukončenia aj zostávajúci čas podľa existujúceho dynamického výpočtu kalibrácie.
- Ak pre aktuálny priebeh nie je možné vytvoriť spoľahlivý odhad, karta namiesto zavádzajúceho presného času zobrazí neurčitý odhad; vysvetlenie výpočtu je dostupné v nápovede.
- Záložná verzia bola zvýšená na 1.76.151.

## [1.76.150] – 2026-09-05

### Optimalizované
- Priebežný diagnostický stav kalibrácie sa namiesto každého živého obnovenia zapisuje najviac raz za 30 sekúnd; zmeny fázy, stavu, teplotnej brány alebo počtu stabilných peakov sa naďalej zapíšu okamžite.
- Chyby, varovania, zásahy operátora a začiatok či koniec behu zostávajú zaznamenané bez obmedzenia, pričom dlhé kalibrácie už nevytvárajú desiatky megabajtov opakovaných riadkov.
- Záložná verzia bola zvýšená na 1.76.150.

## [1.76.149] – 2026-09-05

### Opravené
- Hodnota teploty komory sa v kompaktnej dlaždici automaticky zmenší spolu s jednotkou, takže sa celé číslo zmestí do rámčeka aj pri zobrazení vlhkosti a referencie.
- Záložná verzia bola zvýšená na 1.76.149.

## [1.76.148] – 2026-09-05

### Zmenené
- Záložka „Zapojenie“ bola na žiadosť operátora vrátená na pôvodné rozloženie s jednou širokou tabuľkou a všetkými údajmi priamo v stĺpcoch.
- Odstránený bol pravý panel „Detail senzora“, súhrnné karty a vyhľadávacie pole z redizajnu; obnova kalibrácie a ostatné novšie opravy zostali zachované.
- Záložná verzia bola zvýšená na 1.76.148.
## [1.76.147] – 2026-09-05

### Pridané
- V histórii kalibrácií pribudlo tlačidlo „Pokračovať od vybraného behu“, ktoré z dokončených plat prerušeného alebo omylom uzavretého behu znovu vytvorí checkpoint.
- Obnovenie zachová hotové plata, pôvodné nastavenia a zapojenie; rozpracované plato sa po spustení stabilizuje a zmeria nanovo.

### Opravené
- V detaile senzora sú všetky informačné väzby na vlastnosti iba na čítanie explicitne jednosmerné, takže otvorenie ani prepínanie obrazovky už nevyvolá chybu `TwoWay binding`.
- Druhé deštruktívne tlačidlo po zastavení sa teraz volá „Zrušiť pokračovanie“ a potvrdenie výslovne upozorňuje, že odstráni checkpoint.
- Záložná verzia bola zvýšená na 1.76.147.

## [1.76.146] – 2026-09-05

### Opravené
- Kalibračné okno sa po redizajne obrazovky „Zapojenie“ opäť otvorí bez chyby WPF väzby.
- Informatívne polia „Sylex SN“ a „Názov snímača“ v detaile senzora sú explicitne jednosmerné a aplikácia sa ich nepokúša zapisovať späť do zdrojového objektu.
- Záložná verzia bola zvýšená na 1.76.146.

## [1.76.145] – 2026-09-05

### Zmenené
- Obrazovka „Zapojenie“ dostala moderný master-detail dizajn: kompaktná tabuľka najdôležitejších údajov vľavo a detail vybraného senzora vpravo.
- Horný súhrn zobrazuje počet vybraných peakov, priradených SN, chýb a aktuálny stav PeakLoggera; pribudlo rýchle vyhľadávanie a samostatná kontrola SN.
- Zriedkavejšie výrobné údaje, CHAIN SN a poznámky sa presunuli do detailu bez odstránenia dát; existujúce automatické ukladanie, API dopĺňanie a validácia zostali zachované.
- Tabuľka používa virtualizáciu, čitateľné pevné stĺpce, jasný vybraný riadok a naďalej chráni rozpracovanú editáciu pred obnovou dát na pozadí.
- Záložná verzia bola zvýšená na 1.76.145.

## [1.76.144] – 2026-09-05

### Zmenené
- Dva kompaktné grafy v karte WIKA referencie boli spojené do jedného orámovaného grafu so spoločnou časovou osou.
- Ľavá os spoločného grafu zobrazuje teplotu WIKA v °C a pravá os stabilný čas v sekundách vrátane cieľovej hranice.
- Graf komory aj spoločný graf WIKA majú zreteľný, ostrý rámček bez rozmazaného efektu; karta pritom zaberá menej výšky.
- Záložná verzia bola zvýšená na 1.76.144.

## [1.76.143] – 2026-09-05

### Pridané
- Do karty WIKA referencie pribudlo tlačidlo „+30 min na ustálenie“, dostupné iba počas aktívneho čakania na stabilitu teploty.
- Každé stlačenie predĺži limit aktuálneho plata o ďalších 30 minút bez vynulovania stabilného skóre alebo doterajších dát a zapíše zásah operátora do udalostí kalibrácie.
- Ručné predĺženie sa zobrazuje v aktuálnych podmienkach oddelene od automatického predĺženia; tlačidlo možno vedome použiť opakovane.
- Záložná verzia bola zvýšená na 1.76.143.

## [1.76.142] – 2026-09-05

### Opravené
- Po chybe alebo zastavení na zásah operátora zostáva dostupné tlačidlo „Ukončiť a uložiť“; operátor môže beh definitívne uzavrieť aj vtedy, keď už runner nebeží.
- Definitívne ukončenie zachová dokončené plata a všetky dovtedy namerané súbory v histórii, označí beh ako zastavený a až po potvrdení odstráni checkpoint na pokračovanie.
- Bežné „Stop a uložiť“ počas aktívneho behu pri prepise checkpointu zachová aj zoznam odložených plat, aby sa po aktualizácii nestratilo poradie návratov.
- Text tlačidla a nápoveda rozlišujú aktívne zastavenie od definitívneho uzavretia už zastaveného behu.
- Záložná verzia bola zvýšená na 1.76.142.

## [1.76.141] – 2026-09-05

### Zmenené
- Plato, ktoré sa ani po základnom limite a maximálnom hodinovom predĺžení neustáli, sa pri prvom neúspechu odloží namiesto zastavenia celej kalibrácie.
- Kalibrácia pokračuje ďalšími dostupnými platami a po ich prejdení sa k odloženému platu automaticky raz vráti; nekonečné opakovanie nie je povolené.
- Zoznam odložených plat sa ukladá do checkpointu, takže poradie návratov zostane zachované aj po obnove aplikácie alebo výpadku.
- Až neúspešný opakovaný pokus zastaví automatický postup a odošle e-mail s výzvou na zásah operátora; odloženie samotné zobrazí informatívne upozornenie bez e-mailu.
- Záložná verzia bola zvýšená na 1.76.141.

## [1.76.140] – 2026-09-05

### Pridané
- Pri odchýlke WIKA je priamo v karte referencie dostupná voliteľná bezpečne obmedzená korekcia setpointu komory, ktorá zasiahne iba mimo povoleného rozsahu a pomôže fyzickú referenciu dorovnať späť.
- Ovládanie zobrazuje limity korekcie a počas prebiehajúcej kalibrácie je uzamknuté; komora naďalej používa vlastný interný regulátor.
- Nápoveda odchýlky vysvetľuje krok 0,30 °C za 10 sekúnd, maximálnu celkovú korekciu ±3,0 °C a predvolene vypnutý bezpečný režim.
- Záložná verzia bola zvýšená na 1.76.140.

## [1.76.139] – 2026-09-05

### Zmenené
- Čakanie na stabilitu WIKA sa po základnom limite automaticky predlžuje po 15 minútach, najviac spolu o jednu hodinu nad základný timeout; predĺženie sa nikdy neopakuje nad tento strop.
- Každé automatické predĺženie sa zaznamená a zobrazí operátorovi vrátane využitého a zostávajúceho rozpočtu predĺženia.
- Po vyčerpaní maximálneho času sa automatický postup bezpečne zastaví, bod sa neprijme a upozornenie obsahuje stabilné skóre aj konkrétne ďalšie kroky.
- Nutný zásah operátora vyvolá výrazné upozornenie a e-mail s predmetom „ZÁSAH OPERÁTORA“; pri vypnutom alebo chybnom e-maile aplikácia zreteľne oznámi, že správa nebola doručená.
- Záložná verzia bola zvýšená na 1.76.139.

## [1.76.138] – 2026-09-05

### Pridané
- Karta WIKA referencie obsahuje kompaktný graf vývoja stabilného skóre v sekundách vrátane čiarkovanej hranice požadovaného času.
- Graf zobrazuje aj pokles skóre po neúspešnom bloku, používa existujúce kalibračné snapshoty bez ďalšieho čítania hardvéru a vykresľuje najviac 240 reprezentatívnych bodov.
- Záložná verzia bola zvýšená na 1.76.138.

## [1.76.137] – 2026-09-05

### Zmenené
- Nápoveda stabilného času WIKA vysvetľuje, že aplikácia priebežne čaká na nové bloky, až kým odchýlka aj drift súčasne nevyhovujú počas celého požadovaného skóre.
- Nápoveda uvádza, že po vypršaní timeoutu sa automatický postup zastaví, vyžiada zásah operátora a nevyhovujúci kalibračný bod sa automaticky neprijme.
- Záložná verzia bola zvýšená na 1.76.137.

## [1.76.136] – 2026-09-05

### Zmenené
- Nápoveda stabilného času WIKA teraz priamo vysvetľuje význam stability a uvádza, že odchýlka od cieľa aj drift musia byť splnené súčasne.
- Nápoveda zrozumiteľne opisuje pripočítanie úspešného bloku, penalizáciu neúspešného bloku a správanie po vypršaní časového limitu.
- Záložná verzia bola zvýšená na 1.76.136.

## [1.76.135] – 2026-09-05

### Pridané
- Karta komory v live prehľade obsahuje malý graf internej teploty aktuálneho kalibračného plata.
- Karta WIKA referencie obsahuje samostatný kompaktný graf referenčnej teploty aktuálneho plata.
- Mini grafy používajú už prijaté dáta bez ďalšieho hardvérového čítania a skrývajú veľké ovládanie zoomu, aby nezvyšovali vizuálnu ani výkonnostnú záťaž.
- Záložná verzia bola zvýšená na 1.76.135.

## [1.76.134] – 2026-09-05

### Zmenené
- Horný live prehľad FBG kalibrácie bol prepracovaný na kompaktný trojstĺpcový panel s profilom, aktuálnym platom, stavom, cieľom, ETA a spoločným progressom bez veľkých prázdnych plôch.
- Sekcia aktuálneho diania má nižšie karty dokončeného, aktívneho a nasledujúceho kroku; podmienky, časy, aktívny peak a upozornenia zostali zachované v hustejšom rozložení.
- Dlhšie doplnkové texty majú plné znenie dostupné v nápovede a detailné pravidlá zostávajú rozbaľovacie.
- Záložná verzia bola zvýšená na 1.76.134.

## [1.76.133] – 2026-09-05

### Zmenené
- Nápoveda workflow a kritérií WIKA sa zobrazí až po kliknutí na otáznik; samotný prechod myšou ju už neotvára.
- Otvorená nápoveda sa automaticky zavrie po odchode kurzora z jej plochy a opakovaným kliknutím na rovnaký otáznik ju možno zavrieť okamžite.
- Záložná verzia bola zvýšená na 1.76.133.

## [1.76.132] – 2026-09-05

### Opravené
- Obrazovka nastavení stability po obnovení kalibrácie spoľahlivo zobrazuje uložené hodnoty aj v uzamknutom stave počas behu.
- Checkpoint po novom uchováva presnú kópiu limitov, intervalov a vybraných kalibračných bodov; obnovený beh preto pokračuje s rovnakými rozhodovacími pravidlami ako pred aktualizáciou alebo výpadkom.
- Staršie checkpointy zostávajú kompatibilné a použijú nastavenia uložené pri profile.
- Záložná verzia bola zvýšená na 1.76.132.

## [1.76.131] – 2026-09-05

### Pridané
- Koreňový skript `update.sh` umožňuje z Git Bash bezpečne stiahnuť `origin/main`, zostaviť single-file Windows aplikáciu a automaticky ju spustiť.
- Aktualizácia sa odmietne spustiť, ak aplikácia ešte beží, repozitár nie je na vetve `main` alebo obsahuje neuložené zmeny, aby sa predišlo strate kalibrácie alebo lokálnej práce.
- Parameter `--no-start` pripraví aktualizáciu bez automatického spustenia aplikácie.
- Záložná verzia bola zvýšená na 1.76.131.

## [1.76.130] – 2026-09-05

### Pridané
- Kliknutie na „Stop a uložiť“ počas FBG kalibrácie najprv zobrazí potvrdzovacie okno s cieľovou teplotou a vysvetlením následkov zastavenia.
- Operátor môže zvoliť „Ukončiť a uložiť“ alebo „Pokračovať v kalibrácii“; bez potvrdenia sa kalibrácia ani komora nezastavia.
- Potvrdzovacie okno sa otvorí nad práve aktívnym oknom kalibrácie, aby nezostalo skryté za maximalizovaným pracovným priestorom.
- Záložná verzia bola zvýšená na 1.76.130.

## [1.76.129] – 2026-09-05

### Pridané
- Tlačidlo „Stop a uložiť“ bezpečne zastaví FBG kalibráciu a pred zastavením uloží obnovovací checkpoint vhodný na aktualizáciu aplikácie.
- Po reštarte možno pokračovať aj v kalibrácii, ktorá ešte nemala dokončené prvé plato; dokončené plata sa zachovajú a rozpracované plato sa z bezpečnostných dôvodov znovu stabilizuje a zmeria z čerstvých vzoriek.
- Checkpoint sa uloží aj pri riadnom ukončovaní aplikácie počas aktívnej kalibrácie a diagnostika zaznamená dôvod jeho vytvorenia.
- Záložná verzia bola zvýšená na 1.76.129.

## [1.76.128] – 2026-09-05

### Pridané
- Karta „Komora“ v živom prehľade kalibrácie zobrazuje vedľa aktuálnej teploty aj výrazný cieľ, na ktorý komora smeruje.
- Záložná verzia bola zvýšená na 1.76.128.

## [1.76.127] – 2026-09-05

### Pridané
- Každá karta v sekcii „Stabilita jednotlivých FBG peakov“ zobrazuje samostatne označený progress stability aj zelený progress finálneho merania.
- Pri oboch progress baroch je priamo uvedený aktuálny a požadovaný počet vzoriek daného peaku.
- Záložná verzia bola zvýšená na 1.76.127.

## [1.76.126] – 2026-09-05

### Pridané
- Karta „FBG peaky“ zobrazuje kompaktný zoznam všetkých peakov, stav každého peaku a jeho samostatný priebeh stabilizácie.
- Karta „Meranie vzoriek“ zobrazuje pre každý peak stav merania, počet nazbieraných finálnych vzoriek a vlastný progress bar.
- Zoznamy používajú existujúce živé dáta dashboardu bez ďalšieho zberu alebo výpočtového cyklu a pri väčšom počte peakov sa plynulo posúvajú.
- Záložná verzia bola zvýšená na 1.76.126.

## [1.76.125] – 2026-09-05

### Opravené
- Tooltip na krivke FBG grafu zobrazuje vlnovú dĺžku na šesť desatinných miest v nm, takže sú viditeľné aj pikometrové zmeny, napríklad `1552,464100 nm`.
- Osi grafov jednotlivých FBG peakov zachovávajú najmenej štyri desatinné miesta a rovnaká presnosť sa prenesie aj do zväčšeného grafu.
- Záložná verzia bola zvýšená na 1.76.125.

## [1.76.124] – 2026-09-05

### Pridané
- Živá FBG karta na dashboarde zobrazuje ID aktuálnej kalibrácie priamo pod názvom profilu.
- Tlačidlo „Otvoriť súbory“ otvorí presný priečinok aktuálneho behu so súhrnom, výsledkami, raw samples, wavelength trace a diagnostickým logom.
- Tlačidlo používa kompaktný výrazný hover stav bez rozmazania a záložná verzia bola zvýšená na 1.76.124.

## [1.76.123] – 2026-09-05

### Pridané
- Dokončovací e-mail FBG kalibrácie zobrazuje jednoznačný výsledok PASS, PASS S UPOZORNENIAMI alebo FAIL a podrobnú tabuľku výsledkov každého plata a peaku.
- Tabuľka obsahuje cieľovú a WIKA teplotu, SN, kanál, peak, priemernú vlnovú dĺžku, počet vzoriek, výsledok a prípadný problém.
- K e-mailu sa pripája výsledkový CSV a ZIP so všetkými súbormi kalibračného behu vrátane súhrnu, raw samples, wavelength trace a diagnostického logu.
- E-mail obsahuje lokálnu cestu aj odkaz na priečinok kalibračného behu a záložná verzia bola zvýšená na 1.76.123.

## [1.76.122] – 2026-09-05

### Pridané
- Kritériá WIKA odchýlky, driftu a stabilného času majú vlastné otázniky s výrazným hover stavom.
- Pomocník odchýlky vysvetľuje toleranciu cieľa, pomocník driftu výpočet z blokov piatich vzoriek a pomocník času spôsob pripočítania aj penalizácie stabilného skóre.
- Vysvetlenia používajú aktuálne nastavené limity a timeout, takže zodpovedajú práve spustenej kalibrácii.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.122.

## [1.76.121] – 2026-09-05

### Zmenené
- Workflow karta sa pri prejdení myšou zvýrazní jasnejším modrým rámčekom a kontrastnejším pozadím bez rozmazania alebo tieňa.
- Otáznik v každej workflow karte má samostatný výrazný hover stav a kurzor pomoci, aby bolo zrejmé, že obsahuje vysvetlenie kroku.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.121.

## [1.76.120] – 2026-09-05

### Pridané
- Každá položka v udalostiach kalibrácie zobrazuje plató, na ktorom vznikla, a samostatne zachytenú cieľovú, WIKA aj komorovú teplotu.
- Kontext udalosti sa ukladá v okamihu jej vzniku, takže staršie riadky nemenia hodnoty pri prechode na ďalšie plató.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.120.

## [1.76.119] – 2026-09-05

### Opravené
- Lokálny výstup priečinka `publish` sa už nezobrazuje medzi nezaradenými súbormi repozitára po overení rovnakého publikačného kroku, aký používa GitHub Actions.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.119.

## [1.76.118] – 2026-09-05

### Opravené
- GitHub Actions používa jediný spoločný build namiesto dvoch prekrývajúcich sa workflowov, čím sa odstránia duplicitné e-mailové hlásenia ku každému commitu.
- Novší push automaticky zruší rozpracovanú kontrolu staršieho commitu na rovnakej vetve, takže sa nekopia zastarané buildy.
- Testy slovenských číselných textov už nezávisia od regionálneho nastavenia počítača a prejdú aj na anglickom GitHub runneri.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.118.

## [1.76.117] – 2026-09-05

### Pridané
- Živý prehľad obsahuje samostatnú sekciu grafov finálneho merania pre každý vybraný FBG peak vrátane individuálneho počítadla, progresu a stavu.
- Graf finálneho merania prijíma iba nové výsledkové vzorky po potvrdení stability; stabilizačné dáta sa s nimi nemiešajú.
- Ak peak počas finálneho merania stratí stabilitu, jeho rozpracovaný merací graf sa vyčistí spolu so vzorkami, ktoré runner zahodil.
- Karty meracích grafov používajú rovnaké rýchle hover zvýraznenie rámčeka ako stabilizačné grafy.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.117.

## [1.76.116] – 2026-09-05

### Zmenené
- Karta každého FBG peaku v živom prehľade sa pri prejdení myšou zvýrazní jasnejším modrým rámčekom a jemne odlíšeným pozadím.
- Hover zvýraznenie nepoužíva rozmazanie ani tieň, takže text a graf zostávajú ostré a nevzniká zbytočná záťaž prekresľovania.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.116.

## [1.76.115] – 2026-09-05

### Pridané
- V nastaveniach kalibrácie je nový perzistentný interval odberu FBG vzoriek v rozsahu 1 až 30 sekúnd; predvolená hodnota je 1 sekunda.
- Kalibračný runner používa zvolený interval pri stabilizácii aj finálnom meraní FBG peakov.
- Workflow pomocník zobrazuje nastavený interval, odhad času zberu vzoriek a po spustení aj skutočne pozorovanú dĺžku dátového cyklu.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.115.

## [1.76.114] – 2026-09-05

### Pridané
- Štyri hlavné karty živého FBG prehľadu teraz zobrazujú jednoznačný stav `DONE`, `WAITING`, `RUNNING`, `PENDING`, `MONITORING` alebo `STOPPED` s farebným badge.
- Nad kartami je vysvetlené poradie brán: WIKA referencia, stabilita každého FBG peaku a následné meranie; komora je iba monitorovaná a jednotlivé peaky postupujú paralelne.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.114.

## [1.76.113] – 2026-09-05

### Zmenené
- Pomocník kroku WIKA referencia teraz podrobne vysvetľuje zber stabilného skóre po blokoch piatich vzoriek, kontrolu tolerancie a driftu, pripočítanie času aj penalizáciu neúspešného bloku.
- Viacriadkové vysvetlenia workflow majú čitateľný zalamovaný tooltip s obmedzenou šírkou, takže sa celý rozpis zmestí na obrazovku.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.113.

## [1.76.112] – 2026-09-05

### Opravené
- Karta nasledujúceho kroku už neorezáva dlhý popis stability FBG na jednom riadku; celý text sa zalomí a zostane čitateľný.
- Pre kroky stability a finálneho merania sa samostatne zobrazuje odhad času požadovaného počtu vzoriek aj aktuálne pozorovaná dĺžka jedného dátového cyklu.
- Kým aplikácia nemá reálnu kadenciu PeakLoggera, karta namiesto zavádzajúceho času jasne oznámi, že čaká na zmeranie dátového cyklu.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.112.

## [1.76.111] – 2026-09-05

### Opravené
- Nadpis grafu USB referenčnej teploty a údaj o COM porte/kanáli sa už neprekrývajú ani nezlievajú do jedného textu.
- Port a kanál sú oddelené v kontrastnom badge, zatiaľ čo nadpis má vlastný pružný priestor a pri užšom okne sa bezpečne skráti.
- Odstránený bol krehký runtime zásah do hlavičky; rozloženie je teraz jednoznačne definované priamo v XAML bez dodatočnej práce pri načítaní okna.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.111.

## [1.76.110] – 2026-09-05

### Pridané
- Projektový skill pre rýchlosť a prevádzkovú spoľahlivosť WPF aplikácie: nezablokovaný UI thread, merateľné optimalizácie, riadené prekresľovanie, časové limity, bezpečný reconnect a jednoznačné vlastníctvo zdrojov.
- Kontrolné zoznamy pre obnovu po výpadku, atómové checkpointy, idempotentný restore/dispose, súbehy, chybové stavy a regresné testovanie systémových zlyhaní.

### Zmenené
- Skills pre UX/UI, grafy, nastavenia a MVVM navigáciu teraz povinne smerujú na pravidlá výkonu a odolnosti pri živých dátach, I/O, obnovovaní a práci na pozadí.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.110.

## [1.76.109] – 2026-09-05

### Pridané
- Projektové skills pre vlastné WPF grafy, perzistenciu nastavení a MVVM navigáciu s presnými pravidlami podľa aktuálnej architektúry aplikácie.
- Referenčné inventáre kontraktov grafov, gest, JSON úložísk, migrácií, navigačných trás a hraníc viewmodelov.

### Zmenené
- Skill pre WPF UX/UI je užší, používa progresívne načítavané referencie a rešpektuje poradie tém, centrálny systém notifikácií aj laboratórnu ergonómiu.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.109.

## [1.76.108] – 2026-09-05

### Zmenené
- WIKA referenčné teplotné grafy zobrazujú na osi Y minimálne tri desatinné miesta, napríklad `60,001 °C`.
- Hodnota v informačnej bubline pod kurzorom rešpektuje rovnakú presnosť ako os grafu, takže tisíciny zostávajú viditeľné aj pri odčítaní konkrétnej vzorky.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.108.

## [1.76.107] – 2026-09-05

### Pridané
- Karty jednotlivých FBG grafov majú jemný plynulý hover: rámček sa zvýrazní farbou príslušného peaku a pozadie sa mierne zosvetlí.
- Hover nemení rozmery karty ani polohu grafu a nepoužíva rozmazanie, takže ovládacie prvky a krivka zostávajú ostré a stabilné.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.107.

## [1.76.106] – 2026-09-05

### Zmenené
- Každý FBG live graf je vizuálne oddelený vlastnou modernou kartou s nadpisom, kontrastným okrajom, zaoblením a konzistentným vnútorným odsadením.

### Opravené
- Live prekresľovanie dát už počas ťahania ľavým tlačidlom nezmaže obdĺžnikový výber zoomu; graf zmrazí aktuálny vizuál počas výberu a najnovšie dáta vykreslí po uvoľnení tlačidla.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.106.

## [1.76.105] – 2026-09-05

### Opravené
- Prvý plynulý setpoint po obnove kalibrácie vždy vychádza z čerstvo odmeranej teploty komory, nie z cieľa posledného dokončeného plata uloženého v checkpointe.
- Ak je komora pri obnove už na cieli ďalšieho plata, aplikácia cieľ iba potvrdí a nevyvolá zbytočné ochladenie ani opätovný nábeh.
- Doplnený regresný test scenára, v ktorom checkpoint končí na 30 °C, komora je fyzicky na 40 °C a obnovené plato má cieľ 40 °C.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.105.

## [1.76.104] – 2026-09-05

### Opravené
- Roadmapa obnoveného kalibračného behu načíta dokončené plata z checkpointu a už ich nesprávne nezobrazuje ako `PENDING`.
- Každé obnovené plato zobrazuje stav `DONE`, skutočný lokálny dátum a čas dokončenia a reálne trvanie bodu; body s neúspešným peakom sú označené upozornením.
- Obnovené dokončenia sa doplnia aj do udalostí kalibrácie, takže operátor vidí auditnú časovú stopu pred pokračovaním od ďalšieho plata.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.104.

## [1.76.103] – 2026-09-05

### Opravené
- Obnova prerušeného behu premietne zapojenie a SN z checkpointu aj do peakov, ktoré PeakLogger vytvoril skôr než sa checkpoint načítal.
- Autosave už nemôže platné checkpointové zapojenie prepísať prázdnymi live riadkami; ochrana funguje nezávisle od poradia načítania profilu, zariadení a dát.
- Tlačidlo `Pokračovať od plata č. N` sa po obnove povolí aj pri tomto súbehu inicializácie bez potreby ručného opätovného zadania ôsmich SN.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.103.

## [1.76.102] – 2026-09-05

### Opravené
- Ak bol setup po prerušení behu prepísaný prázdnymi live riadkami, obnova kalibrácie automaticky načíta vybrané peaky, FBG SN, kanálové SN, CHAIN a metadata z autoritatívneho checkpointu.
- Opravené mapovania sa ihneď znovu uložia k profilu a komore, takže tlačidlo `Pokračovať od plata č. N` sa po pripojení rovnakého PeakLoggera povolí.
- Existujúce platné operátorské zapojenie má prednosť a checkpoint ho nikdy automaticky neprepíše.
- Doplnené regresné testy obnovy SN a ochrany novšieho operátorského zapojenia.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.102.

## [1.76.101] – 2026-09-05

### Opravené
- Progress bary stability WIKA a jednotlivých FBG peakov používajú explicitný jednosmerný binding a už sa nepokúšajú zapisovať do read-only vlastností dashboardu.
- Otvorenie obnoveného kalibračného dashboardu už nevyvolá modálnu chybu `TwoWay or OneWayToSource binding cannot work on the read-only property`.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.101.

## [1.76.100] – 2026-09-05

### Pridané
- FBG kalibrácia po reštarte rozpozná checkpoint vybraného profilu a komory a namiesto nového behu ponúkne tlačidlo `Pokračovať od plata č. N`.
- Obnovený beh zachová pôvodné ID, dokončené plata, raw samples, wavelength trace a diagnostický log; nové dáta sa pripájajú bez prepisovania pôvodných súborov.

### Opravené
- Pri obnovení sa už dokončené plata preskočia a prvé rozpracované plato sa z bezpečnostných dôvodov stabilizuje a zmeria celé nanovo.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.100.

## [1.76.99] – 2026-09-05

### Opravené
- Počas aktívnej kalibrácie zostáva operátorom schválený zoznam FBG peakov nemenný; live dáta aktualizujú iba existujúce riadky a nemôžu rozbiť WPF tabuľku zmenou jej zdroja počas vykresľovania.
- Live grafy zosúlaďujú svoje väzby na peaky až po dokončení spracovania zmeny kolekcie, nie priamo vo WPF `CollectionChanged` udalosti.
- Známa prechodná chyba konzistencie `ItemsControl` už nevytvára sériu vnorených modálnych okien; ostatné neočakávané chyby majú zároveň ochranu proti duplicitnému dialógu.
- Doplnené regresné testy uzamknutia topológie peakov počas aktívneho behu.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.99.

## [1.76.98] – 2026-09-05

### Opravené
- Live FBG grafy už po dosiahnutí 3000 bodov nezahadzujú začiatok behu, zatiaľ čo os X naďalej ukazuje čas od spustenia.
- Dlhé záznamy sa priebežne redukujú chronologickou min/max obálkou, ktorá zachová celý časový rozsah, teplotné skoky, trend aj krátke špičky bez preťaženia WPF vykresľovania.
- Rovnaká ochrana celého rozsahu bola doplnená pre internú teplotu komory v Live data.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.98.

## [1.76.97] – 2026-09-05

### Opravené
- Graf ustáľovania WIKA sa počas kalibrácie škáluje iba z dát aktuálneho plata, aby sa cieľ a dynamické limitné čiary pri veľkom počte bodov nezlievali.
- Rovnaké oddelenie plat bolo použité aj v rozšírenom Live data grafe; kompletný WIKA trace zostáva naďalej uložený pre históriu a audit.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.97.

## [1.76.96] – 2026-09-05

### Opravené
- Odhad zostávajúceho času už neodpočítava celý rozpracovaný bod od jednoduchého priemeru a nezobrazuje zavádzajúci čas konca po prekročení dostupných údajov.
- ETA používa historické mediány a maximá jednotlivých plat, aktuálny priebeh a započítava zostávajúci plynulý nábeh setpointu.
- Pri fyzikálne nepredvídateľnom čakaní na WIKA alebo FBG sa zobrazí stav `Neurčitý` bez falošného predpokladaného konca a s vysvetlením dôvodu.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.96.

## [1.76.95] – 2026-09-04

### Pridané
- Nastavenia stability obsahujú samostatný plynulý nábeh setpointu komory s predvolenou rýchlosťou 1 °C/min a rozsahom 0,1 až 20 °C/min.
- Kalibračný runner posúva setpoint po malých krokoch a v live stave zobrazuje prikázaný setpoint, cieľ a nastavenú rýchlosť.

### Zmenené
- Workflow vysvetľuje, že komora sa reguluje vlastným interným snímačom a WIKA iba overuje stabilitu bez druhej regulačnej slučky.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.95.

## [1.76.94] – 2026-09-04

- Textové polia v nastaveniach stability používajú jednotný tmavý dizajn aplikácie, výrazný focus, čitateľný disabled stav a pohodlnejšie rozmery.
- Karty nastavení dostali jemný hover, lepší kontrast a zjednotené odsadenie bez svetlosivých systémových vstupov.
- Do finálneho merania pribudlo samostatné pole pre počet meracích samples, ktoré priamo upravuje aktívne nastavenie `RequiredMeasurementSamples`.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.94.

## [1.76.93] – 2026-09-04

- Každý bod kalibračného workflow má viditeľný otáznik s podrobným vysvetlením podmienok, vzoriek, časov, resetov a výsledku kroku.
- Vysvetlenia sa dynamicky skladajú z aktuálnych nastavení stability, počtov vzoriek a timeoutov; počas behu dopĺňajú odhad podľa reálne pozorovaného cyklu dát.
- Paralelný zber teraz správne používa samostatné nastavenie počtu finálnych meracích vzoriek namiesto počtu stabilizačných vzoriek.
- Projektový skill vyžaduje synchronizáciu workflow nápovedy pri každej budúcej zmene kalibračnej logiky.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.93.

## [1.76.92] – 2026-09-04

- Voľba zvukového upozornenia pri chybnom SN bola presunutá z Live dát do záložky Zapojenie k režimom zadávania a párovania snímačov.
- Funkcia upozornenia aj jej globálne nastavenie pre celú aplikáciu zostali nezmenené.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.92.

## [1.76.91] – 2026-09-04

- Grafy majú väčšie a čitateľnejšie ovládacie prvky na priblíženie, oddialenie a návrat na celý rozsah dát.
- Live dáta obsahujú spoločné tlačidlo „Odzoomovať všetky grafy“, ktoré naraz obnoví úplný rozsah všetkých peakov, WIKA aj komory.
- Stav priblíženia sa zobrazuje priamo v paneli grafu a širšia os Y už neskracuje dôležité desatinné hodnoty.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.91.

## [1.76.90] – 2026-09-04

- Upozornenie na rozdiel teploty WIKA a komory sa po návrate aktuálnych hodnôt do povolenej tolerancie automaticky zruší.
- Dashboard zapíše návrat teplôt do zhody ako úspešnú udalosť, ale zachová pôvodné historické upozornenie v udalostiach kalibrácie.
- Zrušenie teplotného upozornenia nevymaže inú aktívnu chybu peaku alebo zariadenia.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.90.

## [1.76.89] – 2026-09-04

- Nové súbory analýzy histórie profilov boli zjednotené na projektový Windows CRLF formát, aby Visual Studio nehlásilo nekonzistentné konce riadkov.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.89.

## [1.76.88] – 2026-09-04

- Ku každému kalibračnému profilu sa z uložených behov automaticky počíta počet použití, posledné použitie, posledné a priemerné trvanie aj typický odhad ďalšieho behu.
- História zobrazuje rozpis trvania jednotlivých plató vrátane mediánu, priemeru, rozsahu a počtu dostupných meraní.
- Analýza používa existujúce uložené výsledky ako jediný zdroj pravdy a po skončení každého behu sa automaticky obnoví.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.88.

## [1.76.87] – 2026-09-04

- Karta komory v prehľade kalibrácie má nový vektorový indikátor smeru teploty s modernou šípkou a jemnou pulznou animáciou.
- Zelený indikátor znamená približovanie k cieľovej teplote, červený vzďaľovanie od cieľa a modrý stabilný smer bez výraznej zmeny.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.87.

## [1.76.86] – 2026-09-04

- Legenda grafov má vlastné kontrastné pozadie, takže ju už neprekrývajú merané krivky, cieľové čiary ani hranice stability.
- Riadky legendy majú väčšie rozostupy a prerušované čiary sa zobrazujú rovnakým štýlom ako v grafe.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.86.

## [1.76.85] – 2026-09-04

- Predvolený povolený rozdiel medzi WIKA CTH7000 a internou teplotou komory bol zvýšený z ±5 °C na ±10 °C.
- Staré nastavenia s pôvodnou implicitnou hodnotou sa pri prvom načítaní jednorazovo migrujú; neskoršie ručné nastavenia operátora sa zachovajú.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.85.

## [1.76.84] – 2026-09-04

- Kompaktná karta aktívnej FBG kalibrácie na domovskom dashboarde dostala moderný live vzhľad a zrozumiteľný slovenský stav.
- Karta teraz priebežne ukazuje aktuálnu činnosť, plato, čas fázy, cieľovú teplotu, WIKA referenciu, počet stabilných peakov a presný postup kalibračných bodov.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.84.

## [1.76.83] – 2026-09-04

- Vo všetkých grafoch možno ľavým tlačidlom myši označiť obdĺžnikovú oblasť a priblížiť ju v osi času aj meranej hodnoty.
- Počas výberu sa zobrazuje modrý priehľadný rámček; dvojklik obnoví celý rozsah a pravé tlačidlo posúva priblížený graf.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.83.

## [1.76.82] – 2026-09-04

- Prehľad kalibrácie teraz zobrazuje samostatný živý graf stability pre každý vybraný FBG peak.
- Každá karta peaku ukazuje aktuálnu vlnovú dĺžku, priebeh posledných 180 vzoriek, počet stabilných vzoriek, rozsah, smerodajnú odchýlku a drift voči nastaveným limitom.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.82.

## [1.76.81] – 2026-09-04

- Karta teplotnej stability WIKA sa dá rozbaliť a zobrazuje detailný graf posledných 50 vzoriek použitých pri stabilizácii.
- Vedľa grafu pribudol zoznam vzoriek s presným časom, teplotou na tri desatinné miesta a odchýlkou od cieľa.
- Graf zaznamenáva priamo vzorky použité kalibračným detektorom namiesto samostatného päťsekundového čítania WIKA.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.81.

## [1.76.80] – 2026-09-04

- Zobrazovaný stabilný čas WIKA teraz medzi päťvzorkovými kontrolnými blokmi plynulo pribúda podľa skutočných časov vzoriek.
- Bezpečnostné rozhodnutie o stabilite naďalej používa pôvodné blokové vyhodnotenie; zmena opravuje iba neaktuálne zobrazenie priebehu.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.80.

## [1.76.79] – 2026-09-04

- Sekcia „Čo sa deje práve teraz“ bola prerobená na moderný dynamický timeline s dokončeným, aktuálnym a nasledujúcim krokom.
- Aktuálny krok je výrazne zvýraznený a pri čakaní používa jemný pulz; spojovacie čiary a stavové body okamžite ukazujú smer procesu.
- Podmienky aktuálneho kroku, časy, aktívny peak a upozornenia zostávajú oddelené v prehľadných blokoch pod timeline.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.79.

## [1.76.78] – 2026-09-04

- Karta WIKA v živom prehľade teraz dynamicky zobrazuje odchýlku od cieľa, povolenú toleranciu, aktuálny drift a nazbieraný stabilný čas.
- Každé kritérium má samostatný farebný stav a stabilný čas má priebehový indikátor; splnené hodnoty sú zelené a čakajúce oranžové.
- Čakajúce kritériá používajú jemnú modernú animáciu bez rozmazania textu alebo rušivého blikania.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.78.

## [1.76.77] – 2026-09-04

- Graf WIKA už nezobrazuje pevné `cieľ ± tolerancia` ako hranice stability; cieľ kalibračného plata je samostatná žltá čiara.
- Počas úspešného zbierania skóre stability sa dynamicky zobrazí zelené minimum a maximum vzoriek, ktoré patria do aktuálneho okna ustáľovania.
- Teplota ustálená mimo povolenej odchýlky od cieľa zostáva správne zablokovaná, aby sa kalibračný bod nezaznamenal s chybnou referenčnou teplotou.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.77.

## [1.76.76] – 2026-09-04

- Kontrola formátu a duplicity FBG SN sa už nespúšťa pri každom napísanom znaku, ale až po potvrdení bunky Enterom alebo po odchode z bunky.
- Rovnaké správanie platí pre vyhľadanie a kontrolu SN cez Sylex FOS API, takže rozpracovaný text nevytvára opakované upozornenia.
- Do projektových pravidiel bola doplnená povinnosť nevykonávať textové validácie ani zobrazovať chyby počas písania.
- Normalizácia koncov riadkov teraz zahŕňa aj XAML obrazovky upravované vo Visual Studiu.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.76.

## [1.76.75] – 2026-09-04

- Natrvalo zjednotené konce riadkov podľa typu súboru, aby Visual Studio prestalo zobrazovať dialóg „Inconsistent Line Endings“.
- Pridaný opakovateľný skript `scripts/Normalize-LineEndings.ps1`, ktorý po úpravách normalizuje všetky sledované textové súbory.
- PowerShell skripty majú v Git pravidlách explicitne nastavené Windows konce riadkov CRLF.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.75.

## [1.76.74] – 2026-09-04

- Opravené pravidelné nepravdivé špičky a „zuby“ v grafoch FBG na záložke Live dáta.
- Reálnu vlnovú dĺžku teraz počas behu aktualizuje výhradne kontinuálny PeakLogger monitor; starší päťsekundový progress snapshot ju už neprepisuje.
- Aktualizácia z progress snapshotu zostáva zachovaná iba pre simulátor, ktorý sa počas kalibrácie zámerne nečíta druhýkrát.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.74.

## [1.76.73] – 2026-09-04

- Live terminál kalibrácie je v záložke Prehľad predvolene skrytý a zobrazí sa až tlačidlom „Zobraziť live terminál“.
- Rovnaké tlačidlo umožňuje diagnostický terminál opäť skryť bez zastavenia zapisovania logov do súboru.
- Nové udalosti už neposúvajú celý Prehľad nadol ani nepreberajú fokus; pri otvorenom paneli sa posúva iba jeho vlastný vnútorný výpis.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.73.

## [1.76.72] – 2026-09-04

- Opravené vypadávanie kriviek a dát peakov v záložke Live dáta pri obnovení topológie alebo krátkom výpadku PeakLoggera.
- História živého grafu sa teraz viaže na stabilnú identitu zariadenia, kanála a Peak ID namiesto dočasného objektu riadku tabuľky.
- Opätovne načítaný rovnaký fyzický peak plynulo pokračuje v existujúcej krivke bez vymazania predchádzajúcich vzoriek.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.72.

## [1.76.71] – 2026-09-04

- Okno poradového párovania SN sa už pri otvorení FBG kalibrácie nezobrazuje automaticky.
- Predvolený pohľad zapojenia je tabuľka SN s fokusom na prvom prázdnom sériovom čísle.
- Tmavé okno poradového párovania sa otvorí až po vedomom zvolení režimu „Poradové párovanie“.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.71.

## [1.76.70] – 2026-09-04

- Potvrdzovacie okno ukončenia aplikácie je väčšie, kontrastnejšie a má výrazný červený rám aj bezpečnostné upozornenie.
- Pôvodný textový výstražný symbol nahradila čistá škálovateľná vektorová ikona, ktorá zostáva ostrá pri každom DPI.
- Akcie „Skryť do tray“ a „Áno, ukončiť“ sú vizuálne jasnejšie odlíšené a majú väčšie plochy na ovládanie.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.70.

## [1.76.69] – 2026-09-04

- Aktuálne fokusovaná bunka v tabuľke zapojenia FBG je výrazne označená červeným rámčekom, aby operátor vždy videl miesto zadávania.
- Červený rámček zostáva viditeľný aj počas editácie textu v bunke a po presune klávesom Enter sa premiestni na nasledujúcu bunku.
- Opravená šablóna buniek DataGrid, ktorá predtým nastavený rámček nevykresľovala.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.69.

## [1.76.68] – 2026-09-04

- Predvoleným spôsobom zadávania zapojenia FBG je teraz poradové párovanie cez okno pre SN; tabuľkový režim zostáva dostupný ako voliteľná možnosť.
- Okno poradového párovania používa kompletnú tmavú tému vrátane titulku, kontrastného vstupu, stavového textu a hlavného tlačidla podľa dizajnového systému aplikácie.
- Rozloženie dialógu bolo sprehľadnené, zväčšené pre skener a klávesnicu a zachováva automatický fokus na zadanie ďalšieho SN.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.68.

## [1.76.67] – 2026-09-04

- Predvolene riadi kalibračnú teplotu vlastný interný regulátor komory podľa jej internej teploty.
- Staršie uložené konfigurácie s automaticky zapnutým vonkajším riadením podľa WIKA sa pri prvom načítaní bezpečne prepnú na interný regulátor komory.
- Riadenie podľa WIKA zostáva dostupné ako vedomá voliteľná voľba operátora v nastaveniach stability.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.67.

## [1.76.66] – 2026-09-04

- Živý terminál kalibrácie bol presunutý na úplný koniec obsahu záložky Prehľad.
- Terminál už nezaberá trvalo pripnutú spodnú časť obrazovky; zobrazí sa až po zrolovaní za všetky karty a grafy prehľadu.
- Automatické posúvanie výpisu a úplný súborový `diagnostics.log` zostávajú zachované.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.66.

## [1.76.65] – 2026-09-04

- Opravené počítanie stability WIKA: požadovaný čas sa už odvodzuje zo skutočných časových značiek meraní, nie z nesprávneho predpokladu jednej vzorky za sekundu.
- Desať minút nastavenej stability teraz zodpovedá približne desiatim minútam reálne stabilnej referenčnej teploty aj pri pomalšej komunikácii CTH7000.
- Do spodnej časti záložky Prehľad pribudol tmavý živý terminál kalibrácie s automatickým posunom a poslednými 500 diagnostickými udalosťami aktuálneho behu.
- Živý terminál zobrazuje Run ID, stavové prechody, teploty, stabilitu, peaky, limity, dôvody čakania, zásahy operátora a chyby zapisované aj do `diagnostics.log`.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.65.

## [1.76.64] – 2026-09-04

- Každý kalibračný beh vytvára vo svojom adresári samostatný terminálovo čitateľný súbor `diagnostics.log` s Run ID a okamžitým zápisom na disk.
- Diagnostika obsahuje konfiguráciu behu a stability, vybrané peaky, stavové prechody, WIKA a komorovú teplotu, priebeh každého peaku, limity, blokujúce dôvody, upozornenia, chyby a zásahy operátora.
- Existujúce `raw-samples.csv`, `wavelength-trace.csv`, výsledky a denný aplikačný log zostávajú zachované; cesta k diagnostike sa zapisuje aj do aplikačného logu.
- API kľúče ani iné prihlasovacie tajomstvá sa do kalibračnej diagnostiky nezapisujú.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.64.

## [1.76.63] – 2026-09-04

- Zapojenie FBG snímačov ponúka dva režimy: priame zadávanie SN v tabuľke a poradové automatické párovanie po pripojení snímača.
- V tabuľkovom režime zostáva fokus v stĺpci `FBG sensor SN (kanál)` a Enter presunie editáciu na nasledujúci riadok toho istého stĺpca.
- Poradové párovanie najprv overí SN a načíta výrobné údaje zo Sylex FOS API, potom čaká na nový peak a priradí SN všetkým peakom zisteného kanála.
- Po úspešnom priradení sa vstup automaticky pripraví na ďalšie SN; priebežné uloženie zapojenia a ochrana aktívnej editácie zostávajú zachované.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.63.

## [1.76.62] – 2026-09-04

### Zmenené – stručný live prehľad a ID kalibrácie
- Horný nadpis live prehľadu zobrazuje iba kód profilu a prvú stručnú časť názvu; celý automatický popis zostáva dostupný v bubline po podržaní myši.
- Pri komore sa zobrazuje ID aktuálneho kalibračného behu zhodné s ID uloženým vo výsledkoch a exportoch.
- Počas prípravy behu je stav ID zobrazený zrozumiteľne a po jeho pridelení sa prehľad okamžite aktualizuje.

## [1.76.61] – 2026-09-04

### Pridané – vynútenie ďalšieho kroku kalibrácie
- Počas čakania na stabilitu môže operátor tlačidlom „Vynútiť ďalší krok“ pokračovať na stabilizáciu FBG, ak je dostupná platná aktuálna teplota.
- Vynútenie platí iba pre aktuálne plato a neovplyvní pravidlá nasledujúcich kalibračných bodov.
- Akcia sa uloží medzi upozornenia behu spolu s cieľom, teplotou WIKA a teplotou komory, aby zostala dohľadateľná vo výsledkoch.

## [1.76.60] – 2026-09-04

### Zmenené – presnosť a hranice stability v teplotných grafoch
- Osa Y v grafoch WIKA a komory vždy zobrazuje teplotu najmenej na dve desatinné miesta, aby boli viditeľné stotiny dôležité pre stabilitu.
- Grafy počas kalibrácie zobrazujú prerušovanú dolnú a hornú hranicu stabilného pásma vypočítanú z aktuálneho cieľa a nastavenej tolerancie.
- Legenda uvádza presné hodnoty oboch hraníc v °C; pásmo sa automaticky zmení pri prechode na ďalší kalibračný bod.

## [1.76.59] – 2026-09-04

### Opravené – záznam teploty komory v kalibrácii
- Graf „Komora teplota“ odoberá vzorky priamo z telemetrie kalibrácie s presnou časovou značkou a nevynechá ich pri skrytom okne ani pri viacerých aktualizáciách v rovnakej sekunde.
- Prvá teplota komory načítaná pri spustení kalibrácie sa okamžite zobrazí a uloží do live priebehu.
- Pri novom behu sa starý priebeh komory vyčistí; po skončení zostáva posledný priebeh viditeľný až do ďalšieho spustenia.

## [1.76.58] – 2026-09-04

### Zmenené – povinné vydanie na GitHub
- Každá dokončená zmena projektu sa odteraz commitne priamo do vetvy `main` a odošle na GitHub do `origin/main`.
- Pred dokončením sa overí zhoda lokálneho commitu so vzdialenou vetvou; prepis histórie pomocou force-push zostáva zakázaný.
- Povinné zvýšenie verzie aplikácie a slovenský zápis v hlavnom `CHANGELOG.md` zostávajú súčasťou každej zmeny.

## [1.76.57] – 2026-09-04

### Opravené – súbežné čítanie WIKA CTH7000
- Prvá automatická vzorka a pravidelný 5-sekundový refresh už nemôžu súčasne vytvoriť dva klienty pre rovnaký COM port.
- Súbežné požiadavky zdieľajú existujúce otvorené spojenie; odpojenie, vynútené pripojenie a ukončenie klienta sú serializované rovnakým zámkom.
- Overená WIKA sekvencia 9600 8N1, CR, DTR/RTS, 25 ms medzi znakmi a `SYSTEM:REMOTE → *IDN? → MEASURE:CHANNEL? → SYSTEM:LOCAL` zostáva nezmenená.

## [1.76.56] – 2026-09-04

### Opravené – jednotné konce riadkov
- Pridané pravidlá `.gitattributes`, ktoré používajú Windows CRLF pre Visual Studio/.NET súbory a LF pre skripty a dátové súbory.
- Dotknuté zdrojové a projektové súbory už neobsahujú zmiešané konce riadkov, takže Visual Studio nežiada ich opakovanú normalizáciu.

## [1.76.55] – 2026-09-04

### Opravené – spustenie aplikácie
- Projekt aplikácie je opäť platné XML a dá sa zostaviť aj spustiť; komentár k automatickému verzovaniu už neobsahuje nepovolenú dvojicu spojovníkov.
- Záložná verzia pre zostavenia bez Git metadát bola zvýšená na 1.76.55.
- Zdieľaný Visual Studio launch profil spúšťa projekt `VotschVc3.App`, nie knižnicu `VotschVc3.Core`.
- Nulová požadovaná doba stability WIKA už neobíde kontrolu tolerancie prvej vzorky a pri nestabilnej referencii sa správne vyžiada zásah operátora.
- Kroky prehľadu kalibrácie správne rozlišujú teplotnú bránu, stabilizáciu FBG a meranie; bez externej referencie je krok WIKA označený ako nepoužitý.

## [1.76.52] – 2026-09-04

### Opravené – celý priebeh referenčnej teploty FBG
- Samostatný zber WIKA sa spúšťa ihneď s FBG kalibráciou a číta referenciu každých 5 sekúnd, aj počas rampy, pauzy a pri zatvorenom grafe.
- Graf behu zachováva všetky získané vzorky od štartu bez prerieďovania starších bodov. Nový beh začne nový priebeh; po skončení zostáva posledný priebeh dostupný počas otvorenej aplikácie.
- Okno referenčnej teploty sa obnoví po uložení vzorky a časová os vychádza zo štartu kalibrácie. Pri chybe čítania sa nevkladá vymyslená hodnota; ďalší interval čítanie zopakuje.

## [1.76.51] – 2026-09-04

### Opravené – zrozumiteľný aktuálny stav kalibrácie
- Prehľad dostáva stav aj počas úvodnej kontroly a časovaných krokov profilu. Pri rampe zobrazuje odoslaný setpoint, cieľ, číslo kroku a zostávajúci čas namiesto neaktuálneho stavu „Príprava“.
- Panel „Čo sa deje práve teraz“ je nad workflow a zobrazuje konkrétnu správu runnera vrátane dôvodu čakania, teplotných limitov a ďalšieho kroku.
- Pred vyhodnocovaním stability sa už nezobrazuje zavádzajúce čakanie na teplotnú bránu. Pripájanie komory a prvé čítanie zariadení majú vlastné hlásenia.
- Doplnené regresné overenie priebežných hlásení rampy, jej zobrazenia a zastavenia pred prvým kalibračným bodom. Riadenie komory a podmienky merania sa nemenia.

## [1.76.50] – 2026-09-04

### Zmenené – samostatné live grafy FBG peakov
- Každý zobrazený peak má vlastný graf s nezávislou mierkou vlnovej dĺžky a označením SN, kanála a Peak ID.
- Grafy sa zobrazujú v dvoch stĺpcoch na širšom okne a pod sebou na užšom. Predvolene sa zobrazia všetky peaky označené na kalibráciu.
- Filtre aktívneho/vybraných peakov, samostatné grafy WIKA a komory aj zber dát zostávajú zachované. Peaky bez vzoriek zobrazujú čakací stav.

## [1.76.49] – 2026-09-04

### Zmenené – príprava FBG kalibrácie a zapojenie
- Hlavné karty začínajú poradím `Nastavenia → Zapojenie → Prehľad`. Pri otvorení nebežiacej kalibrácie sa zobrazia Nastavenia s PeakLoggerom, teplotnou sondou a profilom.
- Zapojenie je samostatná hlavná karta. Jednotlivé peaky majú jemné riadkovanie; spoločný rámček zvýrazňuje iba susediace peaky rovnakého kanála a obnovuje sa aj po zoradení.
- Textovo sa upravujú iba `FBG sensor SN (kanál)`, `FBG sensor SN CHAIN` a `Poznámky`. Tieto bunky majú modré podfarbenie a ceruzku; ostatné údaje sú iba na čítanie.
- Výber peakov checkboxom a uzamknutie úprav počas kalibrácie zostávajú zachované. Varovania sériových čísel majú prednosť pred rámčekom skupiny.

## [1.76.48] – 2026-09-04

### Pridané – operátorský prehľad FBG kalibrácie
- Nová hlavná karta `Prehľad` zobrazuje profil, stav behu, aktuálne plato, fázu, dokončené body a odhad zostávajúceho času.
- Vizuálny workflow, roadmapa kalibračných bodov, live karty komory/WIKA/FBG, viacúrovňový progres a časovaný operátorský log uľahčujú sledovanie dlhého behu.
- Stabilizácia a meranie jednotlivých peakov sa zobrazujú paralelne. Progres teploty používa skutočné skóre stability; ETA sa počíta z dokončených bodov a počas pauzy sa nezobrazuje.

### Zmenené – kalibračný workspace a grafy
- Nastavenia zariadení a zapojenie sú oddelené od live prehľadu; technické stavy zostávajú v diagnostike a výsledky s exportom v histórii.
- `Live dáta` prepínajú graf FBG peakov, referencie WIKA a komory. Predvolený FBG graf sleduje aktívny peak, dostupné sú aj všetky alebo vybrané peaky.
- Existujúce kalibračné rozhodovacie pravidlá a ovládacie príkazy zostávajú zachované.

### Zmenené – jednotné e-mailové šablóny
- Testovacie správy, alarmy, rozdiel teploty WIKA/komora, FBG upozornenia a dokončenia aj súhrny profilov používajú spoločnú tmavú hlavičku, farebný stav, zvýraznenú správu a prehľadné údaje.
- Súhrn profilu používa spoločný dizajn so sekciou grafu a príloh. Nepotvrdené vypnutie komory je výrazne označené ako upozornenie.

### Opravené
- Celkový progres kalibrácie počíta dokončené body; stabilizačné a meracie vzorky sa nezamieňajú.
- Dokončený peak s chybou sa pri návrate do čakania na teplotu neoznačí za úspešný.
- E-mail neoznamuje chýbajúci CSV súbor ako priložený. Súradnice SVG grafu majú platný formát aj pri slovenskom nastavení desatinnej čiarky.

### Verzia
- Desktop aplikácia zvýšená z `1.76.47` na `1.76.48`.

## [1.76.34] – 2026-09-03

### UI – tiché úspešné pripojenie zariadení
- Bežné modré in-app popupy pri úspešnom pripojení zariadenia, napríklad `SIKA PolyTech · Pripojené na 10.88.6.28:80`, sa už nezobrazujú.
- Stav pripojenia zostáva viditeľný priamo na karte zariadenia; chybové, alarmové a varovné upozornenia zostávajú zachované.

### Verzia
- Desktop aplikácia zvýšená z `1.76.33` na `1.76.34`.

## [1.76.33] – 2026-09-03

### FBG workspace – úplná obnova po reštarte
- Pri zatvorení FBG kalibračného okna aj pri ukončení celej aplikácie sa ešte pred async teardownom synchronne uloží posledný workspace a detailný `CalibrationStore` setup.
- `fbg-calibration-workspaces.json` si okrem profilu a PeakLogger endpointu pamätá aj simulátor/scenario, zobrazenie grafu referencie a poslednú otvorenú kartu.
- Po opätovnom spustení sa obnoví profil, presný PeakLogger endpoint alebo simulátor, zapojenie SN/CHAIN, vybrané peaky, kalibračné plata, per-peak timeouty, stability nastavenia a posledná karta.
- Výber kalibračných plat a stability nastavenia sa priebežne autosavujú; produkčné SN/CHAIN zostávajú chránené existujúcim autosave mechanizmom.
- Opravená obnova WIKA CTH7000 kanála A/B: uložený kanál sa aplikuje ešte pred priradením COM zariadenia, takže kanál B sa po reštarte neprepne späť na A.

### FBG upozornenia
- Rovnaký popup `FBG zapojenie` sa pre jeden typ chyby zobrazí iba raz za session aplikácie; periodická revalidácia ani písanie SN znak po znaku ho už neopakujú.
- Rovnaký hardvérový warning beep pri neštandardnom/duplicitnom SN sa tiež prehrá iba raz za daný typ chyby počas session.
- Ostatné aplikačné popupy naďalej používajú bežný krátky dedupe interval a môžu sa neskôr legitímne zopakovať.

### Verzia
- Desktop aplikácia zvýšená z `1.76.32` na `1.76.33`.

## [1.76.32] – 2026-09-03

### WIKA referencia a live grafy
- Opravený `NullReferenceException` v `SylexFosCalibrationIntegration.DetachRow()` pri dynamickom PeakLogger refreshi; attach/detach riadkov je null-safe a idempotentný.
- Referenčná WIKA CTH7000 teplota sa zaznamenáva od prvých platných live vzoriek a kliknutie na dashboardovú dlaždicu `Referencia` otvorí samostatný live graf priebehu.
- `Live monitor` FBG kalibrácie zobrazuje samostatnú wavelength krivku pre každý vybraný FBG peak a časovo zarovnanú WIKA referenčnú teplotu na vlastnej osi/grafe.
- Pridaný voliteľný systémový zvuk pri zlom SN alebo nezhode sondy so Sylex FOS API; používateľ ho môže vypnúť a voľba sa persistuje.

### Voliteľné riadenie podľa referencie
- Pre zariadenie je možné zapnúť kalibračné dorovnávanie setpointu podľa WIKA referencie; režim je defaultne vypnutý.
- Dorovnávanie používa pomalý bounded outer-loop trim, nemení interný regulátor komory a má bezpečnostné limity kroku a celkovej korekcie.
- WIKA zostáva autoritatívnou kalibračnou teplotou; lokálna teplota komory je pre FBG stability gate informatívna.

### Verzia
- Desktop aplikácia zvýšená z `1.76.31` na `1.76.32`.

## [1.76.31] – 2026-09-03

### Jednotné upozornenia v aplikácii
- Pridaný centrálny `AppNotificationService` pre dočasné operátorské hlášky typu `Info`, `Success`, `Warning` a `Error`.
- Popup sa zobrazuje hore nad aktívnym oknom aplikácie, neberie focus, má jednotný farebný štýl, frontu, automatické zatvorenie a ochranu proti opakovanému spamu rovnakej správy.
- Existujúce `DesktopNotifier` udalosti sa pri viditeľnej aplikácii zobrazia aj cez rovnaký in-app popup; Windows tray balloon, zvuk a bliknutie taskbaru zostávajú zachované pre prácu na pozadí.
- Inline upozornenia na konflikt manuálneho ovládania a testovacieho profilu boli z dashboardových kariet odstránené a nahradené popup hláškami, aby nemenili výšku a layout zariadenia.
- Stav Sylex FOS API vo FBG kalibrácii používa rovnaký centrálny popup systém. Kontrola jednotlivých SN zostáva zámerne tichá, aby nevzniklo upozornenie pre každý symbol.
- Trvalé stavové indikátory ako `ALARM`, pripojenie zariadenia a `FBG CALIBRATION` zostávajú priamo na karte; popup systém je určený pre prechodné udalosti a upozornenia.

### Dashboard – FBG kalibrácia
- Opravené umiestnenie FBG status karty: vkladá sa ako samostatný sibling nad celú sekciu `Rýchle ovládanie`, nie do jej headeru, takže sa už neprekrýva s `Rýchle ovládanie / Upraviť predvoľby`.
- Neaktívna FBG status karta je zbalená; zobrazí sa až počas aktívneho FBG runu, čím sa uvoľní miesto na kartách zariadení.

### Verzia
- Desktop aplikácia zvýšená z `1.76.30` na `1.76.31`.

## [1.76.30] – 2026-09-03

### Opravené – zapojenie počas editácie
- `Zapojenie` už nevolá `CollectionView.Refresh()` počas aktívnej `DataGrid` editácie, takže zapisovanie FBG SN nevyhadzuje kurzor z bunky a nevzniká WPF chyba `Refresh is not allowed during an AddNew or EditItem transaction`.
- Background zmeny PeakLogger topológie a Sylex API metadata sa počas editácie SN odložia a aplikujú až po commitnutí bunky.
- Existujúci 350 ms autosave vo ViewModeli zostáva aktívny; po dokončení editácie sa vykoná aj bezpečný finálny save zapojenia.

### FBG workspace – layout a priebeh kalibrácie
- Celé FBG okno má page-level vertikálny scroll a karta `Zapojenie` dostala priestor približne na 16 produkčných riadkov naraz plus vlastné scrollbary.
- `Live monitor` zobrazuje pred spustením plán: profil, poradie a teploty plat, počet vybraných FBG peakov, WIKA referenciu a počet požadovaných stabilných samples.
- Počas kalibrácie sa explicitne zobrazuje `AKTUÁLNY KROK`, `ČAKÁM NA`, aktuálne plato, aktívny SN/kanál/peak, wavelength, samples, WIKA teplota a počet stabilných peakov.
- Historický stav `WaitingForChamberStability` je v operátorskom UI interpretovaný ako čakanie na stabilitu WIKA referencie; teplota komory je informatívna.

### Referenčná teplota na pozadí
- Päťsekundová aktualizácia WIKA CTH7000 už nevolá UI command `Načítať teplotu`, takže tlačidlo sa samo vizuálne nestláča.
- Background refresh číta priamo z existujúceho CTH7000 klienta, bez WMI rescanov a bez zmeny overeného 25 ms / 1000 ms Pali timing baseline.

### Bezpečnosť ovládania zariadenia
- Keď na konkrétnom zariadení beží FBG kalibrácia, jeho `Rýchle ovládanie` a `Testovací profil` sú na dashboarde zablokované.
- Stavový chip zariadenia sa počas behu prepne na `FBG CALIBRATION` namiesto zavádzajúceho `MANUÁL`.
- Tlačidlo `FBG Kalibrácia` aktívneho zariadenia používa pomalý červený pulzujúci prechod; po ukončení behu sa vráti do pôvodného štýlu.

### Verzia
- Desktop aplikácia zvýšená z `1.76.29` na `1.76.30`.

## [1.76.29] – 2026-09-03

### FBG produkčný workspace
- Tabuľka `Zapojenie` bola rozšírená pre produkčný workflow: read-only `Sylex SN`, `Typ FBG` zo Sylex FOS API, autosave SN/CHAIN a jednoznačné zvýraznenie nového PeakLogger riadku.
- Redundantný stĺpec `Snímač` bol odstránený; zmeny PeakLogger topológie aktualizujú tabuľku a operátor dostane stručnú informáciu o pridanom/odstránenom riadku.
- Kalibračné plata sa po prvom výbere profilu defaultne označia všetky, pričom uložený setup zostáva zdrojom pravdy pri následnom obnovení.
- Pridaná karta `Dáta` s cestami k run adresárom a metadátami RunId/čas začiatku/operátor.
- Počas aktívneho runu sú profil, PeakLogger, referencia, stability a zapojenie zamknuté proti zmene.
- PeakLogger integrácia podporuje operátorské otvorenie spektra kanála z kontextu zapojenia.

### Verzia
- Desktop aplikácia zvýšená z `1.76.28` na `1.76.29`.

## [1.76.28] – 2026-09-03

### FBG kalibrácia – stabilita merania
- Kalibračný runner používa ako zdroj pravdy kalibračné plata označené operátorom v `CalibrationSegmentIndices`; historický príznak `IsCalibrationPoint` zostáva iba ako spätná kompatibilita pre staré setupy bez uloženého výberu.
- Ak je ku kalibrácii priradený WIKA CTH7000, meranie peakov začne až po súčasnom ustálení teploty komory aj referencie WIKA v nastavenej tolerancii, čase stability a limite driftu.
- Pri neustálenej alebo chýbajúcej WIKA referencii sa kalibrácia neprepne do vyhodnocovania wavelength a po limite prejde do bezpečného stavu vyžadujúceho zásah operátora.
- Každý vybraný peak naďalej používa vlastný rolling stability tracker a vlastný výsledok; pridané bolo tlačidlo `Vybrať všetky peaky` pre kalibráciu všetkých PeakLogger peakov.

### Obnova zapojenia FBG workspace
- Pri zatvorení FBG kalibrácie sa explicitne uloží aktuálne zapojenie: výber peakov, produkčné SN/CHAIN, timeouty, poznámky, vybrané kalibračné plata a nastavenia stability.
- Pre každú komoru sa pamätá posledný zvolený kalibračný profil a posledný PeakLogger host/port v `fbg-calibration-workspaces.json`.
- Po opätovnom otvorení alebo reštarte aplikácie sa obnoví posledný profil a po prvom vykreslení sa asynchrónne skúsi iba posledný známy PeakLogger endpoint; nevykonáva sa pomalý široký discovery scan.
- Po úspešnom reconnecte PeakLoggera sa uložené mappingy obnovia podľa stabilnej identity interrogátor/kanál/peak, takže sa vrátia priradené SN/CHAIN, výber peakov a per-peak timeouty.
- Persistentné priradenie WIKA CTH7000 ku komore zostáva nezávislé a zachované podľa pravidiel z verzie 1.76.27.

### Testy
- Pridaný regresný test, že explicitný UI výber plat má prednosť pred profilovým `IsCalibrationPoint`.
- Pridaný regresný test, že nestabilná WIKA referencia zablokuje začiatok peak stability a skončí kontrolovaným `REFERENCE_STABILITY_TIMEOUT`.

### Verzia
- Desktop aplikácia zvýšená z `1.76.27` na `1.76.28`.

## [1.76.27] – 2026-09-03

### Pridané – exkluzívna referencia FBG kalibrácie
- WIKA CTH7000 sa po výbere trvalo priradí ku konkrétnemu zariadeniu/FBG kalibračnému workspace a ten istý fyzický teplomer nemožno súčasne priradiť inej FBG kalibrácii.
- Fyzická referencia sa identifikuje primárne podľa USB sériového čísla a sekundárne podľa COM portu; odpojenie USB ani zatvorenie kalibračného okna priradenie automaticky neuvoľní.
- Otvorenie ďalšej FBG kalibrácie už automaticky nepreberie prvý dostupný COM port. Pri pokuse použiť obsadenú referenciu aplikácia zobrazí zariadenie, ku ktorému je už priradená.
- Priradenia sa ukladajú do `fbg-reference-thermometers.json`; živá teplota sa po reštarte neobnovuje ako stará hodnota.

### Dashboard – referencia na karte zariadenia
- Hlavná karta zariadenia v Classic aj Professional režime zobrazuje kompaktnú referenčnú teplotu WIKA CTH7000 a priradený COM port.
- Ak referencia nie je fyzicky pripojená, miesto teploty zostáva `—`, ale uložený COM port a stabilný layout karty zostávajú zachované.
- Metrické dlaždice boli zhutnené tak, aby sa referencia zmestila bez zbytočného zväčšovania karty.

### Opravené
- Opravený WPF lifecycle responsive layoutu FBG okna, ktorý v CI spôsoboval duplicitný konštruktor partial triedy.
- Persistent reference store má explicitné `System.IO` závislosti pre `Path`, `File` a `Directory`.

### Verzia
- Desktop aplikácia zvýšená z `1.76.26` na `1.76.27`.

## [1.76.26] – 2026-09-03

### Opravené – FBG kalibrácia na menších obrazovkách
- FBG kalibračná stránka má page-level vertikálny scrollbar, takže rozbalenie grafu referenčnej teploty už neschová spodnú časť pracovného priestoru.
- Karta `Zapojenie` má stabilnú pracovnú výšku a jej DataGrid má explicitný vertikálny aj horizontálny scrollbar.
- Minimálne šírky produkčných stĺpcov zabraňujú zbytočnému stláčaniu hlavičiek a textov.
- Opravené prekrytie titulku `Priebeh USB referenčnej teploty` s textom `Port / kanál`.

### Zrýchlené – načítanie referenčnej teploty
- Opakované kliknutie na `Načítať teplotu` už pri pripojenom CTH7000 nespúšťa nový detailný WMI scan USB zariadení.
- Čerstvý detailný scan sa krátko cacheuje; ľahké obnovenie portov používa `SerialPort.GetPortNames()`.
- Overený fyzický CTH7000 timing 25 ms/znak a 1000 ms REMOTE settle zostáva nezmenený.

### Dokumentácia
- `SKILL.md` teraz obsahuje potvrdený produkčný baseline WIKA CTH7000 V1.0 vrátane Pali/AutoOptical timingov a príkazovej sekvencie.

### Verzia
- Desktop aplikácia zvýšená z `1.76.25` na `1.76.26`.

## [1.76.25] – 2026-09-03

### Opravené – zatváranie RAW debug okna
- Odstránená WPF `InvalidOperationException` pri zatvorení `Cth7000DebugWindow` počas async cleanup-u.
- Finálne zatvorenie okna sa vykoná až v ďalšom Dispatcher cykle po `SYSTEM:LOCAL`, close a dispose.

## [1.76.24] – 2026-09-03

### Opravené – produkčná komunikácia WIKA CTH7000 podľa fyzického testu
- Produkčný desktop aj Bridge používajú overený 25 ms inter-character pacing.
- Fresh session používa poradie `SYSTEM:REMOTE` → 1000 ms settle → `*IDN?` → `MEASURE:CHANNEL?` → `SYSTEM:LOCAL`.
- Nastavenie bolo potvrdené na fyzickom WIKA CTH7000 V1.0; kanál A vrátil platný rámec a kanál B korektne `NoProbe`.

## [1.76.23] – 2026-09-03

### Výkon – otvorenie FBG kalibrácie
- FBG okno sa otvára UI-first bez automatického aktívneho COM probe a širokého PeakLogger discovery.
- Detailný WMI USB scan a voliteľné kontroly sa vykonávajú až po prvom renderi na pozadí.
- Referenčný teplomer používa v kalibračnom workspaci one-shot režim bez konkurenčného pollingu.

## [1.76.22] – 2026-09-03

### Diagnostika – Pali / AutoOptical preset
- RAW CTH7000 debug dostal preset 9600 8N1, CR, 25 ms medzi bajtmi a 8 s timeout na reprodukciu pôvodného AutoOptical/Pali drivera.
- Preset umožnil na fyzickom zariadení izolovať rozdiel medzi nefunkčným 2 ms fresh-open dotazom a funkčným Pali timingom.

## [1.76.21] – 2026-09-03

### Diagnostika – RAW CTH7000 terminal
- Pridaný samostatný RAW debug režim s manuálnym COM open/close, TX/RX ASCII a HEX logom, nastaviteľným timingom, terminátorom, DTR/RTS a timeoutom.
- Debug režim neodosiela pri otvorení portu žiadny automatický príkaz a podporuje núdzové `SYSTEM:LOCAL + close`.

## [1.76.20] – 2026-09-03

### Opravené – CTH7000 lifecycle
- Čítanie referenčného teplomera používa bezpečný REMOTE/MEASURE/LOCAL lifecycle a pokúša sa vrátiť panel do LOCAL aj pri chybe alebo dispose.
- Desktop a Bridge boli zosúladené na dokumentované CTH7000 meracie príkazy namiesto starého `READ?`.
- Doplnené priame `System.IO.Ports` a `System.Management` závislosti pre desktop build.

## [1.76.19] – 2026-09-02

### Opravené – hover farba tlačidiel
- `AccentOutlineButton` pri hoveri a stlačení nastavuje `Foreground` na bielu spolu s modrým pozadím, takže text a ikonka už nezostávajú modré na modrom tlačidle.
- Oprava používa priamo `Button.Foreground`, aby sa správne prefarbili aj vektorové ikony a `IconLabel` vo vnútri tlačidla.

### Verzia
- Desktop aplikácia zvýšená z `1.76.18` na `1.76.19`.

## [1.76.18] – 2026-09-02

### Overené a opravené – WIKA CTH7000 USB príkazy
- Podľa oficiálneho WIKA manuálu je USB rozhranie `9600 8N1`, bez flow control a s odstupom `1–2 ms` medzi znakmi.
- Aktívna sekvencia aplikácie používa dokumentované príkazy `*IDN?` → `SYSTEM:REMOTE` → `MEASURE:CHANNEL? 1/2` → `SYSTEM:LOCAL`.
- `MEASURE:CHANNEL? 1` je kanál A, `MEASURE:CHANNEL? 2` je kanál B a `MEASURE:CHANNEL? -` je diferenciálne meranie A-B.
- Nedokumentované `READ?` a `CONFIGURE:CHANNEL ...` sa už pri CTH7000 fallbacke neposielajú; staré API symboly zostali iba kvôli zdrojovej kompatibilite.
- Zoznam baud rate bol zúžený na dokumentovaných `9600 Bd`.
- Jednotka odporu bola zosúladená s dokumentáciou na `R` (UI môže naďalej zobrazovať `Ω`).
- Čítanie odpovede používa polling `ReadExisting()` s celkovým limitom 8 s, aby lepšie zodpovedalo overenej komunikácii s reálnym CTH7000.

### Verzia
- Desktop aplikácia zvýšená z `1.76.17` na `1.76.18`.

## [1.76.17] – 2026-09-02

### Opravené – kompilácia USB teplomera
- Obnovený `using VotschVc3.Core.Thermometers;` v `SerialPortEnumerator`, ktorý používa typ `SerialDeviceInfo`.
- Tým sa odstránila chyba kompilátora `The type or namespace name 'SerialDeviceInfo' could not be found`.

### Verzia
- Desktop aplikácia zvýšená z `1.76.16` na `1.76.17`.

## [1.76.16] – 2026-09-02

### Opravené – WIKA CTH7000 identifikácia vs. meranie
- Odpoveď z `*IDN?`, napríklad `WIKA,CTH7000,000000,V1.0,01/05/2013`, sa už nikdy neinterpretuje ako teplota.
- Parser CTH7000 teraz akceptuje ako teplotu iba platný merací rámec s kanálom a hodnotou, napríklad `2,24.332,"CEL"`.
- Tým sa odstránila chyba, pri ktorej dátum vo výrobnej/identifikačnej odpovedi vytvoril falošnú hodnotu `2013.000 °C`.
- Zachovaná bola zdrojová kompatibilita starších parserových volaní.

### Verzia
- Desktop aplikácia zvýšená z `1.76.15` na `1.76.16`.

## [1.76.15] – 2026-09-02

### Opravené – názov referenčného teplomera v e-mailoch
- Upozornenia na rozdiel referenčnej teploty už nepoužívajú staré označenie `F100`.
- V predmete, texte aj HTML tele e-mailu sa používa aktuálny názov `WIKA CTH7000 Temp. reference`.

### Verzia
- Desktop aplikácia zvýšená z `1.76.14` na `1.76.15`.

## [1.76.14] – 2026-09-02

### UI – čitateľnosť tlačidiel
- Opravené `IconLabel` texty v tlačidlách: popis teraz preberá `Button.Foreground`, takže napríklad `FBG kalibrácia`, `Upraviť zariadenie` a ďalšie ikonové tlačidlá zostávajú kontrastné na akcentovom pozadí aj pri hover/pressed stave.
- Zachovaný ostrý rendering bez blur efektov.

### Upratané – starý pasívny test teplomera
- Pasívny test starého teplomera už nie je vo FBG kalibrácii používateľsky dostupný; jeho tlačidlo je skryté, aby sa nepoužívala vyradená Talk Only cesta.

### Verzia
- Desktop aplikácia zvýšená z `1.76.13` na `1.76.14`.

## [1.76.13] – 2026-09-02

### Diagnostika – USB / WIKA CTH7000
- Rozšírené logovanie USB životného cyklu: vytvorenie klienta, konfigurácia 9600/8N1, DTR/RTS, otvorenie portu, COM lease, čistenie RX/TX a inicializačná pauza.
- Loguje sa presná sekvencia `*IDN?` → `SYSTEM:REMOTE` → `MEASURE:CHANNEL? 1/2` → `SYSTEM:LOCAL` vrátane inter-character pauzy 2 ms.
- Pri každom TX/RX sa zapisuje port, pokus, príkaz, odpoveď a timeout/reconnect stav.
- Chyby teraz obsahujú konkrétny typ výnimky a správu pre otvorenie COM portu, identifikáciu, meranie, retry, reconnect aj návrat do `SYSTEM:LOCAL`.
- Po parsovaní sa loguje výsledná teplota, jednotka a surová odpoveď, takže je viditeľné, či problém vznikol v USB komunikácii alebo v parsovaní odpovede.
- Neúspešný príkaz je explicitne označený ako FAILED po každom pokuse aj po definitívnom zlyhaní.

### Verzia
- Desktop aplikácia zvýšená z `1.76.12` na `1.76.13`.
