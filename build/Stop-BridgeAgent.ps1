<#
.SYNOPSIS
    Ukonci beziaci Bridge Agent, ktory drzi zamknute subory v build vystupe.

.DESCRIPTION
    Bridge Agent (VotschVc3.Agent.exe) bezi ako samostatny proces na pozadi
    (naplanovana uloha, alebo ho spustila desktopova aplikacia). Ked bezi priamo
    z build vystupu, MSBuild nedokaze prepisat VotschVc3.Agent.exe / .dll a build
    zlyha na "The process cannot access the file ... because it is being used by
    another process".

    Skript ukonci IBA procesy spustene presne zo zadanych ciest, takze agenta
    nainstalovaneho inde (produkcna instalacia, iny pracovny priecinok) necha
    bezat. Nikdy nekonci build - kazde zlyhanie iba ohlasi.

.PARAMETER Path
    Jedna alebo viac ciest k VotschVc3.Agent.exe. Relativne cesty sa vyhodnocuju
    voci aktualnemu priecinku (MSBuild ho nastavuje na priecinok projektu).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]] $Path
)

$ErrorActionPreference = 'Stop'

$targets = @()
foreach ($candidate in $Path) {
    if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
    $targets += [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine((Get-Location).ProviderPath, $candidate))
}
if ($targets.Count -eq 0) { exit 0 }

# Win32_Process nesie ExecutablePath aj pre procesy ineho pouzivatela; Process.MainModule
# by pri agentovi spustenom naplanovanou ulohou vyhodil vynimku. Ak WMI nie je dostupne,
# skus aspon vlastne procesy.
$candidates = @()
try {
    $candidates = @(Get-CimInstance -ClassName Win32_Process `
            -Filter "Name = 'VotschVc3.Agent.exe'" -ErrorAction Stop |
        ForEach-Object { [PSCustomObject]@{ Id = [int]$_.ProcessId; Image = $_.ExecutablePath } })
}
catch {
    $candidates = @(Get-Process -Name 'VotschVc3.Agent' -ErrorAction SilentlyContinue |
        ForEach-Object {
            $image = $null
            try { $image = $_.MainModule.FileName } catch { }
            [PSCustomObject]@{ Id = $_.Id; Image = $image }
        })
}

$stoppedIds = @()
foreach ($candidate in $candidates) {
    if ([string]::IsNullOrWhiteSpace($candidate.Image)) { continue }
    if ($targets -notcontains $candidate.Image) { continue }

    try {
        Write-Host "Ukoncujem Bridge Agent (PID $($candidate.Id)), ktory zamyka $($candidate.Image)"
        Stop-Process -Id $candidate.Id -Force -ErrorAction Stop
        $stoppedIds += $candidate.Id
    }
    catch {
        Write-Warning "Bridge Agent (PID $($candidate.Id)) sa nepodarilo ukoncit: $($_.Exception.Message)"
    }
}

# Zamok na subore sa uvolni az ked Windows proces skutocne zrusi.
foreach ($id in $stoppedIds) {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($null -eq (Get-Process -Id $id -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 250
    }
}

exit 0
