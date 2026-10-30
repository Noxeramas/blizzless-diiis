# Exercise the launcher's process boundary without editing hosts or starting a game.
param([string]$Launcher = (Join-Path $PSScriptRoot '..\tools\play.ps1'))
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this check as a normal user, like play.bat.'
}
$testState = [pscustomobject]@{ Calls = [Collections.Generic.List[object]]::new(); Failure = ''; Prompted = $false }
$calls = $testState.Calls

function Get-ItemProperty { [pscustomobject]@{ DisplayName = 'Diablo III'; InstallLocation = 'C:\Test Game' } }
function Test-Path { $true }
function Get-NetTCPConnection { [pscustomobject]@{ LocalPort = 1119 } }
function New-Object { [pscustomobject]@{ Thumbprint = 'TEST' } }
function Get-ChildItem { }
function Set-Content { throw 'Test must never write the hosts file.' }
function Import-Certificate { throw 'Test must never change certificates.' }
function Wait-Process { }
function Start-Sleep { }
function Read-Host { $testState.Prompted = $true }
function Start-Process {
    param($FilePath, $ArgumentList, $Verb, $WorkingDirectory, $WindowStyle, [switch]$Wait, [switch]$PassThru)
    $testState.Calls.Add([pscustomobject]@{ File = $FilePath; Args = @($ArgumentList); Verb = $Verb; Wait = $Wait; Hidden = $WindowStyle -eq 'Hidden' })
    if ($testState.Failure -eq 'game' -and $FilePath -like '*Diablo III64.exe') { throw 'Game launch failed.' }
    if ($testState.Failure -eq 'cancel' -and $Verb -eq 'RunAs') { throw 'UAC cancelled.' }
    $result = [pscustomobject]@{ ExitCode = $(if ($testState.Failure -eq 'redirect') { 1 } else { 0 }) }
    $result | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { }
    if ($PassThru) { $result }
}
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Reset-Check($failure = '') { $testState.Calls.Clear(); $testState.Failure = $failure; $testState.Prompted = $false }
function Assert-Redirect($call, $action) {
    Assert ($call.File -eq 'powershell' -and $call.Verb -eq 'RunAs' -and $call.Wait -and $call.Hidden) 'Only a waited, hidden hosts helper should be elevated.'
    Assert ($call.Args[-2] -eq '-Redirect' -and $call.Args[-1] -eq $action) "Expected isolated redirect action $action."
}

& $Launcher
Assert ($calls.Count -eq 4 -and -not $testState.Prompted) 'Expected redirect, game, meter, then redirect cleanup.'
Assert-Redirect $calls[0] 'Enable'
Assert ($calls[1].File -like '*Diablo III64.exe' -and -not $calls[1].Verb) 'Game must inherit the normal user token.'
Assert ($calls[2].File -like '*D3Offline.CombatMeter.exe' -and -not $calls[2].Verb) 'Meter must inherit the same normal user token.'
Assert-Redirect $calls[3] 'Disable'
'PASS game and meter inherit the normal user; only hosts helpers elevate'

Reset-Check 'game'
& $Launcher
Assert ($calls.Count -eq 3 -and $testState.Prompted) 'Game failure must restore the redirect and report the error.'
Assert-Redirect $calls[2] 'Disable'
'PASS redirect cleanup after game launch failure'

foreach ($failure in 'redirect', 'cancel') {
    Reset-Check $failure
    & $Launcher
    Assert ($calls.Count -eq 1 -and $testState.Prompted) 'Failed or cancelled hosts permission must prevent game and meter launch.'
    "PASS $failure failure prevents game and meter launch"
}

Reset-Check
& $Launcher -Undo
Assert ($calls.Count -eq 1 -and -not $testState.Prompted) 'Undo must not start a game or meter.'
Assert-Redirect $calls[0] 'Disable'
'PASS Undo only requests redirect removal'
