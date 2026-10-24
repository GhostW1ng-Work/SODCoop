# SodCoop: co-op multiplayer for Shadows of Doubt

**English** · [Русский](#русский)

Play Shadows of Doubt together with friends: one city, one clock, the same citizens, cases and side jobs, for 2–4 players.

**SodCoop is completely free.** If you enjoy it and want to support development: [Boosty](https://boosty.to/flyingfrogs) · [Patreon](https://patreon.com/FlyingFrogs)

[Discord](https://discord.gg/7JTs84Qt6w)

## Features

**Shared world**
- Citizens are synced.
- Doors, locks, windows, lights, breakers, elevators, furniture and items are synced.
- Security systems: alarms, cameras, shutters and guards react to every player.
- Time and weather are shared.

**Cases and side jobs together**
- Job boards, accepted jobs, objectives, phone calls and hand-in forms are shared.
- Murder cases are shared: victims, crime scenes and evidence.
- Right-click an item on your case board and choose **Share with players** to send it to the others.
- Rewards (money, social credit, reward items) go to every player, or only to whoever handed the case in, depending on the server rules.

**Players**
- Your own look, first name and surname in the **Character** editor.
- Each player has their own inventory, money and keys on every server.
- A downed player can be revived by a teammate (hold **H**).
- Player markers on the map.

**Game modifiers**
- The host's game modifiers apply to every player.
- Film Noir and Short-Sighted (everyone starts with glasses).
- Gambling Debt: every player has their own debt, and debt collectors come for every player who misses a payment.
- House Arrest: only the police react to a player outside home.
- Fame & Fortune: fans and haters approach every player.
- Snail Nemesis: every player has their own snail, and the others can see it.
- Rat Detective: every player is rat-sized; the others see you as a tiny detective.
- Permadeath and Ironman: a client who dies becomes a ghost and can only watch; if the host dies, the game is over for everyone. Without Ironman the last save is loaded instead.

**Hosting**
- Configurable server rules.
- LAN discovery, optional password, direct connect by IP.
- Joining players download the host's save automatically.

**Convenience**
- Checks for new versions at game start; one click to update.
- **Collect logs** button that creates a zip for bug reports.
- Cheat menu (F2), off by default.
- Menus in English and Russian.

## Installation
1. Download the latest `SodCoop-x.y.z.zip` from [Releases](https://github.com/GhostW1ng-Work/SODCoop/releases/latest).
2. Open the game folder. On Steam: right-click Shadows of Doubt → Manage → Browse local files.
3. **Extract everything from the archive into the game folder**, next to `Shadows of Doubt.exe`.
4. Start the game.
5. The main menu now has **Multiplayer** and **Character** buttons.

**All players need the same mod version and the same game version.**
From 0.13.0 on, the game offers new mod versions at start: press **Update** and restart.

## How to play
- **Host:** Multiplayer → Host → optionally **Server rules...** → **Host: load save** or **Host: new game**.
- **Join:** Multiplayer → pick the server from the list → Connect, or use **Direct** with the host's IP address.

## Bug reports
Multiplayer → **Collect logs** puts a zip on your desktop. Send it with a short description of what happened to our [Discord](https://discord.gg/7JTs84Qt6w).

## Uninstall
Delete `BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini` and `.doorstop_version` from the game folder.

## Use it for your own mods
The code is MIT-licensed: fork it, copy parts of it, or use this repository as the base for your own Shadows of Doubt mods.

## Made with Claude
This mod was built with [Claude](https://claude.ai) (Anthropic) as a coding partner: reverse engineering the game's native code, writing the networking and sync, and fixing bugs from playtest logs. Testing, decisions and direction come from the author and the players. The approach follows the [Co-op Anything](https://github.com/cranebeaker/coop-anything) playbook.

## Credits
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1)
- [LiteNetLib](https://github.com/RevenantX/LiteNetLib) (MIT)
- [Co-op Anything](https://github.com/cranebeaker/coop-anything) (MIT)
- Shadows of Doubt by ColePowered Games

## License
MIT, see [LICENSE.txt](LICENSE.txt).

---

# Русский

Играйте в Shadows of Doubt вместе с друзьями: один город, одно время, одни и те же жители, дела и подработки, от 2 до 4 игроков.

**SodCoop полностью бесплатный.** Если мод нравится и хочется поддержать разработку: [Boosty](https://boosty.to/flyingfrogs) · [Patreon](https://patreon.com/FlyingFrogs)

[Discord](https://discord.gg/7JTs84Qt6w)

## Возможности

**Общий мир**
- Синхронизированы жители.
- Двери, замки, окна, свет, щитки, лифты, мебель и предметы синхронизированы.
- Системы охраны: тревоги, камеры, защитные шторы и охранники реагируют на каждого игрока.
- Время и погода общие;

**Дела и подработки вместе**
- Общие доски объявлений, взятые задания, цели, звонки и бланки сдачи дела.
- Общие дела об убийствах: жертвы, места преступлений и улики.
- Нажмите правой кнопкой на улику на доске дела и выберите **Share with players**, чтобы отправить её остальным.
- Награды (деньги, социальный кредит, предметы) получает каждый игрок или только тот, кто сдал дело, в зависимости от правил сервера.

**Игроки**
- Свой внешний вид, имя и фамилия в редакторе **Персонаж**.
- У каждого игрока на каждом сервере свой инвентарь, деньги, ключи.
- Раненого игрока может поднять напарник (держать **H**);
- Отметки игроков на карте.

**Модификаторы игры**
- Модификаторы хоста действуют на всех игроков.
- «Фильм нуар» и «Близорукость» (каждый начинает с очками).
- «Долг за азартные игры»: у каждого игрока свой долг, и коллекторы приходят к каждому, кто пропустил платёж.
- «Домашний арест»: на игрока вне дома реагирует только полиция.
- «Слава и богатство»: поклонники и ненавистники подходят к каждому игроку.
- «Улитка-заклятый враг»: у каждого игрока своя улитка, и остальные её видят.
- «Крысиный детектив»: все игроки размером с крысу, остальные видят вас крошечным детективом.
- «Перманентная смерть» и «Железный человек»: погибший клиент становится призраком и может только наблюдать; если погиб хост, игра заканчивается для всех. Без «Железного человека» вместо этого загружается последнее сохранение.

**Сервер**
- Настраиваемые параметры сервера
- Поиск серверов в локальной сети, пароль, прямое подключение по IP.
- Подключившийся игрок сам скачивает сохранение хоста.

**Удобства**
- Проверка новых версий при запуске игры и обновление одной кнопкой.
- Кнопка **Собрать логи** делает архив для сообщения об ошибке.
- Меню читов (F2), по умолчанию выключено.
- Меню на русском и английском.

## Установка
1. Скачайте последний `SodCoop-x.y.z.zip` в [Releases](https://github.com/GhostW1ng-Work/SODCoop/releases/latest).
2. Откройте папку игры. В Steam: правой кнопкой по Shadows of Doubt → Управление → Просмотреть локальные файлы.
3. **Распакуйте всё из архива в папку игры**, рядом с `Shadows of Doubt.exe`.
4. Запустите игру.
5. В главном меню появятся кнопки **Мультиплеер** и **Персонаж**.

**У всех игроков должна быть одна и та же версия мода и одна и та же версия игры.**
Начиная с 0.13.0 игра сама предлагает новую версию мода при запуске: нажмите **Обновить** и перезапустите игру.

## Как играть
- **Хост:** Мультиплеер → Создать → при желании **Правила сервера...** → **Загрузить сейв** или **Новая игра**.
- **Подключиться:** Мультиплеер → выберите сервер в списке → Подключиться, или **По адресу** с IP-адресом хоста.

## Сообщить об ошибке
Мультиплеер → **Собрать логи** положит архив на рабочий стол. Отправьте его с коротким описанием, что случилось, в наш [Discord](https://discord.gg/7JTs84Qt6w).

## Удаление
Удалите из папки игры `BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini` и `.doorstop_version`.

## Используйте для своих модов
Код под лицензией MIT: делайте форки, берите части кода или используйте репозиторий как основу для своих модов к Shadows of Doubt.

## Сделано с Claude
Мод сделан вместе с [Claude](https://claude.ai) (Anthropic) как напарником по коду: разбор нативного кода игры, сетевая часть и синхронизация, исправление ошибок по логам тестов. Тестирование, решения и направление разработки — за автором и игроками. Подход основан на руководстве [Co-op Anything](https://github.com/cranebeaker/coop-anything).

## Благодарности
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1)
- [LiteNetLib](https://github.com/RevenantX/LiteNetLib) (MIT)
- [Co-op Anything](https://github.com/cranebeaker/coop-anything) (MIT)
- Shadows of Doubt от ColePowered Games

## Лицензия
MIT, см. [LICENSE.txt](LICENSE.txt).
