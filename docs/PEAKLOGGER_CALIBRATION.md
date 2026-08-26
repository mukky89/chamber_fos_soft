# PeakLogger – FBG teplotná kalibrácia

Tento modul synchronizuje klimatickú komoru s meraním FBG wavelength cez PeakLogger. Je navrhnutý ako samostatná kalibračná cesta; bežné profily naďalej používajú existujúci `ProfileRunner` bez závislosti na PeakLoggeri.

## Princíp

1. Operátor vyberie profil a komoru.
2. PeakLogger sa pripojí a načíta senzory a ich peaky.
3. Stabilná identita peaku je `SerialNumber + Channel + PeakId`; aktuálna wavelength sa nikdy nepoužíva ako identifikátor.
4. Operátor checkboxom označí iba peaky určené na kalibráciu.
5. Hold segmenty profilu sa označia ako kalibračné plata.
6. Po skončení minimálneho času plata aplikácia najskôr overí stabilitu teploty komory.
7. Následne vyhodnocuje všetky označené peaky **paralelne** z rovnakých PeakLogger batchov.
8. Každý peak má vlastný rolling buffer, vlastný čas stabilizácie a môže mať vlastný timeout.
9. Default je 50 stabilných vzoriek. Stabilita sa určuje pomocou range, štandardnej odchýlky a lineárneho driftu.
10. Na ďalšie plato sa prejde až keď sú všetky vybrané peaky stabilné alebo ukončené nakonfigurovanou error policy.

## Temperature-response validation

Prvý dokončený kalibračný bod je baseline. Pri ďalšom bode s dostatočným `ΔT` sa pre každý stabilný peak vyhodnotí `Δλ` a voliteľne smer zmeny. Peak, ktorý nereaguje na teplotu podľa limitov, vytvorí `NO_TEMPERATURE_RESPONSE` warning.

Default politika je `PauseForOperator`. V pokročilých nastaveniach je možné povoliť explicitný override s textovým dôvodom. Override sa uloží do výsledku runu.

## Stabilita wavelength

`RollingStabilityDetector` drží iba posledných N hodnôt, takže pamäť nerastie s dĺžkou testu. Pre okno počíta:

- mean,
- median,
- min / max,
- range,
- standard deviation,
- lineárny slope / drift za minútu,
- trvanie okna.

Interné raw wavelength zostávajú v nm. Range, stddev a drift sa pri vyhodnotení wavelength zobrazujú v pm / pm/min.

Hodnota limitu `0` vypne príslušné kritérium. Produkčné limity musia byť nastavené podľa internej kalibračnej špecifikácie; defaulty v aplikácii nie sú náhradou metrologického predpisu.

## Stabilita komory

Pred vytvorením finálneho 50-sample okna musí byť komora stabilná:

- v tolerancii od target temperature,
- minimálne definovaný čas,
- pod povoleným temperature driftom.

Nominal hold duration profilu je pri kalibrácii **minimálny čas plata**. Ak sa senzory po jeho skončení ešte nestabilizovali, aplikácia drží ten istý setpoint ďalej.

## Dáta

Kalibračné dáta sa ukladajú pod:

`Dokumenty\Lab Control\Calibration`

Štruktúra:

- `Setups\<ProfileId>.json` – zapojenie a nastavenia profilu,
- `Runs\<RunId>\summary.json` – auditovateľný výsledok,
- `Runs\<RunId>\summary.csv` – tabuľkový export,
- `Runs\<RunId>\raw-samples.csv` – priebežne zapisované raw dáta,
- `Checkpoints\<ChamberId>.json` – checkpoint posledného dokončeného plata.

Raw CSV obsahuje RunId, ProfileId, plato, target/actual/reference temperature, timestamp, SN, channel, PeakId, PeakIndex, wavelength a intensity, ak ju PeakLogger poskytne.

## PeakLogger API

Repository zatiaľ neobsahuje oficiálny PeakLogger API kontrakt. Preto modul obsahuje:

- `IPeakLoggerClient` – transport-neutrálne rozhranie,
- `FakePeakLoggerClient` – simulátor pre vývoj a testy,
- `PeakLoggerApiClient` – zámerne nedokončený produkčný adapter, ktorý **nevymýšľa** neexistujúce endpointy.

Na dokončenie reálneho adaptera treba dodať:

- API dokumentáciu / verziu PeakLoggera,
- host/port alebo base URL,
- autentifikáciu,
- sensor discovery request/response,
- measurement request/response alebo streaming protokol,
- presný field pre Serial Number,
- stabilný PeakId/PeakIndex,
- wavelength field a jednotku,
- intensity field a jednotku, ak existuje,
- reálnu update/sample frekvenciu,
- error/reconnect semantics.

## Simulátor

`FakePeakLoggerClient` má scenáre:

- `Normal`,
- `OneNonResponsivePeak`,
- `OneNoisySlowPeak`,
- `OneNeverStablePeak`,
- `DisconnectAfterSamples`,
- `PeakDisappears`.

Simulátor obsahuje aj SN s desiatimi peakmi, aby sa overilo, že používateľ môže z jedného senzora kalibrovať iba konkrétny peak.

## SQL metadata

Produkt, zákazník a zákazka sú momentálne editovateľné ručne. `IProductionMetadataProvider` je pripravený ako seam pre neskoršie SQL napojenie. Konkrétny SQL server, query ani schéma sa v Core nepredpokladajú.

## UI

Na domovskej obrazovke je tlačidlo **FBG kalibrácia**. Otvorí samostatný operator workspace s kartami:

- Zapojenie,
- Kalibračné plata,
- Live monitor,
- Nastavenia stability,
- História.

Live monitor ukazuje aktuálne plato, target/actual temperature, počet stabilných wavelengthov a pre každý peak current wavelength, počet samples, stddev, drift, elapsed time, timeout a stav.

## Bezpečnosť

Kalibrácia používa existujúce `IChamberDevice` implementácie a nevypína chamber alarmy ani watchdog. Bežný `ProfileRunner` nebol zmenený, takže normálne profily zostávajú oddelené od PeakLogger logiky.

Pred ostrým nasadením treba overiť správanie na bezpečných setpointoch a zabrániť súbežnému ovládaniu tej istej komory z normálneho profilu a kalibračného okna.

## Aktuálne obmedzenia

- Reálny `PeakLoggerApiClient` čaká na vendor API kontrakt.
- F100 je v Core pripravený ako voliteľný reference-temperature callback, ale samostatné kalibračné okno ho zatiaľ neprepája s existujúcim `ThermometersViewModel`.
- Checkpoint sa ukladá po dokončených platach; plný operator-guided resume workflow kalibračného runu ešte treba dopojiť do UI.
- SQL provider je iba interface; konkrétne DB mapovanie sa doplní po dodaní schémy.
