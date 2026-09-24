@echo off
set "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR=C:\Program Files\dotnet"
set "MSBuildSDKsPath=C:\Program Files\dotnet\sdk\8.0.425\Sdks"
cd /d "C:\Users\vikas\CleanBoost"
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" src\CleanBoost.App\CleanBoost.App.csproj -t:Build -p:Configuration=Debug -v:m -nologo -p:UseXamlCompilerExecutable=false
