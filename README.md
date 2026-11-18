# Агентская разработка: Telegram-бот + Mini App (Claude Code)

## Что внутри
```
CLAUDE.md                              — общие правила, стек, структура, модульность
.claude/agents/
  architect.md                         — спецификация + вопросы (opus)
  backend-developer.md                 — .NET: домен, API, бот, EF Core (sonnet)
  frontend-developer.md                — Mini App: React + TS (sonnet)
  tester.md                            — xUnit / WebApplicationFactory / Vitest (sonnet)
  critic.md                            — независимая проверка, read-only (opus)
.claude/skills/
  telegram-mini-app/SKILL.md           — initData, deep links, тема, кнопки
  bot-module/SKILL.md                  — контракт IBotModule, структура и чек-лист модуля
.claude/commands/feature.md            — оркестрация цикла
docs/product/movie-night.md            — бриф первого модуля + этапы
docs/tasks/_TEMPLATE.md                — шаблон спецификации
```
Агенты = роли (кто делает). Скиллы = знания (как правильно), их читают несколько ролей.

## Старт
0. Установите .NET 10 SDK (`dotnet --list-sdks` → 10.0.x) и Node.js LTS.
1. Скопируйте в корень пустого репозитория (включая `.mcp.json`), `git init`.
1a. Context7: получите ключ на context7.com (без ключа работает, но с жёсткими лимитами), задайте
    переменную окружения `CONTEXT7_API_KEY`. При первом запуске `claude` подтвердите сервер из `.mcp.json`,
    проверьте `/mcp` — `context7` должен быть `connected`.
    Вместо переменной окружения ключ можно положить в `.claude/settings.local.json` (`env.CONTEXT7_API_KEY`) —
    файл игнорируется git.
    Важно: сервер должен называться именно `context7` — от этого зависят имена инструментов
    `mcp__context7__*` в агентах. Если ставите Context7 как плагин, имена будут другими — поправьте строку
    `tools:` в агентах или просто удалите её (тогда агент получит все инструменты сессии).
2. Заполните таблицу ответов в `docs/product/movie-night.md`.
3. Создайте двух ботов в BotFather (рабочий и тестовый), для тестового — `/newapp`.
4. `claude` → `/agents` (должно быть 5) → `/feature Этап 0 из docs/product/movie-night.md: каркас платформы без модулей`.
5. Дальше — этапы 1–4 по одному, после каждого проверяйте вручную в Telegram; изменения — через ветку и PR (см. `CLAUDE.md`,
   «Ветки и pull request»).

## Если меняете стек фронтенда
Замените React/Vite в `CLAUDE.md`, `frontend-developer.md`, `tester.md` (Vitest) и команды сборки.

## Локальный запуск
1. Токен тестового бота (BotFather) — только в user-secrets, не в коде и не в репозитории:
   ```
   dotnet user-secrets set "Bot:Token" "<токен>" --project src/Bot.Host
   ```
   Секрет webhook (`^[A-Za-z0-9_-]{1,256}$`) — тоже только в user-secrets / env:
   ```
   dotnet user-secrets set "Bot:WebhookSecret" "<случайная строка>" --project src/Bot.Host
   ```
   Без `Bot:Token` и `Bot:WebhookSecret` Host не стартует (ошибка называет ключ).
2. Mini App открывается в Telegram только по HTTPS — поднимите туннель на порт Host (`http://localhost:5080`),
   например `cloudflared tunnel --url http://localhost:5080` или `ngrok http 5080`.
2a. Адрес туннеля — в `Bot:PublicUrl` (`dotnet user-secrets set "Bot:PublicUrl" "https://<туннель>" --project src/Bot.Host`).
   При старте Host сам регистрирует webhook (`<PublicUrl>/bot/webhook`) и список команд бота в Telegram
   (отключается `Bot:SetupOnStartup=false`). Кнопка меню бота настраивается вручную в BotFather.
3. В BotFather для тестового бота: `/newapp` → URL приложения = адрес туннеля.
4. Фронтенд, вариант A (раздаёт Host): `cd webapp && npm run build`, затем `dotnet run --project src/Bot.Host`.
   Host раздаёт `webapp/dist` (путь меняется ключом `WebApp:DistPath`).
   Вариант B (разработка): `cd webapp && npm run dev` — Vite проксирует `/api` на `http://localhost:5080`;
   Host при этом запущен отдельно, туннель указывает на dev-сервер Vite; `/bot` (webhook) тоже проксируется на Host.
   Для туннеля добавьте его хост в `server.allowedHosts` в `webapp/vite.config.ts`
   (например `['.trycloudflare.com']`; ведущая точка разрешает домен и поддомены; `true` не использовать — риск DNS rebinding);
   по умолчанию Vite отвечает «Blocked request» на чужие хосты.
5. БД — SQLite `data/bot.db` (путь относительно ContentRoot Host, т.е. `src/Bot.Host/data/bot.db`), миграции применяются при старте.
   Путь меняется ключом `ConnectionStrings:Default`.

## Секреты и конфигурация
Секреты не хранятся в репозитории: ни в коде, ни в `appsettings*.json`, ни в документации. Локально — user-secrets или
переменные окружения, на сервере — `/etc/multibot/multibot.env` (шаблон — `deploy/multibot.env.example`, права 600).

| Ключ | Секрет | Локально | Сервер |
|---|---|---|---|
| `Bot:Token` | да | user-secrets / переменная `Bot__Token` | да (`Bot__Token`) |
| `Bot:WebhookSecret` | да | user-secrets / переменная `Bot__WebhookSecret` | да (`Bot__WebhookSecret`) |
| `Bot:PublicUrl` | нет | user-secrets / переменная `Bot__PublicUrl` | да (`Bot__PublicUrl`) |
| `Bot:Username` | нет | user-secrets / переменная `Bot__Username` | нет |
| `Bot:MiniAppShortName` | нет | user-secrets / переменная `Bot__MiniAppShortName` | нет |
| `Bot:SetupOnStartup` | нет | user-secrets / переменная `Bot__SetupOnStartup` | нет |
| `Bot:ReplyTimeout` | нет | user-secrets / переменная `Bot__ReplyTimeout` | нет |
| `WebApp:DistPath` | нет | переменная `WebApp__DistPath` (по умолчанию `webapp/dist`) | да (`WebApp__DistPath`) |
| `ConnectionStrings:Default` | нет | переменная `ConnectionStrings__Default` (по умолчанию `data/bot.db`) | да (`ConnectionStrings__Default`) |
| `CONTEXT7_API_KEY` | да | переменная окружения или `.claude/settings.local.json` (`env.CONTEXT7_API_KEY`); только для разработки агентами | нет |
| `ASPNETCORE_ENVIRONMENT` | нет | не требуется | да (`ASPNETCORE_ENVIRONMENT`) |
| `ASPNETCORE_URLS` | нет | `launchSettings.json` | да (`ASPNETCORE_URLS`) |
| `Logging__LogLevel__Microsoft.EntityFrameworkCore` | нет | `appsettings*.json` | да (`Logging__LogLevel__Microsoft.EntityFrameworkCore`) |

- Переменные `VITE_*` попадают в клиентский бандл — секреты в них запрещены.
- При утечке: токен бота — BotFather `/revoke`; секрет webhook — новый `openssl rand -hex 32` и перезапуск сервиса;
  ключ Context7 — перевыпуск на context7.com.
- Автотест-сканер секретов (`tests/Bot.Platform.Tests/RepositoryHygiene/`) входит в `dotnet test`; отдельно:
  `dotnet test --filter RepositoryHygiene` (тесты гигиены), сканер секретов отдельно —
  `dotnet test --filter "FullyQualifiedName~RepositorySecretsTests"`.

## CI, ветки и релизы
- `main` защищена: изменения попадают в неё только через pull request (squash merge) с зелёной проверкой `build`.
  Правила — `CLAUDE.md`, «Ветки и pull request».
- `ci` (`.github/workflows/ci.yml`): запускается на PR в `main`, push в `main`, вручную и из `release`. Job `build`:
  actionlint, `dotnet build -warnaserror`, `dotnet test --nologo`, `dotnet format --verify-no-changes`,
  `npm ci`/`build`/`test`/`lint` в `webapp`, проверка отсутствия внешних адресов в `index.html`. Имя обязательной
  проверки — `build`.
- `release` (`.github/workflows/release.yml`): только вручную из ветки `main` — Actions → release → Run workflow,
  версия `vX.Y.Z`. Прогоняет `ci`, собирает self-contained архив `linux-x64` (`multibot.tar.gz` + `.sha256`) и
  публикует GitHub Release. Развёртывание на сервере — вручную: `DEPLOY_CHECKLIST.md`, раздел «Релиз из GitHub».

Ручные настройки GitHub (кодом не задаются, делает владелец репозитория):
1. **Ruleset для `main`** (Settings → Rules → Rulesets → New branch ruleset, target: default branch, Enforcement:
   Active, bypass list — пустой): Restrict deletions; Block force pushes; Require a pull request before merging
   (required approvals = 0 — разработчик один; при появлении второго — 1); Require status checks to pass → `build`
   (Require branches to be up to date — вкл.); Require linear history. Для приватного репозитория на бесплатном
   плане rulesets не применяются — тогда правило держится только процессом.
2. **Settings → General → Pull Requests:** оставить только «Allow squash merging» (default commit message — «Pull
   request title»), выключить merge commits и rebase merging; включить «Automatically delete head branches».
3. **Settings → Actions → General:** Workflow permissions = «Read repository contents and packages permissions»;
   выключить «Allow GitHub Actions to create and approve pull requests»; Fork PR workflows — «Require approval for
   all outside collaborators».
4. **Settings → Environments → New environment `release`:** Deployment branches and tags → Selected branches →
   `main`. Секретов не требуется (публикация — `GITHUB_TOKEN`).
5. Проверка: после первого PR в Rulesets → Insights видно, что прямой push в `main` отклонён.

## Модуль MovieNight (`src/Modules/MovieNight`)
Этап 1: группы (создание, ссылка-приглашение, вступление, порядок участников, выход, переименование, перевыпуск
ссылки, исключение владельцем). API — `/api/movienight`, таблицы `movienight_*` (история миграций
`__ef_migrations_movienight`), миграции применяются при старте. Ссылки-приглашения строятся только при заданном
`Bot:Username` (иначе `miniAppLink`/`botLink` = `null`, и в Mini App кнопка «Пригласить» скрыта); желательно задать и
`Bot:MiniAppShortName` (см. раздел ниже). Уведомления («Петя теперь в группе…») получают только пользователи, открывавшие
Mini App или писавшие боту.

## Ссылки на бота и Mini App (`Bot:Username`, `Bot:MiniAppShortName`)
Нужны для ссылок-приглашений (`IDeepLinks`); без `Bot:Username` ссылки не строятся (`null`), Host стартует как обычно.
- `Bot:Username` — имя бота **без** `@` (`^[A-Za-z][A-Za-z0-9_]{4,31}$`), видно в BotFather (`/mybots`).
- `Bot:MiniAppShortName` — short name Mini App: в BotFather `/newapp` (при создании просят short name; список —
  `/myapps`). Ссылка тогда `https://t.me/<бот>/<short name>?startapp=...`. Если не задан, ссылка ведёт на основное
  Mini App бота (BotFather → бот → Bot Settings → Configure Mini App / «Main Mini App»): `https://t.me/<бот>?startapp=...`.
- `Bot:ReplyTimeout` — срок ожидания текстового ответа пользователя (по умолчанию `00:10:00`, допустимо от 1 минуты до суток).
```
dotnet user-secrets set "Bot:Username" "<имя_бота>" --project src/Bot.Host
dotnet user-secrets set "Bot:MiniAppShortName" "<short_name>" --project src/Bot.Host
```

## telegram-web-app.js
Клиентский скрипт Telegram лежит в репозитории — `webapp/public/telegram-web-app.js` — и раздаётся нашим сервером
(`/telegram-web-app.js`, `Cache-Control: no-cache`); Mini App не обращается к `telegram.org`. Файл обновляется вручную:
1. скачать `https://telegram.org/js/telegram-web-app.js`;
2. заменить `webapp/public/telegram-web-app.js`;
3. пересобрать фронтенд (`cd webapp && npm run build`) и обновить строку ниже.

Версия: в самом файле нет номера версии (константа `webAppVersion = '6.0'` — значение по умолчанию, реальную версию
клиент сообщает в `tgWebAppVersion`; проверять `Telegram.WebApp.version` в клиенте). Дата последнего обновления файла:
2026-11-15.
