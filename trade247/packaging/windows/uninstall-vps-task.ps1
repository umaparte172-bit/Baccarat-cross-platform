<#
.SYNOPSIS
  Removes the scheduled task created by install-vps-task.ps1. Settings, logs and reports are not touched.

.PARAMETER TaskName  Name of the scheduled task. Default: Trade247
.PARAMETER DryRun    Say what would be removed, change nothing.
#>
[CmdletBinding()]
param(
  [string]$TaskName = 'Trade247',
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
if ($TaskName -notmatch '^[A-Za-z0-9 _.-]{1,60}$') { throw "TaskName may only contain letters, digits, spaces, '_', '.' and '-'." }

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if (-not $task) {
  Write-Host "There is no scheduled task named '$TaskName'. Nothing to do."
  return
}
if ($DryRun) {
  Write-Host "Dry run: would stop and remove the scheduled task '$TaskName'."
  return
}
if ($task.State -eq 'Running') { Stop-ScheduledTask -TaskName $TaskName }
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
Write-Host "Removed the scheduled task '$TaskName'. Your settings, logs and reports were not touched."
