# PeakLogger – FBG teplotná kalibrácia

Tento modul synchronizuje klimatickú komoru s meraním FBG wavelength cez PeakLogger. Je navrhnutý ako samostatná kalibračná cesta; bežné profily naďalej používajú existujúci `ProfileRunner` bez závislosti na PeakLoggeri.

## Princíp

1. Operátor vyberie profil a komoru.
2. PeakLogger sa pripojí a načíta aktuálne peaky z lokálneho REST API.
3. PeakLogger-side identita je `device.deviceSN + Channel + PeakId`. Wavelength sa nepoužíva ako identifikátor, pretože sa s teplotou mení.
4. Produkčné sériové číslo FBG senzora je samostatný údaj. PeakLogger API ho neposkytuje; rovnako ako v pôvodnom `Auto_calibrator_Pali` sa páruje na kanál/peak operátorom alebo neskôr cez databázový provider.
5. Operátor checkboxom označí iba peaky určené na kalibráciu.
6. Hold segmenty profilu sa označia ako kalibračné plata.
7. Po skončení minimálneho času plata aplikácia najskôr overí stabilitu teploty komory.
8. Následne vyhodnocuje všetky označené peaky **paralelne** z rovnakých PeakLogger batchov.
9. Každý peak má vlastný rolling buffer, vlastný čas stabilizácie a môže mať vlastný timeout.
10. Default je 50 stabilných vzoriek. Stabilita sa určuje pomocou range, štandardnej odchýlky a lineárneho driftu.
11. Na ďalšie plato sa prejde až keď sú všetky vybrané peaky stabilné alebo ukončené nakonfigurovanou error policy.

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

Raw aj summary export uchovávajú **dve rôzne sériové čísla**:

- `SensorSerialNumber` – produkčné SN FBG senzora,
- `PeakLoggerDeviceSN` – `device.deviceSN` z PeakLogger API, teda SN interrogátora/PeakLogger zariadenia.

Raw CSV ďalej obsahuje RunId, ProfileId, plato, target/actual/reference temperature, timestamp, channel, PeakId, PeakIndex, wavelength a intensity.

## PeakLogger API – kontrakt z existujúceho repozitára

Reálny API kontrakt je už použitý v repozitári `Auto_calibrator_Pali`, najmä v `definitions.py`, `SensTemp/ThreadWL.py` a fixture `SensTemp/testy/test4.py`. `PeakLoggerApiClient` používa rovnaký kontrakt; endpointy nie sú odhadované.

### Adresa a endpointy

Default:

`http://localhost:43122`

Používané requesty:

- `GET /swagger/index.html` – availability check,
- `GET /peaks?` – všetky aktuálne detegované peaky,
- pôvodná aplikácia pozná aj `GET /peaks?channel=<channel>&enableFos4x=false` pre konkrétny kanál.

V existujúcej implementácii nie je pre tieto requesty použitá autentifikácia.

Pôvodný `ThreadWL.py` obnovuje všetky wavelength približne každých **500 ms**. Kalibračný modul môže používať vlastný polling interval podľa požadovaného sample rate; samotný endpoint vždy vracia aktuálny snapshot.

### Response `/peaks?`

Fixture v repozitári obsahuje pre každý peak tieto polia:

- `index`,
- `channel`,
- `wavelength`,
- `cog`,
- `intensity`,
- `returnLoss`,
- `slsr`,
- `width`,
- `asymmetry`,
- `device.deviceType`,
- `device.deviceSN`,
- `device.connector`,
- `fos4x`.

Pre kalibráciu sa aktuálne používajú `index`, `channel`, `wavelength`, `intensity` a `device.deviceSN`. `PeakId` sa vytvorí deterministicky ako `P<index>`.

PeakLogger response neobsahuje timestamp merania, preto adapter uloží timestamp prijatia snapshotu (`DateTimeOffset.UtcNow`). HTTP 404 na `/peaks` sa spracuje rovnako ako v pôvodnej Python implementácii – ako prázdna sada peakov.

### Dôležité: `deviceSN` nie je SN FBG senzora

Vo fixture je napríklad:

`deviceSN = HIAER3`, `deviceType = Hyperion`.

Ide teda o SN interrogátora/zariadenia. Pôvodný `Auto_calibrator_Pali` číta/skenerom získava produkčné SN senzora samostatne a následne ho páruje ku kanálu a vybraným wavelengthom. Nový dátový model preto drží:

- `CalibrationSensorMapping.SerialNumber` = produkčné FBG SN,
- `CalibrationSensorMapping.PeakLoggerDeviceSerialNumber` = PeakLogger `device.deviceSN`,
- `SourceIdentity` = PeakLoggerDeviceSN + Channel + PeakId pre live matching,
- `Identity` = SensorSerialNumber + Channel + PeakId pre výsledky a históriu.

Pre staršie uložené setupy bez `PeakLoggerDeviceSerialNumber` zostáva fallback na pôvodné `SerialNumber`, aby sa existujúce simulátorové/setup dáta dali načítať.

## Simulátor

`FakePeakLoggerClient` má scenáre:

- `Normal`,
- `OneNonResponsivePeak`,
- `OneNoisySlowPeak`,
- `OneNeverStablePeak`,
- `DisconnectAfterSamples`,
- `PeakDisappears`.

Simulátor obsahuje aj SN s desiatimi peakmi, aby sa overilo, že používateľ môže z jedného zdroja kalibrovať iba konkrétny peak.

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

- Reálny `PeakLoggerApiClient` je implementovaný podľa kontraktu, ktorý už používa `Auto_calibrator_Pali`.
- Produkčné FBG SN PeakLogger API neposkytuje; musí sa spárovať operátorom/skenerom alebo neskôr z databázy, rovnako ako v pôvodnom kalibrátore.
- F100 je v Core pripravený ako voliteľný reference-temperature callback, ale samostatné kalibračné okno ho zatiaľ neprepája s existujúcim `ThermometersViewModel`.
- Checkpoint sa ukladá po dokončených platach; plný operator-guided resume workflow kalibračného runu ešte treba dopojiť do UI.
- SQL provider je iba interface; konkrétne DB mapovanie sa doplní po dodaní schémy.
