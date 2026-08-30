param(
    [string]$ConfigPath = "$env:USERPROFILE\Documents\Lab Control\bridge.json",
    [string]$StatusPath = "$env:USERPROFILE\Documents\Lab Control\bridge-status.json",
    [string]$TaskName = "Sylex Lab Control Bridge",
    [int]$MaxStatusAgeSeconds = 120,
    [switch]$SkipScheduledTaskCheck
)

$ErrorActionPreference = 'Stop'
$script:Failures = 0
$script:Warnings = 0

function Write-Check {
    param(
        [ValidateSet('PASS','WARN','FAIL')][string]$Level,
        [string]$Name,
        [string]$Detail
    )

    switch ($Level) {
        'PASS' { $prefix = '[PASS]' }
        'WARN' { $prefix = '[WARN]'; $script:Warnings++ }
        'FAIL' { $prefix = '[FAIL]'; $script:Failures++ }
    }
    Write-Host ("{0} {1} - {2}" -f $prefix, $Name, $Detail)
}

function Resolve-ExpandedPath {
    param([string]$Path)
    return [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Path))
}

function Test-HttpReachability {
    param([Uri]$Uri)

    try {
        $request = [System.Net.HttpWebRequest]::Create($Uri)
        $request.Method = 'HEAD'
        $request.AllowAutoRedirect = $true
        $request.Timeout = 10000
        $request.ReadWriteTimeout = 10000
        $request.UserAgent = 'Sylex-Lab-Bridge-Acceptance/2'
        $response = [System.Net.HttpWebResponse]$request.GetResponse()
        try {
            return @{ Reachable = $true; Status = [int]$response.StatusCode }
        }
        finally {
            $response.Dispose()
        }
    }
    catch [System.Net.WebException] {
        # 401/403/404 still proves DNS/TCP/TLS/HTTP reachability. Do not authenticate here,
        # because this script must never expose or replay the pairing token in console output.
        if ($_.Exception.Response) {
            $response = [System.Net.HttpWebResponse]$_.Exception.Response
            try {
                return @{ Reachable = $true; Status = [int]$response.StatusCode }
            }
            finally {
                $response.Dispose()
            }
        }
        return @{ Reachable = $false; Error = $_.Exception.Message }
    }
}

Write-Host '============================================================'
Write-Host ' SYLEX Lab Control Bridge v2 - acceptance pre-flight'
Write-Host '============================================================'
Write-Host ('Computer: {0}' -f $env:COMPUTERNAME)
Write-Host ('User:     {0}' -f $env:USERNAME)
Write-Host ''

$configFull = Resolve-ExpandedPath $ConfigPath
$statusFull = Resolve-ExpandedPath $StatusPath

$config = $null
if (Test-Path -LiteralPath $configFull -PathType Leaf) {
    Write-Check PASS 'bridge.json' $configFull
    try {
        $config = Get-Content -LiteralPath $configFull -Raw | ConvertFrom-Json
        Write-Check PASS 'bridge.json JSON' 'Konfigurácia sa dá načítať.'
    }
    catch {
        Write-Check FAIL 'bridge.json JSON' $_.Exception.Message
    }
}
else {
    Write-Check FAIL 'bridge.json' "Súbor neexistuje: $configFull"
}

$dashboardUri = $null
if ($config) {
    $dashboardUrl = [string]$config.dashboardUrl
    if ([string]::IsNullOrWhiteSpace($dashboardUrl)) {
        Write-Check FAIL 'Dashboard URL' 'dashboardUrl chýba.'
    }
    else {
        try {
            $dashboardUri = [Uri]$dashboardUrl
            if (-not $dashboardUri.IsAbsoluteUri -or $dashboardUri.Scheme -ne 'https') {
                Write-Check FAIL 'Dashboard URL' 'Musí ísť o absolútnu HTTPS adresu.'
                $dashboardUri = $null
            }
            else {
                Write-Check PASS 'Dashboard URL' $dashboardUri.GetLeftPart([System.UriPartial]::Authority)
            }
        }
        catch {
            Write-Check FAIL 'Dashboard URL' 'Adresa nie je platná URI.'
        }
    }

    $agentKey = [string]$config.agentKey
    if ($agentKey -match '^lab_.{40,}$') {
        Write-Check PASS 'Pairing token' 'Token je prítomný a má očakávaný formát. Hodnota sa nevypisuje.'
    }
    else {
        Write-Check FAIL 'Pairing token' 'Chýba alebo nemá očakávaný lab_ formát.'
    }

    foreach ($entry in @(
        @{ Name = 'Chamber config'; Value = [string]$config.chambersFile },
        @{ Name = 'Profile library'; Value = [string]$config.profilesFile }
    )) {
        if ([string]::IsNullOrWhiteSpace($entry.Value)) {
            Write-Check WARN $entry.Name 'Cesta nie je explicitne uvedená v bridge.json; použije sa default agenta.'
            continue
        }

        $resolved = Resolve-ExpandedPath $entry.Value
        if ($entry.Name -eq 'Profile library' -and [System.IO.Path]::GetExtension($resolved) -eq '.json') {
            $resolved = Split-Path -Parent $resolved
        }

        if (Test-Path -LiteralPath $resolved) {
            Write-Check PASS $entry.Name $resolved
        }
        else {
            Write-Check WARN $entry.Name "Cesta zatiaľ neexistuje: $resolved"
        }
    }
}

if ($dashboardUri) {
    $net = Test-HttpReachability -Uri $dashboardUri
    if ($net.Reachable) {
        Write-Check PASS 'HTTPS reachability' ("Dashboard odpovedal HTTP {0}." -f $net.Status)
    }
    else {
        Write-Check FAIL 'HTTPS reachability' $net.Error
    }
}

if (-not $SkipScheduledTaskCheck) {
    if ($env:OS -ne 'Windows_NT') {
        Write-Check FAIL 'Scheduled Task' 'Tento acceptance skript je určený pre Windows.'
    }
    else {
        try {
            $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
            Write-Check PASS 'Scheduled Task' ("{0}; state={1}" -f $TaskName, $task.State)

            $action = @($task.Actions) | Select-Object -First 1
            if ($action -and $action.Execute) {
                $exe = [Environment]::ExpandEnvironmentVariables([string]$action.Execute).Trim('"')
                if (Test-Path -LiteralPath $exe -PathType Leaf) {
                    Write-Check PASS 'Agent executable' $exe
                }
                else {
                    Write-Check FAIL 'Agent executable' "Scheduled Task ukazuje na neexistujúci súbor: $exe"
                }
            }
            else {
                Write-Check FAIL 'Agent executable' 'Scheduled Task nemá spustiteľnú action.'
            }
        }
        catch {
            Write-Check FAIL 'Scheduled Task' ("Úloha '{0}' nebola nájdená alebo sa nedá prečítať: {1}" -f $TaskName, $_.Exception.Message)
        }
    }
}

$status = $null
if (Test-Path -LiteralPath $statusFull -PathType Leaf) {
    try {
        $status = Get-Content -LiteralPath $statusFull -Raw | ConvertFrom-Json
        Write-Check PASS 'bridge-status.json' $statusFull
    }
    catch {
        Write-Check FAIL 'bridge-status.json' ("Súbor sa nedá načítať: {0}" -f $_.Exception.Message)
    }
}
else {
    Write-Check FAIL 'bridge-status.json' "Súbor neexistuje: $statusFull"
}

if ($status) {
    if ([int]$status.contractVersion -eq 2) {
        Write-Check PASS 'Bridge contract' 'contractVersion=2.'
    }
    else {
        Write-Check FAIL 'Bridge contract' ("Očakáva sa contractVersion=2, status obsahuje '{0}'. Aktualizuj agent." -f $status.contractVersion)
    }

    if ($status.running -eq $true) {
        Write-Check PASS 'Agent process state' 'Agent reportuje Running=true.'
    }
    else {
        Write-Check FAIL 'Agent process state' 'Agent reportuje Running=false.'
    }

    if ($status.dashboardReachable -eq $true) {
        Write-Check PASS 'Agent heartbeat state' 'Agent reportuje DashboardReachable=true.'
    }
    else {
        $detail = if ($status.lastError) { [string]$status.lastError } else { 'DashboardReachable=false.' }
        Write-Check FAIL 'Agent heartbeat state' $detail
    }

    try {
        $updatedUtc = [DateTime]::Parse([string]$status.updatedUtc).ToUniversalTime()
        $age = ([DateTime]::UtcNow - $updatedUtc).TotalSeconds
        if ($age -le $MaxStatusAgeSeconds -and $age -ge -10) {
            Write-Check PASS 'Status freshness' ("Aktualizované pred {0:N0} s." -f [Math]::Max(0, $age))
        }
        else {
            Write-Check FAIL 'Status freshness' ("Status je starý {0:N0} s; limit je {1} s." -f $age, $MaxStatusAgeSeconds)
        }
    }
    catch {
        Write-Check FAIL 'Status freshness' 'updatedUtc sa nedá vyhodnotiť.'
    }

    if ($status.lastHeartbeatUtc) {
        try {
            $heartbeatUtc = [DateTime]::Parse([string]$status.lastHeartbeatUtc).ToUniversalTime()
            $heartbeatAge = ([DateTime]::UtcNow - $heartbeatUtc).TotalSeconds
            if ($heartbeatAge -le $MaxStatusAgeSeconds -and $heartbeatAge -ge -10) {
                Write-Check PASS 'Last heartbeat' ("Posledný úspešný heartbeat pred {0:N0} s." -f [Math]::Max(0, $heartbeatAge))
            }
            else {
                Write-Check FAIL 'Last heartbeat' ("Posledný heartbeat je starý {0:N0} s." -f $heartbeatAge)
            }
        }
        catch {
            Write-Check FAIL 'Last heartbeat' 'lastHeartbeatUtc sa nedá vyhodnotiť.'
        }
    }
    else {
        Write-Check FAIL 'Last heartbeat' 'Agent ešte nezapísal úspešný heartbeat.'
    }

    if ($status.version) {
        Write-Check PASS 'Agent version' ([string]$status.version)
    }
    else {
        Write-Check WARN 'Agent version' 'Verzia nie je v status súbore uvedená.'
    }
}

Write-Host ''
Write-Host '---------------- Acceptance summary ----------------'
Write-Host ("FAIL: {0}   WARN: {1}" -f $script:Failures, $script:Warnings)

if ($script:Failures -gt 0) {
    Write-Host 'Výsledok: NOT READY. Oprav FAIL položky pred remote-control testom.'
    exit 1
}

Write-Host 'Výsledok: PRE-FLIGHT READY.'
Write-Host 'Ďalší krok: v Dashboarde potvrď live hodnoty a profile/config roundtrip,'
Write-Host 'potom povoľ AllowControl iba na jednej testovacej komore a vykonaj bezpečný setpoint test.'
exit 0
