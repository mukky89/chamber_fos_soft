#!/usr/bin/env bash
set -euo pipefail

# Bezpečná aktualizácia desktopovej aplikácie z GitHub main pre Windows Git Bash.
# Kalibráciu najprv ukončite cez "Stop a uložiť" a aplikáciu zatvorte.

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
publish_dir="$repo_dir/publish/VotschVc3.App"
start_app=true

case "${1:-}" in
  "") ;;
  --no-start) start_app=false ;;
  --help|-h)
    printf '%s\n' \
      'Použitie: bash update.sh [--no-start]' \
      '  --no-start  Aktualizuje a zostaví aplikáciu, ale nespustí ju.'
    exit 0
    ;;
  *)
    printf 'Neznámy parameter: %s\n' "$1" >&2
    exit 2
    ;;
esac

if tasklist.exe 2>/dev/null | tr -d '\r' | grep -Fqi 'VotschVc3.App.exe'; then
  printf '%s\n' \
    'CHYBA: VotschVc3.App stále beží.' \
    'V kalibrácii kliknite "Stop a uložiť", potvrďte ukončenie a aplikáciu zavrite.' >&2
  exit 1
fi

for command_name in git dotnet powershell.exe; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'CHYBA: Chýba príkaz %s.\n' "$command_name" >&2
    exit 1
  fi
done

cd "$repo_dir"

if [[ "$(git branch --show-current)" != "main" ]]; then
  printf '%s\n' 'CHYBA: Aktualizáciu spustite z vetvy main.' >&2
  exit 1
fi

if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
  printf '%s\n' \
    'CHYBA: Repozitár obsahuje neuložené zmeny.' \
    'Aktualizácia ich nebude prepisovať. Najprv ich commitnite alebo odložte.' >&2
  exit 1
fi

printf '%s\n' 'Sťahujem najnovšiu overenú verziu z origin/main...'
git fetch origin main
git merge --ff-only origin/main

printf '%s\n' 'Zostavujem Windows aplikáciu...'
dotnet publish src/VotschVc3.App/VotschVc3.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained false \
  -p:PublishSingleFile=true \
  -p:IncludeAllContentForSelfExtract=true \
  -p:DebugType=none \
  -o "$publish_dir"

app_exe="$publish_dir/VotschVc3.App.exe"
if [[ ! -f "$app_exe" ]]; then
  printf 'CHYBA: Zostavená aplikácia sa nenašla: %s\n' "$app_exe" >&2
  exit 1
fi

printf 'Aktualizácia je pripravená: %s\n' "$app_exe"

if [[ "$start_app" == true ]]; then
  app_windows="$(cygpath -w "$app_exe")"
  powershell.exe -NoProfile -Command "Start-Process -FilePath '$app_windows'"
  printf '%s\n' 'Aplikácia bola spustená. V FBG kalibrácii zvoľte "Pokračovať od plata č. X".'
fi
