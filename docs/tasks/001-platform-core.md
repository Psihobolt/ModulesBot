# 001 — Платформа: Host, модули, авторизация initData, оболочка Mini App

**Статус:** DONE
**Модуль:** platform   **Бриф:** docs/product/movie-night.md (Этап 0, часть 1 из 2)
**Следующая часть:** docs/tasks/002-platform-bot.md (webhook, роутинг бота, IMessageSender)

## Исходное описание
«Этап 0 из docs/product/movie-night.md: каркас платформы без модулей».
Из брифа: «Платформа: Host, `IBotModule`, webhook, раздача Mini App, проверка initData, оболочка webapp
(тема, роутинг по start_param, API-клиент), тестовые проекты.»

Этап разбит на две спецификации, чтобы каждая укладывалась примерно в день:
- **001 (эта)** — решение и Host, контракт `IBotModule` (без бота), авторизация initData, формат ошибок API,
  раздача `webapp/dist`, реестр пользователей, оболочка Mini App, тестовая инфраструктура.
- **002** — бот: webhook, роутинг команд/колбэков/start-payload, `IMessageSender`, регистрация webhook и команд.
  Идёт после 001 (расширяет `IBotModule` методом `MapBot`). Фронтенда в 002 нет.

## Цель
Получить рабочий модульный монолит без модулей: Host запускается, авторизует запросы Mini App по initData,
раздаёт Mini App; модуль подключается одной строкой и сразу получает `/api/<name>` за авторизацией;
Mini App запускается в Telegram, применяет тему, вызывает API и умеет маршрутизировать `start_param` в модуль.

## Сценарии
1. Разработчик пишет `builder.AddModule<XModule>()` — у модуля появляются сервисы и эндпоинты
   под `/api/x`, защищённые initData. Больше в Host ничего не меняется.
2. Пользователь открывает Mini App в Telegram → видит «Загрузка…» → главный экран «Привет, <имя>!»
   и список разделов (сейчас — пустой, с заглушкой).
3. Пользователь открывает ссылку `t.me/<bot>/<app>?startapp=<module>_<payload>` → оболочка открывает
   нужный экран модуля; неизвестная ссылка → главная с пометкой «Ссылка не распознана».
4. Пользователь открывает URL приложения в обычном браузере → заглушка «Откройте приложение в Telegram».
5. Кто-то подделывает/переиспользует старый initData → 401, данные не отдаются.

## Критерии приёмки

### Модули и Host
- **AC-1** Given Host с тестовым модулем `demo` (зарегистрирован через `AddModule<DemoModule>()`, мапит
  `GET /ping`, возвращающий `GetTelegramUserId()`) When `GET /api/demo/ping` с валидным initData пользователя 42
  Then 200 и в ответе `42`; сервисы из `DemoModule.AddServices` доступны в DI.
- **AC-2** Given модуль `demo` When `GET /api/demo/ping` без заголовка `Authorization`
  Then 401, тело — ProblemDetails с `code = "auth_missing"`; обработчик модуля не вызван.
  (Модуль не может «забыть» авторизацию: она навешана на группу `/api/{Name}` платформой.)
- **AC-3** Given регистрация модуля с некорректным именем (`""`, `"Demo"`, `"demo_x"`, `"1demo"`, длиннее 32),
  с зарезервированным (`platform`, `bot`, `health`, `assets`) или повторным именем
  When вызывается `AddModule<T>()` Then бросается `InvalidOperationException`, в сообщении есть имя модуля и причина.
- **AC-4** Given Host без модулей When он запускается Then `GET /health` → 200 `{"status":"ok"}` без авторизации.

### Авторизация initData
- **AC-5** Given initData, подписанный токеном бота из конфигурации, с `auth_date` не старше `Auth:InitDataMaxAge`
  (по умолчанию 24 ч) When запрос к любому `/api/*` с `Authorization: tma <initData>` Then запрос проходит,
  `HttpContext.GetInitData()` возвращает пользователя, `auth_date`, `start_param` из initData.
  Граница: ровно `MaxAge` — проходит.
- **AC-6** Given валидный initData, в котором после подписи изменено любое поле (`user`, `auth_date`,
  `start_param`) или `hash` When запрос Then 401 `code = "auth_invalid"`.
- **AC-7** Given initData с `auth_date` старше `MaxAge` (на 1 секунду) When запрос Then 401 `code = "auth_expired"`.
  Given `auth_date` в будущем больше чем на `Auth:AllowedClockSkew` (по умолчанию 5 мин) Then 401 `auth_invalid`.
- **AC-8** Given заголовок с другой схемой (`Bearer …`), пустой initData после `tma `, initData без `hash`,
  без `user`, с `user` без `id`/с невалидным JSON, без `auth_date`, сырой initData (часть значения заголовка
  после префикса `tma `, до URL-декодирования) длиннее 16 384 символов,
  или подписанный токеном другого бота When запрос Then 401 `code = "auth_invalid"`.
  Граница лимита: корректно подписанный initData длиной ровно 16 384 символа (добит дополнительным полем) — проходит (200);
  16 385 символов — 401 `auth_invalid` (проверка длины до проверки подписи).
- **AC-9** Given валидный initData пользователя (id, first_name, last_name, username, language_code)
  When `GET /api/platform/me` Then 200 `MeDto` с этими полями (отсутствующие в initData → `null`).

### Ошибки API
- **AC-10** Given любой неизвестный маршрут под `/api/` (с валидной авторизацией) When запрос
  Then 404 ProblemDetails `code = "not_found"` с `Content-Type: application/problem+json` (не `index.html`).
  Given тот же неизвестный маршрут под `/api/` (включая `/api/platform/unknown` и `/api/nosuchmodule/x`) без заголовка
  `Authorization` Then 401 `code = "auth_missing"`; с невалидным initData Then 401 `code = "auth_invalid"`
  (авторизация проверяется раньше, чем выясняется отсутствие маршрута; существование маршрутов не раскрывается).
- **AC-11** Given эндпоинт модуля бросает необработанное исключение When запрос Then 500 ProblemDetails
  `code = "internal_error"`, в теле нет stack trace и текста исключения; исключение залогировано.

### Конфигурация и секреты
- **AC-12** Given в конфигурации нет `Bot:Token` (или пустой) When Host стартует Then запуск падает
  с сообщением, называющим ключ `Bot:Token`.
- **AC-13** Given запуск Host и серия запросов (валидных и невалидных по initData) When собираются все логи
  Then значение токена бота не встречается ни в одной записи лога и ни в одном теле ответа.

### Раздача Mini App
- **AC-14** Given `WebApp:DistPath` указывает на папку с `index.html` и `assets/app-abc123.js`
  When `GET /`, `GET /movienight/groups/1` (любой путь вне `/api`, `/bot`, `/health`, `/assets`,
  последний сегмент которого не содержит точки)
  Then 200 с содержимым `index.html` и `Cache-Control: no-cache`;
  When `GET /assets/app-abc123.js` Then 200 с `Cache-Control: public, max-age=31536000, immutable`;
  When `GET /assets/missing.js`, `GET /assets/foo` (несуществующий файл под `/assets`) Then 404, не `index.html`;
  When `GET /missing.png`, `GET /movienight/file.txt` (несуществующий путь, последний сегмент с точкой) Then 404,
  не `index.html`.
- **AC-15** Given `WebApp:DistPath` указывает на несуществующую папку When Host стартует Then старт успешен,
  в логе предупреждение; `GET /` → 404; `/api/*` и `/health` работают.

### Реестр пользователей
- **AC-16** Given пустая БД When Host стартует Then миграции `PlatformDbContext` применены автоматически
  (таблица `platform_users`, история миграций в `__ef_migrations_platform`).
- **AC-17** Given пользователь 42 ещё не известен When `GET /api/platform/me` Then в `platform_users` появляется запись
  (id, имена, `language_code`, `FirstSeenAt = LastSeenAt = IClock.UtcNow`, `CanReceiveMessages` = `allows_write_to_pm == true`).
  Given запись есть и пользователь сменил имя When снова `GET /api/platform/me` Then имена и `LastSeenAt` обновлены,
  `FirstSeenAt` не изменился, `CanReceiveMessages` не сбрасывается в `false`, если в initData нет `allows_write_to_pm`.
- **AC-18** Given новый пользователь When два запроса `GET /api/platform/me` выполняются одновременно
  Then оба → 200, в таблице ровно одна запись.
- **AC-19** Given в реестре пользователи 1 и 2 When `IUserDirectory.FindManyAsync([1, 2, 3])` Then словарь с ключами 1 и 2;
  `FindAsync(3)` → `null`; `DisplayName` = `"FirstName LastName"` без лишних пробелов, если `LastName` пустой — `"FirstName"`.

### Оболочка Mini App
- **AC-20** Given `telegram.isAvailable() === false` (нет `window.Telegram.WebApp` или пустой `initData`)
  When приложение стартует Then экран «Откройте приложение в Telegram», API не вызывается.
- **AC-21** Given приложение открыто в Telegram When стартует Then `ready()` и `expand()` вызваны ровно по одному разу;
  выполняется `GET /api/platform/me` с заголовком `Authorization: tma <initData>`; пока запрос идёт — «Загрузка…»;
  после ответа — главная «Привет, <firstName из ответа сервера>!».
- **AC-22** Given список модулей пуст When главная Then текст «Здесь пока ничего нет — разделы появятся скоро.»;
  Given зарегистрированы 2 модуля Then список их `title`, нажатие ведёт на `basePath` модуля;
  Given зарегистрирован ровно 1 модуль и нет `start_param` Then сразу открывается его `basePath` (он и есть «главная»).
  **Главная (`homePath`)**: при 0 или ≥2 модулях — `/`; при ровно 1 модуле — `basePath` этого модуля. В режиме одного
  модуля маршрут `/` при попадании на него заменяется (`replace`) на `basePath`, платформенный экран со списком не показывается.
- **AC-23** Given `GET /api/platform/me` вернул 401 с `auth_expired`/`auth_invalid`/`auth_missing`
  Then экран «Сессия устарела. Закройте приложение и откройте его снова.» без MainButton.
  Given сетевая ошибка или 5xx Then экран «Не удалось загрузить данные.» и MainButton «Повторить»;
  нажатие повторяет запрос, при успехе — главная, MainButton скрыта.
- **AC-24** Given `start_param = "demo_abc"` и зарегистрирован модуль `demo`, чей `resolveStartParam("abc")` → `"/demo/items/abc"`
  When старт Then открыт `/demo/items/abc`, история `[homePath, /demo/items/abc]`, BackButton видна и ведёт на `homePath`.
  Исключение: если цель совпадает с `homePath` (режим одного модуля, `resolveStartParam` вернул `basePath`) — история `[homePath]`.
  Given `start_param` не по формату (`"demo"`, `"Demo_x"`, `"demo_"`, `"demo_a b"`, длиннее 64), для незарегистрированного
  модуля, или `resolveStartParam` вернул `null` Then открыт `homePath` (история `[homePath]`) и оболочка показывает уведомление
  «Ссылка не распознана — открыта главная.». Уведомление рисует платформенный layout над содержимым маршрута (не экран
  главной и не модуль), поэтому оно одинаково видно и на `/`, и на `basePath` в режиме одного модуля;
  уведомление исчезает после первой навигации (смены location) и повторно не появляется.
- **AC-25** `parseStartParam`: `"movienight_join_AB12-cd"` → `{ module: "movienight", payload: "join_AB12-cd" }`;
  невалидные значения из AC-24 → `null`.
- **AC-26** Given текущий `pathname === homePath` Then BackButton скрыта **всегда**, независимо от истории
  (в т.ч. после PUSH на главную, после «На главную», и на `basePath` в режиме одного модуля).
  Given любой другой маршрут и в истории есть предыдущая запись (индекс > 0) Then BackButton видна, нажатие — `navigate(-1)`;
  Given другой маршрут без предыдущей записи Then BackButton скрыта. `useMainButton`/`useBackButton`: при размонтировании компонента кнопка скрывается и обработчик отписан
  (после размонтирования нажатие не вызывает старый обработчик); при смене параметров не накапливаются обработчики.
- **AC-27** Given неизвестный маршрут внутри приложения When открыт Then экран «Страница не найдена», MainButton «На главную».
  When нажата «На главную» Then `navigate(homePath, { replace: true })`: текущая запись истории заменяется на `homePath`
  (длина истории не растёт), BackButton скрыта (по AC-26), MainButton скрыта. В режиме одного модуля `homePath` = `basePath`
  модуля; при 0/≥2 модулях — `/`.
- **AC-28** API-клиент: Given ответ 2xx с JSON → возвращает распарсенный объект; 204 → `undefined`;
  Given ответ с ProblemDetails → бросает `ApiError` с `status`, `code`, `title`, `errors`;
  Given ошибка без JSON-тела → `ApiError` с `code = "unknown"`; Given сетевая ошибка → `ApiError` со `status = 0`,
  `code = "network_error"`. Все запросы идут на `/api<path>` и несут `Authorization: tma <initData>`.
- **AC-29** Тема: Given `telegram.colorScheme()` = `"dark"` When старт Then `document.documentElement.dataset.colorScheme === "dark"`;
  When срабатывает `themeChanged` и схема стала `"light"` Then атрибут обновлён. В `webapp/src/**/*.css` и `*.tsx`
  нет цветовых литералов (`#rgb`/`#rrggbb`/`#rrggbbaa`, `rgb(`, `rgba(`, `hsl(`, `hsla(`, именованные цвета в `color`/`background`).
  Единственное исключение — файл `webapp/src/platform/theme.css`, и в нём литерал допустим **только** как значение
  объявления пользовательского свойства `--tg-theme-*` внутри правила, селектор которого ровно `:root` или
  `:root[data-color-scheme='dark']` (двойные кавычки тоже допустимы). Литерал в любом другом селекторе или объявлении
  (в т.ч. в том же файле) — нарушение. Других правил в этих двух блоках, кроме `--tg-theme-*` и `color-scheme`, нет.

## Контракт API

| Метод | Маршрут | Auth | Request | Response | Ошибки |
|---|---|---|---|---|---|
| GET | `/health` | нет | — | `HealthDto` | — |
| GET | `/api/platform/me` | initData | — | `MeDto` | 401 |
| * | `/api/{module}/…` | initData | (модуль) | (модуль) | 401, 404, 500 + коды модуля |
| GET | `/*` (кроме `/api`, `/bot`, `/health`) | нет | — | `index.html` / статика | 404 если нет dist |

Префикс `platform` зарезервирован под эндпоинты платформы.

### Авторизация
- Заголовок: `Authorization: tma <сырой Telegram.WebApp.initData>`.
- Проверка по алгоритму из `.claude/skills/telegram-mini-app/SKILL.md`. Уточнения, обязательные для backend и tester:
  - `data_check_string` — **все** поля, кроме `hash` (поле `signature`, если пришло, **включается**), значения
    URL-декодированы, сортировка по ключу ordinal, разделитель `\n`. Сверить с https://core.telegram.org/bots/webapps
    («Validating data received via the Mini App»); при расхождении — права документация, отметить в отчёте.
  - сравнение хэшей — `CryptographicOperations.FixedTimeEquals` по байтам (hex регистронезависимо).
  - время — только через `IClock`.
- Реализация — authentication handler ASP.NET Core (схема `TelegramInitData`), группа `/api` — `RequireAuthorization()`.
  Поведение (401 + ProblemDetails) идентично описанному в скилле «middleware».

### Формат ошибок (общий для платформы и модулей)
RFC 7807 ProblemDetails + расширение `code` (snake_case, машинный код) и опционально `errors` (валидация).

| HTTP | code | Когда |
|---|---|---|
| 401 | `auth_missing` | нет заголовка `Authorization` |
| 401 | `auth_invalid` | неверная схема/формат/подпись, нет user, будущий auth_date |
| 401 | `auth_expired` | `auth_date` старше `MaxAge` |
| 400 | `validation_failed` | ошибка валидации (`errors`: поле → сообщения) — для модулей |
| 403 | `forbidden` | нет прав — для модулей |
| 404 | `not_found` | нет маршрута/ресурса |
| 500 | `internal_error` | необработанное исключение |

Модули добавляют свои коды (например `movienight_group_not_found`) через тот же хелпер.

```csharp
// Bot.Platform/Api
public sealed record HealthDto(string Status);                       // "ok"
public sealed record MeDto(long Id, string FirstName, string? LastName, string? Username, string? LanguageCode);

public static class ApiErrors
{
    public static IResult Problem(int status, string code, string title,
        IReadOnlyDictionary<string, string[]>? errors = null);
    public static IResult Validation(IReadOnlyDictionary<string, string[]> errors);   // 400 validation_failed
    public static IResult Forbidden(string code = "forbidden", string title = "Forbidden");
    public static IResult NotFound(string code = "not_found", string title = "Not found");
}
```
```ts
// webapp/src/platform/api.ts
export type HealthDto = { status: 'ok' };
export type MeDto = { id: number; firstName: string; lastName: string | null; username: string | null; languageCode: string | null };
export type ProblemDetails = { type?: string; title: string; status: number; detail?: string; code: string; errors?: Record<string, string[]> };
```
JSON — camelCase (System.Text.Json web defaults). Telegram user id укладывается в 52 бита → `number` в TS безопасен.

## Контракты платформы (C#)

```csharp
// Bot.Platform/Modules
public interface IBotModule
{
    string Name { get; }  // ^[a-z][a-z0-9]{1,31}$, без "_" (разделитель в start_param)
    void AddServices(IServiceCollection services, IConfiguration config);
    void MapApi(IEndpointRouteBuilder api);   // уже /api/{Name}, уже RequireAuthorization()
    // void MapBot(IBotRouteBuilder bot);     // добавляется в 002
}

public static class PlatformHostExtensions
{
    public static WebApplicationBuilder AddPlatform(this WebApplicationBuilder builder);
    public static WebApplicationBuilder AddModule<TModule>(this WebApplicationBuilder builder)
        where TModule : class, IBotModule, new();
    public static WebApplication MapPlatform(this WebApplication app); // /health, /api/platform/*, /api/{module}, статика + fallback
}
```
`Bot.Host/Program.cs` — целиком:
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddPlatform();
// builder.AddModule<MovieNightModule>();   // этап 1+
var app = builder.Build();
app.MapPlatform();
app.Run();

public partial class Program;   // для WebApplicationFactory
```

```csharp
// Bot.Platform/Auth
public sealed record TelegramUser(long Id, string FirstName, string? LastName, string? Username,
    string? LanguageCode, bool IsPremium, bool? AllowsWriteToPm);
public sealed record InitData(TelegramUser User, DateTimeOffset AuthDate, string? StartParam,
    string? QueryId, string? ChatType, string? ChatInstance);

public enum InitDataError { Missing, Invalid, Expired }
public sealed record InitDataValidationResult(InitData? Data, InitDataError? Error);
public interface IInitDataValidator { InitDataValidationResult Validate(string? rawInitData); }

public sealed class AuthOptions          // секция "Auth"
{
    public TimeSpan InitDataMaxAge { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan AllowedClockSkew { get; set; } = TimeSpan.FromMinutes(5);
}

public static class TelegramHttpContextExtensions
{
    public static InitData GetInitData(this HttpContext context);       // InvalidOperationException, если не авторизован
    public static long GetTelegramUserId(this HttpContext context);
}

// Bot.Platform/Configuration
public sealed class BotOptions { public required string Token { get; set; } }   // секция "Bot"; 002 добавит поля

// Bot.Platform/Time
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);   // нужен 002 (повтор при 429)
}
```

```csharp
// Bot.Platform/Persistence
public static class ModuleDbContextExtensions
{
    // SQLite из ConnectionStrings:Default (по умолчанию "Data Source=data/bot.db"; относительный путь
    // разрешается относительно ContentRoot, не текущей директории процесса; папка создаётся),
    // история миграций в "__ef_migrations_{moduleName}", миграции применяются при старте Host.
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services,
        IConfiguration config, string moduleName) where TContext : DbContext;
}
```
Префикс таблиц модуля — `{moduleName}_` (задаётся в `OnModelCreating` модуля).

```csharp
// Bot.Platform/Users
public sealed record PlatformUser(long Id, string FirstName, string? LastName, string? Username,
    string? LanguageCode, bool CanReceiveMessages, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt)
{
    public string DisplayName { get; }   // "First Last" / "First"
}
public interface IUserDirectory          // для модулей: имена участников, можно ли написать
{
    Task<PlatformUser?> FindAsync(long userId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<long, PlatformUser>> FindManyAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken);
}
public interface IUserRegistry           // для платформы (002 использует из бота)
{
    Task UpsertAsync(TelegramUser user, bool? canReceiveMessages, CancellationToken cancellationToken);
    Task MarkCannotReceiveMessagesAsync(long userId, CancellationToken cancellationToken);
}
```
Правило `CanReceiveMessages` при upsert: `canReceiveMessages == true` → `true`; `false` → `false`; `null` → новое значение
`false` для новой записи, без изменений для существующей. Для `/api/platform/me` передаётся `user.AllowsWriteToPm == true ? true : null`.

### Конфигурация
| Ключ | Обязателен | По умолчанию | Назначение |
|---|---|---|---|
| `Bot:Token` | да | — | токен бота (подпись initData, Telegram API). Только user-secrets / env |
| `Auth:InitDataMaxAge` | нет | `1.00:00:00` | срок жизни initData |
| `Auth:AllowedClockSkew` | нет | `00:05:00` | допуск `auth_date` в будущее |
| `WebApp:DistPath` | нет | `../../webapp/dist` относительно ContentRoot | откуда раздавать Mini App |
| `ConnectionStrings:Default` | нет | `Data Source=data/bot.db` (относительный путь — от ContentRoot) | SQLite (платформа и модули, один файл) |

Порт Host для разработки — `http://localhost:5080` (`launchSettings.json`).

## Контракт бота
В этой спецификации нет. Всё про бота — в `002-platform-bot.md`.

## Экраны Mini App

| Экран | Маршрут | Содержимое | MainButton | BackButton |
|---|---|---|---|---|
| Вне Telegram | — (до роутера) | «Откройте приложение в Telegram» / «Это мини-приложение работает внутри Telegram. Откройте его через бота.» | — | — |
| Загрузка | — (до роутера) | «Загрузка…» | скрыта | скрыта |
| Сессия устарела | — | «Сессия устарела. Закройте приложение и откройте его снова.» | скрыта | скрыта |
| Ошибка загрузки | — | «Не удалось загрузить данные.» | «Повторить» → повтор `GET /api/platform/me` | скрыта |
| Главная | `homePath` (`/` при 0/≥2 модулях; `basePath` модуля при 1 модуле) | При `/`: «Привет, {firstName}!»; список модулей или «Здесь пока ничего нет — разделы появятся скоро.» При 1 модуле — экран модуля на `basePath`. | скрыта (при 1 модуле — на усмотрение модуля) | скрыта всегда |
| Уведомление о ссылке | платформенный layout, над любым маршрутом | «Ссылка не распознана — открыта главная.»; показывается на `homePath` после старта с нераспознанным `start_param`, исчезает при первой смене location | — | — |
| Не найдено | `*` | «Страница не найдена» | «На главную» → `navigate(homePath, { replace: true })` | видна, если индекс истории > 0 |
| Экраны модулей | `/{module}/…` | модуль | модуль | видна, если `pathname !== homePath` и индекс истории > 0 |

Язык интерфейса — русский.

### Контракты оболочки (TS)
```ts
// webapp/src/platform/telegram.ts — единственное место, где трогается window.Telegram
export type ColorScheme = 'light' | 'dark';
export type MainButtonParams = { text: string; onClick: () => void; enabled?: boolean; loading?: boolean };
export interface TelegramPlatform {
  isAvailable(): boolean;                    // WebApp существует и initData непустая
  initData(): string;                        // сырая строка для Authorization
  startParam(): string | undefined;          // initDataUnsafe.start_param — только для роутинга
  colorScheme(): ColorScheme;
  ready(): void;
  expand(): void;
  onThemeChanged(cb: () => void): () => void;            // возвращает отписку
  mainButton: { show(p: MainButtonParams): () => void; hide(): void };   // show возвращает cleanup
  backButton: { show(onClick: () => void): () => void; hide(): void };
  haptic: {
    impact(style: 'light' | 'medium' | 'heavy'): void;
    notification(type: 'success' | 'warning' | 'error'): void;
    selection(): void;
  };
}
export const telegram: TelegramPlatform;     // методы — no-op, если WebApp недоступен

// webapp/src/platform/buttons.ts
export function useMainButton(params: MainButtonParams | null): void;    // null → скрыта
export function useBackButton(onBack: (() => void) | null): void;        // null → скрыта

// webapp/src/platform/api.ts
export class ApiError extends Error {
  readonly status: number; readonly code: string; readonly title: string;
  readonly errors?: Record<string, string[]>;
}
export const api: {
  get<T>(path: string): Promise<T>;
  post<T>(path: string, body?: unknown): Promise<T>;
  put<T>(path: string, body?: unknown): Promise<T>;
  del<T>(path: string): Promise<T>;
};   // path без "/api": api.get<MeDto>('/platform/me')

// webapp/src/platform/router.tsx
export type WebModule = {
  name: string;                 // = IBotModule.Name
  title: string;                // подпись на главной
  basePath: string;             // `/${name}`
  routes: RouteObject[];        // react-router, относительно basePath
  resolveStartParam?: (payload: string) => string | null;   // абсолютный путь в приложении или null
};
export function parseStartParam(raw: string | undefined): { module: string; payload: string } | null;
// формат: ^([a-z][a-z0-9]{1,31})_([A-Za-z0-9_-]+)$, общая длина ≤ 64

// webapp/src/modules/index.ts
export const modules: WebModule[] = [];      // этап 1 добавит movieNightModule

// webapp/src/App.tsx
export function App(props: { modules: WebModule[] }): JSX.Element;   // модули инжектируются — для тестов
```
Роутер — `react-router` с `createMemoryRouter` (HashRouter конфликтует с `#tgWebAppData=…` в URL Telegram).
Deep link открывается с историей `[homePath, target]`, чтобы BackButton вела на главную.
`homePath` вычисляет оболочка: `modules.length === 1 ? modules[0].basePath : '/'`. Автоматическая BackButton
(FE-5) скрыта при `pathname === homePath` и при индексе истории 0, иначе — `navigate(-1)`.

## Задачи

### Backend
- **BE-1** Решение `MultiBot.slnx`, `global.json` (SDK 10.0.x, `rollForward: latestFeature`), `Directory.Build.props`
  (net10.0, nullable, `TreatWarningsAsErrors`, `LangVersion` по умолчанию), `Directory.Packages.props` (CPM),
  проекты `src/Bot.Host`, `src/Bot.Platform`, `tests/Bot.Platform.Tests` (xUnit, NSubstitute,
  Microsoft.AspNetCore.Mvc.Testing, ссылается на Bot.Host и Bot.Platform). `.gitignore`, `.editorconfig`.
  Telegram.Bot в этой задаче не обязателен (нужен в 002). (AC-4)
- **BE-2** `IBotModule`, `AddPlatform`, `AddModule<T>` с валидацией имени/резерва/дубликатов, `MapPlatform`,
  группа `/api/{Name}` с авторизацией, `Program.cs` как в контракте, `GET /health`. (AC-1..AC-4)
- **BE-3** `IClock` + системная реализация; `BotOptions`/`AuthOptions` с валидацией при старте (`ValidateOnStart`),
  ошибка называет ключ, не значение. (AC-12)
- **BE-4** `IInitDataValidator` + authentication handler `TelegramInitData`, коды 401, `GetInitData`/`GetTelegramUserId`.
  Ни токен, ни сырой initData не логируются (в лог — только причина отказа). (AC-5..AC-8, AC-13)
- **BE-5** `ApiErrors`, ProblemDetails с `code` для 401/404/500 (включая неизвестные `/api/*`), обработчик исключений
  без утечки деталей. (AC-10, AC-11)
- **BE-6** `GET /api/platform/me`. (AC-9)
- **BE-7** Раздача `WebApp:DistPath`: статика, SPA-fallback на `index.html` кроме `/api`, `/bot`, `/health`,
  `/assets` и путей «похожих на файл» (шаблон `{*path:nonfile}`), заголовки кэша, отсутствие папки — предупреждение. (AC-14, AC-15)
- **BE-8** `AddModuleDbContext<T>`, автоприменение миграций при старте, `PlatformDbContext` (`platform_users`),
  миграция, `IUserDirectory`/`IUserRegistry`, upsert в `/api/platform/me` с защитой от гонки
  (конфликт ключа при вставке → обновление). (AC-16..AC-19)
- **BE-9** README: раздел «Локальный запуск» — user-secrets для `Bot:Token`, туннель, `/newapp` в BotFather,
  `npm run build` → Host раздаёт dist, либо `npm run dev` с прокси.

### Frontend
- **FE-1** Каркас `webapp/`: Vite + React + TypeScript strict, `react-router`, Vitest + Testing Library + jsdom,
  ESLint (typescript-eslint, react-hooks). Скрипты: `dev`, `build` (`tsc -b && vite build`, вывод `webapp/dist`),
  `test` (`vitest`), `lint`. Dev-прокси `/api` → `http://localhost:5080`. Setup-файл тестов `webapp/src/test/setup.ts`
  (jest-dom). Версии — актуальные стабильные, сверить через Context7. Других зависимостей нет.
- **FE-2** `index.html`: `<script src="https://telegram.org/js/telegram-web-app.js"></script>` до бандла;
  `platform/telegram.ts` по контракту (свои минимальные типы `window.Telegram.WebApp`, без сторонних пакетов типов). (AC-20)
- **FE-3** `platform/theme.css` (fallback-переменные в `:root`, только `var(--tg-theme-*)` в остальных стилях),
  `data-color-scheme` на `<html>` и подписка на `themeChanged`. (AC-29)
- **FE-4** `platform/api.ts` по контракту. (AC-28)
- **FE-5** `platform/buttons.ts` (`useMainButton`, `useBackButton`) и автоматическая BackButton по истории роутера. (AC-26)
- **FE-6** `platform/router.tsx`: `parseStartParam`, `WebModule`, сборка memory-роутера из модулей, обработка
  `start_param`, режим одного модуля. (AC-22, AC-24, AC-25, AC-27)
- **FE-7** `App` и экраны оболочки: вне Telegram, загрузка, сессия устарела, ошибка с «Повторить», главная, не найдено;
  `ready()`/`expand()` один раз; `modules/index.ts` с пустым списком. (AC-20..AC-23, AC-27)

### Tester
- **TEST-1** Хелперы: `TestInitData.For(userId, authDate?, startParam?, extra fields?)` — подпись тестовым токеном
  (`123456:TEST-TOKEN`) по алгоритму из раздела «Авторизация»; `FakeClock`; `PlatformFactory : WebApplicationFactory<Program>`
  с тестовой конфигурацией, `DemoModule` (в тестовом проекте), подменой `IClock`, захватом логов, SQLite in-memory
  с удерживаемым соединением. (все backend-AC)
- **TEST-2** Модули: AC-1..AC-4 (негативы: все невалидные имена из AC-3, дубликат, резерв).
- **TEST-3** Авторизация: AC-5..AC-8 (граница MaxAge ровно/+1 с, skew, каждый невалидный вариант отдельно, чужой токен,
  подмена каждого поля). Юнит на `IInitDataValidator` + интеграционные через HTTP.
- **TEST-4** `/api/platform/me`, ошибки, конфиг, секреты: AC-9..AC-13 (поиск токена во всех захваченных логах и телах).
- **TEST-5** Статика: AC-14, AC-15 (временная папка dist).
- **TEST-6** Реестр: AC-16..AC-19 (параллельные первые запросы, сохранение `FirstSeenAt`, правило `CanReceiveMessages`).
- **TEST-7** Frontend (`webapp/src/platform/__tests__/`): мок `platform/telegram.ts` и `fetch`;
  AC-20..AC-29 (загрузка/пусто/ошибка, «Повторить», start_param валидный/невалидный/неизвестный модуль,
  отписка кнопок после размонтирования, ApiError всех видов, тема; проверка цветовых литералов — тест, читающий файлы).

## Вне скоупа
- Бот: webhook, команды, колбэки, отправка сообщений — `002-platform-bot.md`.
- Генерация ссылок `t.me/<bot>/<app>?startapp=…` и конфиг `Bot:Username`/`MiniApp:ShortName` — этап 1 (приглашения).
- Запуск Mini App вне Telegram с поддельным initData для разработки.
- `requestWriteAccess`, CloudStorage, полноэкранный режим, safe-area.
- Модуль MovieNight и проект `tests/Modules.MovieNight.Tests` — этап 1.
- CI, Docker, деплой.

## Открытые вопросы
Нет.

## Принятые решения (ответы пользователя)
- **Реестр пользователей в Этапе 0 — да (вариант A).** Платформа хранит `platform_users` и даёт модулям
  `IUserDirectory`; в Этап 0 входят `AddModuleDbContext` и автоприменение миграций при старте
  (AC-16..AC-19, BE-8, TEST-6).
- **Разбивка принята:** сначала 001, затем 002 отдельным циклом `/feature`.

### Решения архитектора по замечаниям critic (круг 1)
- **Тёмный fallback разрешён.** В `theme.css` допустимы два fallback-блока: `:root` и `:root[data-color-scheme='dark']`,
  литералы — только в значениях `--tg-theme-*`. Причина: вне Telegram/до инъекции темы тёмная схема без fallback
  даёт чёрный текст на тёмном фоне; Telegram выставляет `--tg-theme-*` inline-стилем на `<html>`, что перекрывает
  оба блока. Проверяемое правило — в AC-29.
- **«Главная» = `homePath`.** В режиме одного модуля главная — `basePath` модуля, платформенный `/` редиректит
  (`replace`) на него; нераспознанная ссылка открывает `homePath`. Уведомление «Ссылка не распознана» рисует
  платформенный layout (не экран главной), поэтому оно работает в обоих режимах. «На главную» в NotFound ведёт
  на тот же `homePath`. (AC-22, AC-24, AC-27)
- **BackButton на главной скрыта всегда**, критерий — `pathname === homePath`, а не позиция в истории.
  «На главную» — `navigate(homePath, { replace: true })`: история не растёт, возврата на NotFound нет. (AC-26, AC-27)
- **Лимит 16 384** — на длину сырого initData после префикса `tma ` (до URL-декодирования), включительно 16 384 —
  допустимо, 16 385 — `auth_invalid`; проверяется до подписи. (AC-8)
- **Неизвестный `/api/*` без авторизации → 401 `auth_missing`** (с невалидной — `auth_invalid`), 404 `not_found` —
  только для авторизованных. Авторизация на всей группе `/api` раньше роутинга 404; список маршрутов не раскрывается. (AC-10)

### Решения архитектора по замечаниям critic (круг 2)
- **SPA-fallback не отдаёт `index.html` для `/assets/*` и путей «похожих на файл» — принято.** Кроме `/api`, `/bot`,
  `/health` из fallback исключены префикс `/assets` и пути, последний сегмент которых содержит точку (стандартный
  шаблон `{*path:nonfile}` у `MapFallback`). Причина: отсутствующий бандл/картинка должны давать 404, а не HTML
  с `200` (иначе браузер получает `index.html` вместо JS — ошибка MIME/парсинга, маскирующая реальную проблему
  деплоя, и такой ответ может закэшироваться). Роутинг Mini App — memory-роутер, URL экранов на сервер не приходят,
  поэтому точка в пути экрана ограничением не является. (AC-14, BE-7)
- **Относительный путь БД — от ContentRoot.** Относительный `Data Source` в `ConnectionStrings:Default`
  (в т.ч. значение по умолчанию `data/bot.db`) разрешается относительно `IHostEnvironment.ContentRootPath`, а не
  текущей директории процесса; абсолютный путь и `:memory:`/`Mode=Memory` используются как есть. Причина:
  предсказуемое место файла при запуске из IDE, `dotnet run --project` и сервиса. (контракт `AddModuleDbContext`, BE-8)

## Допущения
- Этап 0 разбит на 001 (ядро + Mini App) и 002 (бот); 002 начинается после закрытия 001.
- Авторизация — authentication handler, а не произвольный middleware: группа `/api` защищена `RequireAuthorization()`,
  модуль не может случайно открыть эндпоинт без авторизации (только явным `AllowAnonymous`).
- Имя модуля без `_` — `_` отделяет имя модуля от payload в `start_param`.
- Тестовый модуль `DemoModule` живёт только в тестах; в production-коде модулей нет.
- Интеграционные тесты Host — в `tests/Bot.Platform.Tests` (отдельного `Bot.Host.Tests` нет, как в структуре CLAUDE.md).
- `MeDto.firstName` для приветствия берётся из ответа сервера, а не из `initDataUnsafe`.
- `start_param` для роутинга берётся из `initDataUnsafe` — это навигация, а не решение; модуль обязан проверять доступ на сервере.
- Решение — `.slnx` (формат по умолчанию в .NET 10).
- Цветовые литералы разрешены только как fallback-значения `--tg-theme-*` в блоках `:root` и
  `:root[data-color-scheme='dark']` файла `platform/theme.css` (см. AC-29).
- Решения о повторной отправке/кэше заголовков: `index.html` — `no-cache`, хэшированные ассеты — `immutable`.

## Остаточные замечания (MINOR, не блокируют)
Зафиксированы critic при APPROVED (круг 3); можно закрыть попутно в следующих задачах.
- `webapp/package.json`: лишняя devDependency `@testing-library/user-event` (не используется).
- `HistoryTracker` / `router.dispose()` не вызываются при размонтировании оболочки Mini App.
- Тесты: тестовая БД — временный SQLite-файл вместо in-memory (отклонение от TEST-1); `DemoHost` дублирует
  композицию Host вместо переиспользования `Program`.
- README, п.5: формулировка про путь БД неточна — относительный путь разрешается от ContentRoot
  (см. «Принятые решения»).
- `EnsureDirectory` (создание каталога для файла БД) вызывается при каждом создании DbContext, а не один раз при старте.
