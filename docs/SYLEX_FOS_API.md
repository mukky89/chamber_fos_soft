# Sylex FOS API – integrácia pre FBG kalibráciu

`chamber_fos_soft` používa centrálnu službu **Sylex FOS API** na načítanie produkčných údajov k FBG senzoru. Aplikácia sa nepripája priamo na ISYS ani DBFOS.

## Tok dát

```text
FBG Calibration window
        |
        | production FBG SN (XXXXXX/XXXX)
        v
SylexFosCalibrationIntegration
        |
        v
SylexFosApiProductionMetadataProvider
        |
        v
Sylex FOS API
GET /api/v1/calibrations/fbg/context?serialNumber=XXXXXX%2FXXXX
        |
        v
ISYS / neskôr DBFOS
```

Aktuálna verzia automaticky dopĺňa:

- `ProductDescription`
- `Customer`
- `Order` iba vtedy, keď ho centrálna API vráti

`Order` zostáva zatiaľ ručne editovateľný, pretože centrálny `OrderNumber` mapping ešte nemusí byť overený. Keď sa mapping doplní na strane API, Chamber app nebude potrebovať meniť endpoint ani klientsky kontrakt.

## Správanie pri kalibrácii

Po zadaní alebo naskenovaní produkčného FBG SN do kalibračného riadku:

1. aplikácia počká krátky debounce interval 250 ms;
2. zavolá centrálnu API;
3. overí, že operátor medzitým SN nezmenil;
4. doplní dostupné produkčné metadata;
5. existujúce polia zostávajú editovateľné.

Výpadok API **nesmie zastaviť kalibráciu**. Pri chybe sa zapíše warning do AppLog a operátor môže metadata vyplniť ručne.

## Konfigurácia na firemnom notebooku

Ak Sylex FOS API beží na rovnakom notebooku na porte 5080, URL netreba nastavovať. Default je:

```text
http://localhost:5080
```

Ak API beží inde, nastav Windows environment variable:

```powershell
[Environment]::SetEnvironmentVariable(
    "SYLEX_FOS_API_URL",
    "http://NAZOV-SERVERA:5080",
    "Machine"
)
```

API key sa **neukladá do repozitára ani do calibration JSON súborov**. Nastav ho ako environment variable:

```powershell
[Environment]::SetEnvironmentVariable(
    "SYLEX_FOS_API_KEY",
    "<RAW_API_KEY_PRE_CHAMBER_FOS>",
    "Machine"
)
```

Po nastavení Machine environment variables reštartuj Chamber aplikáciu. Ak bola otvorená cez Visual Studio, reštartuj aj Visual Studio, aby nový proces zdedil nové hodnoty.

## API client na centrálnej API

Odporúčaný client id:

```text
chamber-fos
```

Minimálny scope pre aktuálnu integráciu:

```text
calibrations.read
```

Na API serveri vytvor kľúč napríklad cez:

```powershell
./scripts/New-ApiKey.ps1 -ClientId chamber-fos -Scopes calibrations.read
```

Raw key patrí iba do `SYLEX_FOS_API_KEY` na počítači, kde beží Chamber aplikácia. API server uchováva iba hash.

## Health check

Health check nevyžaduje API key:

```text
GET /health
```

`SylexFosCalibrationIntegration` ho vykoná pri otvorení calibration workspace a výsledok zapíše do AppLog.

## Stabilný calibration endpoint

```http
GET /api/v1/calibrations/fbg/context?serialNumber=123456%2F0001
X-API-Key: <secret>
```

Príklad odpovede:

```json
{
  "serialNumber": "123456/0001",
  "productId": "123456",
  "productDescription": "FBG temperature sensor",
  "customer": "Customer A",
  "customerCode": "CUST-A",
  "orderNumber": null,
  "source": "ISYS product lookup",
  "retrievedAtUtc": "2026-08-30T00:00:00Z"
}
```

Sériové číslo je query parameter zámerne. Produkčné SN obsahuje `/`, preto sa nepoužíva ako route segment; riešenie tak zostáva spoľahlivé aj za IIS/reverse proxy.

## Bezpečnostné pravidlá

- Chamber aplikácia nikdy neotvára SQL connection na ISYS/DBFOS.
- Raw API key sa necommitne do GitHubu.
- API key sa neposiela do logu.
- Integrácia je read-only.
- API je určené iba pre firemnú LAN/VPN.
- Chyba enrichmentu nesmie zmeniť setpoint, profil ani bezpečnostnú logiku komory.
