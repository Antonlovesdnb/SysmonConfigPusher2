<#
.SYNOPSIS
    Creates a backup of the SysmonConfigPusher SQLite database.

.DESCRIPTION
    This script creates a timestamped backup of the SQLite database.
    It can optionally prune old backups to save disk space.

.PARAMETER BackupPath
    The directory to store backups. Defaults to %ProgramData%\SysmonConfigPusher\backups.

.PARAMETER RetainDays
    Number of days to retain backups. Older backups are deleted. Default is 30.

.PARAMETER NoRotation
    If specified, skips the deletion of old backups.

.EXAMPLE
    .\backup-database.ps1

.EXAMPLE
    .\backup-database.ps1 -BackupPath "D:\Backups\SysmonPusher" -RetainDays 90

.EXAMPLE
    # Run as a scheduled task daily at 2 AM
    $action = New-ScheduledTaskAction -Execute "PowerShell.exe" `
        -Argument "-ExecutionPolicy Bypass -File C:\scripts\backup-database.ps1"
    $trigger = New-ScheduledTaskTrigger -Daily -At 2:00AM
    Register-ScheduledTask -TaskName "SysmonPusher-Backup" -Action $action -Trigger $trigger
#>

param(
    [string]$BackupPath = (Join-Path $env:ProgramData "SysmonConfigPusher\backups"),
    [int]$RetainDays = 30,
    [switch]$NoRotation
)

$ErrorActionPreference = "Stop"

$DataPath = Join-Path $env:ProgramData "SysmonConfigPusher"
$DbPath = Join-Path $DataPath "sysmon.db"

# Check if database exists
if (-not (Test-Path $DbPath)) {
    Write-Error "Database not found at: $DbPath"
    exit 1
}

# Create backup directory if needed
if (-not (Test-Path $BackupPath)) {
    Write-Host "Creating backup directory: $BackupPath" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
}

# Generate backup filename with timestamp
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupFile = Join-Path $BackupPath "sysmon-$timestamp.db"

Write-Host "Backing up database..." -ForegroundColor Cyan
Write-Host "  Source: $DbPath"
Write-Host "  Destination: $backupFile"

# Copy the database
# Note: SQLite databases can be safely copied while in use (WAL mode handles this)
try {
    Copy-Item -Path $DbPath -Destination $backupFile -Force

    # Also copy WAL file if it exists
    $walPath = "$DbPath-wal"
    if (Test-Path $walPath) {
        Copy-Item -Path $walPath -Destination "$backupFile-wal" -Force
    }

    # Also copy SHM file if it exists
    $shmPath = "$DbPath-shm"
    if (Test-Path $shmPath) {
        Copy-Item -Path $shmPath -Destination "$backupFile-shm" -Force
    }

    $size = (Get-Item $backupFile).Length / 1MB
    Write-Host "Backup complete: $([math]::Round($size, 2)) MB" -ForegroundColor Green
}
catch {
    Write-Error "Failed to create backup: $_"
    exit 1
}

# Rotate old backups
if (-not $NoRotation) {
    Write-Host ""
    Write-Host "Cleaning up backups older than $RetainDays days..." -ForegroundColor Cyan

    $cutoffDate = (Get-Date).AddDays(-$RetainDays)
    $oldBackups = Get-ChildItem -Path $BackupPath -Filter "sysmon-*.db" |
                  Where-Object { $_.CreationTime -lt $cutoffDate }

    if ($oldBackups.Count -gt 0) {
        foreach ($old in $oldBackups) {
            Write-Host "  Removing: $($old.Name)"
            Remove-Item -Path $old.FullName -Force

            # Also remove associated WAL and SHM files
            $oldWal = "$($old.FullName)-wal"
            $oldShm = "$($old.FullName)-shm"
            if (Test-Path $oldWal) { Remove-Item $oldWal -Force }
            if (Test-Path $oldShm) { Remove-Item $oldShm -Force }
        }
        Write-Host "Removed $($oldBackups.Count) old backup(s)." -ForegroundColor Yellow
    } else {
        Write-Host "No old backups to remove." -ForegroundColor Gray
    }
}

# Summary
Write-Host ""
Write-Host "Backup Summary:" -ForegroundColor Cyan
$currentBackups = Get-ChildItem -Path $BackupPath -Filter "sysmon-*.db"
$totalSize = ($currentBackups | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "  Total backups: $($currentBackups.Count)"
Write-Host "  Total size: $([math]::Round($totalSize, 2)) MB"
Write-Host "  Location: $BackupPath"
