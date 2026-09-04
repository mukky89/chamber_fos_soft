# Changelog

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
