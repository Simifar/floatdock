param(
    [Parameter(Mandatory)][string]$Exe,
    [int]$Seconds = 20
)

# Starts the app with plain settings, lets it run, and checks that it stays alive, logs no error of its own and exits on request.
# Used by CI (also on a Windows Server 2022 runner, whose kernel is the Windows 10 era) and by hand. Backs up and restores
# settings.json and startup-attempts.json, and verifies the restore.
$ErrorActionPreference = 'Stop'
$data = Join-Path $env:LOCALAPPDATA 'FloatDock'
$settings = Join-Path $data 'settings.json'; $attempts = Join-Path $data 'startup-attempts.json'
$log = Join-Path $data 'logs/floatdock.log'
$backup = Join-Path ([IO.Path]::GetTempPath()) ('fd-' + [guid]::NewGuid())
New-Item -ItemType Directory $backup | Out-Null
New-Item -ItemType Directory -Force $data | Out-Null
foreach ($f in $settings, $attempts) { if (Test-Path $f) { Copy-Item $f $backup } }
$failed = @()
try {
    $os = Get-CimInstance Win32_OperatingSystem
    "OS: $($os.Caption) build $($os.BuildNumber)"
    Remove-Item $attempts -ErrorAction SilentlyContinue
    '{"FirstRunCompleted":true,"Mode":0}' | Set-Content $settings
    $since = [DateTimeOffset]::UtcNow
    $process = Start-Process -FilePath (Resolve-Path $Exe).Path -PassThru
    Start-Sleep -Seconds $Seconds
    $process.Refresh()
    if ($process.HasExited) { $failed += "the app exited after less than $Seconds s (code $($process.ExitCode))" }
    else { "Running after $Seconds s: working set $([int]($process.WorkingSet64 / 1MB)) MB, $($process.Threads.Count) threads" }

    $errors = @()
    if (Test-Path $log) {
        $errors = Get-Content $log | ForEach-Object { try { $_ | ConvertFrom-Json } catch { $null } } |
            Where-Object { $_ -and $_.pid -eq $process.Id -and $_.level -in 'Error', 'Fatal' -and [DateTimeOffset]$_.time -ge $since }
    }
    foreach ($entry in $errors) { $failed += "log $($entry.level): $($entry.eventName) $($entry.errorType)" }
    # Warnings are information here: a missing GPU feature on a server SKU is not a failure.
    if (Test-Path $log) {
        Get-Content $log | ForEach-Object { try { $_ | ConvertFrom-Json } catch { $null } } |
            Where-Object { $_ -and $_.pid -eq $process.Id -and $_.level -eq 'Warning' } | ForEach-Object { "log Warning: $($_.eventName) $($_.errorType)" }
    }

    if (-not $process.HasExited) {
        $quit = [Threading.EventWaitHandle]::OpenExisting('Local\FloatDock.Quit'); [void]$quit.Set(); $quit.Dispose()
        if (-not $process.WaitForExit(15000)) { $failed += 'the app did not exit on request' }
    }
}
finally {
    Get-Process FloatDock.App -ErrorAction SilentlyContinue | Stop-Process -Force
    foreach ($f in $settings, $attempts) {
        $saved = Join-Path $backup (Split-Path $f -Leaf)
        if (Test-Path $saved) { Copy-Item $saved $f -Force } else { Remove-Item $f -ErrorAction SilentlyContinue }
    }
    $restored = $true
    foreach ($saved in Get-ChildItem $backup) { if ((Get-FileHash $saved.FullName).Hash -ne (Get-FileHash (Join-Path $data $saved.Name)).Hash) { $restored = $false } }
    if ($restored) { Remove-Item $backup -Recurse } else { Write-Warning "Restore failed; the copies are in $backup" }
}
if ($failed.Count -gt 0) { throw ('Smoke start failed: ' + ($failed -join '; ')) }
'Smoke start passed.'
