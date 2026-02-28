@echo off
SETLOCAL

echo Checking prerequisites...
where git >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
  echo Git not found in PATH. Install Git or add it to PATH.
  exit /b 1
)

where docker >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
  echo Docker not found in PATH. Install Docker or add it to PATH.
  exit /b 1
)

echo Pulling latest code from origin/main...
git pull origin main
if %ERRORLEVEL% NEQ 0 (
  echo Git pull failed with error %ERRORLEVEL%.
  exit /b %ERRORLEVEL%
)

echo Bringing down existing containers (if any)...
docker compose -f infra/docker/compose.prod.yml down --volumes --remove-orphans
if %ERRORLEVEL% NEQ 0 (
  echo docker compose down returned %ERRORLEVEL% (continuing)
)

echo Building and starting services (detached)...
docker compose -f infra/docker/compose.prod.yml up -d --build
if %ERRORLEVEL% NEQ 0 (
  echo docker compose up failed with %ERRORLEVEL%.
  exit /b %ERRORLEVEL%
)

echo Optional cleanup: pruning unused images and containers...
docker container prune -f
docker image prune -f

echo Deployment finished successfully.
ENDLOCAL
exit /b 0
