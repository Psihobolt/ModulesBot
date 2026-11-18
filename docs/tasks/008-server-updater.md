# 008 — Автообновление сервера из релизов GitHub

**Статус:** DONE
**Модуль:** platform (деплой и документация; C#-код не меняется, меняется только `appsettings.json` хоста)   **Бриф:** —

> Нумерация: 008 — эта задача (следующий свободный номер). Рефакторинг «MovieNight: сценарии групп в Application»,
> зарезервированный в 005/006/007 под 008, сдвинут на **009** (ссылки в 005, 006, 007 обновлены).
> Ветка: `chore/008-server-updater` (переименование из `chore/server-updater` выполняет оркестратор).

## Исходное описание
«Добавить в deploy/server/ файлы автообновления сервера (multibot-update.sh, .service, .timer, updater.env.example —
приложу). Переписать раздел «Релиз из GitHub» в DEPLOY_CHECKLIST.md: сервер сам забирает последний релиз, app и webapp —
ссылки на /opt/multibot/releases/<версия>, код принадлежит root, ручная выкладка через rsync и chown -R multibot больше
не используется, откат — автоматический. Убрать Logging__LogLevel__Microsoft.EntityFrameworkCore из multibot.env.example
(systemd не принимает точку в имени переменной), вместо этого добавить "Microsoft.EntityFrameworkCore": "Warning" в
LogLevel в appsettings.json.»

Решение пользователя (через оркестратора): шаблон `deploy/multibot.env.example` **переносится** в
`deploy/server/multibot.env.example`.

## Цель
Сервер сам ставит последний GitHub Release и сам откатывается при неудаче; документация и шаблоны описывают ровно
поведение файлов пользователя в `deploy/server/`, а автотесты сторожат согласованность путей, имён и шаблонов.

## Входные данные (коммит ae11d1c, файлы пользователя)
`deploy/server/multibot-update.sh`, `multibot-update.service`, `multibot-update.timer`, `updater.env.example`.
Поведение скрипта (источник правды для текста чек-листа):

| Что | Значение в скрипте |
|---|---|
| Запуск | unit `multibot-update.service` (`Type=oneshot`, `ExecStart=/usr/local/sbin/multibot-update`, `TimeoutStartSec=15min`); таймер: `OnBootSec=2min`, `OnUnitInactiveSec=5min`, `RandomizedDelaySec=20s`; одновременный запуск исключён `flock` на `/run/multibot-update.lock` |
| Настройки | `/etc/multibot/updater.env` (600, root; читается как shell): `REPO` (обязателен, `owner/repo`), `GITHUB_TOKEN`, `WEBHOOK_IP`, `NOTIFY_CHAT_ID`. Из `/etc/multibot/multibot.env` читает `Bot__Token`, `Bot__WebhookSecret`, `Bot__PublicUrl` |
| Источник | `GET api.github.com/repos/$REPO/releases/latest`; ассеты `multibot.tar.gz` и `multibot.tar.gz.sha256` |
| Пропуск | тег = `/var/lib/multibot-updater/current` → выход; тег = `.../failed` → выход («уже не прошёл проверку») |
| Проверки до остановки бота | `sha256sum -c`; в архиве только `app/`, `webapp/`, `COMMIT`, нет `..`; есть исполняемый `app/Bot.Host` и `webapp/index.html` |
| Раскладка | распаковка в `/opt/multibot/releases/<тег>` (через `<тег>.tmp`), владелец `root:root`, без записи для group/other; `/opt/multibot/app` → `releases/<тег>/app`, `/opt/multibot/webapp` → `releases/<тег>/webapp` (замена ссылки атомарная) |
| Выкладка | `systemctl stop multibot` → копия `bot.db`, `-wal`, `-shm` из `/var/lib/multibot` в `/var/backups/multibot/<YYYYmmdd-HHMMSS>-before-<тег>/` → переключение ссылок → `systemctl start multibot` |
| Здоровье | сервис active **и** `http://127.0.0.1:5080/health` = 2xx; до 30 попыток через 2 с; проверка дважды с паузой 5 с |
| Успех | `current` = тег, `failed` удалён; если задан `WEBHOOK_IP` — до 6 попыток закрепить webhook (`<PublicUrl>/bot/webhook` + `ip_address`); очистка: релизы — 3 новейших + текущий, бэкапы — 10 новейших; уведомление в `NOTIFY_CHAT_ID` |
| Неудача | 40 строк журнала `multibot`, тег → `failed`, stop, восстановление БД из бэкапа, ссылки на прежний релиз, start, проверка здоровья; уведомление «возвращён <прежний>» или «откат не помог — нужен ручной разбор»; без прежнего релиза — «откатываться не на что» |
| Ошибки | временные (GitHub API, скачивание) — без уведомления, повтор при следующем запуске; дефект релиза (нет ассета, sha256, лишние пути, нет `Bot.Host`/`index.html`) — тег в `failed` + уведомление |
| Первый запуск | если `/opt/multibot/app` — обычный каталог (ручная установка): `app` и `webapp` переносятся в `releases/initial`, вместо них ссылки, `current` = `initial` |
| Зависимости | `bash`, `curl`, `jq`, `flock`, `sha256sum`, `tar`, `systemctl`, `journalctl` |

## Находки по скрипту (сверка с проектом)
Совпадает с проектом: имя ассета и формат `.sha256` (`release.yml`: `sha256sum multibot.tar.gz > multibot.tar.gz.sha256`,
проверка в каталоге скачивания); структура архива (`tar -C pkg -czf multibot.tar.gz app webapp COMMIT`); `/health`
(`PlatformHostExtensions.MapPlatform`); порт 5080 (`ASPNETCORE_URLS` шаблона); unit `multibot`; пути БД и
`WebApp__DistPath` шаблона; путь webhook `/bot/webhook` (`BotDefaults.WebhookPath`). `WebApp:DistPath` через
ссылку работает: `Path.GetFullPath` ссылку не раскрывает, `Directory.Exists`/`PhysicalFileProvider` идут по ней.

| # | Находка | Риск | Предложение |
|---|---|---|---|
| F1 | `fix_webhook` строит `"${PUBLIC_URL}/bot/webhook"` без удаления `/` в конце, а Host делает `TrimEnd('/')` (`BotSetupService`). При `Bot__PublicUrl=https://host/` и заданном `WEBHOOK_IP` скрипт сам переставит webhook на `https://host//bot/webhook` → 404, бот молчит | Средний (только при `WEBHOOK_IP` и `/` в конце) | Убрать хвостовой `/` в скрипте; минимум — предупреждение в чек-листе |
| F2 | Регулярка тега `^[A-Za-z0-9._-]+$` шире формата релизов (`release.yml`: `^v[0-9]+\.[0-9]+\.[0-9]+$`): тег `initial` совпал бы с каталогом ручной установки; имя тега — часть пути под `rm -rf` | Низкий (git-правила запрещают `..`, релизы создаёт только workflow) | Сузить до формата `release.yml` |
| F3 | `bad_release` после распаковки (нет `Bot.Host`/`index.html`) оставляет `releases/<тег>.tmp` до очистки по возрасту | Низкий | Удалять `.tmp` при отказе |
| F4 | Первый запуск: есть `app`, но нет `webapp` → `app` уже перенесён, `mv webapp` падает (`set -e`), ссылки `app` нет — сервис не стартует | Низкий (чек-лист создаёт оба каталога) | Проверять оба каталога до переноса |
| F5 | «Последний» релиз может стать старее текущего (удалили новейший релиз или пометили старый как latest) → скрипт выкатит более старую версию на БД с более новыми миграциями | Средний, по действию человека | Ставить только тег новее `current` (сравнение версий) или ограничение в чек-листе |
| F6 | Host при каждом старте регистрирует webhook без `ip_address`; скрипт закрепляет IP только после выкладки. Ручной `systemctl restart multibot` или перезапуск после сбоя (`Restart=always`) сбрасывает IP до следующего релиза | Низкий (`WEBHOOK_IP` необязателен) | Правильное решение — настройка `Bot:WebhookIpAddress` в Host (отдельная спека); сейчас — ограничение в чек-листе |
| F7 | `Bot__…` читаются `sed` построчно: кавычки вокруг значения (допустимые для systemd) попадут в токен/URL | Низкий | Ограничение в чек-листе: значения без кавычек (шаблон так и написан) |
| F8 | `retry_later` завершает unit с кодом 1: при недоступности GitHub `multibot-update.service` в состоянии failed (шум в `systemctl --failed`) | Низкий | Принять как есть; описать в «Частых проблемах» |

Решение пользователя — вариант A: скрипт не меняется; F1, F4, F5, F6, F7 — ограничения в чек-листе, F8 — в «Частых
проблемах»; исправления — отдельной спекой позже, если понадобятся.

## Сценарии
1. Новый сервер: администратор по §2 ставит бота (unit `multibot`, без кода) и автообновление; первый запуск
   `multibot-update` ставит последний релиз, создаёт ссылки, бот отвечает на `/health`.
2. Пользователь выпускает релиз `vX.Y.Z` (Actions → release); в течение ~5 минут сервер его ставит и (если задан
   `NOTIFY_CHAT_ID`) пишет «выкачен vX.Y.Z (<sha>)».
3. Релиз не поднимается → скрипт возвращает прежний релиз и БД, пишет об откате, этот тег больше не пробует.
4. Сервер, развёрнутый вручную по старому чек-листу, переходит на автообновление: первый запуск переносит код в
   `releases/initial`.
5. Релиз прошёл проверку здоровья, но функционально сломан → ручной откат по чек-листу или новый релиз с исправлением.

## Критерии приёмки
Автотесты — xUnit в `tests/Bot.Platform.Tests/RepositoryHygiene/` (корень — `GitRepo.Root`), без запуска скрипта и без
сети. Текст чек-листа по существу — ревью critic (реестр).

- **AC-1 (файлы на месте).** Given репозиторий, Then существуют `deploy/server/multibot-update.sh`,
  `multibot-update.service`, `multibot-update.timer`, `updater.env.example`, `multibot.env.example`; файла
  `deploy/multibot.env.example` нет (ни на диске, ни в `git ls-files` после коммита). **And** режим
  `deploy/server/multibot-update.sh` в индексе git (`git ls-files -s`) — `100755`; первая строка — `#!/usr/bin/env bash`,
  в скрипте есть `set -euo pipefail`.
- **AC-2 (.gitignore).** Given нейтрализованные глобальные исключения, Then игнорируются `deploy/server/multibot.env`,
  `deploy/server/updater.env`, `deploy/multibot.env`; **не** игнорируются `deploy/server/multibot.env.example`,
  `deploy/server/updater.env.example`, `deploy/server/multibot-update.sh`, `deploy/server/multibot-update.service`,
  `deploy/server/multibot-update.timer`. Остальные пути 006 AC-1/AC-2 — без изменений, кроме замены
  `deploy/multibot.env.example` → `deploy/server/multibot.env.example`.
- **AC-3 (имена переменных — для systemd и bash).** Given каждый файл `deploy/**/*.env.example`, Then каждая
  непустая строка, не начинающаяся с `#`, имеет вид `KEY=VALUE`, где `KEY` соответствует `^[A-Za-z_][A-Za-z0-9_]*$`
  (в частности, без `.`); ключи не повторяются. Негатив (встроенный образец): `Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning`
  — нарушение, сообщение называет файл, номер строки и ключ, но не значение.
- **AC-4 (шаблон бота).** Given `deploy/server/multibot.env.example`, Then его ключи = `ASPNETCORE_ENVIRONMENT`,
  `ASPNETCORE_URLS`, `Bot__Token`, `Bot__WebhookSecret`, `Bot__PublicUrl`, `WebApp__DistPath`,
  `ConnectionStrings__Default` (ровно эти); значения по умолчанию и заглушки — как в 006 AC-6 (без ключа EF Core);
  ключи = ключам README с пометкой «Сервер: да»; файл упоминает `/etc/multibot/multibot.env` и `600`; проходит сканер
  секретов. (Тесты 006 AC-5/AC-6 сохраняются с новым путём.)
- **AC-5 (шаблон автообновления).** Given `deploy/server/updater.env.example`, Then его ключи = `REPO`,
  `GITHUB_TOKEN`, `WEBHOOK_IP`, `NOTIFY_CHAT_ID` = переменные, которые скрипт обнуляет перед чтением конфигурации
  (строка `REPO=; GITHUB_TOKEN=; …`); `GITHUB_TOKEN` пуст; файл упоминает `/etc/multibot/updater.env` и `600`;
  проходит сканер секретов.
- **AC-6 (согласованность скрипта с проектом).** Given значения по умолчанию в скрипте (`NAME=${MBU_…:-значение}`
  или `NAME=значение`), Then:
  (а) `CONF` = путь из первой строки-комментария `updater.env.example`; (б) `BOT_ENV` = `/etc/multibot/multibot.env`;
  (в) `DB` = путь после `Data Source=` в `ConnectionStrings__Default` шаблона; (г) `BASE` + `/webapp` =
  `WebApp__DistPath` шаблона; (д) `HEALTH_URL` = `http://127.0.0.1:<порт>/health`, где `<порт>` — порт
  `ASPNETCORE_URLS` шаблона; (е) `ASSET` = `multibot.tar.gz`, и `release.yml` создаёт архив `multibot.tar.gz` и файл
  `multibot.tar.gz.sha256`; (ж) `SERVICE` = `multibot`, и `DEPLOY_CHECKLIST.md` описывает unit
  `/etc/systemd/system/multibot.service`. Не найдено значение — тест падает с именем переменной.
- **AC-7 (unit-файлы).** Given `multibot-update.service`, Then `[Service]` содержит `Type=oneshot` и
  `ExecStart=/usr/local/sbin/multibot-update`; `DEPLOY_CHECKLIST.md` устанавливает скрипт ровно по этому пути.
  Given `multibot-update.timer`, Then есть секция `[Timer]` и `[Install]` с `WantedBy=timers.target`; ключа `Unit=` нет
  либо он равен `multibot-update.service` (таймер запускает именно этот сервис).
- **AC-8 (EF Core в appsettings).** Given `src/Bot.Host/appsettings.json`, Then `Logging:LogLevel` содержит
  `"Microsoft.EntityFrameworkCore": "Warning"`, а `Default` = `Information` и `Microsoft.AspNetCore` = `Warning`
  остаются. Ключа `Logging__LogLevel__Microsoft.EntityFrameworkCore` нет ни в одном шаблоне и ни в README.
- **AC-9 (ссылки в документах).** Given `README.md`, `DEPLOY_CHECKLIST.md`, `.github/workflows/*.yml`, Then каждое
  вхождение пути вида `deploy/<…>` (символы `[A-Za-z0-9._/-]`, после `deploy/`) указывает на существующий файл
  или каталог репозитория; `deploy/multibot.env.example` не упоминается. `docs/tasks/*` не проверяются (история).
- **AC-10 (нет старой ручной выкладки).** Given `DEPLOY_CHECKLIST.md`, Then в нём нет подстрок `rsync` и
  `chown -R multibot`; ни одна строка, содержащая `app.prev`, не содержит `cp ` или `mv ` (копию прежней версии
  чек-лист больше не создаёт; упоминание и удаление `app.prev` при переходе с ручной выкладки допустимы); есть `/opt/multibot/releases`, `multibot-update.timer`, `/usr/local/sbin/multibot-update`,
  `/etc/multibot/updater.env`.
- **AC-11 (текст чек-листа).** `DEPLOY_CHECKLIST.md` изменён по BE-4 и описывает поведение из «Входных данных» без
  противоречий скрипту. *Ревью critic.*
- **AC-12 (регрессия).** `dotnet build -warnaserror`, `dotnet test --nologo` (включая сканер секретов 006 и
  `WorkflowTests` 007), `dotnet format --verify-no-changes`, фронтенд-команды — зелёные.

## Контракт API / бота / экраны Mini App
Не меняются. Меняется только уровень логирования категории `Microsoft.EntityFrameworkCore` (Warning) во всех
окружениях, где действует `appsettings.json`.

## Задачи
### Backend (конфигурация и документация; C#-код не меняется)
- **BE-1** `deploy/server/multibot.env.example` — создать из `git show HEAD:deploy/multibot.env.example` без строки
  `Logging__LogLevel__Microsoft.EntityFrameworkCore` (и без комментария только к ней); остальное содержимое без
  изменений. Старый файл не восстанавливать. Файлы пользователя в `deploy/server/` не менять (решение 1A; режим
  скрипта в индексе уже `100755`). (AC-1, AC-3, AC-4)
- **BE-2** `src/Bot.Host/appsettings.json`: `"Microsoft.EntityFrameworkCore": "Warning"` в `Logging:LogLevel`
  (после `Microsoft.AspNetCore`). (AC-8)
- **BE-3** `README.md`: в «Секреты и конфигурация» — путь шаблона `deploy/server/multibot.env.example`; строку
  таблицы `Logging__LogLevel__Microsoft.EntityFrameworkCore` заменить на
  `| \`Logging:LogLevel:Microsoft.EntityFrameworkCore\` | нет | \`appsettings.json\` (\`Warning\`) | нет |`; после
  таблицы — пункт: «Автообновление сервера — `/etc/multibot/updater.env` (шаблон `deploy/server/updater.env.example`,
  права 600, владелец root); `GITHUB_TOKEN` — секрет, нужен только для приватного репозитория (fine-grained,
  Contents: Read-only)». В «CI, ветки и релизы» фразу «Развёртывание на сервере — вручную…» заменить на
  «Развёртывание — автоматическое: сервер примерно раз в 5 минут забирает последний релиз
  (`deploy/server/multibot-update.sh`), при неудаче откатывается; см. `DEPLOY_CHECKLIST.md`, раздел «Релиз из GitHub»». (AC-4, AC-8, AC-9)
- **BE-4** `DEPLOY_CHECKLIST.md` (AC-6 ж, AC-7, AC-9…AC-11):
  - «Готовность проекта», строка 4: EF Core — уровень `Warning` задан в `appsettings.json`; в `multibot.env`
    переменную с точкой не задавать (systemd такие имена не принимает).
  - §2: убрать установку ASP.NET Core Runtime (релиз self-contained); пакеты — добавить `curl jq`; каталоги:
    `/opt/multibot` — `root:root` 755 (`install -d`), `/var/lib/multibot` — `multibot:multibot` (тоже через
    `install -d`, без `chown -R`), `/etc/multibot`; никакого `chown` на `/opt/multibot`. Шаблон — `deploy/server/multibot.env.example` (scp + `install -m 600 -o root
    -g root`, как сейчас). Unit `multibot.service`: только `ExecStart=/opt/multibot/app/Bot.Host`
    (вариант А и комментарии о нём убрать), `WorkingDirectory=/opt/multibot/app`; `systemctl enable multibot` без
    `--now` (кода ещё нет — первый запуск делает автообновление).
  - Раздел «Релиз из GitHub» — переписать целиком, подразделы:
    1. **Как работает** — по таблице «Входные данные»: таймер, пропуск текущего/неудачного тега, проверки,
       `releases/<тег>` (владелец root), ссылки `app`/`webapp`, бэкап БД, здоровье, автоматический откат, очистка
       (3 релиза + текущий, 10 бэкапов), уведомления, временные ошибки vs дефект релиза.
    2. **Установка (один раз)** — с машины разработчика `scp` четырёх файлов из `deploy/server/` в `/tmp`; на сервере:
       `install -m 755 -o root -g root … /usr/local/sbin/multibot-update`; `install -m 644 …` unit-файлов в
       `/etc/systemd/system/`; `install -m 600 -o root -g root … /etc/multibot/updater.env`, в нём
       `REPO=Psihobolt/ModulesBot` (публичный — `GITHUB_TOKEN` пуст); `systemctl daemon-reload`; первый прогон
       `sudo systemctl start multibot-update` + `journalctl -u multibot-update -n 50 --no-pager`; затем
       `sudo systemctl enable --now multibot-update.timer` и проверка `systemctl list-timers multibot-update.timer`.
    3. **Переход с ручной выкладки** — `sudo chown root:root /opt/multibot`; unit — `ExecStart` варианта Б;
       `/opt/multibot/app` и `/opt/multibot/webapp` должны существовать оба (F4); удалить `/opt/multibot/app.prev`;
       первый запуск переносит их в `releases/initial`; по желанию `chown -R root:root /opt/multibot/releases/initial`.
    4. **Выпуск релиза** — Actions → release → Run workflow, `main`, `vX.Y.Z`; сервер подхватит в течение ~5 минут,
       сразу — `sudo systemctl start multibot-update`; проверка — §4 и `readlink /opt/multibot/app`.
    5. **Откат** — автоматический при непрохождении проверки здоровья. Если релиз прошёл проверку, но сломан:
       предпочтительно — исправление/revert через PR и новый релиз; аварийно вручную: `stop multibot` → ссылки `app` и
       `webapp` на прежний `releases/<тег>` → при новой миграции — БД из `/var/backups/multibot/<…>-before-<плохой тег>/`
       → записать плохой тег в `/var/lib/multibot-updater/failed`, прежний — в `.../current` (иначе таймер
       выкатит плохой тег снова) → `start multibot`. Повторить неудачный тег — удалить `.../failed`.
    6. **Ограничения** — ставится только `releases/latest`; последний релиз должен быть самым новым — не удалять
       новейший релиз и не помечать старый как latest (F5); только `linux-x64`; порт 5080, unit `multibot` и пути
       заданы в скрипте; `Bot__PublicUrl` без `/` в конце и значения в `multibot.env` без кавычек (F1, F7);
       `WEBHOOK_IP` закрепляется только после выкладки (F6).
  - §3 «Сборка и публикация (на машине разработчика)» — удалить целиком (ручная выкладка больше не используется,
    проверки перед релизом выполняет CI); ссылки «§3»/«вариант А/Б» в оставшемся тексте — не должны висеть.
  - §5 «Эксплуатация»: пункт «Откат» → ссылка на «Релиз из GitHub» → «Откат»; к «Бэкап БД» — что автообновление
    дополнительно делает бэкап перед каждой выкладкой в `/var/backups/multibot/`.
  - «Частые проблемы» — строки: `REPO … не задан или неверен`; «релиз … уже не прошёл проверку, жду следующий»
    (удалить `failed` или выпустить новый); `multibot-update.service` в failed при недоступности GitHub (F8);
    `jq: command not found`.
- **BE-5** `release.yml`, комментарий шапки (строка 5): «Развёртывание на сервере — автоматически:
  deploy/server/multibot-update.sh (таймер на сервере), см. DEPLOY_CHECKLIST.md, раздел «Релиз из GitHub».» Больше
  ничего в workflow не менять. (AC-9)
- **Не трогать:** `CLAUDE.md`, `.claude/*`, `docs/tasks/*`, файлы пользователя в `deploy/server/`.

### Frontend
Нет.

### Tester
- **TEST-1** `GitIgnoreTests` — списки по AC-2. `RepositorySecretsTests` — контрольный путь
  `deploy/server/multibot.env.example`. (AC-2)
- **TEST-2** `EnvTemplateTests` — новый путь, `Defaults` без ключа EF Core (AC-4). Заодно закрыть MINOR 006 №2: общий
  разбор `ReadTemplate(string text)`, которым пользуются и чтение файла, и тест AC-5 о нераскрытии строки (без
  отдельной копии `ParseForTest`). Проверка AC-3 для всех `deploy/**/*.env.example` + негатив; AC-5. (AC-3…AC-5)
- **TEST-3** Новый `DeployServerTests`: AC-1 (существование, отсутствие старого файла, режим `100755` через
  `git ls-files -s`, shebang, `set -euo pipefail`), AC-6 (разбор значений по умолчанию скрипта; негатив —
  встроенный образец скрипта с другим портом даёт падение (д)), AC-7, AC-10. (AC-1, AC-6, AC-7, AC-10)
- **TEST-4** AC-8 — разбор `appsettings.json` (`System.Text.Json`), AC-9 — ссылки `deploy/…` в документах. (AC-8, AC-9)
- **TEST-5** Полный прогон (AC-12).

## Вне скоупа
- Правка скрипта (F1–F8) и исполняемые тесты скрипта (фейки `curl`/`systemctl`/`journalctl`, переменные `MBU_*`) —
  отдельная спека позже, если понадобится (решение 1A).
- Настройка `Bot:WebhookIpAddress` в Host (F6) — отдельная спека.
- Поддержка `linux-arm64` в релизах.

## Открытые вопросы
Нет.

## Принятые решения (ответы пользователя)
1. **Правка скрипта — вариант A.** Скрипт и остальные файлы пользователя в `deploy/server/` в 008 не меняются.
   F1, F4, F5, F6, F7 описываются в `DEPLOY_CHECKLIST.md` как ограничения (BE-4, «Релиз из GitHub» → «Ограничения»;
   F4 — также в «Переход с ручной выкладки»), F8 — строкой в «Частых проблемах». F2, F3 — без действий в 008.
   Исправления — отдельной спекой позже, если понадобятся.
2. **Режим скрипта** в индексе git уже `100755` (проверено оркестратором `git ls-files -s`); AC-1 проверяет это как есть.
3. **Ветка** — `chore/008-server-updater` (переименовывает оркестратор).

## Принятые решения / допущения
- §3 (ручная сборка и выкладка) удаляется: пользователь явно отказался от `rsync`/`chown -R multibot`; ручной
  релиз в `releases/<имя>` таймер сразу заменил бы последним GitHub-релизом. Следствие: сервер — только `linux-x64`.
- `/opt/multibot` и код релизов — `root:root`; пользователь `multibot` пишет только в `/var/lib/multibot`. Иначе
  сервисный пользователь мог бы подменить ссылки/каталоги, с которыми работает root-скрипт (`rm -rf`, `chown -R`).
- `.gitignore` не меняется: `*.env` уже игнорирует `deploy/server/multibot.env` и `deploy/server/updater.env`,
  `!*.env.example` оставляет шаблоны (подтверждают тесты AC-2).
- `GITHUB_TOKEN` не попадает в таблицу ключей README: тест 006 сравнивает её «серверные» ключи с ключами
  `multibot.env.example`; токен описан отдельным пунктом.
- Ключ EF Core теряет переопределение через env на сервере; при необходимости — `appsettings.json` релиза или
  `Logging__LogLevel__Default`. Скрываются и Info-сообщения EF о применении миграций — приемлемо (ошибки миграций —
  уровня Error).
- Спека 006 не переписывается: в шапку добавлена пометка о переносе шаблона в 008; актуальные списки путей — AC-2/AC-4
  этой спеки.
- Пример ветки `feat/008-movienight-groups-app` в `CLAUDE.md` — только пример формата, не ссылка на номер; не
  меняется (`CLAUDE.md` — файл пользователя).

## Реестр принятых отклонений
- AC-11 (текст чек-листа) проверяется ревью critic, а не автотестом.
- AC-10 в ред. 2 (решение по DEVIATIONS backend, круг 0): запрет `app.prev` целиком противоречил BE-4 (удалить
  `/opt/multibot/app.prev` при переходе); проверяется не подстрока, а отсутствие создания копии (`cp `/`mv ` в строке
  с `app.prev`). В чек-листе — явная команда `sudo rm -rf /opt/multibot/app.prev`, без glob.
- Приняты дополнения backend в `DEPLOY_CHECKLIST.md`: в «Переходе с ручной выкладки» — шаг «выполнить «Установку
  (один раз)»»; уточнение, что первый запуск сразу ставит последний релиз; нумерация §4/§5 сохранена после удаления
  §3 (висящих ссылок на §3 нет); в «Частых проблемах» у `WebApp__DistPath` убрано «не залит webapp/dist»; пример
  `ln -sfn` + `mv -Tf` (или `ln -sfn`) в ручном откате.
- Тестовые списки 006 AC-1/AC-2 меняются спекой 008 (перенос шаблона), это не нарушение AC 006.
- MINOR 007 №1 (`pressMainWhenReady()` без текста) не входит в 008 — остаётся в реестре 007 до задачи по фронтенду.

## Сверка документации
Внешних библиотечных API нет. systemd: `EnvironmentFile=` (допустимые имена переменных), `OnUnitInactiveSec`,
`OnBootSec`, запуск таймером unit в состоянии failed — `man systemd.exec`/`systemd.timer` (отметить в `DOCS`).
Фильтр логирования по префиксу категории (`Microsoft.EntityFrameworkCore` покрывает
`Microsoft.EntityFrameworkCore.Database.Command`) — документация Microsoft.Extensions.Logging, при сомнении — Context7.

## Остаточные замечания (MINOR, не блокируют)
critic вынес APPROVED на круге 1. Не исправлено:
1. `DeployServerTests.AC1_OldTemplateLocation_IsGone` (строки 44–45): проверка индекса git фильтрует записи по наличию на диске
   и после проверки диска ничего не добавляет. На CI покрыто косвенно (свежий checkout вернул бы файл, и упала бы проверка диска).
2. `DEPLOY_CHECKLIST.md`, ручной откат, шаг восстановления БД — нет команды. Риски: `cp` без `-a` даёт файлы root
   (SQLite readonly), остаётся `bot.db-wal`/`-shm` плохого релиза. Предложение: `sudo rm -f /var/lib/multibot/bot.db-wal
   /var/lib/multibot/bot.db-shm && sudo cp -a /var/backups/multibot/<…>-before-<плохой тег>/. /var/lib/multibot/`.
3. `DEPLOY_CHECKLIST.md`, «Переход с ручной выкладки»: если сервер ставился по старому «варианту А» (framework-dependent),
   `releases/initial/app/Bot.Host` может не быть linux-x64 исполняемым — откат на `initial` не сработает. Нужна строка-предупреждение.
4. F8 (таймер срабатывает снова после выхода `multibot-update.service` с кодом 1) — по семантике systemd правдоподобно,
   подтвердить на сервере после первого `retry_later`.
