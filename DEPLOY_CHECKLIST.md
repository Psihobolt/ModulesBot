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
| 4 | EF Core пишет каждый SQL-запрос на уровне Information. Значения параметров не логируются, но шума много | Уровень `Warning` для `Microsoft.EntityFrameworkCore` задан в `appsettings.json`; в `multibot.env` переменную с точкой в имени не задавать (systemd такие имена не принимает) |
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
  sudo apt install -y nginx certbot python3-certbot-nginx sqlite3 ufw curl jq
  ```
- [ ] Файрвол: открыты только SSH и HTTP/HTTPS.
  ```bash
  sudo ufw allow OpenSSH && sudo ufw allow 'Nginx Full' && sudo ufw enable
  ```
- [ ] Системный пользователь и каталоги:
  ```bash
  sudo useradd --system --home /opt/multibot --shell /usr/sbin/nologin multibot
  ```
  ```bash
  sudo install -d -m 755 -o root -g root /opt/multibot
  ```
  ```bash
  sudo install -d -o multibot -g multibot /var/lib/multibot && sudo install -d /etc/multibot
  ```
  `/opt/multibot` и весь код в нём принадлежат root: пользователь `multibot` пишет только в `/var/lib/multibot`.
- [ ] Файл с секретами `/etc/multibot/multibot.env` создаётся из шаблона `deploy/server/multibot.env.example`
  (заполните значения в `<…>` и адрес `Bot__PublicUrl`), права `600`, владелец root. На сервере нет клона репозитория,
  поэтому сначала скопируйте шаблон (выполняется на машине разработчика, из корня репозитория):
  ```bash
  scp deploy/server/multibot.env.example deploy@bot-test.example.com:/tmp/multibot.env.example
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
  ExecStart=/opt/multibot/app/Bot.Host
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
  Без `--now`: кода в `/opt/multibot/app` ещё нет, первый запуск сделает автообновление (раздел «Релиз из GitHub»).
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

## Релиз из GitHub

Сервер сам забирает последний GitHub Release и сам откатывается при неудаче. Архив собирает workflow `release`
(self-contained `linux-x64`, .NET на сервере не нужен).

### Как работает

- [ ] Запуск: `multibot-update.timer` запускает `multibot-update.service` (`Type=oneshot`, скрипт
  `/usr/local/sbin/multibot-update`, `TimeoutStartSec=15min`) через 2 минуты после загрузки и затем через 5 минут после
  завершения предыдущего запуска (плюс случайная задержка до 20 с). Одновременный запуск исключён блокировкой `flock`
  на `/run/multibot-update.lock`.
- [ ] Настройки: `/etc/multibot/updater.env` (600, root): `REPO` (обязателен, `owner/repo`), `GITHUB_TOKEN`, `WEBHOOK_IP`,
  `NOTIFY_CHAT_ID`. Из `/etc/multibot/multibot.env` скрипт читает `Bot__Token`, `Bot__WebhookSecret`, `Bot__PublicUrl`.
- [ ] Источник: `releases/latest` репозитория `REPO` на api.github.com; ассеты `multibot.tar.gz` и `multibot.tar.gz.sha256`.
- [ ] Пропуск: если тег равен `/var/lib/multibot-updater/current` — ничего не делается; если равен
  `/var/lib/multibot-updater/failed` — в журнале «релиз … уже не прошёл проверку, жду следующий».
- [ ] Проверки до остановки бота: `sha256sum -c`; в архиве только `app/`, `webapp/`, `COMMIT` и нет путей с `..`; после
  распаковки есть исполняемый `app/Bot.Host` и `webapp/index.html`.
- [ ] Раскладка: распаковка в `/opt/multibot/releases/<тег>` (через `<тег>.tmp`), владелец `root:root`, без записи для
  group/other. `/opt/multibot/app` и `/opt/multibot/webapp` — ссылки на `releases/<тег>/app` и `releases/<тег>/webapp`,
  замена ссылки атомарная.
- [ ] Выкладка: `systemctl stop multibot` → копия `bot.db`, `bot.db-wal`, `bot.db-shm` из `/var/lib/multibot` в
  `/var/backups/multibot/<YYYYmmdd-HHMMSS>-before-<тег>/` → переключение ссылок → `systemctl start multibot`.
- [ ] Здоровье: сервис active и `http://127.0.0.1:5080/health` отвечает 2xx; до 30 попыток с паузой 2 с; проверка
  выполняется дважды с паузой 5 с между ними.
- [ ] Успех: `current` = тег, `failed` удаляется. Если задан `WEBHOOK_IP` — до 6 попыток закрепить webhook
  (`<Bot__PublicUrl>/bot/webhook` с `ip_address`). Очистка: остаются 3 новейших релиза и текущий, 10 новейших бэкапов.
  Если задан `NOTIFY_CHAT_ID` — бот пишет «выкачен <тег> (<sha>)».
- [ ] Неудача (бот не прошёл проверку здоровья): в журнал выводятся 40 последних строк `multibot`, тег записывается в
  `failed` (больше не пробуется), сервис останавливается, БД восстанавливается из бэкапа, ссылки возвращаются на прежний
  релиз, сервис запускается и проверяется. Уведомление: «возвращён <прежний>» либо «откат … не помог — нужен ручной
  разбор». Если прежнего релиза нет — «откатываться не на что».
- [ ] Ошибки двух видов. Временные (GitHub API, скачивание): без уведомления, следующий запуск повторит. Дефект релиза
  (нет ассета или `.sha256`, сумма не сходится, лишние пути в архиве, нет `Bot.Host`/`index.html`): тег записывается в
  `failed` и приходит уведомление.

### Установка (один раз)

- [ ] На машине разработчика (из корня репозитория) скопировать четыре файла на сервер:
  ```bash
  scp deploy/server/multibot-update.sh deploy/server/multibot-update.service deploy/server/multibot-update.timer deploy/server/updater.env.example deploy@bot-test.example.com:/tmp/
  ```
- [ ] На сервере разложить файлы:
  ```bash
  sudo install -m 755 -o root -g root /tmp/multibot-update.sh /usr/local/sbin/multibot-update
  ```
  ```bash
  sudo install -m 644 -o root -g root /tmp/multibot-update.service /tmp/multibot-update.timer /etc/systemd/system/
  ```
  ```bash
  sudo install -m 600 -o root -g root /tmp/updater.env.example /etc/multibot/updater.env && rm /tmp/multibot-update.sh /tmp/multibot-update.service /tmp/multibot-update.timer /tmp/updater.env.example
  ```
- [ ] Заполнить `/etc/multibot/updater.env`: `REPO=Psihobolt/ModulesBot` (репозиторий публичный — `GITHUB_TOKEN` оставить
  пустым; для приватного нужен fine-grained токен с Contents: Read-only), при необходимости `WEBHOOK_IP` и `NOTIFY_CHAT_ID`:
  ```bash
  sudo nano /etc/multibot/updater.env
  ```
- [ ] Первый прогон вручную (в репозитории должен быть хотя бы один релиз):
  ```bash
  sudo systemctl daemon-reload && sudo systemctl start multibot-update
  ```
  ```bash
  sudo journalctl -u multibot-update -n 50 --no-pager
  ```
- [ ] Включить таймер и проверить его:
  ```bash
  sudo systemctl enable --now multibot-update.timer
  ```
  ```bash
  systemctl list-timers multibot-update.timer
  ```

### Переход с ручной выкладки

Для сервера, развёрнутого вручную по прежнему чек-листу (`app` и `webapp` — обычные каталоги).

- [ ] Код должен принадлежать root: `sudo chown root:root /opt/multibot`.
- [ ] В unit `multibot.service` должен быть `ExecStart=/opt/multibot/app/Bot.Host` (self-contained); после правки —
  `sudo systemctl daemon-reload`.
- [ ] Должны существовать оба каталога, `/opt/multibot/app` и `/opt/multibot/webapp`. Если одного нет, скрипт на
  первом запуске перенесёт `app` и упадёт на `webapp`, ссылки `app` не будет и сервис не стартует.
- [ ] Удалить остатки старой схемы: удалить `/opt/multibot/app.prev`, если он есть:
  ```bash
  sudo rm -rf /opt/multibot/app.prev
  ```
- [ ] Выполнить «Установку (один раз)». Первый запуск скрипта переносит `app` и `webapp` в `releases/initial`, вместо них
  создаёт ссылки, записывает `current` = `initial` и сразу ставит последний релиз.
- [ ] По желанию: `sudo chown -R root:root /opt/multibot/releases/initial`.

### Выпуск релиза

- [ ] GitHub → Actions → release → Run workflow, ветка `main`, версия `vX.Y.Z`. Дождаться зелёного запуска (CI прогоняется
  внутри workflow).
- [ ] Сервер подхватит релиз в течение ~5 минут; сразу: `sudo systemctl start multibot-update`.
- [ ] Проверка — §4 и `readlink /opt/multibot/app` (должно быть `releases/vX.Y.Z/app`).

### Откат

- [ ] Автоматический: если релиз не прошёл проверку здоровья, скрипт сам возвращает прежний релиз и БД (см. «Как работает»).
- [ ] Релиз прошёл проверку, но сломан функционально: предпочтительно исправление или revert через PR и новый релиз.
  Аварийно вручную:
  1. `sudo systemctl stop multibot`;
  2. переключить ссылки `app` и `webapp` на прежний `releases/<тег>` (например,
     `sudo ln -sfn releases/<тег>/app /opt/multibot/app && sudo ln -sfn releases/<тег>/webapp /opt/multibot/webapp`);
  3. если в плохом релизе была новая миграция — вернуть БД из `/var/backups/multibot/<…>-before-<плохой тег>/`
     (при остановленном сервисе, вместе с `-wal`/`-shm`);
  4. записать плохой тег в `/var/lib/multibot-updater/failed`, прежний — в `/var/lib/multibot-updater/current`
     (иначе таймер выкатит плохой тег снова);
  5. `sudo systemctl start multibot`.
- [ ] Повторить неудачный тег: удалить `/var/lib/multibot-updater/failed`.

### Ограничения

- Ставится только `releases/latest`. Последний релиз должен быть самым новым: не удаляйте новейший релиз и не помечайте
  старый как latest, иначе скрипт выкатит более старую версию на БД с более новыми миграциями.
- Только `linux-x64`. Порт 5080, имя unit `multibot` и пути заданы в скрипте.
- `Bot__PublicUrl` — без `/` в конце (при заданном `WEBHOOK_IP` скрипт соберёт адрес webhook с двойным `/`), значения в
  `multibot.env` — без кавычек (скрипт читает их построчно через `sed`, кавычки попадут в значение).
- `WEBHOOK_IP` закрепляется только после выкладки. Ручной `systemctl restart multibot` или перезапуск после сбоя
  (`Restart=always`) сбрасывает IP, потому что Host регистрирует webhook без `ip_address`, до следующего релиза.

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
  Кроме того, автообновление делает бэкап перед каждой выкладкой в `/var/backups/multibot/`.
- [ ] **Откат:** см. «Релиз из GitHub» → «Откат». Миграции только накатываются вперёд, поэтому БД возвращается из бэкапа.
- [ ] **Смена секретов:** поправить `/etc/multibot/multibot.env` и выполнить `sudo systemctl restart multibot`. Webhook перерегистрируется с новым секретом сам.
- [ ] **Локальная разработка с тем же ботом:** запускайте локально с `Bot__SetupOnStartup=false`, иначе локальный Host перехватит webhook.
      После этого перезапустите серверный сервис, чтобы он вернул webhook себе.

## Частые проблемы

| Симптом | Причина |
|---------|---------|
| Host не стартует, в логе `Bot:Token` / `Bot:WebhookSecret` | Нет переменной в `multibot.env` или опечатка (разделитель — двойное подчёркивание `__`) |
| `/` отвечает 404, в логе `WebApp dist folder not found` | Не задан или неверен `WebApp__DistPath`, или ссылка `/opt/multibot/webapp` не создана (автообновление ещё не отработало) |
| `SQLite Error 14: unable to open database file` | У пользователя `multibot` нет прав на `/var/lib/multibot` или каталог не в `ReadWritePaths` |
| Бот молчит, в `getWebhookInfo` ошибка 401 | Секрет в Telegram не совпадает с текущим. Перезапустите сервис, он перерегистрирует webhook |
| Бот молчит, в `getWebhookInfo` SSL error | Сертификат не выпущен или не тот домен в `Bot__PublicUrl` |
| В логе при старте предупреждение DataProtection про ключи в памяти / `~/.aspnet` | Ожидаемо: приложение не использует Data Protection (нет cookie/antiforgery), предупреждение безвредно |
| Mini App: «Сессия устарела» сразу после открытия | Сильно расходятся часы сервера. Проверьте `timedatectl`, должен быть включён NTP |
| `ERROR: REPO в /etc/multibot/updater.env не задан или неверен` | В `updater.env` пустой или неверный `REPO` (нужен вид `owner/repo`) |
| В журнале `релиз … уже не прошёл проверку, жду следующий` | Этот тег уже откатывался. Удалите `/var/lib/multibot-updater/failed` (повторить) или выпустите новый релиз |
| `multibot-update.service` в состоянии failed в `systemctl --failed` | GitHub или сеть недоступны: скрипт завершается с кодом 1 и повторит при следующем запуске. Это ожидаемо |
| `jq: command not found` | Не установлен пакет `jq` (и `curl`): `sudo apt install -y curl jq` |
