# Changelog

## [1.76.11] – 2026-09-02

### Opravené – USB / WIKA CTH7000
- Aplikácia sa vrátila k overenému spôsobu USB prenosu z lokálneho testu: každý znak SCPI príkazu sa odosiela samostatne s 2 ms medzerou.
- Rovnaký transport sa teraz používa pre `*IDN?`, `SYSTEM:REMOTE`, `MEASURE:CHANNEL? 1/2`, `SYSTEM:LOCAL` aj ostatné príkazy.
- Identifikácia už nepoužíva rýchly jednorazový `_port.Write(frame)`, ktorý sa odlišoval od úspešného hardvérového testu.
- Zachované zostávajú COM lease, retry/reconnect, čistenie RX/TX bufferov, A/B kanály a TX/RX diagnostika.

### Verzia
- Desktop aplikácia zvýšená z `1.76.10` na `1.76.11`.

## [1.76.10] – 2026-09-02

### Opravené
- Changelog parser už ignoruje hlavičku `[Nezverejnené]`, takže sa v aplikácii nezobrazuje falošná verzia `vNezverejnené`.
- Zjednotený zdroj histórie verzií: aplikácia načítava iba koreňový `CHANGELOG.md`, ktorý zostáva jediným kanonickým changelogom.

### Upratané
- Odstránené duplicitné súbory `CHANGELOG_<verzia>.md`; ich obsah je vedený v koreňovom `CHANGELOG.md`.

### Verzia
- Desktop aplikácia zvýšená z `1.76.9` na `1.76.10`.

## [1.76.9] – 2026-09-02

### UI / tlačidlá
- Odstránené `DropShadowEffect`/blur z tlačidiel používaných na kartách zariadení a vo FBG kalibrácii.
- Hover stav `GhostButton`, `AccentOutlineButton` a `AccentButton` je teraz ostrý a používa rovnaký outline/fill princíp ako hlavné menu.
- Texty a ikonky tlačidiel sa pri hoveri už nerenderujú cez rozmazanú efektovú bitmapu.

### Opravené
- Doplnený `System.IO` namespace pre `IOException` v COM lease vrstve, aby projekt opäť korektne kompiloval.

### Verzia
- Desktop aplikácia zvýšená z `1.76.8` na `1.76.9`.

## [1.76.8] – 2026-09-02

### Opravené – USB / WIKA CTH7000
- Pridaná procesovo-globálna ochrana COM portov, takže dve inštancie `F100Client` v jednej aplikácii už nemôžu súčasne otvoriť rovnaký port.
- Diagnostický pasívny scan používa rovnakú ochranu a neotvára COM port, ktorý práve vlastní živé meranie.
- `UnauthorizedAccessException` z `SerialPort.Open()` sa mapuje na jasný stav obsadeného portu namiesto falošného reconnect cyklu.
- Manuálny výber `COMx` zostáva zachovaný; ak je port dočasne obsadený iným procesom, používateľ ho môže po uvoľnení znovu otvoriť.
- Tiché prázdne odpovede z query komunikácie sa už nepovažujú za úspešné čítanie a vyvolajú timeout/retry.
- Existujúci reconnect po skutočnom USB výpadku zostáva zachovaný.
- Blokujúce `SerialPort` operácie zostávajú mimo UI threadu.

### Dokumentácia
- `SKILL.md` doplnené o povinnú procesovo-globálnu COM lease, pravidlá pre obsadené porty a regresné kontroly.

### Verzia
- Desktop aplikácia zvýšená z `1.76.7` na `1.76.8`.

## [1.76.7] – 2026-09-02

### USB / WIKA CTH7000
- Vnútorná synchronizácia `F100Client` bola rozšírená tak, aby jeden klient nevedel zavrieť port počas vlastného aktívneho čítania/zápisu.
- Reconnect po dočasnom USB výpadku bezpečne zatvorí a znovu otvorí port a vyčistí RX/TX buffre.
- TX/RX diagnostika zapisuje komunikáciu WIKA CTH7000 s portom a číslom pokusu.

## [1.76.6] – 2026-09-02

## USB / WIKA CTH7000
- Zjednotená synchronizácia `SerialPort` proti súbehu skenovania, čítania a zatvárania portu.
- Spoľahlivejší reconnect po dočasnom USB/COM výpadku; komunikácia skúsi bezpečné znovuotvorenie portu a druhý pokus.
- RX/TX buffer sa pri otvorení a reconnecte zahodí pred ďalšou komunikáciou.
- USB komunikácia zostáva plne asynchrónna z pohľadu UI; blokujúce operácie `SerialPort` bežia mimo UI threadu.
- TX/RX rámce WIKA CTH7000 sa zapisujú do aplikačného diagnostického logu s označením portu a pokusu.
- Existujúce automatické vyhľadávanie COM portov, zachovanie manuálneho `COMx`, A/B kanálov a pasívna detekcia staršieho ASL F100 zostávajú zachované.
- Zachovaná je kompatibilita názvu `F100Client`, ale používateľské a diagnostické texty používajú WIKA CTH7000.

## Bezpečnosť prihlásenia
- Changelog už nie je dostupný priamo z prihlasovacej obrazovky.
- Tým sa odstráni cesta, pri ktorej sa po otvorení changelogu dalo dostať do hlavnej aplikácie bez úspešného prihlásenia.
- Changelog je dostupný až po prihlásení z aplikácie.

## Verzia
- Desktop aplikácia zvýšená z `1.76.5` na `1.76.6`.

## [1.76.5] – 2026-09-02

### Opravené
- Parser rozpoznáva reálny CSV rámec WIKA CTH7000 (`kanál,teplota,\"CEL\"`). Čiarka oddeľujúca kanál sa už nepovažuje za desatinnú čiarku: `2,24.559,\"CEL\"` sa načíta ako `24.559 °C`, nie `559`.
- Pridané regresné testy s rámcami nameranými na COM4 a COM7.

## [1.76.4] – 2026-09-01

### Zmenené
- Aktuálne používateľské označenia ASL F100 boli v navigácii, správe teplomerov, kalibrácii a stavových hláškach nahradené názvom `WIKA CTH7000`.

### Opravené
- Príkaz `*IDN?` pre WIKA CTH7000 sa odošle ako jeden atómový USB rámec namiesto samostatného zápisu každého znaku. Rovnaký spôsob sa po identifikácii používa aj pre prepnutie režimu, meranie kanála a návrat do lokálneho režimu.
- Jednorazové čítanie vždy odošle `SYSTEM:LOCAL` aj pri chybe alebo timeoute, takže prístroj nezostane zamknutý v režime Remote.

## [1.76.3] – 2026-09-01

### Opravené
- Textové a projektové súbory používajú jednotné Windows ukončenie riadkov `CRLF`. Pravidlo je uložené v `.editorconfig`, takže Visual Studio už pri otvorení `VotschVc3.App.csproj` nezobrazuje dialóg „Inconsistent Line Endings“.

## [1.76.2] – 2026-09-01

### Zmenené
- Kalibračné okno pri otvorení automaticky vyhľadá a pripojí reálne PeakLogger API; simulátor už nie je predvolene zapnutý.
- Referenčný teplomer zobrazuje identifikovaný názov, výrobcu, model a úplnú odpoveď `*IDN?`, napríklad `WIKA CTH7000`.
- Výber kanála sondy A/B je opäť dostupný. Aplikácia kanál naďalej automaticky nastaví podľa vstupu, na ktorom nájde pripojenú sondu.
- Modré obrysové tlačidlá majú svetlejší a hrubší rám, aby boli na tmavom pozadí zreteľné.

### Opravené
- WIKA CTH7000 po otvorení USB portu dostane čas na inicializáciu a po príkaze `SYSTEM:REMOTE` krátku stabilizačnú pauzu. Merací timeout rešpektuje reálny čas konverzie približne 2,1–2,3 sekundy, takže sa odpoveď nestratí ani neposunie k nasledujúcemu kanálu.
- Kalibračné jednorazové čítanie už nesúťaží s automatickým pollingom o rovnakú sériovú odpoveď.
- Z kalibračnej obrazovky boli odstránené zastarané popisy ASL F100 a Talk Only; diagnostika jasne odlišuje WIKA CTH7000 od staršieho pasívneho ASL F100.
- USB identifikácia rozpozná reálne pripojené teplomery WIKA CTH7000 a pri meraní mapuje vstupy A/B na príkazy `MEASURE:CHANNEL? 1/2`. Predchádzajúce textové parametre A/B zariadenie odmietalo ako `ERR CMD`.
- Pôvodný ASL F100 po neúspešnom pasívnom čítaní už nedostáva automatické SCPI príkazy určené pre novšie kompatibilné modely. Aplikácia namiesto zavádzajúceho timeoutu vysvetlí, že na prístroji treba zapnúť `Menu → Options → Talk Only → On`.
- Vyhľadávanie PeakLogger API rozpozná stav, keď bežia dve okná PeakLogger, ale iba prvý proces vlastní pevný REST port `43122`, a zobrazí operátorovi, že druhá inštancia nemá samostatné API.

## [1.76.1] – 2026-08-29

### Zmenené
- Akcie v hlavičke karty zariadenia sú prehľadnejšie: zámok je vľavo, vedľa neho je ikonka ceruzky na nastavenie a ovládanie a vpravo zostala iba pomenovaná akcia „FBG Kalibrácia“.
- Odkaz na webové rozhranie je označený „WWW ↗“ a presunutý priamo vedľa IP adresy zariadenia.
- Výber kalibračného profilu používa rozšírený náhľad s vyhľadávaním, grafom, min/max, časom, cyklami a teplotnými úrovňami.

### Opravené
- Odpojenie alebo vynútené opätovné pripojenie ASL F100 už nezatvára COM port počas blokujúceho čítania; klient bezpečne dokončí alebo časovo ukončí aktívne čítanie.
- Pred kontrolou a vynúteným pripojením sa nanovo skenujú Windows USB/COM porty a výber sa obnovuje podľa USB sériového čísla.
- USB diagnostika má pasívny test F100 bez odoslania SCPI príkazov a výsledky zapisuje do aplikačného logu.
- USB komunikácia s pôvodným ASL F100 najprv skúsi pasívny talk-only dátový prúd a až potom kompatibilný query fallback.
- Pri odpojení sa `SYSTEM:LOCAL` posiela iba zariadeniu, ktoré preukázateľne komunikovalo v query režime.
- PeakLogger API a live monitor dostali viacero opráv a diagnostických zlepšení.

## [1.76.0] – 2026-08-28

### Pridané
- Presnejšie meranie regulátora cez SIMSERV 11004 s poistkami proti zámene kanála.
- Presnejšia hodnota sa používa na karte, v grafe, CSV a pri rozhodovaní o ustálení profilu.

### Bezpečnosť a spoľahlivosť
- SIMSERV hodnota sa prijme iba pri platnej odpovedi a v rámci definovanej tolerancie voči ASCII-2.
- Regulátory bez podpory 11004 sa po zlyhaní zbytočne neopakujú.
- Surová ASCII-2 odpoveď zostáva zachovaná a diagnostikovateľná.

### Dokumentácia
- Pridaná dokumentácia `docs/DEVICE_INTEGRATIONS.md` pre presné SIMSERV meranie.
