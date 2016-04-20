@echo off

rem Usage: bootstrap [path to game] [path to hook file]

set GAME_FOLDER="%ProgramFiles(x86)%\Battle.net\Hearthstone"
set HOOKS="%CD%\example_hooks"

if "%~1"=="" (
	echo No game folder supplied - using default: %GAME_FOLDER%
) else (
	set GAME_FOLDER="%~1"
)

if "%~2"=="" (
	echo No hook file supplied - using default: %HOOKS%
) else (
	set HOOKS="%~2"
)

if not exist %GAME_FOLDER% (
	echo Could not find game folder: %GAME_FOLDER%
	exit /B 1
)

if not exist %GAME_FOLDER%\Hearthstone_Data\Managed\Assembly-CSharp.dll (
	echo Game folder did not contain game DLLs: %GAME_FOLDER%
	exit /B 1
)

if not exist %HOOKS% (
	echo Could not find hooks file: %HOOKS%
	exit /B 1
)

echo Preparing...

rem Backup existing DLLs
copy /y %GAME_FOLDER%\Hearthstone_Data\Managed\Assembly-CSharp.dll %GAME_FOLDER%\Hearthstone_Data\Managed\Assembly-CSharp.dll.previous >nul
copy /y %GAME_FOLDER%\Hearthstone_Data\Managed\Assembly-CSharp-firstpass.dll %GAME_FOLDER%\Hearthstone_Data\Managed\Assembly-CSharp-firstpass.dll.previous >nul

rem Place the original DLLs (from our lib folder) in the game folder
copy /y Assembly-CSharp.dll %GAME_FOLDER%\Hearthstone_Data\Managed >nul
copy /y Assembly-CSharp-firstpass.dll %GAME_FOLDER%\Hearthstone_Data\Managed >nul

rem Save the original DLLs in the game folder too just for clarity
copy /y %GAME_FOLDER%\Hearthstone_Data\Managed\Assembly-CSharp.dll %GAME_FOLDER%\Hearthstone_Data\Managed\Assembly-CSharp.dll.original >nul
copy /y %GAME_FOLDER%\Hearthstone_Data\Managed\Assembly-CSharp-firstpass.dll %GAME_FOLDER%\Hearthstone_Data\Managed\Assembly-CSharp-firstpass.dll.original >nul

rem Modify DLLs

echo Hooking...

Hooker %GAME_FOLDER%\Hearthstone_Data %HOOKS% >nul

echo Done.
echo.
echo Original DLLs at: %GAME_FOLDER%\Hearthstone_Data\Managed\*.dll.original
echo Previous DLLs at: %GAME_FOLDER%\Hearthstone_Data\Managed\*.dll.previous
