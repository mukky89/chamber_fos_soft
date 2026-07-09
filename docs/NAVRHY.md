# Návrhy nových riešení a modulov

Zoznam navrhovaných rozšírení aplikácie, zoradený podľa odhadovaného prínosu
voči prácnosti. Vychádza z revízie kódu pri v1.7.2.

## 1. Simulátor zariadenia (bez hardvéru) — vysoká priorita
Virtuálny `IChamberDevice`, ktorý simuluje tepelnú odozvu (1. rád, časová
konštanta podľa typu zariadenia). Umožní:
- vyskúšať profily, frontu, pauzu aj alarmy bez pripojenej komory,
- písať automatizované testy celej slučky `ProfileRunner` → zariadenie,
- školiť operátorov.
Architektúra je pripravená — stačí tretia implementácia `IChamberDevice`
a voľba „Simulátor" v protokole zariadenia.

## 2. Protokol testu / PDF report — vysoká priorita
Po dokončení profilu vygenerovať report: názov profilu, komora, operátor
(z prihlásenia), čas štartu/konca, graf meranej vs. nastavenej teploty
(+ referenčný teplomer F100 a odchýlka), alarmy počas behu, audit záznamy.
Formát HTML (ľahké, bez závislostí) s tlačou do PDF cez WPF `PrintDialog`.
Dáta už existujú (CSV záznam + audit) — treba ich len spojiť.

## 3. Kalibračný modul (F100 vs. komora) — stredná priorita
Sprievodca: nechá komoru ustáliť na N bodoch (napr. −20/25/60 °C), odčíta
odchýlku komora−referencia a uloží kalibračnú tabuľku s dátumom. Upozorní,
keď kalibrácia stará > 12 mesiacov. Nadväzuje na existujúce prepojenie
teplomerov na karte komory.

## 4. Overenie POL-EKO mapy registrov — technický dlh
`PolEkoRegisterMap` vychádza z verejnej SMART dokumentácie. Pridať:
- „diagnostický sken": prečítať bezpečne (len čítanie) prvých ~20 input aj
  holding registrov a zobraziť tabuľku hodnôt na porovnanie s displejom pece,
- editovateľnú mapu registrov v UI (adresa teploty/setpointu/štartu, škála),
  uloženú v `ChamberConfig`.

## 5. Šablóny a štítky profilov — stredná priorita
Knižnica rastie; pridať do `TestProfile` pole `Tags` (napr. projekt, norma)
a fulltextové hľadanie/filter v knižnici aj vo výbere na dlaždici.

## 6. Vzdialené sledovanie (REST/MQTT) — nižšia priorita
Malý embedded HTTP endpoint (`/status` JSON: teploty, stav profilu, alarmy)
alebo MQTT publisher. Umožní dashboard na intranete / Grafana bez zásahu
do riadenia (len na čítanie — bezpečné).

## 7. Perzistentná fronta testov a plánovač — nižšia priorita
Fronta (`Queue`) sa dnes stráca pri reštarte aplikácie. Uložiť ju do JSON
vedľa profilov a pridať týždenný plánovač (profil X každý pondelok 6:00).

## 8. Lokalizácia SK/EN — podľa potreby
Texty sú dnes v XAML natvrdo po slovensky. Presun do `.resx` slovníkov by
umožnil prepínanie jazyka (užitočné pre audit u zákazníka).

## Opravené v tejto revízii (v1.7.2)
- Ukladanie profilu prepisuje verziu s rovnakým názvom (žiadne duplikáty) —
  komora, editor profilov aj rýchly vytvárač.
- Validácia teploty profilu podľa zariadenia (POL-EKO 0…300 °C, Vötsch −80…200 °C).
- Ukazovateľ „teraz" v náhľade profilu sa resetuje pri štarte nového behu.
- Pauza počas odloženého štartu vysvetlí, že profil ešte nebeží.
- POL-EKO detail komory: skryté ASCII-2 polia, MODBUS nápoveda v termináli,
  predvolený MODBUS príkaz.
- Rýchle predvoľby teploty na dlaždici podľa zariadenia (sušiareň: 60/105/150/250 °C).
- Editor profilov (knižnica): dvojklik načíta profil, text sa zalamuje.
