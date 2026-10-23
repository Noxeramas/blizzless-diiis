<#
Development launcher: points Diablo III at the local D3Offline server, starts the game, and
switches back to Blizzard's servers when the game closes. The server's certificate is trusted
once and left in place; only the hosts redirect is toggled. Use -Undo to remove both.
#>
param([switch]$Undo)

$ErrorActionPreference = 'Stop'
$marker = '# D3Offline'
$hostsPath = Join-Path $env:WINDIR 'System32\drivers\etc\hosts'
# Login servers point at the local server; telemetry and the crash reporter's upload host go nowhere,
# so nothing from an offline session is sent to Blizzard.
$hostNames = 'us.actual.battle.net', 'eu.actual.battle.net', 'telemetry-in.battle.net', 'iir.blizzard.com'
$caPath = Join-Path $env:LOCALAPPDATA 'D3Offline\D3Offline-LocalCA.cer'

# The hosts file needs admin, so relaunch elevated (one UAC prompt per run).
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($Undo) { $argList += '-Undo' }
    Start-Process powershell -Verb RunAs -ArgumentList $argList
    return
}

function Set-Redirect([bool]$enabled) {
    $lines = @(Get-Content $hostsPath | Where-Object { $_ -notlike "*$marker" })
    if ($enabled) { $lines += $hostNames | ForEach-Object { "127.0.0.1 $_ $marker" } }
    Set-Content -Path $hostsPath -Value $lines -Encoding ASCII
    ipconfig /flushdns | Out-Null
}

function Find-GameExe {
    $uninstallKeys = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    $install = Get-ItemProperty $uninstallKeys -ErrorAction SilentlyContinue |
        Where-Object DisplayName -eq 'Diablo III' | Select-Object -First 1 -ExpandProperty InstallLocation
    $exe = if ($install) { Join-Path $install 'x64\Diablo III64.exe' }
    if (-not $exe -or -not (Test-Path $exe)) { throw "Couldn't find Diablo III. Is it installed through Battle.net?" }
    $exe
}

try {
    if ($Undo) {
        Set-Redirect $false
        Get-ChildItem Cert:\CurrentUser\Root | Where-Object Subject -like 'CN=D3Offline Local CA*' | Remove-Item
        Write-Host "Removed the redirect and the D3Offline certificate."
        Start-Sleep -Seconds 3
        return
    }

    $gameExe = Find-GameExe
    if (-not (Test-Path $caPath)) { throw "No certificate at $caPath. Start the server once so it can generate one." }
    if (-not (Get-NetTCPConnection -LocalPort 1119 -State Listen -ErrorAction SilentlyContinue)) {
        throw 'The D3Offline server is not running. Start it first, then run this again.'
    }

    $ca = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($caPath)
    if (-not (Test-Path "Cert:\CurrentUser\Root\$($ca.Thumbprint)")) {
        Write-Host "Trusting this PC's D3Offline certificate (one time). Click Yes on the Windows prompt."
        Import-Certificate -FilePath $caPath -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
    }

    Set-Redirect $true
    try {
        Write-Host 'Diablo III is pointed at the local server. Close the game to switch back.'
        $game = Start-Process -FilePath $gameExe -ArgumentList '-launch' -WorkingDirectory (Split-Path $gameExe) -PassThru
        $game.WaitForExit()
        Wait-Process -Name 'Diablo III64' -ErrorAction SilentlyContinue
    }
    finally {
        Set-Redirect $false
    }
    Write-Host "Game closed. Diablo III uses Blizzard's servers again."
    Start-Sleep -Seconds 3
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    Read-Host 'Press Enter to close'
}
