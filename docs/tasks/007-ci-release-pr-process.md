# 007 — CI/релиз через GitHub Actions и процесс «только через pull request»

**Статус:** DONE (critic APPROVED, круг 2; остаточные MINOR — в конце файла)
**Модуль:** platform (репозиторий и процесс; production-код приложения не меняется)   **Бриф:** —

> Нумерация: рефакторинг «MovieNight: сценарии групп в Application» сдвинут на **009** (008 — «Автообновление сервера
> из релизов GitHub»; ссылки в 005 и 006 обновлены).
>
> Изменено спекой 008: «Релиз из GitHub» в `DEPLOY_CHECKLIST.md` — автоматический (`deploy/server/multibot-update.sh`),
> ручной сценарий BE-4 и §3 удалены; шапка `release.yml` ссылается на скрипт (файл теперь существует).

## Исходное описание
«Я добавил файлы для деплоя приложения через GitHub Actions. Проверь их корректность для проекта. Также исправь skills
для нового поведения — теперь push изменений делается через merge request (pull request), в main ветку лить нельзя».

## Цель
1. Довести `.github/workflows/ci.yml` и `release.yml` пользователя до корректного и безопасного состояния: CI
   повторяет обязательные проверки проекта, релиз собирается только из `main`, а токен с правом записи не попадает
   в шаги со сторонним кодом (`npm ci`, сборка).
2. Закрепить процесс: работа ведётся в ветке задачи, в `main` изменения попадают только через PR с зелёным CI;
   правила записаны в `CLAUDE.md`, `/feature` и определениях агентов.
3. Описать ручные настройки GitHub (ruleset для `main`, environment релиза), которые кодом не задаются.

## Результаты аудита workflow (исходные файлы пользователя)
Сверка: синтаксис и семантика GitHub Actions (Context7 `/websites/github_en_actions`, `/actions/checkout`,
`/actions/setup-node`, `/actions/upload-artifact` — уточняет backend-developer в `DOCS`), код проекта.

### `ci.yml`
| # | Находка | Риск | Решение |
|---|---|---|---|
| C1 | `actions/checkout` без `persist-credentials: false`: токен остаётся в `.git/config` на время `npm ci` (install-скрипты зависимостей) и тестов | Средний | AC-3 |
| C2 | `push: branches: ['**']` + `pull_request`: каждый коммит ветки с открытым PR прогоняется дважды; группы concurrency разные (`refs/heads/x` и `refs/pull/N/merge`), так что дубли не отменяются | Низкий (расход минут) | AC-1 |
| C3 | `cancel-in-progress: true` для всех событий: новый push в `main` отменяет проверку предыдущего merge-коммита, и у него не остаётся результата CI | Средний | AC-7 |
| C4 | `setup-dotnet` с `dotnet-version: '10.0.x'` вместо `global-json-file: global.json`: два источника правды о SDK | Низкий | AC-5 |
| C5 | Версии actions `@v4` (`checkout`, `setup-dotnet`, `setup-node`) — устаревшие majors на рантайме Node 20, который GitHub выводит из эксплуатации | Средний (предупреждения, затем падение) | AC-4, BE-1 |
| C6 | Шаги `run` без явного `shell: bash`: по умолчанию `bash -e` без `pipefail` | Низкий | AC-6 |
| C7 | Нет линтера workflow: опечатка в выражении или ключе всплывает только на GitHub | Низкий | AC-12 |
| C8 | Шаг «без внешних адресов» дублирует и расширяет тест 003 AC-23 (dist без `telegram.org`) | Нет | Оставить: строже теста, дёшево |
| — | Порядок `npm run build` → `npm run test` верный (003 AC-23 читает `dist`); frontend и `dotnet test` идут последовательно (без flaky-параллели); hygiene-тесты 006 работают в shallow-клоне (`ls-files`/`check-ignore`, история не нужна, `fetch-depth` не нужен); `permissions: contents: read`; нет `pull_request_target`; секреты не используются и в логи не выводятся | — | Сохранить |

### `release.yml`
| # | Находка | Риск | Решение |
|---|---|---|---|
| R1 | Job `release` с `contents: write` и `persist-credentials` по умолчанию выполняет `npm ci` и `dotnet publish`. Install-скрипт любой npm-зависимости получает токен с правом записи (создать тег/релиз, push в незащищённые ветки) | **Высокий** | AC-9: сборка в job с `contents: read`, публикация — в отдельном job без checkout/npm/dotnet |
| R2 | Комментарий ссылается на `deploy/server/multibot-update.sh` — **файла нет**; механизм доставки релиза на сервер отсутствует, `DEPLOY_CHECKLIST.md` о релизах GitHub ничего не говорит | **Высокий** (релиз есть, деплоя нет) | Решение 1A: в 007 — ручной сценарий «Релиз из GitHub» в чек-листе (BE-4), ссылка из `release.yml` убрана (BE-2); pull-автоматизация — будущая спека |
| R3 | Запуск не из `main`: job `checks` пропускается по `if`, `release` пропускается по `needs` — run «зелёный/skipped» без объяснения | Низкий | AC-8: явная ошибка |
| R4 | Нет `permissions` на уровне workflow (защищено только тем, что оба job задают свои) — новый job унаследует права по умолчанию репозитория | Средний | AC-2 |
| R5 | `cache: npm` в релизной сборке: кэш главной ветки может быть отравлен любым workflow, работавшим в `main` | Низкий | AC-9: релиз без кэша |
| R6 | Нет `environment`: защиту «только из main» на стороне GitHub не задать | Низкий | AC-8 (+ ручная настройка) |
| R7 | Очистка старых релизов `… \|\| true` скрывает ошибки | Низкий | `continue-on-error: true` вместо `\|\| true` (ошибка видна как предупреждение) |
| R8 | Архив self-contained `linux-x64` ↔ `DEPLOY_CHECKLIST` по умолчанию описывает вариант А (`dotnet Bot.Host.dll`) | Низкий | BE-4: для релизов — вариант Б |
| — | Версия проверяется регуляркой через `env`, без подстановки `${{ inputs.version }}` в скрипт (инъекции нет); раскладка архива `app/` + `webapp/` совпадает с `/opt/multibot/app`, `/opt/multibot/webapp` и `WebApp__DistPath` шаблона; группы concurrency `release` и CI не совпадают (нет взаимной блокировки); `gh` есть на `ubuntu-24.04` | — | Сохранить |

## Сценарии
1. Разработчик (оркестратор по команде) пушит ветку `chore/007-ci-pr-process`, открывает PR в `main` → запускается
   `ci` (job `build`) один раз; merge доступен только при зелёном `build`.
2. Прямой `git push origin main` или `--force` отклоняется GitHub (ruleset, ручная настройка пользователя).
3. После merge `ci` прогоняется на `main` и не отменяется следующим merge.
4. Пользователь запускает `release` (ветка `main`, `v0.1.0`) → CI → сборка → публикация релиза с архивом и sha256.
   Запуск из другой ветки падает с понятной ошибкой.
5. Пользователь вручную разворачивает релиз на сервере по `DEPLOY_CHECKLIST.md`, раздел «Релиз из GitHub».

## Критерии приёмки
AC-1…AC-10 проверяются автотестом, разбирающим YAML (`WorkflowTests`, TEST-1); AC-11…AC-12 — CI на GitHub;
AC-13…AC-16 — ревью critic (см. реестр отклонений); AC-18 — 10 прогонов frontend-тестов (TEST-3). «Все workflow» = все `*.yml`/`*.yaml` в `.github/workflows/`
(множество непусто — тест это проверяет).

- **AC-1 (триггеры CI).** Given `ci.yml`, Then `on` содержит ровно: `pull_request` (с `branches: [main]`),
  `push` с `branches: [main]`, `workflow_dispatch`, `workflow_call`. **And** ни один workflow не использует
  `pull_request_target` и `workflow_run`.
- **AC-2 (права).** Given все workflow, Then у каждого есть `permissions` на верхнем уровне; в `ci.yml` это ровно
  `contents: read`; в `release.yml` — пустой набор (`{}`). У каждого job `release.yml` `permissions` заданы явно;
  `contents: write` есть ровно у одного job (публикация), других scope со значением `write` и `write-all` нет ни в
  одном workflow.
- **AC-3 (учётные данные checkout).** Given все шаги `uses: actions/checkout@…` во всех workflow, Then у каждого
  `with.persist-credentials: false`.
- **AC-4 (ссылки на actions).** Given все `uses:` (шагов и job), Then каждый — либо локальный `./.github/workflows/…`,
  либо `actions/<имя>@<ref>`, где `<ref>` — `v<N>` (мажорный тег) или 40-символьный hex SHA; `@main`, `@master`,
  `@latest` и сторонние владельцы отсутствуют (исключение — `rhysd/actionlint`, если линтер подключён через action,
  а не через docker-образ, с тем же правилом `<ref>`).
- **AC-5 (тулчейн).** Given все шаги `actions/setup-dotnet`, Then `with.global-json-file: global.json` и нет
  `dotnet-version`. Given все шаги `actions/setup-node`, Then у всех одинаковое значение `node-version` (`'22'`).
- **AC-6 (команды CI = CLAUDE.md «Команды»).** Given `ci.yml`, Then есть job с id `build`; `defaults.run.shell` —
  `bash` (на уровне workflow или job `build`); в шагах job `build` в этом порядке встречаются строки:
  `dotnet build -warnaserror`, `dotnet test --nologo`, `dotnet format --verify-no-changes`, `npm ci`,
  `npm run build`, `npm run test -- --run`, `npm run lint`; npm-команды выполняются в `webapp`
  (`working-directory: webapp` шага или `cd webapp` в той же строке). `npm run build` — строго раньше `npm run test`.
  **Источник списка команд — раздел `## Команды` файла `CLAUDE.md`** (решение critic-1 №3): тест читает его, а не
  держит копию; `npm ci` — единственная команда CI сверх `CLAUDE.md` (установка зависимостей), стоит перед первой
  npm-командой.
- **AC-7 (concurrency).** Given `ci.yml`, Then `cancel-in-progress` истинно только для события `pull_request`
  (выражение по `github.event_name`), а для `push` в `main`, `workflow_dispatch` и вызова из релиза — ложно.
  Given `release.yml`, Then `concurrency.group` — константа, `cancel-in-progress: false`. **And** ни при каком
  событии группа CI не может совпасть с группой релиза (инвариант против взаимной блокировки вызывающего и
  вызываемого workflow). **And** CI, вызванный из релиза, не делит группу с самостоятельными прогонами `ci`
  (в т.ч. ручным `workflow_dispatch` из `main`): ожидающий прогон группы GitHub отменяет при постановке нового
  независимо от `cancel-in-progress`, и ручной запуск `ci` не должен отменять проверку релиза.
  **And** (решение critic-1 №2) прогоны `push` двух разных коммитов `main` никогда не делят группу: каждый
  merge-коммит получает собственный результат CI, даже при нескольких merge подряд. Для `pull_request`
  группа по-прежнему общая для всех прогонов одного PR (иначе новый push в PR не отменит устаревший прогон).
- **AC-8 (релиз только из main).** Given `release.yml`, Then первый job (guard) завершается с ошибкой и сообщением
  `::error::Релиз запускается только из ветки main, получено: <ref>`, если `github.ref != 'refs/heads/main'`; все
  остальные job прямо или транзитивно `needs` guard (запуск не из `main` = run со статусом failure, а не skipped).
  Job публикации указывает `environment: release`.
- **AC-9 (разделение сборки и публикации).** Given `release.yml`, Then: (а) job, выполняющий `npm ci`,
  `dotnet publish` или `actions/checkout`, не имеет `contents: write`; (б) job с `contents: write` не содержит
  `actions/checkout`, `setup-node`, `setup-dotnet`, `npm`, `dotnet`, а получает архив через
  `actions/download-artifact` и публикует через `gh` (репозиторий — через `GH_REPO`/`--repo`); (в) ни один
  `setup-node` в `release.yml` не включает `cache`; (г) публикуемый коммит — тот же, что прошёл CI и сборку
  (`--target` = `GITHUB_SHA` того же run); (д) релиз публикуется только с **новым** тегом: если тег версии уже
  существует или его существование не удалось установить (ошибка API, кроме «не найден»), run завершается
  failure с `::error::` до `gh release create` — и в `build`, и непосредственно перед публикацией в `publish`
  (`gh release create` с существующим тегом не падает, а молча привязывает релиз к старому коммиту, игнорируя
  `--target`). Пункт (д) проверяет critic (см. реестр).
- **AC-10 (нет инъекций в скрипты).** Given все шаги `run` во всех workflow, Then в тексте скрипта нет выражений
  `${{ inputs.… }}`, `${{ github.event.… }}`, `${{ github.head_ref }}` — такие значения передаются только через `env`.
- **AC-11 (CI на GitHub).** Given PR этой задачи в `main`, Then проверка `build` зелёная, в логах нет предупреждений
  об устаревшем рантайме Node у actions и нет значений секретов. *Проверяет оркестратор/пользователь по run на GitHub.*
- **AC-12 (actionlint).** Given job `build`, Then первым содержательным шагом после checkout выполняется actionlint
  закреплённой версии (docker-образ `rhysd/actionlint:<версия>` или action по AC-4) по всем workflow; находок нет.
  Локально — если `actionlint` установлен; иначе отметка в отчёте и проверка по AC-11.
- **AC-13 (правила процесса в CLAUDE.md).** `CLAUDE.md` содержит раздел «Ветки и pull request» дословно по BE-5,
  а в «Процесс» — ссылку на него.
- **AC-14 (`/feature`).** `.claude/commands/feature.md` изменён дословно по BE-6.
- **AC-15 (агенты).** Определения агентов изменены дословно по BE-7.
- **AC-16 (документация).** `README.md` и `DEPLOY_CHECKLIST.md` изменены по BE-4/BE-8; `release.yml` не ссылается на
  несуществующие файлы (в частности, нет `deploy/server/multibot-update.sh`), шапка ссылается на
  `DEPLOY_CHECKLIST.md`, раздел «Релиз из GitHub».
- **AC-18 (стабильность frontend-тестов).** Given полный набор `npm run test -- --run`, When он запускается
  10 раз подряд на неизменном коде, Then все 10 прогонов зелёные. Ни один frontend-тест не вызывает
  `tg.pressMain()`/`pressBack()` (или аналог нажатия кнопки Telegram) до того, как наблюдаемо зарегистрирован
  обработчик именно того состояния экрана, которое тест проверяет. (TEST-3)
- **AC-17 (регрессия).** `dotnet build -warnaserror`, `dotnet test --nologo` (включая сканер секретов 006 — он
  проверяет и `.github/workflows/*.yml` по правилам R1–R5), `dotnet format --verify-no-changes`, фронтенд-команды —
  зелёные.

## Контракт API / бота / экраны Mini App
Не меняются.

## Целевая структура workflow (контракт для BE-1/BE-2 и TEST-1)
```
ci.yml      on: pull_request[main], push[main], workflow_dispatch, workflow_call
            permissions: contents: read
            concurrency: group ci-<workflow>-<event>-<ключ>, cancel-in-progress: event == pull_request
                         (<workflow> = github.workflow: в вызове из релиза это «release», иначе «ci»;
                          <ключ> уникален для коммита при push, общий для PR — инварианты AC-7)
            defaults.run.shell: bash
            jobs.build: checkout(persist-credentials:false) → actionlint → setup-dotnet(global.json)
                        → setup-node(22, cache npm) → backend 3 команды → webapp 4 команды → «без внешних адресов»
release.yml on: workflow_dispatch(inputs.version: string, required)
            permissions: {}
            concurrency: group release, cancel-in-progress: false
            jobs.guard   (permissions: {}):            ref == main, иначе ::error:: и exit 1
            jobs.checks  (needs guard, contents:read): uses ./.github/workflows/ci.yml
            jobs.build   (needs checks, contents:read): версия (regex, тег не существует) → checkout(persist:false)
                         → setup-dotnet → setup-node(без cache) → publish self-contained linux-x64 + npm ci/build
                         → упаковка (app/, webapp/, COMMIT; test -x, test -f) → sha256 → upload-artifact (retention 1 день)
            jobs.publish (needs build, contents:write, environment: release):
                         download-artifact → тег не существует (AC-9 д) → gh release create (target = GITHUB_SHA,
                         --generate-notes --latest)
                         → очистка старше 15 (continue-on-error: true, теги не трогать)
```
Имена job `guard`/`build`/`publish` — рекомендация; инварианты — в AC-2, AC-8, AC-9. Тег проверяется в `build`
(до сборки, быстрый отказ) и повторно в `publish` перед `gh release create` (AC-9 д): гонку двух запусков
`release` сериализует concurrency, а тег, созданный вручную между сборкой и публикацией, ловит вторая проверка.

## Задачи
### Backend (конфигурация, документация, процесс; C#-код приложения не меняется)
- **BE-1** `ci.yml` по «Целевой структуре» (AC-1, AC-3…AC-7, AC-10, AC-12). Мажорные версии `actions/checkout`,
  `setup-dotnet`, `setup-node`, `upload-artifact`, `download-artifact` и версию actionlint взять **актуальные на
  дату работы** через Context7 (и README/релизы action), с рантаймом, не помеченным как устаревший; перечислить в
  `DOCS`. Проверить по документации: поддержку выражений в `concurrency.cancel-in-progress`, значение
  `github.event_name`/`github.workflow` внутри вызываемого (`workflow_call`) workflow, `global-json-file` в
  `setup-dotnet`. Сохранить шаг «без внешних адресов» и комментарий про имя проверки `build`.
- **BE-2** `release.yml` по «Целевой структуре» (AC-2, AC-3…AC-5, AC-7…AC-10). `inputs.version` — `type: string`,
  `required: true`; регулярка версии и сообщения об ошибке — как у пользователя. Комментарий в шапке — без ссылки на
  `deploy/server/multibot-update.sh`: вместо неё — «Развёртывание на сервере — вручную: DEPLOY_CHECKLIST.md, раздел
  «Релиз из GitHub»» (решение 1A).
- **BE-3** Если `actionlint` доступен локально — прогнать по обоим файлам, результат в отчёт. Не устанавливать
  глобальные инструменты без согласия пользователя (вопрос → `ISSUES`).
- **BE-4** `DEPLOY_CHECKLIST.md`: новый раздел «Релиз из GitHub» перед §3 (§3 оставить как ручной путь): запуск
  `release` (Actions → release → Run workflow, ветка `main`, `vX.Y.Z`); на сервере — скачать `multibot.tar.gz` и
  `.sha256` из релиза (публичный репозиторий — без токена), `sha256sum -c`, распаковать во временный каталог,
  сохранить `/opt/multibot/app` в `app.prev`, `rsync --delete` `app/` → `/opt/multibot/app/`, `webapp/` →
  `/opt/multibot/webapp/`, `chown`, рестарт, проверка §4. Указать: архив self-contained `linux-x64`, в unit —
  `ExecStart=/opt/multibot/app/Bot.Host` (вариант Б), runtime не нужен; ARM — только ручной путь §3. Адрес
  репозитория в командах — реальный `Psihobolt/ModulesBot` (`https://github.com/Psihobolt/ModulesBot/releases/download/vX.Y.Z/…`),
  не плейсхолдер. (AC-16)
- **BE-5** `CLAUDE.md`: в «Процесс» добавить пункт «Git — только через ветку задачи и pull request, см. «Ветки и pull
  request».» и новый раздел перед «Команды» — **дословно**:
  ```
  ## Ветки и pull request
  - `main` защищена: коммиты, `push` и `push --force` в `main` запрещены. Изменения попадают в `main` только через
    pull request с зелёной проверкой CI `build` (`.github/workflows/ci.yml`).
  - Одна спецификация = одна ветка = один PR. Ветка — от коммита, который уже есть в `origin/main`; имя
    `<type>/<NNN>-<slug>` (`feat/008-movienight-groups-app`, `chore/007-ci-release-pr-process`), без спеки — `<type>/<slug>`.
  - Коммит, push ветки и создание PR (`gh pr create`; без `gh` — ссылка из вывода `git push`) — только по команде
    пользователя, их выполняет оркестратор. Merge PR — только пользователь в GitHub, способом squash merge (заголовок
    PR в Conventional Commits = коммит в `main`); агенты PR не мержат и не одобряют.
  - Переписывать историю (`--force-with-lease`) можно только в своей ветке задачи до merge; `main` — никогда.
  - Субагенты git не меняют: `commit`, `push`, `merge`, `rebase`, `reset`, `switch`/`checkout`, `branch`, `tag`,
    `stash`, `gh pr …` запрещены; чтение (`status`, `diff`, `log`, `show`) разрешено.
  - Команды CI = раздел «Команды». Меняете команды — меняйте и `ci.yml` (сверяет тест `WorkflowTests`).
  ```
  Пункт «Процесс» про коммит не дублировать. (AC-13)
- **BE-6** `.claude/commands/feature.md` (AC-14), дословно по смыслу пунктов ниже, формулировки — как здесь:
  - Шаг 0, новый пункт: «Ветка: `git branch --show-current` и `git status`. Работа в `main` недопустима. Если текущая
    ветка `main` — ветку задачи создашь после шага 1 (нужен номер спеки). Если локальный `main` опережает
    `origin/main` (`git rev-list --count origin/main..main` > 0) — остановись и спроси пользователя: коммиты в `main`
    по правилам не допускаются.»
  - Шаг 1, после «Запускаю разработку?» = да: «Если ты в `main` — `git switch -c <type>/<NNN>-<slug>` (незакоммиченные
    правки переносятся в ветку, ничего не сбрасывай). Если уже в ветке другой задачи с незакрытым PR — спроси
    пользователя.»
  - Шаг 4, заменить последнюю строку на: «Предложи сообщение коммита и заголовок PR (Conventional Commits,
    `feat(<module>): …`). По команде пользователя: коммит в ветку задачи → `git push -u origin <ветка>` → PR в `main`
    (`gh pr create --base main --title … --body …`, тело: ссылка на спеку, список AC, результаты тестов, вердикт
    critic; нет `gh` — дай пользователю ссылку на создание PR из вывода `git push`). Merge — только пользователь после
    зелёного `build`; способ — squash merge, поэтому заголовок PR = сообщение итогового коммита в `main`.
    Никогда: push/force-push в `main`, merge PR. После merge следующая задача начинается с
    `git switch main && git pull --ff-only`.»
- **BE-7** Агенты (AC-15):
  - `backend-developer.md`, `frontend-developer.md`, `tester.md`, `architect.md` — в конец раздела правил/дисциплины
    строка: «- **Git — только чтение** (`status`, `diff`, `log`, `show`). Ветки, коммиты, push и PR делает оркестратор
    (`CLAUDE.md`, «Ветки и pull request»).»
  - `critic.md`, «Что проверить», новый пункт 10: «**Ветка**: изменения задачи — в ветке задачи, не в `main`
    (`git branch --show-current`); ревьюй полный объём `git diff origin/main...HEAD` + незакоммиченное (`git status`).
    Работа в `main` — BLOCKER `[critic/process]`.»
  - `.claude/skills/bot-module/SKILL.md`, `.claude/skills/telegram-mini-app/SKILL.md` — **не меняются** (в них нет
    правил git/процесса; см. допущения).
- **BE-8** `README.md`: шаг 5 «Старт» — «…проверяйте вручную в Telegram; изменения — через ветку и PR (см. `CLAUDE.md`,
  «Ветки и pull request»).»; новый раздел «CI, ветки и релизы»: что делают `ci`/`release`, имя обязательной проверки
  `build`, как выпустить релиз, и **ручные настройки GitHub** (список из раздела ниже). Убрать из раздела «Секреты»
  число «116 тестов» (MINOR 006 №1). (AC-16)

- **BE-9** (critic-1 №2) `ci.yml`: группа concurrency по последнему «And» AC-7 — прогоны `push` разных коммитов
  в разных группах; поведение `pull_request` (общая группа PR + отмена) и остальные инварианты AC-7 не меняются.
  Обновить комментарий над `concurrency` (почему у push группа на коммит: ожидающий прогон вытесняется новым
  независимо от `cancel-in-progress`). Значение контекста для push сверить по Context7 (`DOCS`).

### Frontend
Нет.

### Tester
- **TEST-1** `tests/Bot.Platform.Tests/RepositoryHygiene/WorkflowTests.cs` — структурные тесты AC-1…AC-10 по YAML
  (корень репозитория — существующий `GitRepo.Root`). Разбор — `YamlDotNet` (RepresentationModel или десериализация
  в словари): tester добавляет `PackageVersion` в `Directory.Packages.props` и `PackageReference` в
  `Bot.Platform.Tests.csproj` (исключение из правила «корневые файлы — не tester'а»: backend этот файл в 007 не
  трогает). Версию пакета сверить через Context7/NuGet, указать в `DOCS`. Требования: множество workflow непусто;
  `on`/`true`-ключ YAML 1.1 (`on:` может разобраться как булево) обработан явно; сообщения падения называют файл,
  job и шаг. Негативы — на встроенных YAML-образцах в тесте (по одному на AC-1, AC-2, AC-3, AC-8, AC-9, AC-10):
  `pull_request_target`, job с `contents: write` и `npm ci`, checkout без `persist-credentials`,
  `${{ inputs.version }}` в `run`, release без guard. (AC-1…AC-10)
- **TEST-2** Прогнать полный набор (AC-17), включая сканер секретов 006 на `.github/workflows/*`. Если R5 даёт
  находку на workflow — это `ISSUES` (не allowlist без архитектора).
- **TEST-3** (critic-1 №1, AC-18) Flaky `webapp/src/platform/__tests__/app.test.tsx` «AC23: retry that fails again
  stays on the error screen with MainButton»: нажатие MainButton до регистрации обработчика. Перед каждым
  `pressMain()` дождаться наблюдаемой готовности кнопки (текст нужного состояния и зарегистрированный обработчик —
  по средствам существующего мока `tg`). Проверить тот же шаблон во **всех** frontend-тестах
  (`webapp/src/**/__tests__/*`, включая `groups.test.tsx` и тесты модулей), в т.ч. для BackButton, и исправить
  везде. Критерий — 10 прогонов `npm run test -- --run` подряд зелёные (число прогонов и результат — в отчёт).
  Только тесты; если нестабильность воспроизводится при корректном ожидании (т.е. дефект в production-коде) —
  `ISSUES` с описанием, production-код не трогать.
- **TEST-4** (critic-1 №3, AC-6) `WorkflowRules`: убрать ручной список `CiCommands`; команды брать из раздела
  `## Команды` `CLAUDE.md` (содержимое в обратных кавычках до следующего `##`, разбиение по `&&`, `cd webapp` →
  признак «npm-команды в `webapp`»). Пустой/неразобранный раздел — падение теста с понятным сообщением, а не
  пропуск. `npm ci` добавляется тестом явно (перед первой npm-командой). Позитив — на реальных файлах; негатив —
  встроенный образец CLAUDE.md с командой, которой нет в YAML.
- **TEST-5** (critic-1 №4, AC-9 б) Негативы на встроенных YAML: job с `contents: write` без `checkout`, но с
  `run: dotnet …` и отдельно с `run: npm run …` — правило AC-9 (б) срабатывает и называет job.

## Ручные настройки GitHub (делает пользователь; в README по BE-8)
1. **Ruleset для `main`** (Settings → Rules → Rulesets → New branch ruleset, target: default branch, Enforcement:
   Active, bypass list — пустой): Restrict deletions; Block force pushes; Require a pull request before merging
   (required approvals = 0 — разработчик один; при появлении второго — 1); Require status checks to pass → `build`
   (Require branches to be up to date — вкл.); Require linear history. Для приватного репозитория на
   бесплатном плане rulesets не применяются — тогда правило держится только процессом.
2. **Settings → General → Pull Requests:** оставить только «Allow squash merging» (default commit message — «Pull
   request title»), выключить merge commits и rebase merging; вкл. «Automatically delete head branches».
3. **Settings → Actions → General:** Workflow permissions = «Read repository contents and packages permissions»;
   выкл. «Allow GitHub Actions to create and approve pull requests»; Fork PR workflows — «Require approval for all
   outside collaborators».
4. **Settings → Environments → New environment `release`:** Deployment branches and tags → Selected branches →
   `main`. Секретов не требуется (публикация — `GITHUB_TOKEN`).
5. Проверка: после первого PR в Rulesets → Insights видно, что прямой push в `main` отклонён.

## Вне скоупа
- Автоматическая доставка релиза на сервер (pull: скрипт + systemd-таймер) — будущая отдельная спека (решение 1A);
  сейчас не пишется. SSH-деплой из Actions не делается.
- Подпись/attestation артефактов, SBOM, Dependabot, gitleaks в CI (сканер секретов уже в `dotnet test`).
- Кэш NuGet в CI (нет `packages.lock.json`; включение lock-файлов — отдельное решение).
- Изменение C#-кода и фронтенда.

## Открытые вопросы
Нет.

## Принятые решения (ответы пользователя)
1. **Доставка релиза — вариант A (pull позже).** В 007 только ручной сценарий «Релиз из GitHub» в `DEPLOY_CHECKLIST.md`
   (BE-4); ссылка на несуществующий `deploy/server/multibot-update.sh` удаляется из `release.yml` и заменяется
   ссылкой на ручной сценарий (BE-2, AC-16). Автоматизация pull (скрипт + systemd-таймер) — будущая отдельная спека,
   сейчас не пишется. Секретов для деплоя в GitHub нет.
2. **PR — вариант A.** Оркестратор по команде пользователя: коммит → `git push -u origin <ветка>` → `gh pr create`
   (без `gh` — ссылка на создание PR из вывода `git push`). Merge — только пользователь (BE-5, BE-6).
3. **Merge — вариант A.** Squash merge + Require linear history; заголовок PR в Conventional Commits = коммит в
   `main` (BE-5, BE-6, ручные настройки п. 1–2).

## Принятые решения архитектора / допущения
- Версии actions и actionlint сверяет backend-developer через Context7 (доступен); actionlint локально не проверен —
  BE-3 как есть (прогнать, если установлен, иначе отметка в отчёте; гарантия — AC-12 в CI).
- Кто правит `.claude/*` и `CLAUDE.md`: обычно пользователь, здесь — по его прямой просьбе. Делает
  backend-developer **дословно по тексту спеки** (BE-5…BE-7); critic сверяет текст. Ограничение 006 «не трогать
  `CLAUDE.md`/`.claude/*`» к 007 не применяется.
- Skills `bot-module` и `telegram-mini-app` описывают модули и Mini App и не содержат правил git — не меняются;
  «skills» в запросе пользователя понимаются как вся конфигурация агентов (`CLAUDE.md`, `/feature`, агенты).
  Отдельный skill для git не заводится: правило короткое и должно быть в `CLAUDE.md`, который читают все роли.
- CI на push только в `main`; ветки проверяются через PR (в т.ч. draft PR). Ранняя проверка ветки без PR — через
  `workflow_dispatch`.
- Node в CI — `22`, как выбрал пользователь (поддерживается Vite 8 / Vitest 5 / ESLint 10); единый для всех шагов
  (AC-5). Отдельный `.nvmrc` не вводится.
- Required approvals = 0: при одном разработчике 1 approval невозможен без bypass, а bypass ослабил бы запрет.
- Релиз собирается заново из того же SHA, что прошёл CI (не переиспользуются артефакты CI-сборки: CI собирает Debug).
- Хранение: 15 последних релизов, теги не удаляются (решение пользователя из исходного файла).
- Проверяемость без GitHub: структурные тесты YAML (TEST-1) + actionlint в CI (AC-12) + локально, если установлен.
  Поведение на GitHub (AC-11, первый релиз) — ручная проверка.
- Ветка текущей работы `chore/ci-deploy-workflows` создана до правила об именах; переименовывать не требуется.

## Решения по DEVIATIONS/ISSUES backend (круг 1)
1. **Проверка тега через `gh api repos/$GH_REPO/git/ref/tags/$VERSION`** (до checkout, `contents: read`) — принято.
   Доработать (backend): сейчас `>/dev/null 2>&1` трактует любую ошибку API как «тега нет»; нужно различать
   «не найден» (продолжить) и прочие ошибки (failure) — AC-9 (д). Та же проверка — в `publish` перед
   `gh release create`.
2. **Тег из `build` в `publish` через job output `tag`** — принято (значение прошло regex; в `publish` передаётся
   через `env`, AC-10 соблюдён).
3. **actionlint — docker-образ `rhysd/actionlint:1.7.12`** — принято (вариант AC-12; `uses:` только `actions/*`).
4. **CI: `pull_request`/`push` — `branches: [main]`; push в другие ветки CI не запускает** — принято, это и есть
   AC-1 (допущение «CI на push только в main»).
5. **`GH_REPO` в `publish`, очистка — `continue-on-error: true`** — принято (требуется AC-9 б и R7).
6. **README — ручные настройки текстом спеки** — принято. **DEPLOY_CHECKLIST — плейсхолдер `<owner>/<repo>`**
   заменить на `Psihobolt/ModulesBot` (публичный репозиторий, адрес известен; копипаста команд без правки). Логин
   GitHub — публичная идентичность, к персональным данным 006 не относится.
- ISSUE «actionlint локально не установлен» — принято по BE-3/AC-12 (гарантия — CI, AC-11).
- ISSUE «версии actions» (checkout@v7, setup-dotnet@v6, setup-node@v7, upload-artifact@v7, download-artifact@v8,
  actionlint 1.7.12) — принято; фактическое отсутствие предупреждений о рантайме подтверждает AC-11.
- ISSUE «concurrency»: с AC-7 в редакции круга 0 не противоречит (группа `ci-…` ≠ `release`, взаимной блокировки нет).
  Но очередь общая с ручным `ci` из `main`, а ожидающий прогон группы GitHub вытесняет новым **независимо от
  `cancel-in-progress`** — второй ручной запуск `ci` мог бы отменить проверку релиза. AC-7 дополнен; backend:
  в группу добавить `github.workflow` (в вызываемом workflow контекст `github` — вызывающего, т.е. `release`),
  префикс `ci-` сохраняется, комментарий в `ci.yml` обновить. Tester: существующая проверка префикса остаётся
  верной; новый тест не требуется.

## Решения по замечаниям critic (круг 1)
1. **[MAJOR] flaky AC23 retry в `app.test.tsx`** — включено в 007: 007 делает frontend-тесты обязательной
   проверкой merge, нестабильный тест блокировал бы PR случайно. AC-18, **TEST-3** (tester).
2. **[MINOR] concurrency push в `main`** — исправить: инвариант «разные коммиты push — разные группы» в AC-7,
   **BE-9** (backend). Структурно не тестируется — ревью critic (реестр).
3. **[MINOR] `CiCommands` дублирует CLAUDE.md** — тест читает команды из `CLAUDE.md` (строка BE-5 «сверяет тест
   `WorkflowTests`» становится верной без правки текста). AC-6, **TEST-4** (tester).
4. **[MINOR] нет негатива на `dotnet`/`npm run` в publish** — добавить. **TEST-5** (tester).

Распределение: backend — BE-9; tester — TEST-3, TEST-4, TEST-5 (+ повтор TEST-2). Frontend — нет.

## Реестр принятых отклонений
- Инвариант AC-7 «push разных коммитов — разные группы» проверяется ревью critic, а не `WorkflowTests`
  (семантика выражения группы на GitHub структурным тестом не проверяется).
- TEST-3 правит тесты вне скоупа 007 (`webapp/src/**/__tests__`) — допущено как условие обязательности CI.
- AC-11 (run на GitHub) и AC-13…AC-16 (тексты процесса и документации) проверяются ревью/запуском, а не автотестом
  (по прецеденту 006 AC-7).
- Tester правит корневой `Directory.Packages.props` (добавление `YamlDotNet`) — исключение для 007.
- AC-9 (д) (повторная проверка тега и различение «не найден»/ошибка) и разделение групп CI по `github.workflow`
  (AC-7, последнее «And») проверяются ревью critic, а не `WorkflowTests` (поведение shell-скрипта и семантика
  контекста на GitHub структурным тестом не проверяются).
- Проверка тега через REST API (`gh api …/git/ref/tags/…`) вместо `git ls-remote`; передача тега через job output;
  actionlint через docker-образ; push в ветки, отличные от `main`, CI не запускает (круг 1, решения 1–4).

## Сверка документации
GitHub Actions — Context7 `/websites/github_en_actions` (permissions, concurrency и выражения в ней, `workflow_call`
и контекст `github` в вызываемом workflow, environments, `defaults.run.shell`, script injection), `/actions/checkout`
(`persist-credentials`), `/actions/setup-node` (`cache`, `cache-dependency-path`), `/actions/upload-artifact`,
download-artifact, setup-dotnet (`global-json-file`), actionlint (README). `gh release create/list/delete` — `gh help`.
YamlDotNet — версия из `Directory.Packages.props` после добавления. Всё сверенное — в `DOCS` отчётов.

## Остаточные замечания (MINOR, не блокируют)
critic вынес APPROVED на круге 2. Не исправлено:
1. Во многих местах frontend-тестов `pressMainWhenReady()` вызывается без `text` (например, `groups.test.tsx:62`, `joinRename.test.tsx:42, 92`, `group.test.tsx:225`, `app.test.tsx:383`): хелпер ждёт любой зарегистрированный обработчик, а не обработчик конкретного состояния, как формулирует AC-18. Сейчас безопасно (перед вызовом есть `expectText`, у экрана один обработчик); лучше передавать текст кнопки везде.
2. actionlint локально не установлен — AC-11/AC-12 подтверждаются только первым запуском CI на GitHub после push ветки.
