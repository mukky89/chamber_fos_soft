# Changelog — 1.76.6

Dátum: 2026-09-02

## USB / WIKA CTH7000
- Zjednotená synchronizácia `SerialPort` proti súbehu skenovania, čítania a zatvárania portu.
- Spoľahlivejší reconnect po dočasnom USB/COM výpadku; komunikácia skúsi bezpečné znovuotvorenie portu a druhý pokus.
- RX/TX buffer sa pri otvorení a reconnecte zahodí pred ďalšou komunikáciou.
- USB komunikácia zostáva plne asynchrónna z pohľadu UI; blokujúce operácie `SerialPort` bežia mimo UI threadu.
- TX/RX rámce WIKA CTH7000 sa zapisujú do aplikačného diagnostického logu s označením portu a pokusu.
- Existujúce automatické vyhľadávanie COM portov, zachovanie manuálneho `COMx`, A/B kanálov a pasívna detekcia staršieho ASL F100 zostávajú zachované.
- Zachovaná je kompatibilita názvu `F100Client`, ale používateľské a diagnostické texty používajú WIKA CTH7000.

## Bezpečnosť prihlásenia
- Changelog už nie je dostupný priamo z prihlasovacej obrazovky.
- Tým sa odstráni cesta, pri ktorej sa po otvorení changelogu dalo dostať do hlavnej aplikácie bez úspešného prihlásenia.
- Changelog je dostupný až po prihlásení z aplikácie.

## Verzia
- Desktop aplikácia zvýšená z `1.76.5` na `1.76.6`.
