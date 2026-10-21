@echo off
rem Собирает один AnnounceMe.exe в папку dist (нужен .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0)
dotnet publish src\AnnounceMe.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist
