@echo off
rem Farmverse local backend: builds the Cloud Functions, starts the Firebase emulators
rem (Auth, Firestore, Functions, Storage) and the island engine, then publishes the catalogue (coupon codes are made by the server).
rem Everything stays on this PC: the emulators never touch the real Firebase project.
rem Started from Unity (Farmverse > Local Backend > Start) or by double-clicking;
rem start-backend.cmd --fresh ignores the saved local farm.
setlocal
cd /d "%~dp0..\.."
title Farmverse local backend
set PROJECT=farmverse-8deb6

where node >nul 2>nul || (echo [!] Node.js 22 is not installed: https://nodejs.org & pause & exit /b 1)
rem Java: from PATH, else JAVA_HOME, else a Temurin install (a just-installed JDK is not on
rem the PATH of programs that were already open, such as Unity)
where java >nul 2>nul && goto :java_ok
if defined JAVA_HOME if exist "%JAVA_HOME%\bin\java.exe" set "PATH=%JAVA_HOME%\bin;%PATH%" & goto :java_ok
for /d %%d in ("%ProgramFiles%\Eclipse Adoptium\jdk-2*") do if exist "%%d\bin\java.exe" set "PATH=%%d\bin;%PATH%"
where java >nul 2>nul || (echo [!] Java 21 is not installed; the Firestore emulator needs it: https://adoptium.net & pause & exit /b 1)
:java_ok

if not exist node_modules (
  echo Installing packages...
  call npm install || goto :fail
)
echo Building the backend...
call npm run build --workspace @farmverse/functions || goto :fail

rem the local farm (players, signed-in users, world) is kept in backend\.emulator-data
rem between runs; it is saved when the emulators stop with Ctrl+C, not when the window is closed
set DATA=.emulator-data
set IMPORT=
if exist "backend\%DATA%\firebase-export-metadata.json" set IMPORT=--import=%DATA%
if "%~1"=="--fresh" set IMPORT=
if defined IMPORT (echo Loading the saved local farm from backend\%DATA%...) else (echo Starting with an empty local backend...)

rem parameter values for the emulator only (git-ignored; without them the CLI stops and asks)
if not exist "backend\functions\.env.local" (
  >"backend\functions\.env.local" (echo MIN_CLIENT_VERSION=0.0.0& echo FARMVERSE_ENV=dev& echo WARM_INSTANCES=0)
)
rem the island engine (C#, the same rules as the game) runs islands for island-sync, chests, achievements,
rem the Farm Pass, coupons, the leaderboard and visits; locally on port 8090 (8080 is Firestore).
rem Needs the .NET 8 SDK: https://dotnet.microsoft.com/download
findstr /b /c:"ISLAND_ENGINE_URL=" "backend\functions\.env.local" >nul 2>nul || (>>"backend\functions\.env.local" echo ISLAND_ENGINE_URL=http://127.0.0.1:8090)
where dotnet >nul 2>nul && (
  echo Starting the island engine in its own window...
  start "Farmverse island engine" cmd /k "set PORT=8090&& dotnet run --project tools\dotnet\Farmverse.IslandEngine -c Release"
) || echo [!] .NET 8 SDK not found: island features will fail until it is installed.
rem the first load of the functions on Windows (cold disk, antivirus scan) can exceed the CLI's 10 s
set FUNCTIONS_DISCOVERY_TIMEOUT=120
echo Starting the emulators in their own window...
start "Farmverse emulators" cmd /k "cd /d backend && npx --yes firebase-tools@15.34.0 emulators:start --only auth,firestore,functions,storage --project %PROJECT% %IMPORT% --export-on-exit=%DATA%"

echo Waiting for Firestore on 127.0.0.1:8080...
for /l %%i in (1,1,90) do (
  curl -s -o nul http://127.0.0.1:8080 && goto :up
  timeout /t 2 /nobreak >nul
)
echo [!] The emulators did not start; see the "Farmverse emulators" window.
goto :fail

:up
rem only ever the emulator: with this set, the publish tools cannot reach the real project
set FIRESTORE_EMULATOR_HOST=127.0.0.1:8080
echo Publishing the catalogue (prices and rewards) into the emulator...
call npm run catalogue:publish --workspace @farmverse/functions -- --project %PROJECT% || goto :fail
echo.
echo Local backend ready. Emulator UI: http://127.0.0.1:4000  -  press Play in Unity.
echo Check every server module: node tools\dev\e2e-money.mjs
echo To stop: press Ctrl+C in the "Farmverse emulators" window (that saves the local farm).
echo Start with --fresh to begin from an empty backend.
pause
exit /b 0

:fail
echo.
echo [!] Something failed above.
pause
exit /b 1
