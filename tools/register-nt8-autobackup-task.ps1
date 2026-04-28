param(
    [string]$RepoPath,
    [int]$IntervalMinutes = 15,
    [string]$TaskName = "NT8 GitHub Auto Backup",
    [switch]$NoPush
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    $RepoPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

if ($IntervalMinutes -lt 5) {
    throw "Use an interval of 5 minutes or more."
}

$backupScript = Join-Path $PSScriptRoot "nt8-autobackup.ps1"
if (-not (Test-Path $backupScript)) {
    throw "Backup script not found: $backupScript"
}

$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", "`"$backupScript`"",
    "-RepoPath", "`"$RepoPath`""
)

if ($NoPush) {
    $arguments += "-NoPush"
}

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument ($arguments -join " ")
$start = (Get-Date).AddMinutes(1)
$trigger = New-ScheduledTaskTrigger `
    -Once `
    -At $start `
    -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) `
    -RepetitionDuration (New-TimeSpan -Days 3650)

$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 10)

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Description "Commits curated NinjaTrader 8 strategy, indicator, and template changes and pushes them to GitHub when a remote is configured." `
    -Force | Out-Null

Write-Output "Registered scheduled task '$TaskName' every $IntervalMinutes minutes."
