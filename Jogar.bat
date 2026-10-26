@echo off
set "GAME_EXE=%~dp0TestePersonagem\Builds\Windows\TestePersonagem.exe"
if not exist "%GAME_EXE%" (
    echo A versao Windows do jogo ainda nao foi gerada nesta pasta.
    echo Abra a pasta TestePersonagem no Unity Hub e gere a build Windows.
    echo Versao do Unity indicada pelo projeto: 6000.6.7f1.
    echo.
    pause
    exit /b 1
)
start "" "%GAME_EXE%" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -force-d3d11
