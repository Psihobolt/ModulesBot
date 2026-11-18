# Telegram-бот с Mini App — правила проекта

Платформа — модульный монолит: один хост (бот + API + Mini App), функциональность добавляется модулями.
Первый модуль — `MovieNight` (см. `docs/product/movie-night.md`).

## Стек
- Backend: .NET 10 (LTS), C# 14, ASP.NET Core (minimal API), Telegram.Bot, EF Core 10 + SQLite
  - `TargetFramework=net10.0`, SDK закреплён в `global.json` (10.0.x, `rollForward: latestFeature`)
  - общие настройки — в `Directory.Build.props`, версии пакетов — в `Directory.Packages.props` (Central Package Management)
  - nullable enabled, `TreatWarningsAsErrors=true`
  - современные BCL-конструкции (FrozenDictionary и т.п.) — только с явной причиной в комментарии
- Frontend (Mini App): React + TypeScript + Vite, официальный `telegram-web-app.js`
- Тесты: xUnit + NSubstitute (backend), Vitest + Testing Library (frontend)

## Структура
```
src/
  Bot.Host/                 — Program.cs, webhook, раздача webapp/dist, подключение модулей
  Bot.Platform/             — контракты платформы: IBotModule, авторизация initData,
                              IMessageSender, общий роутинг команд/колбэков
  Modules/
    MovieNight/             — модуль: Domain, Application, Api, Telegram, Persistence
tests/
  Bot.Platform.Tests/
  Modules.MovieNight.Tests/
webapp/
  src/platform/             — оболочка Mini App: инициализация WebApp, тема, API-клиент, роутинг
  src/modules/movie-night/  — экраны модуля
docs/
  product/                  — продуктовые брифы (пишет пользователь)
  tasks/                    — спецификации задач (пишет только architect)
```

## Правила модульности (обязательны)
- Модуль подключается одной строкой в Host: `builder.AddModule<MovieNightModule>()`.
- Модуль не ссылается на другие модули. Общение только через контракты `Bot.Platform`.
- У модуля свои: DbContext (своя схема/префикс таблиц), API-префикс `/api/<module>`, команды бота,
  фронтенд-папка `webapp/src/modules/<module>`, deep-link префикс `startapp=<module>_...`.
- Как создавать модуль — скилл `.claude/skills/bot-module/SKILL.md`.
- Всё про Mini App (initData, тема, кнопки, deep links) — скилл `.claude/skills/telegram-mini-app/SKILL.md`.

## Сверка с документацией библиотек (Context7)
Знания модели о библиотеках могут быть устаревшими (пример: в Telegram.Bot v22 переименованы методы,
`SendTextMessageAsync` → `SendMessage`). Поэтому:
- Разработчики ОБЯЗАНЫ сверять с Context7 каждое использование API внешней библиотеки, в котором нет
  100% уверенности: новые методы, конфигурация, атрибуты, расширения DI, миграции EF, API Vite/Vitest/React.
- Версию берут из `Directory.Packages.props` / `webapp/package.json` и ищут документацию именно этой версии.
- Порядок: `resolve-library-id` → `query-docs` с конкретным вопросом. BCL и синтаксис C# — без Context7.
- Нет библиотеки/версии в Context7 → официальная документация или README пакета, с отметкой в отчёте.
- Что сверено — перечисляется в отчёте агента (раздел `DOCS`).
Сервер подключён в `.mcp.json` под именем `context7`.

## Безопасность
- Каждый запрос Mini App к API авторизуется по `initData` с проверкой подписи на сервере.
  `initDataUnsafe` на клиенте — только для отображения, никогда для решений.
- Токен бота и ключи внешних API (TMDB, Кинопоиск, Context7) — только из конфигурации/секретов,
  не в коде и не в логах.
- Внешние API вызываются только с сервера; ответы кэшируются в БД; тесты не ходят в реальные API (моки/фейки провайдеров).
- Случайный выбор и любые решения, влияющие на нескольких пользователей, — только на сервере.

## Процесс
`/feature <описание>` → architect → (backend-developer ‖ frontend-developer ‖ tester) → critic.
- Источник правды — спецификация в `docs/tasks/`. Пробел в спеке → вопрос архитектору, не догадка.
- Разработчики не правят тесты, tester не правит production-код.
- Задача закрыта, когда: сборки чистые, все тесты зелёные, critic = `APPROVED`.
- Максимум 3 круга «critic → исправление», дальше — к пользователю.
- Preflight перед `/feature`: Context7 отвечает, `dotnet build -warnaserror` проходит на текущем состоянии.
  Сбой окружения (ключ Context7, NuGet-фид) решается до реализации, а не обходом флагов в агентах.
- Одна спецификация за раз: следующая пишется после `DONE` предыдущей (иначе её дорого сверять с кодом).
- Отклонения от текста спеки разработчики не решают сами: поле `DEVIATIONS`/`ISSUES` → architect.

## Команды
- Backend: `dotnet build -warnaserror`, `dotnet test --nologo`, `dotnet format --verify-no-changes`
- Frontend: `cd webapp && npm run build && npm run test -- --run && npm run lint`
