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
    Важно: сервер должен называться именно `context7` — от этого зависят имена инструментов
    `mcp__context7__*` в агентах. Если ставите Context7 как плагин, имена будут другими — поправьте строку
    `tools:` в агентах или просто удалите её (тогда агент получит все инструменты сессии).
2. Заполните таблицу ответов в `docs/product/movie-night.md`.
3. Создайте двух ботов в BotFather (рабочий и тестовый), для тестового — `/newapp`.
4. `claude` → `/agents` (должно быть 5) → `/feature Этап 0 из docs/product/movie-night.md: каркас платформы без модулей`.
5. Дальше — этапы 1–4 по одному, после каждого проверяйте вручную в Telegram и коммитьте.

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
