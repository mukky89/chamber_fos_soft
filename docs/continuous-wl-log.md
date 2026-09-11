# Kontinuálny kompatibilný WL log

Každý nový alebo obnovený FBG beh vytvára v existujúcom priečinku výsledkov súbor
`ddMMyyyy_HHmmss_<RunId bez pomlčiek>_<interrogator SN>_WL.txt`.
Pri viacerých interrogátoroch vzniká jeden súbor pre každý. Existujúca replikačná
fronta kopíruje súbory spolu so zvyškom behu na nakonfigurovaný server.

Zápis je automatický a nezávislý od prepínača voliteľného CSV trace. Interval
preberá `SampleAcquisitionIntervalSeconds` aktívneho behu; checkpoint zachováva
jeho nastavenie. Nevznikajú dodatočné dotazy na zariadenia. Existujúci live monitor
dodáva dáta aj počas nábehov, prechodov a čakania; meracie cykly dodávajú rovnakému
zapisovaču svoje dávky. Zapisovač obmedzuje spoločnú frekvenciu a duplicity.

## Formát a referenčné súbory

Porovnaných bolo šesť dodaných produkčných súborov z augusta/septembra 2026.
Nový výstup používa variant s referenčnou teplotou podľa súboru
`08092026_163915_sentea dalsia kal._WL.txt`: UTF-8 bez BOM (ASCII pre bežné SN),
tabulátory, CRLF, tri hlavičky, pevné poradie 16 kanálov 1.1–4.4.
Prvý riadok nemá koncový tabulátor; druhý, tretí a dátové riadky ho majú.
Timestamp je miestny čas `dd.MM.yyyy HH:mm:ss.fffff`, teplota `0,000`,
vlnová dĺžka `0,00000`. Prázdne polia sa nesmú odstraňovať.

Riadok SN obsahuje prázdne pole teploty, 16 kanálových SN a potom SN každého
vybraného peaku v poradí kanál/index. Prednosť kanálového SN má uložené
`ChannelSerialNumber`; pri nejednoznačnom alebo chýbajúcom kanálovom SN sa použije
`NOSN`. Jednotlivé peaky vždy nesú vlastné efektívne produkčné `SerialNumber`
(vrátane existujúceho CHAIN mapovania). Počty v dátovom riadku znamenajú počet
logovaných vybraných peakov v kanáli; nesledujú nevybrané peaky ostatných kalibrácií.
Hlavička zachová aj známe kanálové SN bez vybraných peakov, ako vzor z 28.08.

Vzorka z 01.09. obsahuje aj teploty s dvomi desatinnými miestami. Podľa zadania
nový výstup vždy zachováva tri. Vzorka z 20.08. je variant bez teploty a Templog SN;
nový kalibračný log používa variant s WIKA, nevydáva chýbajúcu referenciu za variant
bez teploty. Päť referenčných výrezov s teplotou sa v testoch porovnáva po bajtoch.
Výrezy obsahujú pôvodné tri hlavičky a jeden pôvodný riadok s tromi miestami teploty.

`Templog SN` obsahuje identitu priradeného WIKA z behu (USB identifikátor; ak chýba,
uložený COM port). Nikdy nekopíruje identitu starého Templogu zo vzorov.

## Chýbajúce dáta a obnova

WIKA musí byť platná, z aktuálneho behu, z pôvodnej referencie/kanála a nie staršia
ako 10 sekúnd voči PeakLogger dávke. Opakované použitie tej istej referenčnej vzorky
je zakázané. Rozptyl timestampov peakov v dávke smie byť najviac jedna sekunda.
Chýbajúci peak, duplicita zdroja, neplatná wavelength, zmenený index alebo stará
referencia znamenajú preskočený riadok a samostatnú diagnostiku `WLN_LOG`.
Žiadne nuly, minulá teplota ani chybové komentáre sa nevkladajú do TXT.

Susedný `.layout.json` uchováva presnú hlavičku a zdrojové identity/indexy.
Pri zmene mapovania sa existujúci log neprepíše. Obnova pokračuje bez novej hlavičky
za posledným úplným riadkom, pričom čas posledného zápisu bráni duplicite.
Neúplný posledný riadok po páde sa najprv zachová ako `.partial-<id>.bin`.
Pri chybe zápisu na disk sa ďalší zápis do daného súboru zastaví s diagnostikou;
neúplný koniec sa neopravuje za chodu a nekazí ďalšími riadkami.

Zápis používa asynchrónny FileStream s WriteThrough a flush po riadku. STOP dovolí
dokončiť už prijatý riadok a disposal čaká na jeho dokončenie. Sieťové kopírovanie
prebieha existujúcou frontou mimo meracej slučky. Staré CSV a reporty sa nemenia.
