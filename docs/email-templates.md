# Jednotné e-mailové šablóny Lab Control

Všetky existujúce typy správ používajú spoločný vizuálny štýl: tmavú hlavičku, farebný stav, dôležitú správu nad detailmi, čitateľné riadky údajov a jednotnú pätičku.

Pokryté správy:
- testovací e-mail,
- alarm komory,
- rozdiel teploty WIKA a komory,
- upozornenie FBG kalibrácie,
- dokončená FBG kalibrácia (aj s upozorneniami),
- dokončený profil s grafom a dostupným CSV, vrátane nepotvrdeného vypnutia komory.

`LabControlEmailTemplate` poskytuje spoločný HTML obal. `ProfileCompletionEmail` doň vkladá iba sekciu grafu a súborov. Nepotvrdené vypnutie komory je označené ako upozornenie; názov profilu toto označenie nemení. Správa o chýbajúcom CSV zodpovedá skutočnému zoznamu príloh. Graf používa invariantné súradnice aj pri slovenskej kultúre.

Rozloženie používa tabuľky, inline štýly, fixný obal pre Outlook a voliteľné mobilné úpravy spacingu. Nevyžaduje vzdialené obrázky, fonty ani skripty. Dynamický obsah je HTML-encoded. Prílohy, textová verzia, príjemcovia, konfigurácia a transport odosielania zostávajú zachované.

Overenie: build riešenia bez chýb; 30 e-mailových testov prešlo. Vygenerovaných 8 HTML náhľadov so syntetickými údajmi. Desktopový náhľad skontrolovaný; mobilný viewport a skutočné poštové klienty neboli potvrdené. SVG/CID graf môže byť v niektorých klientoch skrytý, preto obsahuje alternatívny text a odkaz na údaje v aplikácii. Testovacie e-maily neboli odoslané.
