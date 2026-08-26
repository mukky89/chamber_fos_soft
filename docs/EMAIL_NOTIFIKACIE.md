# Notifikácie e-mailom — nastavenie

Po dokončení profilu appka pošle HTML súhrn, graf teploty a voliteľne CSV log.
Odosiela sa cez **Brevo** — buď HTTP API (`BrevoApi`, odporúčané), alebo SMTP relay.

## Čo je povinné

| Spôsob | Povinné polia |
|---|---|
| **BrevoApi** (odporúčané) | adresáti, odosielateľ (from), endpoint URL, **API kľúč** |
| **Smtp** | adresáti, odosielateľ (from), SMTP host, port, používateľ, heslo |

V režime `BrevoApi` sa **SMTP používateľ a heslo nepoužívajú** — pokojne zostanú prázdne.

Panel v *Administrácii → Notifikácie e-mailom* píše priamo pod nastavením, čo ešte
chýba (napr. „⚠ Chýba: API kľúč"), takže to netreba zisťovať metódou „Poslať test".

## Kam dať kľúč, aby neprežil len do najbližšieho buildu

**Nedávaj ho do repozitára.** Sú dve miesta, ktoré prežijú nový build aj
preinštalovanie appky:

### 1. Premenné prostredia Windows (odporúčané)

Appka číta premenné vždy, keď je príslušné pole v nastaveniach **prázdne**.
Premenné sú vo Windows, nie v aplikácii — nový build, `dotnet publish` ani
skopírovanie nového `.exe` sa ich nedotkne. Navyše ich zdieľa aj FOS Dashboard,
takže sa nastavujú raz.

| Premenná | Pole v appke |
|---|---|
| `BREVO_API_KEY` | Brevo API kľúč |
| `EMAIL_SENDER` | Odosielateľ (from) |
| `SMTP_HOST` | SMTP host |
| `SMTP_PORT` | SMTP port |
| `SMTP_USER` | SMTP používateľ |
| `EMAIL_PASSWORD` | SMTP heslo (Brevo SMTP key) |

Nastavenie pre prihláseného používateľa (PowerShell alebo cmd):

```
setx BREVO_API_KEY   "xkeysib-...tvoj-kluc..."
setx EMAIL_SENDER    "no-reply@tvoja-domena.sk"
setx SMTP_HOST       "smtp-relay.brevo.com"
setx SMTP_PORT       "587"
setx SMTP_USER       "...@smtp-brevo.com"
setx EMAIL_PASSWORD  "...tvoj-smtp-key..."
```

Pre premenné platné pre všetkých používateľov a služby (napr. keď Bridge Agent beží
ako Scheduled Task pod iným účtom) pridaj `/M` a spusti príkazový riadok **ako správca**:

```
setx /M BREVO_API_KEY "xkeysib-..."
```

> **Pozor:** bežiaci proces si premenné načítal pri štarte. Po `setx` appku (a Bridge
> Agenta) **reštartuj**; ak sa zmena neprejaví, odhlás sa a prihlás znova.

Alebo klikacie: *Tento počítač → Vlastnosti → Rozšírené nastavenia systému →
Premenné prostredia*.

### 2. Zapísanie priamo do appky

*Administrácia → Notifikácie e-mailom* → vyplň polia → **Uložiť nastavenia**.
Uloží sa do `Dokumenty/VotschVc3/email.json`, ktorý je mimo repozitára aj mimo
výstupu buildu, takže nový build ho tiež neprepíše. Vyplnené pole má vždy prednosť
pred premennou prostredia.

## Časté príčiny, prečo e-mail neodíde

- **Odosielateľ nie je overený v Brevo.** Adresa v poli „Odosielateľ (from)" musí byť
  v Brevo overený sender (alebo doména s overeným DKIM). S neoverenou adresou Brevo
  požiadavku odmietne aj so správnym kľúčom — to je najčastejšia príčina.
- **Vypnutý hlavný prepínač** notifikácií.
- **Prázdny zoznam adresátov.**
- Po zmene premennej prostredia **nebola appka reštartovaná**.

`APP_URL` (adresa FOS Dashboardu) používa dashboard, nie desktopová appka —
na odosielanie notifikácií nemá vplyv.
