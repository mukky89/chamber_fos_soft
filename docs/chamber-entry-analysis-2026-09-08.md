# Ustálenie komory pred WIKA — analýza 8. 9. 2026

## Dáta a metóda

Analyzovaných bolo 92 uložených plat zo 4 behov a 7 520 jedinečných teplotných záznamov. Zdrojom sú lokálne summary.json a wavelength-trace.csv. Riadky jednotlivých peakov sa deduplikovali podľa timestampu; použili sa iba časové intervaly uložených plat. Rampy pred platami nie sú započítané.

Dáta identifikujú komoru 1, WIKA COM7 a interrogátor SIACCT; komoru 2, WIKA COM4 a HIAER3. Výsledky nepredstavujú fyzické overenie novej implementácie. Staršie odbery sú približne 30-sekundové, preto neukazujú krátke oscilácie medzi vzorkami. Nedokončené behy bez uložených výsledkov plat sa nedali zahrnúť do rovnakého porovnania. Limity preto treba potvrdiť na ďalších reálnych behoch.

Hľadalo sa prvé úplné okno v tolerancii, s rozsahom max–min ≤ 0,5 °C a absolútnym lineárnym regresným driftom pod limitom. Medzera nad 90 s resetuje okno.

| Tolerancia | Drift [°C/min] | Okno [s] | Splnené | Medián od začiatku plata [min] | Maximum [min] |
|---|---|---|---|---|---|
| ±1,0 °C | 0,1 | 120 | 92/92 | 11,725 | 19,16 |
| **±0,5 °C** | **0,1** | **120** | **92/92** | **12,365** | **23,84** |
| ±0,5 °C | 0,05 | 180 | 92/92 | 13,405 | 25,35 |

Odporúčanie je prostredný variant. Užšie pásmo oproti ±1 °C obmedzí príliš skorý prechod pri nábehu cez cieľ. Tretí variant predlžuje typické čakanie o približne minútu; dostupné dáta nedokazujú zlepšenie finálnej presnosti.

| Beh / prefix ID | Plata | Komora | Medián [min] | Minimum–maximum [min] |
|---|---|---|---|---|
| 01-2026-09-07 / 01573baf | 9 | 1 / COM7 | 12,58 | 5,86–13,63 |
| 3aff380e | 12 | 2 / COM4 | 11,99 | 2,24–19,56 |
| 779a9148 | 35 | 1 / COM7 | 12,21 | 2,53–23,84 |
| d51d9aae | 36 | 1 / COM7 | 12,405 | 2,53–21,81 |

## Implementácia a nastavenia

- Voliteľná jednorazová vstupná brána komory na každom plate pred WIKA: tolerancia ±0,5 °C, okno 120 s, rozsah ≤0,5 °C, drift ≤0,1 °C/min.
- WIKA a wavelength trace sa stále čítajú a logujú. Pred vstupom sa okno stability WIKA resetuje. Až splnenie komory umožní začať nové okno WIKA.
- Po otvorení vstupnej brány rozhoduje existujúca priebežná kontrola WIKA. Bez externej WIKA zostáva doterajšia priama kontrola komory.
- Existujúci timeout teplotnej fázy platí pre obe fázy spolu. Zachované sú obmedzené predĺženia a operátorský dohľad.
- Administrácia aj nastavenie FBG obsahujú prepínač a všetky štyri číselné limity.
- Nové administrátorské predvoľby majú bránu zapnutú. Staršie zapojenia a checkpointy bez nastavenia sa nemenia automaticky; bránu možno zapnúť vo FBG nastavení alebo pri obnove výslovne aplikovať aktuálne predvoľby.
