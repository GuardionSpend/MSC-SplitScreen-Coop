@echo off
REM ============================================================
REM  Сборка мода Split Screen Co-op БЕЗ Visual Studio.
REM  Просто дважды кликни по этому файлу на Windows.
REM  Скрипт сам найдёт компилятор C# и установленную игру.
REM ============================================================
setlocal enabledelayedexpansion
cd /d "%~dp0"

REM ---------- 1) ищем встроенный компилятор C# ----------
set "CSC="
for %%D in (Framework64 Framework) do (
  for /f "delims=" %%C in ('dir /b /ad /o-n "C:\Windows\Microsoft.NET\%%D\v4*" 2^>nul') do (
    if exist "C:\Windows\Microsoft.NET\%%D\%%C\csc.exe" if not defined CSC set "CSC=C:\Windows\Microsoft.NET\%%D\%%C\csc.exe"
  )
)
if not defined CSC (
  echo [!] Не найден csc.exe в C:\Windows\Microsoft.NET
  echo     Установи .NET Framework 4.x ^(обычно уже стоит на Windows^).
  pause & exit /b 1
)
echo Компилятор: %CSC%

REM ---------- 2) ищем установленную игру ----------
set "GAME="
REM сначала стандартные места
for %%P in (
  "C:\Program Files (x86)\Steam\steamapps\common\My Summer Car"
  "C:\Program Files\Steam\steamapps\common\My Summer Car"
  "D:\SteamLibrary\steamapps\common\My Summer Car"
  "E:\SteamLibrary\steamapps\common\My Summer Car"
  "D:\Steam\steamapps\common\My Summer Car"
  "E:\Steam\steamapps\common\My Summer Car"
) do (
  if not defined GAME if exist "%%~P\mysummercar_Data" set "GAME=%%~P"
)
REM если не нашли — пробуем прочитать библиотеки Steam
if not defined GAME (
  for %%R in ("C:\Program Files (x86)\Steam" "C:\Program Files\Steam") do (
    if exist "%%~R\steamapps\libraryfolders.vdf" (
      for /f "tokens=2 delims=^"" %%L in ('findstr /i "\"path\"" "%%~R\steamapps\libraryfolders.vdf"') do (
        set "CAND=%%L"
        set "CAND=!CAND:\\=\!"
        if not defined GAME if exist "!CAND!\steamapps\common\My Summer Car\mysummercar_Data" set "GAME=!CAND!\steamapps\common\My Summer Car"
      )
    )
  )
)
if not defined GAME (
  echo [!] Не нашёл установленную игру автоматически.
  echo     Открой build.bat в блокноте и впиши путь вручную:
  echo        set "GAME=ПУТЬ\К\My Summer Car"
  pause & exit /b 1
)
echo Игра: %GAME%

set "MANAGED=%GAME%\mysummercar_Data\Managed"

REM ---------- 3) собираем ссылки на DLL ----------
set "REFS="
for %%f in ("%MANAGED%\*.dll") do set "REFS=!REFS! /reference:"%%f""
if exist "%GAME%\Mods\References" (
  for %%f in ("%GAME%\Mods\References\*.dll") do set "REFS=!REFS! /reference:"%%f""
)

REM проверка, что MSCLoader вообще нашёлся
set "HASLOADER="
for %%f in ("%MANAGED%\MSCLoader.dll" "%GAME%\Mods\References\MSCLoader.dll" "%GAME%\Mods\MSCLoader.dll") do (
  if exist "%%~f" set "HASLOADER=1"
)
if not defined HASLOADER (
  echo [!] Не нашёл MSCLoader.dll — установлен ли MSCLoader?
  echo     Без него мод не соберётся и не запустится.
  echo     https://github.com/piotrulos/MSCModLoader/releases
  pause & exit /b 1
)

REM ---------- 4) компиляция ----------
echo Компилирую...
REM /nostdlib + /noconfig — чтобы собрать под рантайм игры (.NET 2.0/3.5),
REM а не под v4.0 (иначе ошибка "runtime version v4.0.30319" и System.Action).
REM mscorlib.dll/System.dll/System.Core.dll берутся из папки Managed (см. цикл REFS выше).
"%CSC%" /noconfig /nostdlib /target:library /nologo /out:SplitScreenCoop.dll !REFS! SplitScreenCoop.cs
if errorlevel 1 (
  echo.
  echo [!] Ошибка компиляции — смотри сообщения выше.
  echo     Частая причина: другая версия MSCLoader. Ищи пометки [API] в .cs
  pause & exit /b 1
)

REM ---------- 5) копируем в папку модов ----------
if exist "%GAME%\Mods" (
  copy /y SplitScreenCoop.dll "%GAME%\Mods\" >nul
  echo.
  echo [OK] Готово! SplitScreenCoop.dll лежит в "%GAME%\Mods"
  echo      Запускай игру и проверяй мод в меню MSCLoader.
) else (
  echo [OK] SplitScreenCoop.dll собран рядом с этим файлом — скопируй его в папку Mods вручную.
)
pause
