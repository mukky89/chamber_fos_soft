# Návrat k pôvodnému párovaniu FBG

Pôvodný stav: commit `ed514a5` (1.76.372), vetva `codex/before-pairing-redesign-20260911`.
Táto vetva obsahuje celý pôvodný zdrojový kód a pôvodné rozloženie okna. Existujúce nastavenia aplikácie a zapojenia sa týmto vydaním nemažú ani nemigrujú.

Pre návrat po nasadení použite `git revert` na commit vydania 1.76.373 (nájdite ho cez `git log --oneline --grep="Redesign FBG pairing"`). Revert zachová históriu a neskoršie nesúvisiace zmeny. V návratovom vydaní zvýšte aktuálnu verziu a pridajte nový záznam do CHANGELOG.md; nevracajte číslo verzie späť. Potom Release build, testy, normalizácia riadkov, commit a push na main.

Nevykonávajte `reset --hard` ani force-push. Automatický výber T sa ukladá ako bežný výber peakov; návrat k starému UI nemení už uložené voľby kalibrácie. Tie ostávajú ručne upraviteľné.
