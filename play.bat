@echo off
rem Launches Diablo III against the local D3Offline server (see tools\play.ps1).
rem Run "play.bat -Undo" to remove the redirect and the certificate.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\play.ps1" %*
