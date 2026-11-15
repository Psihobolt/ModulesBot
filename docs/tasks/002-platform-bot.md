# 002 — Платформа: webhook бота, роутинг команд и колбэков, IMessageSender

**Статус:** DONE
**Модуль:** platform   **Бриф:** docs/product/movie-night.md (Этап 0, часть 2 из 2)
**Зависит от:** docs/tasks/001-platform-core.md (DONE). Ревизия 2: спека сверена с кодом 001
(см. «Изменения ревизии 2» в конце).

## Исходное описание
«Этап 0 из docs/product/movie-night.md: каркас платформы без модулей» — часть про бота:
webhook, общий роутинг команд/колбэков, `IMessageSender`.

## Цель
Бот принимает обновления через webhook, отвечает на `/start` и `/help`, маршрутизирует команды, колбэки и
`/start <module>_<payload>` в модули; модули отправляют сообщения через `IMessageSender`, ошибки Telegram
(403, 429) обрабатываются централизованно.

## Сценарии
1. Пользователь пишет боту `/start` → приветствие с кнопкой «Открыть приложение» (открывает Mini App).
2. `/help` → список команд платформы и модулей.
3. Модуль регистрирует команду `/x`, колбэк `x:act` и обработчик `/start x_…` — платформа доставляет обновления
   в его обработчики.
4. Модуль шлёт уведомление пользователю, который заблокировал бота → `Blocked`, без исключения,
   в реестре `CanReceiveMessages = false`.
5. На старте Host регистрирует webhook и список команд в Telegram.

## Критерии приёмки

### Webhook
- **AC-1** Given запрос `POST /bot/webhook` без заголовка `X-Telegram-Bot-Api-Secret-Token`, с пустым или неверным
  значением (в т.ч. другой длины) When приходит Then 401 (тело пустое), диспетчер не вызван. Секрет проверяется
  **до** чтения тела: неверный секрет + некорректный JSON → 401, не 400.
- **AC-2** Given верный секрет и тело — некорректный JSON, пустое тело или JSON `null` When запрос Then 400, диспетчер не вызван.
- **AC-3** Given верный секрет и валидное обновление When запрос Then 200 (пустое тело) после завершения обработки
  (ответ уходит после `DispatchAsync`). Given обработчик модуля (или upsert пользователя) бросил исключение Then всё
  равно 200, исключение залогировано уровнем Error, следующее обновление обрабатывается нормально.
  Given `GET /bot/webhook` или `POST /bot/other` Then 404/405, не `index.html` (исключение `/bot` из SPA-fallback уже есть в 001).
- **AC-4** Given обновление не из личного чата (`chat.type` = group/supergroup/channel), `edited_message`,
  `channel_post`, любой тип кроме `message`/`callback_query`, `message` без `from`, `callback_query` без `message`
  (inline-режим) или с `message.chat.type` не private When обработка Then ничего не отправляется, обработчики не
  вызываются, upsert не выполняется, `answerCallbackQuery` не вызывается, 200.

### Команды платформы
- **AC-5** Given личный чат When `/start` (без payload, в т.ч. `/START`, `/start@AnyBot`) Then ответ — текст
  приветствия (см. «Контракт бота») с inline-кнопкой `web_app` «Открыть приложение» на `Bot:PublicUrl` (как задан).
  Given `Bot:PublicUrl` не задан Then тот же текст без кнопки (`ReplyMarkup = null`).
- **AC-6** Given модуль `demo` с командами `ping` — «Проверка связи», `list` — «Список» (в таком порядке) и второй
  модуль `demob` с командой `zeta` — «Последняя», зарегистрированный после `demo`, When `/help` (аргументы игнорируются)
  Then текст ровно:
  `Команды:\n/start — открыть приложение\n/help — список команд\n/ping — Проверка связи\n/list — Список\n/zeta — Последняя`
  (порядок: платформа, затем модули в порядке `AddModule`, команды — в порядке `MapCommand`).
  Given модулей с командами нет Then только первые три строки.
- **AC-7** Given личный чат When текст начинается с `/`, но команда не зарегистрирована (`/nope`) или не по формату
  (`/`, `/ x`, `/Привет`) Then «Не знаю такую команду. Список команд — /help».
  When текст без `/` в начале (`привет`) или сообщение без `text` (стикер, фото — в т.ч. фото с подписью `/ping`)
  Then «Я понимаю только команды. Список команд — /help».

### Роутинг в модули
- **AC-8** Given модуль `demo` зарегистрировал `MapCommand("ping", …)` When `/ping hello world`, `/ping@AnyBot hello world`
  или `/PING   hello world  ` Then вызван обработчик `demo` ровно один раз с `Command = "ping"`,
  `Arguments = "hello world"` (trim по краям, внутренние пробелы и переводы строк сохраняются), `ChatId = chat.id`,
  `From.Id = from.id`, `From.AllowsWriteToPm = true`. `/ping` без аргументов → `Arguments = ""`.
  Суффикс `@<имя>` отбрасывается без проверки имени бота.
- **AC-9** Given модуль `demo` зарегистрировал `MapCallback("act", …)` When callback с data `demo:act:42`
  Then вызван обработчик с `Action = "act"`, `Payload = "42"` (payload — всё после второго `:`, может содержать `:`:
  `demo:act:a:b` → `"a:b"`; `demo:act` → `""`; `demo:act:` → `""`).
  Callback-запрос отвечен (`IBotApi.AnswerCallbackQueryAsync`) **ровно один раз**: если обработчик вызвал
  `AnswerAsync(text, showAlert)` — с этими параметрами (повторные `AnswerAsync` — no-op); иначе платформа отвечает
  без текста после обработчика, в том числе если обработчик бросил исключение.
  Ошибка Telegram в `answerCallbackQuery` (например, 400 «query is too old») логируется Warning и не пробрасывается
  (ни в обработчик из `AnswerAsync`, ни в webhook).
- **AC-10** Given callback с data неизвестного модуля, неизвестного action, пустой, `null`, без `:`, с пустым
  module/action (`:act`, `demo:`) When обработка Then обработчики не вызваны, ровно один `answerCallbackQuery` с текстом
  «Эта кнопка больше не работает» (`showAlert = false`).
- **AC-11** Given модуль `demo` зарегистрировал `MapStart(…)` When `/start demo_abc-1` Then вызван его обработчик
  с `Payload = "abc-1"`, приветствие платформы не отправляется. Формат payload `/start` — тот же, что `startapp`
  в 001: `^([a-z][a-z0-9]{1,31})_([A-Za-z0-9_-]+)$`, общая длина ≤ 64 (`demo_a_b` → модуль `demo`, payload `a_b`).
  Given `/start other_x` (модуль не зарегистрирован или без `MapStart`), `/start demo`, `/start demo_`,
  `/start demo_a b`, `/start Demo_x`, payload длиннее 64 Then обычное приветствие (AC-5), обработчики не вызваны.
- **AC-12** Регистрация маршрутов бота происходит внутри `AddModule<T>()` (сразу после `AddServices`); нарушение —
  `InvalidOperationException` из `AddModule<T>()`, в сообщении имя модуля и команда/action, и причина:
  команда не по формату `^[a-z0-9_]{1,32}$`; команда `start`/`help`; команда уже занята другим модулем
  (сообщение называет оба модуля); повтор команды или action в одном модуле; action не по формату `^[a-z0-9_]{1,32}$`;
  второй `MapStart` в модуле; описание команды (после trim) короче 3 или длиннее 256 символов;
  `null`-обработчик; общее число команд (с `start` и `help`) больше 100.
  Модуль без переопределения `MapBot` регистрируется без ошибок (реализация по умолчанию — пустая).
- **AC-13** `CallbackData.Create("demo", "act", "42")` → `"demo:act:42"`; без payload или с `""` → `"demo:act"`;
  результат длиннее 64 байт UTF-8 (граница: ровно 64 — ок, 65 — ошибка; проверять с кириллицей в payload),
  module не по формату имени модуля `^[a-z][a-z0-9]{1,31}$`, action не по `^[a-z0-9_]{1,32}$` → `ArgumentException`.
  `CallbackData.TryParse` — обратная операция с правилами AC-9/AC-10 (`false` для невалидных); `Create` → `TryParse`
  возвращает исходные части.

### IMessageSender
- **AC-14** Given `IBotApi.SendMessageAsync` вернул `messageId` When `SendAsync(chatId, message, ct)` Then
  `SendResult(Sent, messageId)`; в `IBotApi` переданы `text`, `replyMarkup`, `html` из `OutgoingMessage` без изменений.
- **AC-15** Given `IBotApi` бросил `ApiRequestException` с `ErrorCode = 403` When `SendAsync` Then `SendResult(Blocked, null)`,
  без исключения, лог Information (с `chatId`, без текста сообщения); вызван
  `IUserRegistry.MarkCannotReceiveMessagesAsync(chatId)` (в личном чате `chatId` = id пользователя). Ошибка самого
  `MarkCannotReceiveMessagesAsync` логируется Warning, результат всё равно `Blocked`.
- **AC-16** Given 429 с `Parameters.RetryAfter` ≤ `Bot:MaxRetryAfter` (граница: равно — ждём) When `SendAsync`
  Then `IClock.DelayAsync(retry_after, ct)` и ровно один повтор; повтор успешен → `Sent`; повтор снова 429 → `RateLimited`
  (без второго ожидания); повтор 403 → `Blocked` (по AC-15). Given `retry_after` > `MaxRetryAfter` или отсутствует
  Then сразу `RateLimited`, `DelayAsync` не вызван. Встроенный авто-повтор клиента Telegram.Bot выключен (политика только наша).
- **AC-17** Given любая другая ошибка (`ApiRequestException` с иным кодом, `RequestException`, `HttpRequestException`,
  таймаут клиента) When `SendAsync` Then `Failed`, лог Warning, без исключения.
  Given отменён переданный `CancellationToken` (до вызова, во время `IBotApi` или во время `DelayAsync`)
  Then `OperationCanceledException` пробрасывается.
- **AC-18** Given `Text` пустой, из одних пробелов или длиннее 4096 символов (`string.Length`; 4096 — ок) When `SendAsync`
  Then `ArgumentException`, `IBotApi` не вызван.

### Старт Host
- **AC-19** Given `Bot:PublicUrl = https://example.org` (или `https://example.org/`) и `Bot:SetupOnStartup = true`
  (по умолчанию) When Host стартует (`app.StartAsync()` завершился) Then вызваны `IBotApi.SetWebhookAsync` с URL
  `https://example.org/bot/webhook`, `secretToken = Bot:WebhookSecret`, `allowedUpdates = [Message, CallbackQuery]`,
  и `SetMyCommandsAsync` со списком: `start` — «открыть приложение», `help` — «список команд», затем команды модулей
  с их описаниями в порядке AC-6.
  Given ошибка (исключение) в любом из вызовов Then она залогирована Error (второй вызов всё равно выполняется),
  Host продолжает работу. Given `Bot:PublicUrl` не задан или `SetupOnStartup = false` Then эти вызовы не делаются.
- **AC-20** Given при старте: `Bot:WebhookSecret` отсутствует или не соответствует `^[A-Za-z0-9_-]{1,256}$`;
  `Bot:PublicUrl` задан, но не абсолютный `https://` URL или содержит query/fragment; `Bot:MaxRetryAfter` < 0
  или > 00:01:00 When Host стартует Then запуск падает (`OptionsValidationException`, как `Bot:Token` в 001)
  с сообщением, называющим ключ, а не значение.
- **AC-21** Given запуск, обработка обновлений (включая ошибки Telegram и исключения обработчиков) и реальный
  HTTP-клиент Telegram.Bot поверх заглушки `HttpMessageHandler` (AC-23) When собраны все логи уровня Trace и выше
  (включая категории `System.Net.Http.*`) Then в них нет значений `Bot:Token` и `Bot:WebhookSecret`.

### Реестр пользователей
- **AC-22** Given личное сообщение (любое, включая неизвестные команды и стикеры) или callback в личном чате
  When обработка Then `IUserRegistry.UpsertAsync(from, true, ct)` вызван до обработчика модуля и до ответа платформы;
  `from` — `TelegramUser` из 001 (`Id`, `FirstName`, `LastName`, `Username`, `LanguageCode`, `IsPremium`,
  `AllowsWriteToPm = true`). В реестре: имена обновлены, `CanReceiveMessages = true` (в т.ч. после ранее
  выставленного `false` по AC-15), `FirstSeenAt` не меняется. Для отфильтрованных по AC-4 обновлений upsert не вызывается.

### Адаптер Telegram API
- **AC-23** Given `IBotApi` (реальная реализация поверх Telegram.Bot) и HTTP-клиент `"telegram"`, у которого
  primary handler подменён заглушкой When заглушка отвечает JSON Bot API
  `{"ok":true,"result":{"message_id":7,...}}` Then `SendMessageAsync` → `7`, запрос ушёл на `sendMessage`
  с `chat_id`, `text`, `parse_mode = HTML` только при `html = true`;
  When `{"ok":false,"error_code":403,...}` Then `ApiRequestException` с `ErrorCode = 403`;
  When `{"ok":false,"error_code":429,"parameters":{"retry_after":3}}` Then `ApiRequestException` с
  `ErrorCode = 429` и `Parameters.RetryAfter = 3`, и заглушка вызвана ровно один раз (встроенный повтор выключен).

## Контракт API
| Метод | Маршрут | Auth | Request | Response | Ошибки |
|---|---|---|---|---|---|
| POST | `/bot/webhook` | заголовок `X-Telegram-Bot-Api-Secret-Token` (без initData, вне группы `/api`) | `Telegram.Bot.Types.Update` (JSON Bot API) | 200, пустое тело | 400 (пустое тело), 401 (пустое тело) |

- Эндпоинт мапится в `MapPlatform()` до `MapFallback`, анонимный (`AllowAnonymous()`), не под `/api`, поэтому
  ProblemDetails и initData-авторизация к нему не применяются.
- Сравнение секрета — `CryptographicOperations.FixedTimeEquals` по UTF-8 байтам; разная длина → 401.
- Десериализация `Update` — вручную из `Request.Body` с `JsonBotAPI.Options` (Telegram.Bot v22; сверить через Context7),
  не через биндинг minimal API с web-defaults. `JsonException` / `null` → 400.
- Токен отмены обработки — `HttpContext.RequestAborted`.

## Контракты платформы (C#)

Namespace новых типов — `Bot.Platform.Messaging` (папка `src/Bot.Platform/Messaging`). Не `Bot.Platform.Telegram`:
внутри `namespace Bot.Platform.*` идентификатор `Telegram` затенял бы корневой namespace `Telegram.Bot`.

```csharp
// Bot.Platform/Modules/IBotModule.cs — расширение контракта из 001
public interface IBotModule
{
    string Name { get; }
    void AddServices(IServiceCollection services, IConfiguration config);
    void MapApi(IEndpointRouteBuilder api);
    void MapBot(IBotRouteBuilder bot) { }   // новое; default-реализация: модуль без бота ничего не переопределяет
}

// Bot.Platform/Modules/PlatformHostExtensions.cs — сигнатуры не меняются:
//   AddPlatform()  — дополнительно регистрирует BotOptions-валидацию, IBotApi, IMessageSender, IUpdateDispatcher,
//                    BotRouteTable, hosted service настройки бота;
//   AddModule<T>() — Add(module) → AddServices → module.MapBot(builder-для-модуля) (ошибки AC-12 отсюда);
//   MapPlatform()  — дополнительно POST /bot/webhook (до MapFallback).
// Program.cs не меняется.

// Bot.Platform/Messaging
public delegate Task BotHandler<in TContext>(TContext context) where TContext : BotContext;

public interface IBotRouteBuilder
{
    string ModuleName { get; }
    IBotRouteBuilder MapCommand(string command, string description, BotHandler<BotCommandContext> handler);
    IBotRouteBuilder MapCallback(string action, BotHandler<BotCallbackContext> handler);   // data "{module}:{action}[:{payload}]"
    IBotRouteBuilder MapStart(BotHandler<BotStartContext> handler);                        // "/start {module}_{payload}"
}

public abstract class BotContext
{
    public required Update Update { get; init; }
    public required long ChatId { get; init; }
    public required TelegramUser From { get; init; }          // Bot.Platform.Auth.TelegramUser (001); AllowsWriteToPm = true
    public required IServiceProvider Services { get; init; }   // scope на одно обновление
    public required IMessageSender Sender { get; init; }
    public required CancellationToken CancellationToken { get; init; }
    public Task<SendResult> ReplyAsync(OutgoingMessage message);   // = Sender.SendAsync(ChatId, message, CancellationToken)
}
public sealed class BotCommandContext : BotContext
{
    public required Message Message { get; init; }
    public required string Command { get; init; }     // без "/", без "@bot", lower-case
    public required string Arguments { get; init; }   // trim, может быть ""
}
public sealed class BotCallbackContext : BotContext
{
    public required CallbackQuery Query { get; init; }
    public required string Action { get; init; }
    public required string Payload { get; init; }
    public Task AnswerAsync(string? text = null, bool showAlert = false);   // повторный вызов — no-op; ошибки Telegram не бросает
}
public sealed class BotStartContext : BotContext
{
    public required Message Message { get; init; }
    public required string Payload { get; init; }
}

public static class CallbackData
{
    public const int MaxBytes = 64;
    public static string Create(string module, string action, string? payload = null);
    public static bool TryParse(string? data, out string module, out string action, out string payload);
}

// Отправка (public — для модулей)
public sealed record OutgoingMessage(string Text, ReplyMarkup? ReplyMarkup = null, bool Html = false);
public enum SendStatus { Sent, Blocked, RateLimited, Failed }
public sealed record SendResult(SendStatus Status, int? MessageId);
public interface IMessageSender      // singleton; для MarkCannotReceiveMessagesAsync сам создаёт scope
{
    Task<SendResult> SendAsync(long chatId, OutgoingMessage message, CancellationToken cancellationToken);
}

// --- internal (InternalsVisibleTo Bot.Platform.Tests уже есть; для NSubstitute BE добавляет
//     InternalsVisibleTo "DynamicProxyGenAssembly2") ---

internal interface IUpdateDispatcher { Task DispatchAsync(Update update, CancellationToken cancellationToken); }

// Единственная обёртка над ITelegramBotClient (в тестах мокается она). ITelegramBotClient в DI не регистрируется —
// модули не могут обойти IMessageSender. Исключения Telegram.Bot пробрасываются как есть
// (ApiRequestException с ErrorCode / Parameters.RetryAfter).
internal interface IBotApi
{
    Task<int> SendMessageAsync(long chatId, string text, ReplyMarkup? replyMarkup, bool html, CancellationToken ct); // messageId
    Task AnswerCallbackQueryAsync(string callbackQueryId, string? text, bool showAlert, CancellationToken ct);
    Task SetWebhookAsync(string url, string secretToken, IReadOnlyCollection<UpdateType> allowedUpdates, CancellationToken ct);
    Task SetMyCommandsAsync(IReadOnlyList<BotCommand> commands, CancellationToken ct);
}

internal static class BotDefaults
{
    public const string HttpClientName = "telegram";        // AddHttpClient(HttpClientName) → TelegramBotClient; тесты подменяют primary handler
    public const string WebhookPath = "/bot/webhook";
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";
}

// Bot.Platform/Configuration/BotOptions.cs — дополняется (SectionName = "Bot" как в 001)
public sealed class BotOptions
{
    public const string SectionName = "Bot";
    public required string Token { get; set; }
    public required string WebhookSecret { get; set; }
    public Uri? PublicUrl { get; set; }                 // https; база для webhook и кнопки Mini App
    public bool SetupOnStartup { get; set; } = true;
    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromSeconds(5);
}
```
Типы `Update`, `Message`, `CallbackQuery`, `ReplyMarkup`, `UpdateType`, `BotCommand`, `ApiRequestException` — из Telegram.Bot v22
(в v22 `IReplyMarkup` заменён базовым классом `ReplyMarkup`; сверить через Context7). Валидация новых полей `BotOptions` —
тем же способом, что `Token` в 001 (`AddOptions<BotOptions>().Validate(...).ValidateOnStart()`, сообщение «Configuration key
'Bot:…' …»).

### Пакет
- `Telegram.Bot` — последняя стабильная **22.x** с nuget.org (мажор 23+ не брать без решения архитектора).
  Backend добавляет `<PackageVersion Include="Telegram.Bot" Version="22.…" />` в `Directory.Packages.props` (точная версия
  без плавающих диапазонов), `PackageReference` — только в `Bot.Platform.csproj`; указывает версию в отчёте.
- `nuget.config` (только nuget.org, `<clear />`) не меняется. Если 22.x на nuget.org недоступна — остановиться и сообщить,
  сторонний feed без решения не добавлять.
- Встроенный повтор при 429 клиента (`TelegramBotClientOptions.RetryCount`/`RetryThreshold` или аналог в выбранной
  версии) — выключить (сверить через Context7).
- Логирование HTTP-клиента `"telegram"` отключить (`RemoveAllLoggers()` у `IHttpClientBuilder`; сверить): URL запросов
  Telegram Bot API содержит токен (`/bot<token>/method`).

### Конфигурация (дополнение к 001)
| Ключ | Обязателен | По умолчанию | Назначение |
|---|---|---|---|
| `Bot:WebhookSecret` | да | — | секрет заголовка webhook, `^[A-Za-z0-9_-]{1,256}$`; только user-secrets / env |
| `Bot:PublicUrl` | нет | — | публичный https-адрес Host (туннель в разработке): webhook + кнопка Mini App |
| `Bot:SetupOnStartup` | нет | `true` | `SetWebhook` + `SetMyCommands` при старте (если задан `PublicUrl`) |
| `Bot:MaxRetryAfter` | нет | `00:00:05` | максимум ожидания при 429, от 0 до `00:01:00` |

## Контракт бота
- Обрабатываются только личные чаты, только `message` и `callback_query` (AC-4).
- Команда — `text`, начинающийся с `/`; разбор `^/([A-Za-z0-9_]{1,32})(@\S+)?(\s+([\s\S]*))?$`, имя → lower-case.
  Не совпало с шаблоном → «неизвестная команда». Entities не используются.
- Команды платформы: `/start` (приветствие или start-payload модуля), `/help`.
- Команды модулей: `/<command>` из `MapCommand`, глобально уникальны.
- Callback data: `<module>:<action>[:<payload>]`, ≤ 64 байт UTF-8.
- Start-payload: `/start <module>_<payload>` (тот же формат, что `startapp` в 001).
- Порядок обработки одного обновления: фильтр (AC-4) → scope → upsert (AC-22) → маршрут → для callback гарантированный
  ответ (AC-9/AC-10).

Тексты (русский, без parse mode):
- Приветствие `/start`:
  «Привет! Это бот с мини-приложением.\nНажмите «Открыть приложение», чтобы начать. Список команд — /help»
  + inline-кнопка `web_app` «Открыть приложение» → `Bot:PublicUrl`.
- `/help`: «Команды:\n/start — открыть приложение\n/help — список команд» + по строке «/<command> — <description>»
  на каждую команду модулей (через `\n`).
- Неизвестная команда: «Не знаю такую команду. Список команд — /help»
- Не команда: «Я понимаю только команды. Список команд — /help»
- Устаревшая кнопка (answerCallbackQuery): «Эта кнопка больше не работает»
- `SetMyCommands`: `start` — «открыть приложение», `help` — «список команд», команды модулей — их описания.

## Экраны Mini App
Нет. Единственное изменение во `webapp/` — dev-прокси (FE-1).

## Задачи

### Backend
- **BE-1** `Telegram.Bot` 22.x в `Directory.Packages.props` + `Bot.Platform.csproj` (см. «Пакет»); именованный HttpClient
  `"telegram"` без логгеров, `TelegramBotClient` поверх него без авто-повтора; `IBotApi` (internal) поверх клиента;
  `InternalsVisibleTo DynamicProxyGenAssembly2`; расширение `BotOptions` и валидация на старте. (AC-20, AC-21, AC-23)
- **BE-2** `IBotModule.MapBot` (default-реализация), `IBotRouteBuilder`, `BotRouteTable` (singleton, заполняется
  в `AddModule<T>()` после `AddServices`) с валидацией AC-12; `CallbackData`. (AC-12, AC-13)
- **BE-3** `POST /bot/webhook` в `MapPlatform()` до fallback: секрет (`FixedTimeEquals`) до чтения тела, десериализация
  `JsonBotAPI.Options`, `IUpdateDispatcher.DispatchAsync`, перехват и Error-лог исключений, 200/400/401 с пустым телом.
  Логи — `update_id`/тип на Debug, без текста сообщений. (AC-1..AC-4)
- **BE-4** `IUpdateDispatcher`: фильтр чатов/типов, scope на обновление (`IServiceScopeFactory`), разбор команд,
  `/start`/`/help`, колбэки с гарантией одного `answerCallbackQuery`, start-payload, тексты; потокобезопасен для
  параллельных обновлений. (AC-4..AC-11)
- **BE-5** `IMessageSender` (singleton) поверх `IBotApi`: проверка текста, 403/429/прочие, один повтор через
  `IClock.DelayAsync`, проброс отмены. (AC-14..AC-18)
- **BE-6** Hosted service настройки бота: в `StartAsync` (ожидается) `SetWebhook` + `SetMyCommands` при заданном
  `PublicUrl` и `SetupOnStartup`; каждая ошибка логируется Error и не роняет Host. (AC-19)
- **BE-7** Upsert пользователя (`IUserRegistry.UpsertAsync(user, true, ct)`) в диспетчере и
  `MarkCannotReceiveMessagesAsync` при `Blocked` (в отдельном scope). (AC-15, AC-22)
- **BE-8** README «Локальный запуск»: `dotnet user-secrets set "Bot:WebhookSecret" …`, `Bot:PublicUrl` = адрес туннеля,
  webhook и команды регистрируются при старте; в варианте B (Vite dev) туннель на Vite, `/bot` проксируется на Host;
  кнопка меню бота — вручную в BotFather. Исправить п.5 (путь БД — относительно ContentRoot; остаточное замечание 001).

Примечание: после BE-1 (обязательный `Bot:WebhookSecret`) интеграционные тесты 001 не стартуют, пока TEST-1 не добавит
секрет в `DemoHost`/`PlatformFactory`. Backend тесты не правит — это ожидаемо до слияния с TEST-1.

### Frontend
- **FE-1** `webapp/vite.config.ts`: в `server.proxy` добавить `'/bot': { target: 'http://localhost:5080', changeOrigin: true }`
  рядом с `/api` (чтобы webhook доходил до Host, когда туннель указывает на Vite dev). Других изменений нет.
  Попутно (MINOR из 001, необязательно): удалить неиспользуемую devDependency `@testing-library/user-event`.

### Tester
- **TEST-1** Хелперы (в `tests/Bot.Platform.Tests/Helpers`):
  - `DemoHost` и `PlatformFactory`: по умолчанию `Bot:WebhookSecret = "test-webhook-secret"`, `Bot:SetupOnStartup` не
    задан (по умолчанию `true`, но без `PublicUrl` вызовов нет); возможность передать переопределения сервисов
    (`Action<IServiceCollection>`) — для подмены `IBotApi`/`IUserRegistry`; существующие тесты 001 зелёные.
  - `TestUpdates`: личное/групповое/канальное сообщение, команда с аргументами и `@bot`, `/start <payload>`, callback
    (в личном/групповом чате, без `message`), стикер, фото с подписью, `edited_message`, сообщение без `from`;
    сериализация в JSON через `JsonBotAPI.Options` для HTTP-тестов.
  - `Substitute.For<IBotApi>()`, фабрика `ApiRequestException` с кодами 403/429(+retry_after)/400.
  - `DemoModule` дополняется `MapBot` (команды `ping` «Проверка связи», `list` «Список», callback `act`, `MapStart`,
    обработчик-«бомба»), второй модуль `demob` (`zeta` «Последняя») и модули с невалидными маршрутами для AC-12.
    `NamedModule` не трогать — проверяет default `MapBot`.
  - Заглушка `HttpMessageHandler` для AC-23 (подмена primary handler клиента `"telegram"`).
- **TEST-2** Webhook через HTTP: AC-1..AC-4 (каждый вариант секрета, порядок «секрет до тела», исключение обработчика
  и следующее обновление, `GET`/`/bot/other` → не `index.html`).
- **TEST-3** Диспетчер (напрямую `IUpdateDispatcher`): AC-5..AC-11 (регистр, `@bot`, пробелы, payload с `:`, ответ на
  колбэк ровно один раз — с `AnswerAsync`, без него, двойной `AnswerAsync`, при исключении, при ошибке Telegram в ответе;
  каждый невалидный start-payload; две параллельные обработки разных пользователей).
- **TEST-4** Регистрация и `CallbackData`: AC-12, AC-13 (каждый невалидный случай отдельно, конфликт двух модулей,
  граница 64/65 байт с кириллицей, round-trip).
- **TEST-5** `IMessageSender`: AC-14..AC-18 (`FakeClock.Delays`, граница `retry_after == MaxRetryAfter`, повтор → 403,
  `retry_after` отсутствует, отмена до/во время, длина 4096/4097, пробельный текст).
- **TEST-6** Старт и секреты: AC-19..AC-21, AC-23 (порядок и параметры `SetWebhook`/`SetMyCommands`, ошибка первого
  вызова не отменяет второй, каждое невалидное значение AC-20 отдельно с проверкой, что сообщение называет ключ и не содержит
  значение; поиск значений токена и секрета во всех `LogCapture.Entries[*].FullText` при Trace).
- **TEST-7** Реестр: AC-15 (вызов `MarkCannotReceiveMessagesAsync`, ошибка в нём), AC-22 на реальной БД
  (новая запись, `false` → `true` после сообщения, `FirstSeenAt` неизменен, отфильтрованные обновления не создают запись).

## Вне скоупа
- Long polling (локально — тот же туннель, что для Mini App; webhook ставится при старте).
- Дедупликация повторно доставленных обновлений по `update_id`.
- `my_chat_member` (отслеживание блокировки без попытки отправки) — `CanReceiveMessages` снимается только по 403.
- Групповые чаты, inline-режим, свободный текст для модулей (например, «пришли ссылку на фильм в бот»).
- Редактирование/удаление сообщений, медиа в `IMessageSender`.
- Кнопка меню бота — настраивается в BotFather вручную (README).
- Очередь/фоновая отправка, массовые рассылки, лимиты 30 msg/s.
- Генерация ссылок `t.me/<bot>?start=…` и `Bot:Username` — этап 1 (приглашения).

## Открытые вопросы
Нет.

## Принятые решения (ответы пользователя)
- **Реестр пользователей платформы есть с Этапа 0 (вариант A, см. 001).** Бот обновляет его из каждого обновления
  и снимает `CanReceiveMessages` при `Blocked` (AC-15, AC-22, BE-7, TEST-7).
- **Порядок:** 002 реализуется отдельным циклом `/feature` после закрытия 001 (001 — DONE).

## Допущения
- Webhook без long polling: туннель нужен в любом случае для HTTPS Mini App; один путь доставки обновлений.
- Секрет webhook — в заголовке `X-Telegram-Bot-Api-Secret-Token`, токен в URL не используется (не попадает в логи доступа).
- Ошибка обработчика не приводит к не-200: иначе Telegram будет бесконечно повторять обновление.
- Суффикс `@bot` у команды отбрасывается без проверки имени: обрабатываются только личные чаты.
- `IBotApi` введён, чтобы тесты не мокали `ITelegramBotClient.SendRequest` (в Telegram.Bot v22 методы — расширения).
- Тексты бота без форматирования (parse mode не задаётся), чтобы не экранировать имена.
- `MapBot` — default interface method с пустым телом: модули без бота и тестовые `NamedModule` из 001 компилируются
  без изменений, backend и tester не блокируют друг друга при параллельной работе.
- Маршруты бота регистрируются в `AddModule<T>()`, а не в `MapPlatform()`: ошибки регистрации видны в том же месте,
  что ошибки имени модуля (AC-3 из 001), и список команд известен hosted service до старта.
- `IBotApi`, `IUpdateDispatcher` — internal: модулям доступны только `IMessageSender` и контексты обработчиков.
- `IMessageSender` — singleton (модули на этапе 3 будут слать уведомления вне обновлений); scoped `IUserRegistry`
  берётся через собственный scope.
- Hosted service выполняет настройку в `StartAsync` с ожиданием: тесты проверяют вызовы сразу после `StartAsync`.
  Обновление, пришедшее до начала прослушивания, Telegram доставит повторно.
- `MarkCannotReceiveMessagesAsync(chatId)`: платформа шлёт только в личные чаты, где `chatId` = id пользователя.
- Webhook URL = `PublicUrl` без завершающего `/` + `/bot/webhook`; Host за path-префиксом не поддерживается.

## Изменения ревизии 2 (сверка с кодом 001)
- Namespace `Bot.Platform/Telegram` → `Bot.Platform.Messaging` (конфликт имён с `Telegram.Bot`).
- `IBotModule.MapBot` — default-реализация; место регистрации — `AddModule<T>()` (в коде 001: `ModuleRegistry.Add` →
  `AddServices`); сигнатуры `AddPlatform`/`AddModule`/`MapPlatform` и `Program.cs` не меняются.
- `BotOptions`: сохранён `SectionName = "Bot"`, валидация тем же механизмом, что в 001; добавлены диапазон `MaxRetryAfter`
  и запрет query/fragment в `PublicUrl`.
- Upsert: точная сигнатура 001 `UpsertAsync(TelegramUser, bool?, CancellationToken)` с `true`; правило `CanReceiveMessages`
  из 001 даёт `false → true` при сообщении.
- `ITelegramBotClient` не регистрируется в DI; `IBotApi`/`IUpdateDispatcher` — internal (+ `DynamicProxyGenAssembly2`).
- Зафиксирован пакет Telegram.Bot 22.x с nuget.org без изменения `nuget.config`; выключены авто-повтор 429 и логирование
  HttpClient (токен в URL); новый AC-23 на адаптер поверх заглушки HTTP.
- `/start`-payload выровнен с форматом `parseStartParam` из 001 (≤ 64, `[A-Za-z0-9_-]`).
- Уточнены граничные случаи: секрет до тела (AC-1), `null`/пустое тело (AC-2), `/bot/*` не `index.html` (AC-3),
  callback без message / сообщение без from (AC-4), точный текст `/help` с двумя модулями (AC-6), разбор `/`-строк (AC-7),
  ошибка `answerCallbackQuery` (AC-9), лимит 100 команд и null-обработчик (AC-12), повтор → 403 и отсутствие `retry_after` (AC-16).
- Тестовые хелперы 001 (`DemoHost`, `PlatformFactory`, `LogCapture`, `FakeClock.Delays`) переиспользуются; им нужен
  `Bot:WebhookSecret` и хук переопределения сервисов (TEST-1).
- Добавлен FE-1 (прокси `/bot` в Vite dev) → HAS_FRONTEND: yes (только конфиг). README-правка пути БД из остаточных замечаний 001.

## Остаточные замечания (MINOR, не блокируют)
Critic: `APPROVED` с первого круга. Ниже то, что стоит учесть в следующих задачах платформы.
- **Авто-повтор 429:** выставлен `RetryCount = 0`, но `RetryThreshold = 0` не задан, а документация Telegram.Bot
  описывает авто-повтор именно через `RetryThreshold`. Нужно явно выставить и `RetryThreshold = 0`.
- **AC-9, исключения `AnswerOnceAsync`:** ловятся только `ApiRequestException`, `RequestException` и
  `HttpRequestException`. Прочие исключения (например, `TaskCanceledException` по таймауту HTTP) могут вылететь
  из `AnswerAsync`, хотя AC-9 требует, чтобы ошибка `answerCallbackQuery` не ломала обработку.
- **Callback при падении upsert:** если upsert пользователя падает, `answerCallbackQuery` не вызывается, и кнопка
  у пользователя продолжает «крутиться» до таймаута клиента. AC-9 этот случай не покрывает.
- **AC-13, `CallbackData.TryParse`:** формат `module`/`action` не проверяется (`Demo:Act` → `true`).
  Формулировка AC-13 тут неоднозначна: при следующей правке спеки надо явно решить, нужна ли проверка формата
  (регистр, допустимые символы).
- **`parse_mode`:** сериализуется как `"Html"`, а не `"HTML"`, как в документации Bot API. Тест сравнивает без учёта
  регистра, поэтому расхождение не ловит. Тест стоит ужесточить или явно зафиксировать это как допущение.
