@echo off
rem Starts NetMonitor without installing it (portable mode).
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0NetMonitor.ps1"
