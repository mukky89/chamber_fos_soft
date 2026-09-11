# Externá vlhkosť – Testo 645

Na karte komory (Classic aj Professional) otvorte **Externá vlhkosť – Testo 645**;
v okne FBG je dostupné tlačidlo **Testo 645**. Obidve cesty používajú tú istú
inštanciu merania pre identifikátor komory. Predvolené nastavenie je vypnuté.

1. Povoľte meranie, vyberte COM port a interval 1–3600 s. Interval je prestávka
   medzi dokončenými meracími cyklami; I/O môže skutočnú periódu predĺžiť.
2. Voliteľne povoľte PeakLogger, vyhľadajte API alebo zadajte host a konkrétny port,
   vyberte API a načítajte peaky. Označiť možno viac kanálov. Vyhľadávanie nikdy
   automaticky nevyberie prvé nájdené API.
3. Pripojte Testo. Pripojenie uloží nastavenia; zmenu povolenia bez pripojenia
   uložte tlačidlom **Uložiť nastavenia**. Po reštarte sa port neotvorí automaticky.
4. Vyberte priečinok/názov nového TXT alebo existujúci TXT na pripisovanie a spustite
   záznam. Nový súbor používa CreateNew a nikdy neprepíše existujúci súbor.
5. Počas merania sú zdroje uzamknuté; počas záznamu aj cieľový súbor. Zastavenie
   záznamu ponechá živé meranie. **Odpojiť** ukončí oboje. Zatvorenie doplnkového
   okna ponechá meranie v prevádzke; stav je dostupný na karte komory.

Nastavenia sú v `AppPaths.SettingsDir/testo645-{chamberId}.json`. Ukladajú sa
atómovou výmenou so zálohou `.bak`; poškodený primárny súbor sa neprepíše potichu.
Namerané hodnoty sú prechodné a po reštarte sa nezobrazujú ako živé.

## Časy a platnosť

Testo nemá v overenom formáte zdrojový čas. Existujúci PeakLogger adaptér tiež
poskytuje iba čas prijatia v PC. V TXT sú preto zdrojové časy výslovne `NA`.
Testo a API sa čítajú súbežne; jeden riadok neznamená hardvérovú synchronizáciu.
TXT uchováva oba časy prijatia, čas vytvorenia riadka, vek oboch vzoriek a absolútny
odstup časov prijatia. Predvolená hranica veku aj odstupu je **5 sekúnd**, nastaviteľná.
Hodnota s prekročeným vekom je `NA`; nadlimitný odstup má samostatný stav
`RECEIPT_SKEW_EXCEEDED`. API zlyhanie nemaže platné Testo hodnoty a naopak.
Pri chýbajúcom alebo nejednoznačnom peaku je jeho hodnota `NA`, nie posledná známa
vlnová dĺžka. Pri spojení sa vyžaduje `device.deviceSN + channel + index`.

Stĺpce: prijatie Testo, %RH Testo, °C Testo, vybrané peaky v nm (zoradené podľa
SN interrogátora/kanála/indexu), prijatie API, stav, čas riadka, vek Testo, vek API,
odstup prijatia, zdrojový čas Testo, zdrojový čas API. Identita interrogátora nie
je výrobné číslo FBG snímača. Každý začiatok záznamu pridáva novú hlavičku relácie
so zdrojmi a podmienkami; pôvodné bajty sa nemenia. Kódovanie pokračuje podľa BOM,
bez BOM sa nové riadky zapisujú ako UTF-8. Staršie riadky sa nedopĺňajú.

Samostatné živé grafy používajú existujúci `ChartView`, uchovávajú priebeh od
pripojenia a oddeľujú výpadky. Profilové CSV pri zapnutej funkcii na začiatku
profilu pridáva oddelené stĺpce Testo. Pri vypnutej funkcii zostáva pôvodná hlavička.
Zmena funkcie uprostred profilu nemení jeho už zapísanú schému; samostatný TXT
možno spustiť kedykoľvek počas merania.

## Overená časť protokolu a obmedzenia

Zdrojom je WinForms referenčný projekt `testo645/source/cesto` poskytnutý používateľom.
Prenesené boli iba komunikačné poznatky, nie WinForms ani jeho závislosti.

- 9600 baud, 8N1, RTS handshake, read/write timeout 500 ms.
- Požiadavka: `12 00 00 00 01 01 55 D1 B7 00`.
- Hlavička odpovede: `21 00 00 00 01`; potrebných najmenej 29 bajtov.
- RH: big-endian bajty 14–15 / 10; kladná teplota: bajty 19–20 / 10.
- Konzervatívna **softvérová akceptačná hranica** je 0–100 %RH a 0–200 °C;
  nejde o deklaráciu rozsahu fyzickej sondy. Nepodporované polia sú `NA` a stav
  obsahuje surové číselné hodnoty. Platné druhé pole sa zachová.
- Záporné teploty, device error kódy a checksum **nie sú špecifikované ani overené**.
  Ani rámec s prijateľnými hodnotami teda nemá overenú integritu checksumom;
  stav `OK_UNVERIFIED_PROTOCOL` toto obmedzenie uchováva aj v zázname.

Parser priebežne synchronizuje hlavičku, spracuje rozdelené aj viaceré rámce
a jeho interný buffer nikdy neprekročí 29 bajtov. Každá nová požiadavka vyčistí
vstup a používa nový parser, takže nedokončená stará odpoveď nie je nová vzorka.
Query má limit 2 s plus najviac práve prebiehajúce ohraničené čítanie; zrušenie
počká na jeho návrat a až potom zatvorí port. Spoločný `SerialPortLease` chráni
port pred súbehom s WIKA a diagnostikou. Výpadok USB vyžaduje ručné pripojenie;
timeout je viditeľný a ďalší cyklus opakuje požiadavku.

Testo nemá referenciu na `IChamberDevice`, nemení setpointy, podporu regulácie
vlhkosti, WIKA stabilitu, bezpečnostné interlocky ani ukončenie kalibrácie.

## Overenie

- `dotnet test tests/VotschVc3.Core.Tests -c Release`
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Testo645Ui.ps1`
- `dotnet build VotschVc3.sln -c Release`

Testy používajú syntetické rámce, falošný sériový transport a falošné API.
WPF test kontroluje uzamknutie zdrojov/súboru, zápis, výpadok API, starnutie
Testo údajov, obnovu nastavení, zdieľanú COM ochranu a vykreslenie pri dvoch šírkach.
Referenčný kód bol podľa zadania overený voči reálnemu PeakLogger API.
**Fyzické Testo 645 nebolo pri tejto implementácii testované.**
