@echo off
cd /d "%~dp0"
if exist ".tools\dotnet\dotnet.exe" (
  ".tools\dotnet\dotnet.exe" run --project src\Halill\Halill.csproj
) else (
  dotnet run --project src\Halill\Halill.csproj
)
