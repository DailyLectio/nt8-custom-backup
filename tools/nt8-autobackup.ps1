param(
    [string]$RepoPath,
    [switch]$NoPush,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    $RepoPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$preferredLogRoot = Join-Path $env:LOCALAPPDATA "NT8GitBackup"
$script:logRoot = $preferredLogRoot
try {
    New-Item -ItemType Directory -Path $script:logRoot -Force | Out-Null
}
catch {
    $script:logRoot = Join-Path $RepoPath ".nt8-backup-logs"
    New-Item -ItemType Directory -Path $script:logRoot -Force | Out-Null
}

$script:logFile = Join-Path $script:logRoot "nt8-autobackup.log"

function Write-BackupLog {
    param([string]$Message)

    $line = "{0} {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    try {
        Add-Content -Path $script:logFile -Value $line
    }
    catch {
        $script:logRoot = Join-Path $RepoPath ".nt8-backup-logs"
        New-Item -ItemType Directory -Path $script:logRoot -Force | Out-Null
        $script:logFile = Join-Path $script:logRoot "nt8-autobackup.log"
        Add-Content -Path $script:logFile -Value $line
    }
    Write-Output $line
}

function Invoke-Git {
    param([string[]]$GitArgs)

    & git -C $RepoPath @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArgs -join ' ') failed with exit code $LASTEXITCODE"
    }
}

try {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "Git is not available on PATH."
    }

    if (-not (Test-Path (Join-Path $RepoPath ".git"))) {
        throw "No .git folder found at $RepoPath."
    }

    Write-BackupLog "Starting NT8 backup from $RepoPath"

    if ($DryRun) {
        Write-BackupLog "Dry run only. Current backup-visible changes:"
        Invoke-Git -GitArgs @("status", "--short")
        exit 0
    }

    Invoke-Git -GitArgs @("add", "-A", "--", ".")

    & git -C $RepoPath diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-BackupLog "No backup changes detected."
        exit 0
    }
    if ($LASTEXITCODE -ne 1) {
        throw "git diff --cached --quiet failed with exit code $LASTEXITCODE"
    }

    $commitMessage = "NT8 autosave $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Invoke-Git -GitArgs @("commit", "-m", $commitMessage)
    Write-BackupLog "Created commit: $commitMessage"

    if ($NoPush) {
        Write-BackupLog "NoPush was set. Skipping GitHub push."
        exit 0
    }

    $remoteUrl = (& git -C $RepoPath remote get-url origin 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($remoteUrl)) {
        Write-BackupLog "No origin remote configured. Commit saved locally only."
        exit 0
    }

    $branch = (& git -C $RepoPath branch --show-current).Trim()
    if ([string]::IsNullOrWhiteSpace($branch)) {
        $branch = "main"
    }

    & git -C $RepoPath rev-parse --abbrev-ref --symbolic-full-name "@{u}" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Invoke-Git -GitArgs @("push")
    }
    else {
        Invoke-Git -GitArgs @("push", "-u", "origin", $branch)
    }

    Write-BackupLog "Pushed backup to origin/$branch"
}
catch {
    Write-BackupLog "ERROR: $($_.Exception.Message)"
    exit 1
}
