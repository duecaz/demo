@echo off
rem Compila Trazos.exe sin instalar nada: usa el compilador de C# que Windows ya
rem trae dentro (.NET Framework 4.x) y las DLL de WPF del GAC. Si tienes el SDK
rem de .NET instalado, es mejor el otro camino:  dotnet publish -c Release
setlocal enabledelayedexpansion

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo No encuentro el compilador de C# de Windows^; instala el SDK de .NET y usa:
  echo    dotnet publish -c Release
  pause
  exit /b 1
)

set "GAC=%WINDIR%\Microsoft.NET\assembly\GAC_MSIL"
set REFS=
call :ref PresentationFramework || goto :fin
call :ref PresentationCore      || goto :fin
call :ref WindowsBase           || goto :fin
call :ref System.Xaml           || goto :fin

echo Compilando...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:"%~dp0Trazos.exe" !REFS! "%~dp0Program.cs"
if errorlevel 1 goto :fin

echo.
echo Listo: "%~dp0Trazos.exe"
pause
exit /b 0

rem Busca una DLL en el GAC sin depender del nombre de la carpeta de version.
:ref
set "HIT="
for /f "delims=" %%F in ('dir /b /s "%GAC%\%~1\%~1.dll" 2^>nul') do set "HIT=%%F"
if not defined HIT (
  echo No encuentro %~1.dll en el GAC.
  exit /b 1
)
set REFS=!REFS! /r:"!HIT!"
exit /b 0

:fin
echo.
echo La compilacion ha fallado.
pause
exit /b 1
