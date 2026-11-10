@echo off
echo A compilar o KoPhonetic.exe...
dotnet build -c Release
if errorlevel 1 goto err

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
  echo Inno Setup 6 nao encontrado. Instale-o de https://jrsoftware.org/isdl.php
  goto err
)

echo A criar o instalador...
"%ISCC%" installer.iss
if errorlevel 1 goto err

echo.
echo Pronto: Output\KoPhonetic-Setup.exe
goto end

:err
echo Falhou.
exit /b 1

:end
