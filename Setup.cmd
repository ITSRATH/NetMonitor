@echo off
rem Installs NetMonitor for the current user (no administrator rights required).
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0NetMonitor.ps1" -Setup
