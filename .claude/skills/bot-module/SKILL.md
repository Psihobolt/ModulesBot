---
name: bot-module
description: Как устроен и как создаётся модуль платформы бота (backend + Mini App + бот-команды). Используй при создании нового модуля, при добавлении в модуль API/экрана/команды и при ревью на соблюдение модульности.
---

# Модуль платформы

## Контракт модуля (Bot.Platform)
```csharp
public interface IBotModule
{
    string Name { get; }                 // "movienight" — используется в API-префиксе и start_param
    void AddServices(IServiceCollection services, IConfiguration config);
    void MapApi(IEndpointRouteBuilder api);         // api уже с префиксом /api/{Name} и авторизацией initData
    void MapBot(IBotRouteBuilder bot);              // команды, callback'и с префиксом "{Name}:"
}
```
Host: `builder.AddModule<MovieNightModule>();` — больше никаких изменений в Host для нового модуля.

## Структура модуля
```
src/Modules/<Name>/
  Domain/         — сущности и правила, без EF, ASP.NET и Telegram.Bot
  Application/    — сценарии (use cases), интерфейсы репозиториев, IRandomizer/IClock и т.п.
  Api/            — endpoints + DTO (request/response records)
  Telegram/       — команды и callback-обработчики бота
  Persistence/    — свой DbContext, миграции, префикс таблиц "<name>_"
  <Name>Module.cs
webapp/src/modules/<name>/
  api.ts          — типизированный клиент; типы DTO 1:1 с backend
  routes.tsx      — маршруты модуля и обработка start_param "<name>_*"
  screens/ components/
tests/Modules.<Name>.Tests/
webapp/src/modules/<name>/__tests__/
```

## Правила
- Зависимости только внутрь: Api/Telegram/Persistence → Application → Domain.
- Модуль не ссылается на другие модули и на Bot.Host.
- Недетерминированное (время, случайность, id) — через интерфейсы (`IClock`, `IRandomizer`), чтобы тестировать.
- Доступ к данным всегда фильтруется по `TelegramUserId` и членству (пользователь видит только свои группы/комнаты).
- DTO — `record`, без сущностей Domain наружу.
- Контракт API (маршрут, DTO, коды ошибок) фиксируется в спецификации ДО реализации:
  по нему backend, frontend и tester работают параллельно.

## Чек-лист нового модуля
- [ ] `<Name>Module` реализует `IBotModule`, подключён в Host одной строкой
- [ ] свой DbContext и миграция
- [ ] API за авторизацией initData, все запросы проверяют членство
- [ ] фронтенд-маршруты и разбор `start_param`
- [ ] команды бота с префиксом модуля
- [ ] тестовые проекты backend и frontend
