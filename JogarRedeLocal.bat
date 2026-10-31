@echo off
cd /d "%~dp0"
python tools\run_local_realtime_lab.py
if errorlevel 1 pause
