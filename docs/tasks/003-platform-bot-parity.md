# 003 — Платформа: инструменты бота для дублирования функций Mini App

**Статус:** DONE
**Модуль:** platform   **Бриф:** docs/product/movie-night.md («Дополнения после проверки Этапа», п. 0)
**Зависит от:** 001, 002 (DONE).   **Блокирует:** 004 (ссылки-приглашения), 005 (бот модуля MovieNight).

## Исходное описание
Из брифа, раздел «Дополнения после проверки Этапа»:
«0. Необходимо продублировать функции, которые появятся в Mini App, через inline команды в боте, чтобы была
возможность работать с ботом через контекстное меню в Telegram».

## Почему это отдельная спецификация платформы
Дополнение — сквозное продуктовое правило: начиная с Этапа 1 каждый этап модуля закрывает свои функции
и в Mini App, и в боте. Сами функции модуля в боте (группы) — в спецификации 005. Но текущая платформа (002) не даёт
модулю того, что для этого нужно:
1. **Ввод текста в боте.** Сейчас любой текст без `/` получает ответ «Я понимаю только команды» — модуль не может
   спросить «Как назовём группу?» и получить ответ. Это понадобится и на Этапе 2 (поиск фильма, ссылка TMDB в бот).
2. **Навигация inline-кнопками.** Меню на inline-кнопках (список групп → карточка → назад) делается редактированием
   того же сообщения; `IMessageSender` умеет только отправлять.
3. **Ссылки `t.me/...`.** Приглашения (Этап 1) требуют ссылок на бота и на Mini App: нужен `Bot:Username`
   и единое место построения ссылок (в 001/002 отложено «до этапа 1»).
4. **Mini App: «Поделиться» и подтверждение.** Обёртка `telegram.ts` не умеет `openTelegramLink` и `showConfirm`,
   а модули не трогают `window.Telegram` напрямую.
Плюс остаточные MINOR-замечания 002, относящиеся к этому же коду.

## Цель
Модуль может строить диалог в личном чате бота: спросить текст и получить ответ, перерисовывать сообщение-меню
по нажатию inline-кнопок, выдавать ссылки на бота и Mini App. Mini App может поделиться ссылкой и спросить подтверждение.

## Сценарии
1. Модуль по кнопке «Создать группу» пишет «Как назовём группу?» и вызывает `ExpectReplyAsync`; следующий текст
   пользователя приходит в обработчик модуля `MapReply`, а не в «Я понимаю только команды».
2. Пользователь вместо ответа отправляет команду (`/help`) — ожидание сбрасывается, команда работает как обычно.
3. Пользователь нажимает «К списку групп» под карточкой — модуль редактирует то же сообщение (`EditAsync`), новое не шлёт.
4. Модуль строит `https://t.me/<bot>?start=movienight_join_AB12cd34EF` и `https://t.me/<bot>/<app>?startapp=…`
   через `IDeepLinks`.
5. Экран Mini App вызывает `telegram.shareUrl(url, text)` — открывается штатный выбор чата Telegram.

## Критерии приёмки

### Ожидание текстового ответа
- **AC-1** Given модуль `demo` зарегистрировал `MapReply("name", h)` и вызвал
  `IBotConversation.ExpectReplyAsync(42, "demo", "name", "p1", ct)` When пользователь 42 в личном чате шлёт текст
  `"  Киноклуб  "` (не начинается с `/`) Then `h` вызван ровно один раз с `Action = "name"`, `Payload = "p1"`,
  `Text = "Киноклуб"` (trim), `ChatId`, `From` как в 002; ответ «Я понимаю только команды» не отправлен;
  ожидание удалено до вызова `h` (если `h` снова вызвал `ExpectReplyAsync` — новое ожидание сохраняется).
- **AC-2** Given активное ожидание When пользователь шлёт текст, начинающийся с `/` (любой: `/help`, `/start`,
  неизвестная команда, команда модуля) Then ожидание удалено, затем команда обрабатывается по правилам 002;
  `h` не вызван. Следующий обычный текст → «Я понимаю только команды».
- **AC-3** Given ожидание, у которого `ExpiresAt <= IClock.UtcNow` (`ExpiresAt = момент вызова + Bot:ReplyTimeout`,
  по умолчанию 10 мин; граница: ровно `ExpiresAt` — истекло) When текст Then ожидание удалено, `h` не вызван,
  ответ «Я понимаю только команды. Список команд — /help».
- **AC-4** Given активное ожидание When сообщение без `text` (стикер, фото, в т.ч. фото с подписью) Then ответ
  «Отправьте ответ текстом.», ожидание сохраняется, `h` не вызван. Given ожидания нет Then поведение 002 (AC-7).
- **AC-5** Given у пользователя ожидание `demo:name` When `ExpectReplyAsync(42, "demo", "other", null)` Then хранится
  только новое (одно ожидание на пользователя; `Payload = ""` для `null`). `CancelAsync(42)` → `true` и ожидание
  удалено; повторный `CancelAsync(42)` → `false`. Ожидания разных пользователей независимы.
- **AC-6** `ExpectReplyAsync` бросает `ArgumentException`, если модуль не зарегистрирован, у модуля нет `MapReply`
  с таким `action`, или `payload` длиннее 256 символов. Регистрация `MapReply` — по правилам `MapCallback` из 002 AC-12
  (формат `^[a-z0-9_]{1,32}$`, повтор в модуле, `null`-обработчик → `InvalidOperationException` из `AddModule<T>()`).
- **AC-7** Given активное ожидание When два текстовых сообщения пользователя обрабатываются одновременно Then `h`
  вызван ровно один раз; второе сообщение получает «Я понимаю только команды» (удаление ожидания атомарно —
  обработчик вызывает только тот, чьё удаление затронуло запись). Given `h` (вызванный первым сообщением) сам вызвал
  `ExpectReplyAsync` для нового ожидания до того, как второе сообщение выполнило удаление Then новое ожидание
  сохраняется: второе сообщение не удаляет и не потребляет его (удаление сравнивает все прочитанные поля строки,
  см. «Хранилище ожиданий»), получает «Я понимаю только команды», `h` по-прежнему вызван ровно один раз.
- **AC-7a** Given активное ожидание When текст из одних пробельных символов (после trim — пустая строка) Then ответ
  «Отправьте ответ текстом.», ожидание сохраняется (не потребляется), `h` не вызван. Given ожидания нет Then
  «Я понимаю только команды».
- **AC-8** Given ожидание, указывающее на `action`, которого больше нет в таблице маршрутов (модуль переименовал
  действие после деплоя) When текст Then ожидание удалено, ответ «Я понимаю только команды», исключения нет.
- **AC-9** Given обработчик `h` бросил исключение When текст Then webhook всё равно 200 (как 002 AC-3), ожидание
  остаётся удалённым.

### Редактирование сообщений
- **AC-10** Given `IBotApi.EditMessageTextAsync` успешен When `IMessageSender.EditAsync(chatId, 7, message, ct)`
  Then `SendResult(Sent, 7)`; в `IBotApi` переданы `text`, inline-разметка и `html` без изменений.
  Given `message.ReplyMarkup` не `InlineKeyboardMarkup` и не `null` Then `ArgumentException`, `IBotApi` не вызван.
  Проверки текста — как 002 AC-18.
- **AC-11** Given `IBotApi` бросил `ApiRequestException` 400, в описании которого есть `message is not modified`
  (без учёта регистра) When `EditAsync` Then `SendResult(Sent, 7)`, лог не выше Debug. Given иная 400
  (`message to edit not found`, `message can't be edited`) Then `Failed`, лог Warning. 403/429/прочие/отмена — как
  `SendAsync` (002 AC-15..AC-17), включая `MarkCannotReceiveMessagesAsync` при 403.
- **AC-12** Given callback из сообщения с `message_id = 7` When обработчик вызывает `context.EditAsync(message)`
  Then вызван `EditAsync(chatId, 7, message, ct)`; `context.MessageId == 7`.
  Given callback без `Query.Message` (`MessageId == null`) When `context.EditAsync(message)` Then
  `InvalidOperationException`, `IMessageSender.EditAsync` не вызван. (На практике диспетчер такие callback'и до
  модуля не доводит — фильтр приватного чата требует `Query.Message`; поведение фиксируется для прямого вызова.)

### Ссылки
- **AC-13** Given `Bot:Username = "MovieNightBot"`, `Bot:MiniAppShortName = "app"` When
  `IDeepLinks.MiniApp("movienight", "join_AB12cd34EF")` Then `"https://t.me/MovieNightBot/app?startapp=movienight_join_AB12cd34EF"`;
  `BotStart("movienight", "join_AB12cd34EF")` → `"https://t.me/MovieNightBot?start=movienight_join_AB12cd34EF"`.
  Given `MiniAppShortName` не задан Then `MiniApp(...)` → `"https://t.me/MovieNightBot?startapp=movienight_join_AB12cd34EF"`
  (основное Mini App бота; BE сверяет формат с https://core.telegram.org/bots/webapps#direct-link-mini-apps и
  «Main Mini App», расхождение — в отчёт). Given `Bot:Username` не задан Then оба метода с валидными аргументами → `null`.
  Given `Bot:Username` не задан и аргументы невалидны (AC-14) Then `ArgumentException` (`ArgumentNullException` для
  `null`) — проверка аргументов выполняется до проверки конфигурации.
- **AC-14** `MiniApp`/`BotStart` с module не по `^[a-z][a-z0-9]{1,31}$`, payload не по `^[A-Za-z0-9_-]+$`
  или `"{module}_{payload}"` длиннее 64 → `ArgumentException` (граница: 64 — ок). `IDeepLinks.WebAppUrl` = `Bot:PublicUrl`
  (или `null`) — для `InlineKeyboardButton.WithWebApp` в модулях.
- **AC-15** Given `Bot:Username` задан, но не по `^[A-Za-z][A-Za-z0-9_]{4,31}$` (в т.ч. с `@`), или
  `Bot:MiniAppShortName` не по `^[A-Za-z0-9_]{3,30}$`, или `Bot:ReplyTimeout` вне `[00:01:00, 1.00:00:00]`
  When Host стартует Then `OptionsValidationException`, сообщение называет ключ, не значение (как 002 AC-20).

### Остаточные замечания 002
- **AC-16** Given upsert пользователя упал при обработке callback When обработка Then `answerCallbackQuery` всё равно
  вызван ровно один раз (без текста), исключение залогировано Error, webhook 200.
- **AC-17** Given `IBotApi.AnswerCallbackQueryAsync` бросил любое исключение, кроме `OperationCanceledException`
  при отменённом токене обработки (например, `TaskCanceledException` по таймауту HTTP, `InvalidOperationException`)
  When `AnswerAsync`/ответ платформы Then исключение не пробрасывается, лог Warning.
- **AC-18** `CallbackData.TryParse` проверяет формат: module по `^[a-z][a-z0-9]{1,31}$`, action по `^[a-z0-9_]{1,32}$`;
  `"Demo:act"`, `"demo:Act"`, `"demo:a-b"` → `false` (и диспетчер отвечает «Эта кнопка больше не работает»).
- **AC-19** Клиент Telegram.Bot создаётся с `RetryCount = 0` и `RetryThreshold = 0` (проверка — 002 AC-23:
  на 429 с `retry_after = 3` заглушка вызвана ровно один раз).

### Mini App
- **AC-20** `telegram.openTelegramLink(url)` вызывает `WebApp.openTelegramLink(url)`;
  `telegram.shareUrl("https://t.me/b?start=x_1", "Привет & пока")` вызывает `openTelegramLink` с
  `https://t.me/share/url?url=https%3A%2F%2Ft.me%2Fb%3Fstart%3Dx_1&text=%D0%9F...` (оба параметра через
  `encodeURIComponent`). Вне Telegram — no-op: `WebApp.openTelegramLink` не вызывается, `window.open`/`location`
  не трогаются, исключения нет.
- **AC-21** `telegram.showConfirm("Выйти?")` вызывает `WebApp.showConfirm(message, cb)` и резолвится значением `cb`
  (`true`/`false`); вне Telegram или если `showConfirm` отсутствует — резолвится `false` без исключения,
  `WebApp.showConfirm` не вызывается.
- **«Вне Telegram» (для AC-20, AC-21)** = `telegram.isAvailable() === false`, т.е. пустой/отсутствующий `initData`.
  Наличие `window.Telegram.WebApp` признаком НЕ является: локальный `telegram-web-app.js` создаёт объект всегда,
  в том числе в обычном браузере. Тест: `WebApp` присутствует с методами-шпионами, `initData = ""` → шпионы не вызваны.

### Локальный `telegram-web-app.js` (дополнение пользователя)
- **AC-22** (исходники) Given `webapp/index.html` Then в нём ровно один `<script src="/telegram-web-app.js">`
  в `<head>`, без `async`, `defer`, `type="module"`; он стоит раньше `<script type="module">` бандла приложения;
  подстрока `telegram.org` в файле отсутствует. Файл `webapp/public/telegram-web-app.js` существует и не пустой.
- **AC-23** (сборка) Given выполнен `npm run build` Then `webapp/dist/telegram-web-app.js` существует и побайтно
  совпадает с `webapp/public/telegram-web-app.js` (Vite копирует `public/` в корень `dist` без хэша);
  `webapp/dist/index.html` содержит `src="/telegram-web-app.js"` (синхронный, раньше бандла) и не содержит `telegram.org`.
- **AC-24** (раздача) Given в `dist` есть `index.html` и `telegram-web-app.js` When `GET /telegram-web-app.js`
  Then 200, тело — содержимое файла (не `index.html`), `Content-Type` — `text/javascript` или `application/javascript`
  (с `charset` или без), `Cache-Control: no-cache` (файл не хэшированный, обновляется вручную; не `immutable`).
  `HEAD /telegram-web-app.js` → 200 с теми же заголовками.
- **AC-25** (раздача) Given в `dist` нет `telegram-web-app.js` When `GET /telegram-web-app.js` Then 404, тело не
  `index.html` (SPA-fallback на файловые пути не срабатывает — как 001 AC-14).

## Контракт API
HTTP-эндпоинтов не добавляется. Статика: `GET|HEAD /telegram-web-app.js` — файл из `dist` (AC-24, AC-25),
существующим механизмом `UseStaticFiles`.

## Контракты платформы (C#)

```csharp
// Bot.Platform/Messaging/BotRouteTable.cs — расширение
public interface IBotRouteBuilder
{
    // ... MapCommand, MapCallback, MapStart из 002
    IBotRouteBuilder MapReply(string action, BotHandler<BotReplyContext> handler);   // новое
}

// Bot.Platform/Messaging/BotContexts.cs
public sealed class BotReplyContext : BotContext
{
    public required Message Message { get; init; }
    public required string Action { get; init; }
    public required string Payload { get; init; }   // "" если не задан
    public required string Text { get; init; }      // trim
}
public sealed class BotCallbackContext : BotContext
{
    // ... из 002
    public int? MessageId { get; }                                   // Query.Message?.Id
    public Task<SendResult> EditAsync(OutgoingMessage message);      // EditAsync(ChatId, MessageId!.Value, …)
}

// Bot.Platform/Messaging/IBotConversation.cs — scoped, хранение в platform DB
public interface IBotConversation
{
    Task ExpectReplyAsync(long userId, string module, string action, string? payload, CancellationToken cancellationToken);
    Task<bool> CancelAsync(long userId, CancellationToken cancellationToken);
}

// Bot.Platform/Messaging/OutgoingMessage.cs
public interface IMessageSender
{
    Task<SendResult> SendAsync(long chatId, OutgoingMessage message, CancellationToken cancellationToken);
    Task<SendResult> EditAsync(long chatId, int messageId, OutgoingMessage message, CancellationToken cancellationToken); // новое
}

// internal IBotApi — новое
Task EditMessageTextAsync(long chatId, int messageId, string text, InlineKeyboardMarkup? replyMarkup, bool html, CancellationToken ct);

// Bot.Platform/Messaging/IDeepLinks.cs — singleton
public interface IDeepLinks
{
    string? MiniApp(string module, string payload);    // null, если Bot:Username не задан
    string? BotStart(string module, string payload);   // null, если Bot:Username не задан
    Uri? WebAppUrl { get; }                            // Bot:PublicUrl
}

// Bot.Platform/Configuration/BotOptions.cs — дополнение
public string? Username { get; set; }                  // без "@"
public string? MiniAppShortName { get; set; }
public TimeSpan ReplyTimeout { get; set; } = TimeSpan.FromMinutes(10);
```

Хранилище ожиданий — таблица `platform_pending_replies` в `PlatformDbContext` (новая миграция):
`UserId` (PK), `Module`, `Action`, `Payload`, `ExpiresAt`. Потребление атомарно (AC-7), один из двух вариантов:
- прочитать строку, затем `ExecuteDeleteAsync` с условием по **всем прочитанным полям**
  (`UserId && Module && Action && Payload && ExpiresAt`) и потреблять, только если затронута ровно 1 строка; либо
- один `DELETE … WHERE UserId = @id RETURNING …` (SQLite ≥ 3.35) — потребляет тот, кому вернулась строка.
Удаление только по `UserId` недопустимо: оно съедает новое ожидание, поставленное обработчиком первого сообщения.
Истёкшее ожидание (AC-3) удаляется тем же условным удалением. Проверка «текст пустой после trim» (AC-7a) выполняется
до попытки потребления.

### Порядок обработки личного сообщения (дополняет 002)
фильтр → scope → upsert → если `text` начинается с `/`: `CancelAsync` → маршрутизация 002;
иначе если `text != null` и `text.Trim() != ""`: попытка потребить ожидание (AC-1, AC-3, AC-7, AC-8) → иначе
«Я понимаю только команды»;
иначе (нет текста или только пробелы): есть активное ожидание → «Отправьте ответ текстом.» (ожидание сохраняется),
иначе — нет текста: 002 AC-7; только пробелы: «Я понимаю только команды».

### Конфигурация (дополнение)
| Ключ | Обязателен | По умолчанию | Назначение |
|---|---|---|---|
| `Bot:Username` | нет | — | имя бота без `@`, для ссылок `t.me`. Без него ссылок-приглашений нет |
| `Bot:MiniAppShortName` | нет | — | short name из `/newapp`; без него ссылка ведёт на основное Mini App бота |
| `Bot:ReplyTimeout` | нет | `00:10:00` | срок ожидания текстового ответа, 1 мин – 1 сутки |

## Контракт бота
- Новый текст платформы: «Отправьте ответ текстом.» (сообщение без текста при активном ожидании).
- Команды платформы не меняются (`/start`, `/help`); `/cancel` не вводится: ввод отменяет любая команда или
  кнопка модуля «Отмена» (модуль вызывает `CancelAsync`).

## Экраны Mini App
Новых экранов нет. Расширение `webapp/src/platform/telegram.ts`:
```ts
export interface TelegramPlatform {
  // ... из 001
  openTelegramLink(url: string): void;
  shareUrl(url: string, text: string): void;            // openTelegramLink('https://t.me/share/url?url=…&text=…')
  showConfirm(message: string): Promise<boolean>;
}
```
Минимальные типы `TgWebApp` дополняются `openTelegramLink(url: string): void` и
`showConfirm(message: string, cb: (ok: boolean) => void): void` (сверить с документацией WebApp через Context7/core.telegram.org).

## Задачи
### Backend
- **BE-1** `MapReply`, `BotReplyContext`, `IBotConversation` (scoped) + таблица `platform_pending_replies` и миграция
  `PlatformDbContext`; доработка `UpdateDispatcher` по порядку обработки выше. (AC-1..AC-9, AC-7a)
- **BE-2** `IBotApi.EditMessageTextAsync` (Telegram.Bot v22 `EditMessageText`, сверить через Context7),
  `IMessageSender.EditAsync`, `BotCallbackContext.MessageId`/`EditAsync`. Общую политику 403/429/ошибок вынести
  и переиспользовать с `SendAsync`. (AC-10..AC-12)
- **BE-3** `IDeepLinks` + поля `BotOptions` и их валидация на старте. (AC-13..AC-15)
- **BE-4** Остаточные замечания 002: ответ на callback при падении upsert, широкий catch в `AnswerOnceAsync`,
  формат в `CallbackData.TryParse`, `RetryThreshold = 0`. (AC-16..AC-19)
- **BE-5** README: `Bot:Username`, `Bot:MiniAppShortName` (как получить в BotFather: `/newapp` или Main Mini App).
  Плюс раздел «telegram-web-app.js»: файл лежит в `webapp/public/`, обновляется вручную (скачать
  `https://telegram.org/js/telegram-web-app.js`, заменить файл, пересобрать), в README фиксируются версия
  (`Telegram.WebApp.version` / комментарий в шапке файла, если есть) и дата последнего обновления.
- Изменений кода раздачи статики нет: текущая логика `PlatformHostExtensions.UseWebApp` уже даёт AC-24/AC-25
  (`UseStaticFiles` из `dist` раньше fallback; всё вне `/assets` — `no-cache`; `MapFallback` по умолчанию не
  ловит пути с расширением файла). Если тесты TEST-7 покажут расхождение — BE правит раздачу, не тесты.

### Frontend
- **FE-1** `telegram.ts`: `openTelegramLink`, `shareUrl`, `showConfirm`. (AC-20, AC-21)
- **FE-2** (попутно, остаточное 001) `HistoryTracker.dispose()`/`router.dispose()` вызываются в cleanup эффекта
  оболочки (размонтирование корневого компонента). Должно корректно переживать двойной mount React StrictMode
  (mount → cleanup → mount): после второго mount трекер/роутер рабочие (подписки активны, BackButton реагирует),
  нет двойных подписок и исключений; `dispose()` идемпотентен. Т.е. создание трекера/подписок — в том же эффекте,
  что и `dispose`, а не в рендере/модульной области с однократной инициализацией.
- **FE-3** Локальный `telegram-web-app.js`: `webapp/index.html` подключает `<script src="/telegram-web-app.js"></script>`
  (уже изменено пользователем — проверить и оставить) вместо `https://telegram.org/js/telegram-web-app.js`; скрипт
  синхронный (без `async`/`defer`), в `<head>`, до бандла приложения. Убедиться, что `npm run build` не падает и
  не переписывает ссылку (Vite оставляет не-модульный скрипт из `public/` как есть; предупреждение
  «can't be bundled without type="module"» допустимо, если `lint`/`build` его не превращают в ошибку).
  Нигде в `webapp/` (кроме самого `public/telegram-web-app.js`) нет ссылок на `telegram.org/js`. (AC-22, AC-23)

### Tester
- **TEST-1** Хелперы: `DemoModule` + `MapReply("name")`; `TestUpdates` — текст без `/`; `FakeClock` для TTL;
  `fakeTelegram.ts` — `openTelegramLink`, `showConfirm`.
- **TEST-2** Диспетчер + реальная БД: AC-1..AC-9, AC-7a (граница TTL, команда сбрасывает, стикер/фото, две параллельные
  обработки, обработчик ставит новое ожидание во время параллельной обработки — оно сохраняется, только пробелы,
  удалённый action, исключение обработчика, одно ожидание на пользователя, независимость пользователей).
  Плюс: `MapReply("x")` и `MapCallback("x")` в одном модуле — `AddModule<T>()` без исключения; `IDeepLinks` с
  невалидными аргументами без `Bot:Username` — исключение (AC-13).
- **TEST-3** `EditAsync`: AC-10..AC-12 (not modified, иные 400, 403 с `MarkCannotReceiveMessages`, 429, не-inline разметка).
- **TEST-4** `IDeepLinks` и конфигурация: AC-13..AC-15 (граница 64, каждое невалидное значение, сообщение без значения).
- **TEST-5** Остаточные: AC-16..AC-19.
- **TEST-6** Frontend: AC-20, AC-21 (кодирование, вне Telegram, отсутствие метода).
- **TEST-7** Backend, `tests/Bot.Platform.Tests/StaticFilesTests.cs`: AC-24 (во временный `dist` положить
  `telegram-web-app.js` с маркером; GET и HEAD — 200, тело-маркер, не `index.html`, content-type, `no-cache` и
  отсутствие `immutable`), AC-25 (файла нет → 404, не `index.html`).
- **TEST-8** Frontend, Vitest (`webapp/src/platform/__tests__/` или рядом с `index.html`, чтение файлов через `node:fs`):
  AC-22 — парсинг `webapp/index.html` (порядок скриптов, отсутствие `async`/`defer`/`telegram.org`, файл в `public/`);
  AC-23 — проверки `webapp/dist/` (см. допущение о порядке build → test).

## Вне скоупа
- Inline-режим (`@bot запрос` в любом чате, `inline_query`) — решение пользователя, см. «Принятые решения».
- Работа бота в групповых чатах (фильтр 002 AC-4 не меняется).
- `/cancel`, многошаговые формы (мастера) на уровне платформы — модуль строит их сам из `MapReply`.
- Удаление сообщений, медиа, `answerInlineQuery`, `savePreparedInlineMessage`/`shareMessage`.
- `WebApp.requestWriteAccess` (кандидат на этап 3 — уведомления).

## Открытые вопросы
Нет.

## Принятые решения (ответы пользователя)
- **Режим бота — вариант A.** Дублирование функций Mini App — только в личном чате с ботом: команды в меню «/»,
  inline-кнопки под сообщениями, ввод текста по запросу бота (`MapReply`). Inline-режим `@bot` и групповые чаты —
  не делаются.
- **Паритет — полный (вариант A).** Каждая будущая спека модуля содержит таблицу паритета Mini App ↔ бот с AC на
  каждую строку. В этой спеке (платформа, функций модуля нет) таблица не нужна — она даёт инструменты для паритета.
- **Порядок:** 003 → 004 → 005, каждая отдельным циклом `/feature`.

## Решения по замечаниям critic, круг 1
1. **Атомарное потребление ожидания.** Удаление — по всем прочитанным полям строки или одним `DELETE … RETURNING`
   (см. «Хранилище ожиданий»). AC-7 дополнен: новое ожидание, поставленное обработчиком первого сообщения,
   не съедается вторым параллельным сообщением.
2. **«Вне Telegram» = `telegram.isAvailable() === false`** (пустой `initData`), а не отсутствие
   `window.Telegram.WebApp`. В этом случае `openTelegramLink`/`shareUrl` — no-op, `showConfirm` → `false` (AC-20, AC-21).
3. **FE-2:** `dispose` — в cleanup эффекта оболочки, идемпотентен, переживает двойной mount StrictMode.
4. **`BotCallbackContext.EditAsync` без `Query.Message`** → `InvalidOperationException`, отправитель не вызывается (AC-12).
5. Неоднозначности tester'а:
   - (а) `IDeepLinks.MiniApp`/`BotStart` с невалидными аргументами при незаданном `Bot:Username` → исключение
     (`ArgumentException`/`ArgumentNullException`): ошибка программиста не должна маскироваться конфигурацией.
     `null` — только для валидных аргументов (AC-13).
   - (б) Ответ из одних пробелов при активном ожидании → «Отправьте ответ текстом.», ожидание сохраняется, обработчик
     не вызывается (новый AC-7a). Модуль никогда не получает пустой `Text`.
   - (в) `MapReply` и `MapCallback` с одинаковым `action` в одном модуле — разрешено: пространства имён независимы
     (повтор запрещён только внутри одного вида маршрутов, AC-6 / 002 AC-12).

## Допущения
- Дополнение п. 0 — сквозное правило для всех этапов модуля; в платформу выносится только инструментарий (эта спека),
  функции модуля в боте — в спеке этапа (для Этапа 1 — 005).
- Ожидание ответа хранится в БД (переживает рестарт), одно на пользователя, TTL 10 мин.
- Любая команда отменяет ожидание; отдельной `/cancel` нет, чтобы не менять `/help` и `SetMyCommands` из 002.
- «Message is not modified» — успех: повторное нажатие той же кнопки не должно выглядеть ошибкой.
- `parse_mode` сериализуется Telegram.Bot как есть (остаточное замечание 002): Bot API принимает значение без учёта
  регистра; фиксируем как допущение, отдельного исправления нет.
- Без `Bot:Username` ссылки — `null`, Host стартует (обратная совместимость с текущей конфигурацией тестового сервера).
- `telegram-web-app.js` раздаётся с нашего сервера (дополнение пользователя): клиент не ходит на telegram.org.
  Обновление — вручную, версия и дата фиксируются в README (BE-5). Автообновление/проверка свежести — вне скоупа.
- Файл не хэшируется и отдаётся с `no-cache` (ревалидация по ETag/Last-Modified): после ручного обновления клиенты
  получают новую версию без смены URL.
- AC-23 проверяется по артефактам сборки: тест читает `webapp/dist/`; при отсутствии `dist` тест AC-23 падает с
  сообщением «run npm run build first» (порядок команд проекта: `npm run build` → `npm run test`). Тест не запускает
  сборку сам.
- Лицензия/происхождение файла не проверяются тестами; файл кладёт пользователь из официального источника.

## Остаточные замечания (MINOR, не блокируют)
Critic: `APPROVED` (круг 2). Замечания ниже зафиксированы для последующих задач, исправления в рамках 003 не требуются.
1. Тесты AC-20 («вне Telegram») не проверяют, что `window.open` / `location` не трогаются. Код их не трогает;
   пробел только в покрытии тестами.
2. Условное удаление в `ConsumeAsync`: если обработчик в тот же тик часов переставит идентичное ожидание (тот же
   module/action/payload/`ExpiresAt`), параллельное второе сообщение теоретически может его «съесть». На реальных
   часах практически невозможно (`ExpiresAt` с точностью 7 знаков); такой вариант реализации разрешён спекой.
3. `App.tsx`: используется `eslint-disable react-hooks/set-state-in-effect` с обоснованием в комментарии; побочный
   эффект — лишний кадр `LoadingScreen`.
4. Context7 был недоступен (Invalid API key) — сверка API Telegram.Bot велась по XML-документации пакета.
