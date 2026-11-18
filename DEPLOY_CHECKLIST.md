# Деплой на тестовый сервер — чек-лист

Тестовый запуск Этапа 0: один Linux-сервер, на нём бот, API и Mini App. Схема такая:
`Telegram / браузер → nginx (HTTPS, 443) → Kestrel 127.0.0.1:5080 → SQLite-файл`.

## Готовность проекта

Проверено 15.11.2026: собрал `dotnet publish -c Release`, запустил в `Production` с конфигурацией из переменных окружения.
`/health` ответил 200, `/` вернул `index.html`, `/api/platform/me` без авторизации дал 401,
webhook без секрета дал 401. Миграции создали БД по абсолютному пути.

**Вывод: блокеров нет, проект можно публиковать.** Учтите при настройке:

| # | Что | Что сделать |
|---|-----|-------------|
| 1 | По умолчанию путь к `webapp/dist` — `../../webapp/dist` от ContentRoot. После publish такого пути нет | Задать `WebApp__DistPath` (абсолютный путь) |
| 2 | БД по умолчанию — `data/bot.db` в папке приложения. При перезаливке папки её легко стереть | Задать абсолютный путь вне папки релиза: `ConnectionStrings__Default` |
| 3 | Без `ASPNETCORE_URLS` Kestrel слушает порт по умолчанию | Задать `ASPNETCORE_URLS=http://127.0.0.1:5080` |
| 4 | EF Core пишет каждый SQL-запрос на уровне Information. Значения параметров не логируются, но шума много | Задать `Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning` |
| 5 | При старте Host регистрирует webhook на свой `PublicUrl`. Второй экземпляр с тем же токеном (например, локальный) перехватит webhook | Для сервера завести отдельного тестового бота или локально ставить `Bot__SetupOnStartup=false` |
| 6 | SQLite работает в режиме WAL | В бэкап копировать `bot.db`, `bot.db-wal` и `bot.db-shm` при остановленном сервисе (или через `sqlite3 .backup`) |
| 7 | Работает один процесс, SQLite, в webhook нет очереди | Только один экземпляр, никакого масштабирования |

Forwarded headers и HTTPS-редирект в приложении не нужны: TLS завершает nginx, а приложение не использует ни схему, ни IP клиента.

---

## 1. Что подготовить заранее

- [ ] Сервер: Ubuntu 24.04 LTS (или аналог), 1 vCPU, 1 ГБ RAM, 10 ГБ диска, публичный IPv4.
- [ ] Домен или поддомен (например `bot-test.example.com`), A-запись указывает на сервер.
      Telegram принимает webhook только по HTTPS с валидным сертификатом на портах 443, 80, 88 или 8443. Берём 443.
- [ ] Тестовый бот в BotFather (`/newbot`), сохранён токен.
- [ ] Сгенерирован секрет webhook (`[A-Za-z0-9_-]`, до 256 символов): `openssl rand -hex 32`.
- [ ] SSH-доступ по ключу.

## 2. Настройка сервера (один раз)

- [ ] Обновить систему и поставить пакеты:
  ```bash
  sudo apt update && sudo apt upgrade -y
  ```
  ```bash
  sudo apt install -y nginx certbot python3-certbot-nginx sqlite3 ufw
  ```
- [ ] Поставить ASP.NET Core Runtime 10 (не SDK). Способ установки — по инструкции Microsoft для вашего дистрибутива.
      Проверка: `dotnet --list-runtimes` показывает `Microsoft.AspNetCore.App 10.0.x`.
      Если ставить runtime не хочется, используйте self-contained publish (раздел 3, вариант Б).
- [ ] Файрвол: открыты только SSH и HTTP/HTTPS.
  ```bash
  sudo ufw allow OpenSSH && sudo ufw allow 'Nginx Full' && sudo ufw enable
  ```
- [ ] Системный пользователь и каталоги:
  ```bash
  sudo useradd --system --home /opt/multibot --shell /usr/sbin/nologin multibot
  ```
  ```bash
  sudo mkdir -p /opt/multibot/app /opt/multibot/webapp /var/lib/multibot /etc/multibot
  ```
  ```bash
  sudo chown -R multibot:multibot /opt/multibot /var/lib/multibot
  ```
- [ ] Файл с секретами `/etc/multibot/multibot.env` создаётся из шаблона `deploy/multibot.env.example`
  (заполните значения в `<…>` и адрес `Bot__PublicUrl`), права `600`, владелец root. На сервере нет клона репозитория,
  поэтому сначала скопируйте шаблон (выполняется на машине разработчика, из корня репозитория):
  ```bash
  scp deploy/multibot.env.example deploy@bot-test.example.com:/tmp/multibot.env.example
  ```
  Затем на сервере:
  ```bash
  sudo install -m 600 -o root -g root /tmp/multibot.env.example /etc/multibot/multibot.env && rm /tmp/multibot.env.example
  ```
  ```bash
  sudo nano /etc/multibot/multibot.env
  ```
- [ ] systemd-юнит `/etc/systemd/system/multibot.service`:
  ```ini
  [Unit]
  Description=MultiBot (Telegram bot + Mini App)
  After=network-online.target
  Wants=network-online.target

  [Service]
  User=multibot
  Group=multibot
  WorkingDirectory=/opt/multibot/app
  EnvironmentFile=/etc/multibot/multibot.env
  # вариант А (framework-dependent):
  ExecStart=/usr/bin/dotnet /opt/multibot/app/Bot.Host.dll
  # вариант Б (self-contained): ExecStart=/opt/multibot/app/Bot.Host
  Restart=always
  RestartSec=5
  KillSignal=SIGINT
  TimeoutStopSec=20
  NoNewPrivileges=true
  ProtectSystem=strict
  ProtectHome=true
  ReadWritePaths=/var/lib/multibot
  PrivateTmp=true

  [Install]
  WantedBy=multi-user.target
  ```
  ```bash
  sudo systemctl daemon-reload && sudo systemctl enable multibot
  ```
  Путь к `dotnet` проверьте командой `which dotnet` и при необходимости поправьте `ExecStart`.
- [ ] nginx `/etc/nginx/sites-available/multibot`:
  ```nginx
  server {
      listen 80;
      server_name bot-test.example.com;

      location / {
          proxy_pass         http://127.0.0.1:5080;
          proxy_http_version 1.1;
          proxy_set_header   Host $host;
          proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
          proxy_set_header   X-Forwarded-Proto $scheme;
          client_max_body_size 1m;
      }
  }
  ```
  ```bash
  sudo ln -s /etc/nginx/sites-available/multibot /etc/nginx/sites-enabled/ && sudo nginx -t && sudo systemctl reload nginx
  ```
- [ ] Сертификат Let's Encrypt. certbot сам допишет `listen 443 ssl` и редирект с 80:
  ```bash
  sudo certbot --nginx -d bot-test.example.com
  ```
  Проверка автопродления: `sudo certbot renew --dry-run`.

## 3. Сборка и публикация (каждый релиз, на машине разработчика)

- [ ] Рабочая копия чистая, все проверки зелёные:
  ```bash
  dotnet build -warnaserror && dotnet test --nologo && dotnet format --verify-no-changes
  ```
  ```bash
  cd webapp && npm ci && npm run build && npm run test -- --run && npm run lint
  ```
- [ ] Собрать backend. **Вариант А** (на сервере стоит runtime):
  ```bash
  dotnet publish src/Bot.Host -c Release -o out/app
  ```
  **Вариант Б** (self-contained, runtime на сервере не нужен):
  ```bash
  dotnet publish src/Bot.Host -c Release -r linux-x64 --self-contained -o out/app
  ```
  Для ARM-сервера укажите `linux-arm64`.
- [ ] Залить файлы на сервер (`webapp/dist` уже собран на предыдущем шаге):
  ```bash
  rsync -az --delete out/app/ deploy@bot-test.example.com:/tmp/multibot-app/
  ```
  ```bash
  rsync -az --delete webapp/dist/ deploy@bot-test.example.com:/tmp/multibot-webapp/
  ```
- [ ] На сервере положить файлы на место и перезапустить сервис:
  ```bash
  sudo systemctl stop multibot
  ```
  ```bash
  sudo rsync -a --delete /tmp/multibot-app/ /opt/multibot/app/ && sudo rsync -a --delete /tmp/multibot-webapp/ /opt/multibot/webapp/
  ```
  ```bash
  sudo chown -R multibot:multibot /opt/multibot && sudo systemctl start multibot
  ```
  `/var/lib/multibot` при этом не трогается, БД сохраняется. Миграции применяются при старте.

## 4. Проверка после запуска

- [ ] Сервис работает и в логах нет ошибок:
  ```bash
  sudo systemctl status multibot
  ```
  ```bash
  sudo journalctl -u multibot -n 100 --no-pager
  ```
  Нет `OptionsValidationException`, нет ошибок `SetWebhook`/`SetMyCommands`, нет предупреждения `WebApp dist folder not found`.
- [ ] HTTP-ответы:
  ```bash
  curl -s https://bot-test.example.com/health
  ```
  ожидается `{"status":"ok"}`;
  ```bash
  curl -s -o /dev/null -w "%{http_code}\n" https://bot-test.example.com/api/platform/me
  ```
  ожидается `401`;
  ```bash
  curl -s -o /dev/null -w "%{http_code}\n" -X POST https://bot-test.example.com/bot/webhook
  ```
  ожидается `401`.
- [ ] Telegram видит webhook. Команду выполнять вручную, токен не сохранять в истории shell:
  `https://api.telegram.org/bot<токен>/getWebhookInfo`. В ответе `url` = `https://bot-test.example.com/bot/webhook`,
  `pending_update_count` близок к 0, `last_error_message` отсутствует.
- [ ] БД создана: `sudo ls -la /var/lib/multibot` показывает `bot.db`.
- [ ] Ссылка Mini App в BotFather: `/newapp` (или Bot Settings → Menu Button) → URL `https://bot-test.example.com`.
- [ ] Пройден [MANUAL_CHECKLIST.md](MANUAL_CHECKLIST.md) (раздел «Подготовка» на сервере уже выполнен).

## 5. Эксплуатация

- [ ] **Логи:** `journalctl -u multibot -f`. Токен и секрет в логи не попадают, это проверено тестами AC-13 и AC-21.
- [ ] **Бэкап БД** (раз в сутки по cron, от root):
  ```bash
  sqlite3 /var/lib/multibot/bot.db ".backup '/var/backups/multibot-$(date +%F).db'"
  ```
- [ ] **Откат:** перед заливкой сохранить `/opt/multibot/app` в `/opt/multibot/app.prev`. Для отката вернуть каталог и перезапустить сервис.
      Миграции только накатываются вперёд, поэтому перед релизом с новой миграцией снимите бэкап БД.
- [ ] **Смена секретов:** поправить `/etc/multibot/multibot.env` и выполнить `sudo systemctl restart multibot`. Webhook перерегистрируется с новым секретом сам.
- [ ] **Локальная разработка с тем же ботом:** запускайте локально с `Bot__SetupOnStartup=false`, иначе локальный Host перехватит webhook.
      После этого перезапустите серверный сервис, чтобы он вернул webhook себе.

## Частые проблемы

| Симптом | Причина |
|---------|---------|
| Host не стартует, в логе `Bot:Token` / `Bot:WebhookSecret` | Нет переменной в `multibot.env` или опечатка (разделитель — двойное подчёркивание `__`) |
| `/` отвечает 404, в логе `WebApp dist folder not found` | Не задан или неверен `WebApp__DistPath`, или не залит `webapp/dist` |
| `SQLite Error 14: unable to open database file` | У пользователя `multibot` нет прав на `/var/lib/multibot` или каталог не в `ReadWritePaths` |
| Бот молчит, в `getWebhookInfo` ошибка 401 | Секрет в Telegram не совпадает с текущим. Перезапустите сервис, он перерегистрирует webhook |
| Бот молчит, в `getWebhookInfo` SSL error | Сертификат не выпущен или не тот домен в `Bot__PublicUrl` |
| В логе при старте предупреждение DataProtection про ключи в памяти / `~/.aspnet` | Ожидаемо: приложение не использует Data Protection (нет cookie/antiforgery), предупреждение безвредно |
| Mini App: «Сессия устарела» сразу после открытия | Сильно расходятся часы сервера. Проверьте `timedatectl`, должен быть включён NTP |
