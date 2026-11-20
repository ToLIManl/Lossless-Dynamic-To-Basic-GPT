#Requires -Version 5.1
# 1-Click Rollback: Restores the last converted disk back to its original Dynamic (LDM) GPT state from Backups\

$ErrorActionPreference = 'Continue'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit 0
}

$baseDir = Split-Path -Parent $PSCommandPath
$binDir  = Join-Path $baseDir 'bin'
$bkRoot  = Join-Path $baseDir 'Backups'
$engExe  = Join-Path $binDir 'DynamicToBasicEngine.exe'
$metaPath = Join-Path $bkRoot 'last_conversion.json'

try {
    if (-not (Test-Path $metaPath)) { throw "No last_conversion.json found in $bkRoot" }
    $meta = Get-Content $metaPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "Restoring Disk #$($meta.DiskIndex) ($($meta.DriveLetter)) from $($meta.BackupDir) ..." -ForegroundColor Yellow

    & $engExe --restore $meta.DiskIndex $meta.DriveLetter $meta.BackupDir
    if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit=$LASTEXITCODE" }

    $tmp = Join-Path $bkRoot 'dp_restore.txt'
    @("select disk $($meta.DiskIndex)", "offline disk noerr", "exit") | Set-Content $tmp -Encoding ASCII
    & diskpart.exe /s $tmp | Out-Null
    Start-Sleep -Seconds 1
    @("select disk $($meta.DiskIndex)", "online disk noerr", "attributes disk clear readonly noerr", "rescan", "exit") | Set-Content $tmp -Encoding ASCII
    & diskpart.exe /s $tmp | Out-Null
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue

    Write-Host "[OK] Disk #$($meta.DiskIndex) restored to Dynamic LDM state!" -ForegroundColor Green
}
catch {
    Write-Host "[FATAL] $($_.Exception.Message)" -ForegroundColor Red
}

Read-Host "Press Enter to close"
