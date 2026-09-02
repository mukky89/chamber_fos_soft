# Changelog

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
