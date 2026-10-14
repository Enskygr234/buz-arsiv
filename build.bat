@echo off
rem Tek dosyalik BuzArsiv.exe uretir: EXE\BuzArsiv.exe
rem Not: klasor yolu cok uzunsa derleme "apphost.exe bulunamadi" diye biter; depoyu kisa bir yola (C:\kod\buz-arsiv) tasi.
dotnet publish BuzArsiv\BuzArsiv.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o EXE
if errorlevel 1 (
  echo Derleme basarisiz.
  exit /b 1
)
echo.
echo Hazir: EXE\BuzArsiv.exe
