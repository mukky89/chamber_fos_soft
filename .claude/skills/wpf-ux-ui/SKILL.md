---
name: wpf-ux-ui
description: "UX/UI design system and conventions for the VotschVc3 chamber-control WPF app. USE FOR: any change to XAML views, styles, colors, layout, new screens/cards/dialogs, charts, or UX copy in this repository. Ensures new UI matches the existing dark theme, Slovak-language conventions and lab-operator ergonomics. DO NOT USE FOR: core protocol/business logic with no UI surface."
---

# VotschVc3 — UX/UI dizajn systém

Aplikácia riadi klimatické komory a pece v laboratóriu. Používatelia sú operátori,
často v rukaviciach, pri stole vzdialenom od monitora. Každá obrazovka musí byť
čitateľná z diaľky, kritické akcie jednoznačné a nebezpečné akcie vizuálne odlíšené.

## 1. Farebné tokeny (Themes/Styles.xaml) — NIKDY nehardcoduj farby vo views

| Token | Hodnota | Použitie |
|---|---|---|
| `BackgroundBrush` | `#181A26` | pozadie okna |
| `SurfaceBrush` | `#22243A` | karty (`Card`) |
| `SurfaceAltBrush` | `#2B2E48` | vnorené panely, inputy, položky zoznamov |
| `BorderBrush` | `#3A3D5C` | rámiky, separátory |
| `AccentBrush` | `#5B8DEF` | primárne akcie, focus |
| `AccentHoverBrush` | `#6F9CF2` | hover primárnych akcií |
| `DangerBrush` | `#E2555B` | stop/zmazať/alarmy |
| `TextBrush` | `#E6E8F2` | hlavný text |
| `MutedBrush` | `#969BB5` | sekundárny text, popisky |
| `OkBrush` | `#4FC17A` | beh/OK, ▶ play, referenčný teplomer |
| `WarnBrush` | `#FFB454` | upozornenie/pauza ⏸, zobrazenie setpointu |
| `ErrorBrush` | `#E5646E` | chybové hodnoty, ⏹ stop ikonka |

Farby kriviek grafov v C# kóde (cez `Freeze(r,g,b)` helper, vždy frozen):
teplota `#FF8A5C` (setpoint `#FFC2A8` čiarkovane), vlhkosť `#4FB6FF`
(setpoint `#A9DCFF`). Jediná povolená výnimka hex vo views: dekoratívne
gradienty LoginView a ilustrácie (`ChamberGraphic`, `PolEkoGraphic`).

## 2. Komponentové štýly — použi existujúce, nevymýšľaj nové

- `Card` (Border) — každý logický blok obsahu; margin 6, padding 14, radius 10.
- `Heading` — nadpis sekcie v karte (15 px semibold).
- `Label` — popisok NAD vstupom (12 px muted, margin dole 3).
- `Caption` — sekundárne info/hint pod prvkom (12 px muted).
- `Metric` — veľká živá hodnota (34 px light) + jednotka malým vedľa, `Baseline`.
- `AccentButton` — primárna akcia (max 1–2 na kartu: Pripojiť, Spustiť, Uložiť).
- `DangerButton` — deštruktívne/stop akcie (Stop, Zastaviť, Zmazať, Odpojiť).
- `GhostButton` — sekundárne/navigačné akcie a ikonky (▶ ⏸ ⏹ ↻ ◀ ▶ ✕).
- `DataGridEditTextBox` — `EditingElementStyle` pre KAŽDÝ `DataGridTextColumn`
  (inak má bunka čierny neviditeľný kurzor na tmavom podklade).
- Kurzor v textboxoch je biely (`CaretBrush=White`) — zachovaj v nových šablónach.

## 3. Konvencie rozloženia

- Obrazovka: `Grid` s riadkami `Auto` (hlavička s „← Domov"), `*` (obsah),
  `Auto` (stavový riadok `StatusMessage` v karte dole).
- Formuláre: `StackPanel` s `FieldStack`/`QpField` (margin dole 8–10);
  dvojice polí cez `Grid` so stĺpcami `* | 12 | *`.
- Hodnoty vedľa seba: `WrapPanel` (nikdy pevný horizontálny StackPanel —
  musí sa zalamovať pri užšom okne, viď oprava vlhkosti v 1.6.11).
- Dlhé texty: `TextWrapping="Wrap"`; v zoznamoch NIKDY neorezávať názvy profilov.
- Široký obsah (grafy, tabuľky): vlastný `ScrollViewer`, nie horizontálny scroll okna.
- Per-zariadenie UI: viditeľnosť riadiť `SupportsHumidity` / `IsPolEko`
  s `BoolToVisibility` (+ `ConverterParameter=Invert`), nie duplicitné views.

## 4. UX pravidlá pre laboratórny softvér

1. **Nebezpečné akcie** (stop komory, zmazanie profilu, odpojenie) = `DangerButton`,
   nikdy vedľa seba tesne s primárnou akciou bez medzery.
2. **Živé hodnoty** (teplota, vlhkosť) = `Metric` štýl, čitateľné z 2 m; setpoint
   menším písmom pod/vedľa s jasným označením „Setpoint".
3. **Stavy vždy viditeľné**: pripojenie (guľôčka + text), beh (zelená guľôčka +
   `ActivityLabel`), alarm (červený badge `DangerBrush` s bielym textom).
4. **Priebeh testu**: progress bar + text „cyklus X/Y · segment A/B · °C" +
   časy „Spustené HH:mm:ss · koniec ~ HH:mm:ss".
5. **Dvojklik = načítať** v zoznamoch profilov (`MouseBinding LeftDoubleClick`);
   vždy doplň hint „dvojklik = načítať" ako `Caption` a ToolTip.
6. **Tooltips po slovensky** na všetko, čo nie je samozrejmé (registre, adresy,
   formáty). UI texty sú po slovensky s diakritikou; v C# literáloch používaj
   slovenské úvodzovky „…" (pozor na balans ASCII úvodzoviek v reťazcoch).
7. **Chyby neblokujú**: chybové stavy do `StatusMessage`/`Status` (stavový riadok),
   `MessageBox` len pri strate dát. Appka nesmie spadnúť kvôli I/O — try/catch
   s hláškou v stavovom riadku.
8. **Potvrdenie akcie textom**: po akcii vždy nastav `StatusMessage`
   („Profil X uložený", „Setpoint zapísaný: …"), operátor potrebuje spätnú väzbu.
9. **Disabled ≠ skryté**: akcie nedostupné kvôli roli/stavu nechaj viditeľné
   ale disabled (CanExecute), aby operátor videl, že existujú. Funkcie
   nedostupné kvôli typu zariadenia (vlhkosť na peci) skry úplne.
10. **Grafy**: `ChartView`/`ProfileEditorChart`; teplota vždy oranžová, vlhkosť
    modrá, setpointy čiarkovane, „teraz" marker čiarkovaný zvislý; jednotka
    v `Unit`, prázdny stav cez `EmptyText` (nikdy prázdna biela plocha).

## 5. Ergonomika ovládania

- Klikacie ciele ≥ 28 px výšky (operátori v rukaviciach); ikonkové tlačidlá
  `Padding="9,3"` a `FontSize=14+`.
- Play/Pause/Stop ikonky: ▶ `OkBrush`, ⏸ `WarnBrush`, ⏹ `ErrorBrush` —
  konzistentne v celej appke.
- Číselné vstupy: `UpdateSourceTrigger=LostFocus` (nechceš prepočty počas
  písania „-" alebo „1"); textové: `PropertyChanged`.
- Enter v terminálovom vstupe odošle príkaz (KeyDown handler).

## 6. Checklist pre novú obrazovku/kartu

- [ ] Používa len tokeny z Styles.xaml (žiadne hex farby vo view)
- [ ] Hlavička s „← Domov" (ak celá obrazovka) + `Heading` v kartách
- [ ] Stavový riadok/`Caption` so spätnou väzbou akcií
- [ ] Texty po slovensky + tooltips, zalamovanie dlhých textov
- [ ] Per-zariadenie viditeľnosť (`SupportsHumidity`/`IsPolEko`)
- [ ] Nebezpečné akcie `DangerButton`, primárna max 1–2× `AccentButton`
- [ ] DataGrid textové stĺpce majú `EditingElementStyle={StaticResource DataGridEditTextBox}`
- [ ] XML validita XAML overená; nové commandy pridané do `RefreshCommands()`
- [ ] **Skontrolované build-lámajúce XAML pasce** (viď `wpf/references/anti-patterns.md`):
  žiadny `Setter TargetName` na transform/Freezable (MC4111), žiadny `Setter.Value="{Binding}"`,
  lokálne hodnoty vs. trigger Settery, `BasedOn` na odvodených štýloch.
- [ ] **Počkať na zelené Windows CI pred mergom** – WPF sa kompiluje len na Windows,
  toto prostredie build chyby nezachytí. Nemergovať PR bez úspešného buildu.
