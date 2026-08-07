@echo off
setlocal

set "REPO_URL=https://github.com/Breakerchap/MonoGame-MedivalChess.git"
set "BRANCH=master"
set "REPO_DIR=%~dp0MonoGame-MedivalChess"
set "SOLUTION=CrownAndSiege.sln"
set "PROJECT=MedivalChess.csproj"

echo ========================================
echo       Crown and Siege Launcher
echo ========================================
echo.

:: Check Git
where git >nul 2>&1
if errorlevel 1 (
    echo ERROR: Git is not installed or is not in PATH.
    echo Install Git from https://git-scm.com/
    pause
    exit /b 1
)

:: Check .NET
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK is not installed or is not in PATH.
    echo Install the .NET SDK from https://dotnet.microsoft.com/
    pause
    exit /b 1
)

:: Clone if the repo doesn't already exist
if not exist "%REPO_DIR%\.git" (
    echo Cloning repository...
    echo.

    git clone --branch "%BRANCH%" "%REPO_URL%" "%REPO_DIR%"

    if errorlevel 1 (
        echo.
        echo ERROR: Failed to clone repository.
        pause
        exit /b 1
    )
)

cd /d "%REPO_DIR%"

echo.
echo Fetching latest changes...
git fetch origin

if errorlevel 1 (
    echo.
    echo ERROR: git fetch failed.
    pause
    exit /b 1
)

echo.
echo Switching to %BRANCH%...
git switch "%BRANCH%"

if errorlevel 1 (
    echo.
    echo ERROR: Could not switch to branch %BRANCH%.
    pause
    exit /b 1
)

echo.
echo Pulling latest changes...
git pull --ff-only origin "%BRANCH%"

if errorlevel 1 (
    echo.
    echo ERROR: git pull failed.
    echo You may have local changes or the branch may have diverged.
    pause
    exit /b 1
)

echo.
echo Restoring dependencies...
dotnet restore "%SOLUTION%"

if errorlevel 1 (
    echo.
    echo ERROR: dotnet restore failed.
    pause
    exit /b 1
)

echo.
echo Building...
dotnet build "%SOLUTION%" --configuration Debug --no-restore

if errorlevel 1 (
    echo.
    echo ERROR: Build failed.
    pause
    exit /b 1
)

echo.
echo ========================================
echo            Starting game...
echo ========================================
echo.

dotnet run --project "%PROJECT%" --configuration Debug --no-build

set "EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%EXIT_CODE%"=="0" (
    echo Game exited with code %EXIT_CODE%.
    pause
)

exit /b %EXIT_CODE%