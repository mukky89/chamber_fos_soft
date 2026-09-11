# História ustálenia snímačov

Menu **Snímače** je dostupné po prihlásení v klasickom aj profesionálnom zobrazení. Výber typu v hornej tabuľke zobrazí jednotlivé peaky a pokusy. Filtre sa použijú tlačidlom **Filtrovať** a prepočítajú aj priemery. **Obnoviť** načíta nové uložené výsledky. Dátumy sa zobrazujú v miestnom čase.

## Zdroj a trvalé uloženie

Názov pochádza z poľa `ApiMetadata.SensorName` v zapojení. Ukladá sa do `CalibrationSensorMapping.SensorName`, vrátane checkpointov, a pri začatí pokusu sa vytvorí nezávislá kópia názvu, SN, kanála, Peak ID a nastavení. Neskoršia úprava zapojenia nemení historické údaje.

Záznamy `SensorSettlingAttempts` sú súčasťou kanonického `summary.json` pôvodnej kalibrácie. Používajú existujúci atómový zápis a replikáciu kalibračného úložiska. Prechody stability zapisujú iba JSON; negenerujú opakovane reporty. Každý skutočný pokus má vlastné GUID. Opakované uloženie alebo načítanie rovnakého pokusu nevytvorí ďalší riadok. Pokračovanie nedokončeného plata začne nový pokus a zachová pôvodný neúspešný pokus.

Pokus vznikne už pred presunom setpointu. Pri prerušení počas presunu ostane viditeľný so všetkými časmi N/A. Meranie časov stability začína až po presune setpointu do cieľa; čas rampy sa nepripočítava. Chyba sekundárneho zápisu sa zaznamená do aplikačného logu a nemení rozhodovanie kalibrácie.

## Hranice jednotlivých fáz

- **Komora s WIKA:** od vstupu do stabilizácie plata po prvé splnenie zapnutej vstupnej podmienky komory. Ak je podmienka vypnutá, čas je N/A.
- **Komora bez WIKA:** od vstupu do stabilizácie plata po prvé otvorenie teplotnej brány používajúcej internú teplotu komory. WIKA je N/A.
- **WIKA:** od začiatku referenčnej kontroly, prípadne od splnenia vstupnej podmienky komory, po prvé otvorenie teplotnej brány. Pri ručnom preskočení je fáza označená ako manuálne preskočená.
- **FBG:** od začatia FBG fázy po prechod konkrétneho peaku do finálneho vzorkovania. Ak sa kvalifikácia neskôr zruší, pripočíta sa nové obdobie ustálenia. Obdobia finálneho vzorkovania sa nezapočítajú. Po strate teplotnej stability sa do nového obdobia čakajúceho FBG započíta aj čakanie na návrat referencie. Toto obdobie sa môže prekrývať s obnovou teploty; fázy sa nikdy nesčítavajú ako celkový čas kalibrácie.

Spoločné časy teplotných brán opisujú ich prvé otvorenie na danom pokuse, nie súčet neskorších obnovení stability počas finálneho merania. Časové intervaly FBG sú uložené samostatne s dátumom začiatku a konca a sú zobrazené v detaile. Timeout, prerušenie a zlyhanie zachovajú dovtedy namerané trvanie s príslušným stavom. Pri nečakanom páde bez ukončenia fázy sa nezobrazuje odhad času do aktuálneho dátumu.

Smer prechodu vychádza z predchádzajúceho prikázaného cieľa; pri prvom plate z teploty odovzdanej runneru. Pri priamom použití orchestrátora bez tohto údaja sa použije prvá platná nameraná teplota plata.

## Priemery a staršie záznamy

Priemery zahŕňajú len úspešné fázy stabilného peaku z dokončeného pokusu a dokončeného behu (aj behu s inými upozorneniami). Nezahŕňajú prerušený beh, timeout, manuálne preskočenie teplotnej brány ani pokus, počas ktorého sa zmenili kritériá. Zmeny nastavení majú vlastné časovo označené snímky dostupné v detaile. Hodnota `n` označuje počet použitých časov.

Pre každý názov snímača sa spoločné časy komory a WIKA deduplikujú podľa ID behu a pokusu. FBG má samostatný čas pre každý peak. Posledný FBG čas je čas naposledy ukončeného peaku v zobrazenej histórii; podrobnosti ostatných peakov zostávajú v spodnej tabuľke.

Staršie výsledky bez explicitných časových udalostí sú zobrazené s N/A a nezapočítavajú sa do priemerov. Historické `StabilizationTime` obsahovalo aj finálne vzorkovanie a nepoužíva sa na dopĺňanie. Ak chýba doložený názov, záznam patrí pod **Neurčený** a zachová SN. Aktuálny názov z iného behu sa spätne nepriraďuje.
