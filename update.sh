#!/usr/bin/env bash
#
# Stiahne najnovšiu verziu aktuálneho branchu z GitHubu a nastaví lokálny
# repozitár presne na serverovú verziu. Určené pre workflow bez lokálnych zmien.
#
# POZOR: `git reset --hard` nenávratne zmaže akékoľvek lokálne úpravy.
# Použi len ak lokálne zmeny nerobíš.
#
# Použitie:
#   ./update.sh            # aktualizuje aktuálny branch
#   ./update.sh main       # prepne na 'main' a aktualizuje ho

set -euo pipefail

# Presuň sa do priečinka, kde leží tento skript (koreň repozitára).
cd "$(dirname "$0")"

# Voliteľný argument: názov branchu, na ktorý sa prepnúť.
target_branch="${1:-}"

echo "→ Sťahujem najnovšie z GitHubu…"
git fetch origin --prune

if [ -n "$target_branch" ]; then
    echo "→ Prepínam na branch '$target_branch'…"
    git checkout "$target_branch"
fi

branch="$(git rev-parse --abbrev-ref HEAD)"
echo "→ Nastavujem '$branch' na origin/$branch…"
git reset --hard "origin/$branch"

echo "✔ Hotovo. Aktuálna verzia:"
git log --oneline -1
