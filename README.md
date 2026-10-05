# Block Team Change + Phantom Fix

Плагин CounterStrikeSharp для CS2 с двумя независимыми функциями:

- блокирует `jointeam` и `spectate` во время freeze time;
- делает только что появившийся pawn неуязвимым и не блокирующим, пока клиент не подключён полностью.
- после перехода из Spectator в T/CT оставляет игрока мёртвым до начала следующего раунда.
- после повторного подключения в середине раунда также не даёт автоматически ожить до следующего раунда.
- переход в Spectator разрешён даже во время freeze time и не блокирует spectator camera.

## Требования

- CounterStrikeSharp API `1.0.376`;
- .NET 10;
- CS2-сервер с CounterStrikeSharp.

## Установка

Скачайте DLL из GitHub Actions или выполните `dotnet build BlockTeamChange.csproj -c Release`. Скопируйте `bin/Release/net10.0/BlockTeamChange.dll` в `addons/counterstrikesharp/plugins/BlockTeamChange/` и перезапустите плагин.

Конфигурация и команды не требуются. В начале раунда смена команды снова разрешается после окончания freeze time.

Переход в T/CT во время текущего раунда не даёт немедленный respawn: после штатного создания pawn плагин сразу применяет обычную смерть, чтобы состояние игрока и scoreboard оставались корректными. На следующем `round_start` ограничение снимается и стандартный spawn выполняется игрой.

## Сборка и проверки

Обычная сборка выполняется workflow `build`. Workflow `dependency-audit` еженедельно проверяет уязвимости NuGet и публикует отчёт об устаревших пакетах.
