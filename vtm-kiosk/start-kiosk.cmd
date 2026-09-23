@echo off
REM Launches the kiosk the way a kiosk has to be launched.
REM
REM Without --auto-select-desktop-capture-source, getDisplayMedia opens a picker and waits
REM for someone to click it. Nobody is standing at a kiosk to do that, so the teller's
REM "View their screen" command just times out. That flag is the whole difference.
REM
REM The separate profile is not optional either: a Chrome that is already running ignores
REM new flags and hands the window to the existing process, picker and all.
REM
REM In production the WPF/WebView2 shell passes the same argument. See docs/06-kiosk-command-set.md.

set CHROME="C:\Program Files\Google\Chrome\Application\chrome.exe"
set URL=http://localhost:4201
set PROFILE=%TEMP%\vtm-kiosk-profile

if not exist %CHROME% (
  echo Chrome was not found at %CHROME%
  echo Edit this file and point CHROME at your install.
  pause
  exit /b 1
)

start "" %CHROME% ^
  --user-data-dir="%PROFILE%" ^
  --auto-select-desktop-capture-source="Entire screen" ^
  --autoplay-policy=no-user-gesture-required ^
  --kiosk ^
  %URL%
