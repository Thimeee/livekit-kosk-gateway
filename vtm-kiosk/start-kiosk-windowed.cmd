@echo off
REM Same as start-kiosk.cmd but in a normal window, for testing on a desk.

set CHROME="C:\Program Files\Google\Chrome\Application\chrome.exe"
set URL=http://localhost:4201
set PROFILE=%TEMP%\vtm-kiosk-profile

start "" %CHROME% ^
  --user-data-dir="%PROFILE%" ^
  --auto-select-desktop-capture-source="Entire screen" ^
  --autoplay-policy=no-user-gesture-required ^
  --window-size=1280,800 ^
  %URL%
