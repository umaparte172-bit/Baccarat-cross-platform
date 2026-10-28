<#
.SYNOPSIS
  Starts TRADE247 automatically at logon and restarts it if it crashes (for a Windows VPS that runs 24/7).

.DESCRIPTION
  Registers a Windows scheduled task for the current user:
    - starts at logon (the bot's browser, when live trading is added, needs the logged-in desktop; a Windows
      service would run without one)
    - restarts the program if it stops with an error, every minute, with no limit on run time
    - runs without opening a browser (--no-open) and without launching Excel for reports (T247_OPEN_REPORTS=0)
  It does NOT start betting by itself: after a restart the program comes up idle and waits for you to press START.

.PARAMETER ExePath   The program. Default: trade247-win-x64.exe next to this script.
.PARAMETER TaskName  Name of the scheduled task. Default: Trade247
.PARAMETER Port      Port for the page on this computer. Default: 8247
.PARAMETER DataDir   Where settings, logs and reports are kept. Default: %APPDATA%\Trade247
.PARAMETER StartNow  Also start the task right after registering it.
.PARAMETER DryRun    Show what would be registered, change nothing.

.EXAMPLE
  .\install-vps-task.ps1 -DryRun
  .\install-vps-task.ps1 -StartNow
#>
[CmdletBinding()]
param(
  [string]$ExePath = (Join-Path $PSScriptRoot 'trade247-win-x64.exe'),
  [string]$TaskName = 'Trade247',
  [int]$Port = 8247,
  [string]$DataDir = '',
  [switch]$StartNow,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { throw "Cannot find the program at '$ExePath'." }
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path
if ($Port -lt 1 -or $Port -gt 65535) { throw "Port must be between 1 and 65535 (got $Port)." }
if ($TaskName -notmatch '^[A-Za-z0-9 _.-]{1,60}$') { throw "TaskName may only contain letters, digits, spaces, '_', '.' and '-'." }

$workDir = Split-Path -Parent $ExePath
# The program is started through cmd.exe so that environment variables can be set for it:
#   T247_OPEN_REPORTS=0  a server has no desktop applications to open reports in; the page downloads them instead
#   T247_DATA_DIR        only when -DataDir is given
$setVariables = 'set "T247_OPEN_REPORTS=0" && '
if ($DataDir) {
  # The folder is passed through cmd.exe, so characters cmd treats specially are refused.
  if ($DataDir -match '["&%^|<>]') { throw "DataDir must not contain any of these characters: `" & % ^ | < >" }
  $setVariables += 'set "T247_DATA_DIR=' + $DataDir + '" && '
}
$argument = '/c ' + $setVariables + '"' + $ExePath + '" --no-open --port ' + $Port
$action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument $argument -WorkingDirectory $workDir

$user = "$env:USERDOMAIN\$env:USERNAME"
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $user
$settings  = New-ScheduledTaskSettingsSet `
  -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
  -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
  -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited

Write-Host "Task name : $TaskName"
Write-Host "Runs as   : $user (only while logged on)"
Write-Host "Starts    : at logon of $user"
Write-Host "Command   : $($action.Execute) $($action.Arguments)"
Write-Host "Restart   : every 1 minute after a crash, no limit"
Write-Host "Page      : http://127.0.0.1:$Port/ (this computer only)"

if ($DryRun) {
  Write-Host "`nDry run: nothing was registered."
  return
}

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
  -Description 'TRADE247-V1 local bot: starts at logon, restarts after a crash.' -Force | Out-Null
Write-Host "`nRegistered. It will start the next time $user logs on."
if ($StartNow) {
  Start-ScheduledTask -TaskName $TaskName
  Write-Host "Started. Open http://127.0.0.1:$Port/ on this computer."
}
Write-Host "To remove it: .\uninstall-vps-task.ps1"
