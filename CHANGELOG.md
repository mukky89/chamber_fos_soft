# Changelog

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

## [1.76.12] – 2026-09-02

### Opravené – čitateľnosť tlačidiel
- Opravené vlastné `ControlTemplate` tlačidlá, ktoré po odstránení blur efektov stratili viditeľný text alebo popis.
- `Button.Foreground` sa teraz správne prenáša do `ContentPresenter`, takže text a textové ikony zostávajú kontrastné aj pri hover/pressed stave.
- Zachovaný ostrý hover bez `DropShadowEffect`.

### Verzia
- Desktop aplikácia zvýšená z `1.76.11` na `1.76.12`.

## [1.76.11] – 2026-09-02

### Opravené – USB / WIKA CTH7000
- Aplikácia sa vrátila k overenému spôsobu USB prenosu z lokálneho testu: každý znak SCPI príkazu sa odosiela samostatne s 2 ms medzerou.
- Rovnaký transport sa teraz používa pre `*IDN?`, `SYSTEM:REMOTE`, `MEASURE:CHANNEL? 1/2`, `SYSTEM:LOCAL` aj ostatné príkazy.
- Identifikácia už nepoužíva rýchly jednorazový `_port.Write(frame)`, ktorý sa odlišoval od úspešného hardvérového testu.
- Zachované zostávajú COM lease, retry/reconnect, čistenie RX/TX bufferov, A/B kanály a TX/RX diagnostika.

### Upratané – pomenovanie CTH7000
- Fyzický klient aplikácie je v `src/VotschVc3.App/Thermometers/CTH7000Client.cs` namiesto historického názvu `F100Client.cs`.
- Zdieľaný protokol je v `src/VotschVc3.Core/Thermometers/CTH7000Protocol.cs` namiesto historického názvu `F100Protocol.cs`.
- Bridge klient a regresné testy používajú názvy súborov `CTH7000Client.cs` a `CTH7000ProtocolTests.cs`.
- Historické `F100Client` / `F100Protocol` symboly zostávajú v týchto súboroch iba kvôli zdrojovej kompatibilite existujúcej architektúry.

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
