@echo off
setlocal
rem XamlCompiler.exe (net472) requires its own directory to resolve dependencies.
rem MSBuild launches it from the project directory, which makes it exit silently.
rem This wrapper cd's into the tool folder after making the json paths absolute.
set "TOOLDIR=C:\Users\vikas\.nuget\packages\microsoft.windowsappsdk\1.6.250602001\tools\net472"
set "IN=%~f1"
set "OUT=%~f2"
cd /d "%TOOLDIR%"
"%TOOLDIR%\XamlCompiler.exe" "%IN%" "%OUT%"
exit /b %ERRORLEVEL%