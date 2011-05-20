@echo off
setlocal

%~D0
cd %~DP0

if not exist "%ALLUSERSPROFILE%\AnyGen" mkdir "%ALLUSERSPROFILE%\AnyGen" 2> nul > nul

c:
cd "%ALLUSERSPROFILE%\AnyGen"

if exist CoApp.AnyGen.dll RegAsm.exe /unregister /codebase CoApp.AnyGen.dll

erase /q *.old 2> nul
if exist CoApp.AnyGen.dll ren *.dll *.old
erase /q *.dll 2> nul
erase /q *.old 2> nul

%~D0
cd %~DP0

copy CoApp.Toolkit.dll "%ALLUSERSPROFILE%\AnyGen"
copy CoApp.AnyGen.dll "%ALLUSERSPROFILE%\AnyGen"

c:
cd "%ALLUSERSPROFILE%\AnyGen"

RegAsm.exe /codebase CoApp.AnyGen.dll > nul 2> nul


