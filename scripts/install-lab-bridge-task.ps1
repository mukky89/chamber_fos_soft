param(
    [Parameter(Mandatory = $true)][string]$AgentExe,
    [string]$ConfigPath = "$env:USERPROFILE\Documents\Lab Control\bridge.json",
    [switch]$Highest
)

$ErrorActionPreference = 'Stop'
$taskName = 'Sylex Lab Control Bridge'

function Test-IsAdministrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
    }
}

try {
    $resolvedExe = (Resolve-Path -LiteralPath $AgentExe -ErrorAction Stop).Path
    $resolvedConfig = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($ConfigPath))

    if (-not (Test-Path -LiteralPath $resolvedConfig -PathType Leaf)) {
        throw "Bridge configuration does not exist: $resolvedConfig"
    }

    $isAdmin = Test-IsAdministrator
    if ($Highest -and -not $isAdmin) {
        throw 'The -Highest option requires PowerShell to be started as Administrator.'
    }

    # Limited is intentional by default. The bridge only needs the same COM/UNC access
    # as the currently logged-in laboratory user, and this lets corporate standard-user
    # accounts install their own logon task without local administrator rights.
    $runLevel = if ($Highest) { 'Highest' } else { 'Limited' }
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name

    $action = New-ScheduledTaskAction -Execute $resolvedExe -Argument ('"{0}"' -f $resolvedConfig)
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
    $settings = New-ScheduledTaskSettingsSet `
        -RestartCount 20 `
        -RestartInterval (New-TimeSpan -Minutes 1) `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -StartWhenAvailable

    Register-ScheduledTask `
        -TaskName $taskName `
        -Action $action `
        -Trigger $trigger `
        -Settings $settings `
        -RunLevel $runLevel `
        -Force `
        -ErrorAction Stop | Out-Null

    Start-ScheduledTask -TaskName $taskName -ErrorAction Stop

    $registered = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
    Write-Host "[PASS] Scheduled Task '$taskName' registered for $currentUser (RunLevel=$runLevel)."
    Write-Host "[PASS] Agent executable: $resolvedExe"
    Write-Host "[PASS] Bridge config: $resolvedConfig"
    Write-Host "[PASS] Task state after start request: $($registered.State)"
    exit 0
}
catch {
    Write-Error ("Bridge Scheduled Task installation failed: {0}" -f $_.Exception.Message)
    Write-Host '[INFO] No success was recorded. The bridge can still be tested manually with:'
    Write-Host ('       & "{0}" "{1}"' -f $AgentExe, $ConfigPath)
    exit 1
}
