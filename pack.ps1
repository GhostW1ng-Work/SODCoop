# Собирает готовый архив для игроков: BepInEx 6 (IL2CPP) + мод. Архив распаковывается в папку игры (Steam или GOG) поверх
# чистой установки, после этого игру можно запускать.
#
#   powershell -ExecutionPolicy Bypass -File pack.ps1
#   powershell -ExecutionPolicy Bypass -File pack.ps1 -Cheats        # с включёнными читами (для тестов)
#
# BepInEx берётся из копии игры, где он уже стоит (по умолчанию «Shadows of Doubt 3»): только файлы самого BepInEx, без
# сгенерированных interop, кэша, логов и чужих настроек. Основную установку игры скрипт не трогает и ничего в игры не копирует.
param(
    [string]$BepInExFrom = "C:\SODMP\Shadows of Doubt 3",
    [string]$OutDir = (Join-Path $PSScriptRoot "dist"),
    [switch]$Cheats
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$src = Join-Path $PSScriptRoot "SODCoop\Class1.cs"
$m = Select-String -Path $src -Pattern 'ModVersion = "([^"]+)"' | Select-Object -First 1
if ($m -eq $null) { throw "ModVersion not found in $src" }
$version = $m.Matches[0].Groups[1].Value
Write-Host "SodCoop $version"

# сборка Release без PostBuild (он копирует dll в папки игры, в том числе в основную установку)
$noCopy = Join-Path $env:TEMP "sodcoop_nocopy.targets"
Set-Content -Path $noCopy -Value '<Project><Target Name="PostBuild" /></Project>' -Encoding ASCII
& dotnet build (Join-Path $PSScriptRoot "SODCoop\SODCoop.csproj") -c Release -nologo -v:m "-p:CustomAfterMicrosoftCommonTargets=$noCopy"
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$bin = Join-Path $PSScriptRoot "SODCoop\bin\Release\net6.0"

# BepInEx: проверяем, что это нужная версия
$bepLog = Join-Path $BepInExFrom "BepInEx\LogOutput.log"
if (Test-Path $bepLog) {
    $first = Get-Content $bepLog -TotalCount 1
    if ($first -notmatch "6\.0\.0-be\.788") { Write-Warning "BepInEx in $BepInExFrom is not be.788: $first" }
}
foreach ($f in "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "dotnet", "BepInEx\core") {
    if (-not (Test-Path (Join-Path $BepInExFrom $f))) { throw "missing $f in $BepInExFrom" }
}

$stage = Join-Path $OutDir "stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

foreach ($f in "winhttp.dll", "doorstop_config.ini", ".doorstop_version") { Copy-Item (Join-Path $BepInExFrom $f) $stage }
Copy-Item (Join-Path $BepInExFrom "dotnet") (Join-Path $stage "dotnet") -Recurse
New-Item -ItemType Directory -Force -Path (Join-Path $stage "BepInEx\plugins"), (Join-Path $stage "BepInEx\config") | Out-Null
Copy-Item (Join-Path $BepInExFrom "BepInEx\core") (Join-Path $stage "BepInEx\core") -Recurse
# базовые библиотеки Unity: без них BepInEx при первом запуске скачивает их из интернета
$unityLibs = Join-Path $BepInExFrom "BepInEx\unity-libs"
if (Test-Path $unityLibs) { Copy-Item $unityLibs (Join-Path $stage "BepInEx\unity-libs") -Recurse }

Copy-Item (Join-Path $bin "SODCoop.dll"), (Join-Path $bin "LiteNetLib.dll") (Join-Path $stage "BepInEx\plugins")

# настройки BepInEx: без консольного окна, лог пишется на диск сразу (чтобы после вылета в нём были последние строки)
@"
[Logging.Console]
Enabled = false

[Logging.Disk]
InstantFlushing = true
"@ | Set-Content -Path (Join-Path $stage "BepInEx\config\BepInEx.cfg") -Encoding UTF8

# настройки мода: читы игрокам выключены (остальное мод допишет сам при первом запуске)
if (-not $Cheats) {
@"
[Cheats]
Hotkeys = false
RevealAllPrintsKey =
RevealCaseKey =
"@ | Set-Content -Path (Join-Path $stage "BepInEx\config\com.flyingfrogs.sodcoop.cfg") -Encoding UTF8
}

@"
SodCoop $version - co-op for Shadows of Doubt / кооператив для Shadows of Doubt

=== RU ===
1. Откройте папку игры. Steam: ПКМ по игре -> Управление -> Просмотреть локальные файлы.
2. Распакуйте ВСЁ содержимое архива в эту папку (рядом с "Shadows of Doubt.exe").
3. Запустите игру. Первый запуск дольше обычного (1-3 минуты): BepInEx готовит файлы под вашу версию игры.
4. В главном меню появятся кнопки "Мультиплеер" и "Персонаж".
У всех игроков должна быть одна и та же версия мода ($version).
Удалить мод: удалите папки BepInEx, dotnet и файлы winhttp.dll, doorstop_config.ini, .doorstop_version.

=== EN ===
1. Open the game folder. Steam: right-click the game -> Manage -> Browse local files.
2. Extract EVERYTHING from this archive into that folder (next to "Shadows of Doubt.exe").
3. Start the game. The first start takes longer (1-3 minutes): BepInEx prepares files for your game version.
4. The main menu now has "Multiplayer" and "Character" buttons.
All players need the same mod version ($version).
Uninstall: delete the BepInEx and dotnet folders and the winhttp.dll, doorstop_config.ini, .doorstop_version files.

Logs for bug reports / логи для баг-репортов: BepInEx\LogOutput.log, BepInEx\UnityErrors.log
"@ | Set-Content -Path (Join-Path $stage "SodCoop_README.txt") -Encoding UTF8

@"
Third-party components / Сторонние компоненты:
- BepInEx 6.0.0-be.788 (IL2CPP): LGPL-2.1, https://github.com/BepInEx/BepInEx (BepInEx\core, winhttp.dll, doorstop_config.ini, dotnet)
- LiteNetLib: MIT, https://github.com/RevenantX/LiteNetLib (BepInEx\plugins\LiteNetLib.dll)
"@ | Set-Content -Path (Join-Path $stage "SodCoop_THIRD_PARTY.txt") -Encoding UTF8

$zip = Join-Path $OutDir ("SodCoop-" + $version + $(if ($Cheats) { "-cheats" } else { "" }) + ".zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $stage -Recurse -Force
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Done: $zip ($size MB)"
