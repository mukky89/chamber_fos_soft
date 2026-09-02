# Changelog

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
