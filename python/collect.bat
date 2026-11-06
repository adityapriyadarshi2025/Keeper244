@echo off
rem Keeper244: record one participant in one go (calibration + rest + biceps + triceps + 3 game blocks).
rem Double-click it, or run  collect  in PowerShell / Command Prompt from the python folder.
cd /d "%~dp0"
echo.
echo  Keeper244 data collection
echo  -------------------------
echo  Before you start: BioCapture and the kit window CLOSED, BioRadio ON, game open with INPUT = EMG over UDP.
echo.
set /p PID=Participant ID (P04, P05, ...): 
if "%PID%"=="" set PID=P99
set /p SITES=Electrodes [Enter = ch1 right biceps, ch2 left triceps]: 
if "%SITES%"=="" set SITES=ch1 right biceps, ch2 left triceps
echo.
python -m emg_bridge protocol --source bioradio --participant %PID% --note "%SITES%" %*
echo.
echo  Data is in  %~dp0..\data\sessions\  (newest folder). Copy the whole data folder to Drive before you leave.
pause
