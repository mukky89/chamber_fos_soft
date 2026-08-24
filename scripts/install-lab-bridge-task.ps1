param(
    [Parameter(Mandatory=$true)][string]$AgentExe,
    [string]$ConfigPath = "$env:USERPROFILE\Documents\Lab Control\bridge.json"
)

$resolvedExe = (Resolve-Path -LiteralPath $AgentExe).Path
$resolvedConfig = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($ConfigPath))
$taskName = 'Sylex Lab Control Bridge'
$action = New-ScheduledTaskAction -Execute $resolvedExe -Argument ('"{0}"' -f $resolvedConfig)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -RestartCount 20 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -RunLevel Highest -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Host "Naplánovaná úloha '$taskName' bola vytvorená a spustená."
