# Autonómne vynechávanie bodov pri neistej identite FBG

## Správanie

Ochrana je automatická a nezávisí od prepínača operátorského dohľadu. Pracuje pred priemerovaním a pred zberom kalibračných vzoriek. Sleduje všetky detegované peaky v kanáloch obsahujúcich vybrané FBG, vrátane nevybraných susedných peakov. Počiatočné priradenie vychádza zo zapojenia overeného pred štartom.

Samostatná stopa zachováva pôvodné priradenie, interné ID fyzického FBG a aktuálny index API. Jednoznačné globálne priradenie v medziach pohybu umožní automatické prečíslovanie; zmena indexu neprepíše SN, kalibračný výber ani historický kľúč.

Zmena počtu, blízke peaky, viac platných priradení, prekročenie pohybových medzí, opakovaný čas zdroja alebo výpadok kontinuity vyradia celý konfliktný optický kanál. Dotknutý bod dostane stav `SkippedIdentityUncertain` a upozornenie `FBG_POINT_SKIPPED_IDENTITY`. Rozpracované finálne vzorky sa odstránia z výsledku; pôvodné detekcie zostanú v zázname.

Ostatné kanály dokončia bod. Keď sú všetky výsledky terminálne, runner pokračuje ďalším plánovaným bodom. Ak sú vyradené všetky kanály, bežný bod nečaká na WIKA stabilitu. Ochrana identity nevydáva STOP ani nemení teplotné setpointy. Plánovaný záverečný návrat na 25 °C zostáva zachovaný: aj pri vyradených FBG musí splniť teplotnú podmienku pred bežným záverečným STOP. Existujúce bezpečnostné pravidlá, ručné Stop a pravidlá poruchy WIKA sa týmto nemenia.

V tejto konzervatívnej verzii je neistota kanála trvalá do konca behu. Neskorší návrat dvoch peakov nie je dôkazom ich identity. Aj zvyšné body a záverečné overenie dotknutého kanála sa vynechajú. Pri obnovení checkpointu chýba dôkaz kontinuity počas vypnutia, preto sa zostávajúce body pôvodných kanálov vynechajú; dokončené body sa zachovajú. Nová kalibrácia vyžaduje nové úvodné overenie zapojenia. Neexistuje tichý reset po každom plate.

## Medze a dostupné údaje

Priradenie je jediné prípustné dokonalé párovanie v bipartitnom grafe. Alternatívne riešenie sa kontroluje odstránením každej použitej hrany. Pri viac možnostiach algoritmus abstinuje. Nepoužíva výsledné koeficienty kalibrácie na dokazovanie identity ani nepredstiera odhadnutú hodnotu ako meranie.

Predvolené technické medze (nie metrologicky validované záruky): základ 0,02 nm + 0,1 nm/min × skutočný čas od predchádzajúceho rámca; minimálny odstup 0,02 nm; maximálna medzera 120 s. Sú uložené v `CalibrationProfileSettings` a kopírujú sa do checkpointu. Nie sú odvodené z limitov stability ani z výrobnej orientačnej tolerancie ±0,5 nm. Na fyzickej zostave treba pred produkčnou kvalifikáciou overiť falošné vyradenia a prípustnú dynamiku. Zvýšenie medzí môže viesť k viacerým nejednoznačným priradeniam; nejde o automatické povolenie zámeny.

Čítanie `localhost:43124/api/v1/peaks` počas implementácie vrátilo 18 detekcií. Polia obsahovali index, channel, wavelength, cog, intensity, returnLoss, slsr, width, asymmetry, compensation, device a fos4x. V tejto odpovedi nebol čas merania ani sekvenčné číslo. Adaptér preto používa čas prijatia. Zamrznutý payload s novým časom prijatia sa nedá spoľahlivo odlíšiť od stabilného signálu. Opakované skutočné zdrojové časy vie ochrana odmietnuť, ak ich klient poskytne.

Medzi dvoma HTTP odbermi nemožno zaručiť odhalenie ľubovoľného rýchleho prekrytia, ktoré nezanechá rozlišovaciu informáciu. Ochrana nie je spektrálna dekompozícia. Limity treba overiť na záznamoch s nezávisle známou identitou; pre silnejšie záruky potrebujeme časovanie zdroja, údaje o detektore a celé spektrá. Počas rampy sa pozoruje pri krokoch runnera, pri čakaní na teplotu v každom cykle a pri odberoch v nastavenej kadencii.

## História a výsledky

- `peak-observations.jsonl`: pôvodné detekcie klienta, aj odmietnuté a nevybrané, pred prepriradením a priemerovaním; nejde o archív plného optického spektra.
- `PeakIdentityEvents` v súhrne behu a `FBG_IDENTITY_EVENT` v diagnostike: čas, zariadenie, kanál, interné ID FBG a starý/nový index alebo dôvod straty identity.
- `PeakIdentityChannels` v behu a checkpointe: stav kontinuity vrátane trvalého vyradenia.
- `SkippedIdentityUncertain`: nulové prijaté vzorky, explicitné vylúčenie z koeficientov aj z finálneho overenia. Platné predchádzajúce body zostávajú; neúplný rozsah je označený problémom.

## Návrat pôvodnej verzie

Pred zmenou bola vytvorená vetva `codex/before-autonomous-identity-20260911` na commite `191474f`. Návrat sa má vykonať revertnutím tejto funkčnej zmeny so zachovaním následných opráv, nie resetom celého repozitára alebo prepísaním histórie. Staré dáta ani zapojenie sa nemažú.

### Obnova po komunikácii (1.76.389)
Počas obnovy komory sa naďalej odoberajú optické pozorovania. Komunikačná medzera sama osebe umožňuje nové overenie, nie nové naučenie identity z aktuálneho poradia. Pre každý pôvodný peak sa použije rozsah ±(IdentityBaseToleranceNm + IdentityMaximumMotionNmPerMinute × celý čas od posledného prijatého pozorovania). Rozsahy všetkých peakov kanála musia byť navzájom oddelené vrátane IdentityMinimumSeparationNm. Nový rámec musí spĺňať pôvodné kontroly kvality a mať jediné úplné priradenie. Platnosť tohto dôkazu závisí od správne nastavenej fyzikálnej hornej medze pohybu; nejde o identifikáciu podľa SN zo spektra.
Prekrytie, zmena počtu, nejednoznačnosť a staré trvalo vyradené kanály sa automaticky neodblokujú. Obnova sa zapisuje do histórie. Už vynechaný bod sa tým spätne nevaliduje ani automaticky neopakuje; na jeho získanie treba nový merací priebeh. Čiastočné vzorky po komunikačnej chybe zahodí existujúci reset stabilizácie a odberu.
