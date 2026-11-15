# 004 — MovieNight: модуль и группы (API + Mini App)

**Статус:** DONE (critic APPROVED, круг 4; остаточные MINOR — в конце файла)
**Модуль:** movienight (new)   **Бриф:** docs/product/movie-night.md (Этап 1, часть 1 из 2)
**Зависит от:** 001, 002, 003 (DONE): `IDeepLinks`, `IMessageSender`, `IUserDirectory`, `AddModuleDbContext`,
`telegram.shareUrl`/`showConfirm`.
**Следующая часть:** 005 — те же функции в боте (дополнение п. 0 брифа).

## Исходное описание
«Этап 1 из docs/product/movie-night.md». Из брифа: «Группы: создать группу, пригласить по ссылке, вступить,
список участников и их порядок, несколько групп у одного пользователя, выход из группы.»
Группа: «Название, участники, порядок участников (по времени вступления) — нужен для режима «По очереди».
Вступление по ссылке-приглашению `startapp=movienight_join_<код>`. Любое количество участников; пользователь может
состоять в нескольких группах.» Допущения брифа: порядок — по времени вступления, менять нельзя; вышедший участник
не участвует в выборе, история сохраняется. Язык — русский.

## Цель
Создать модуль `movienight` по скиллу bot-module и реализовать группы: создание, приглашение по ссылке, вступление,
список и порядок участников, несколько групп, выход; владелец группы переименовывает её, перевыпускает ссылку и
исключает участников. Сценарии — через Mini App и HTTP API; логика — в `Application`, чтобы 005 (бот) переиспользовал её.

## Сценарии
1. Аня открывает Mini App → «Мои группы» пусто → «Создать группу» → «Мы с Петей» → экран группы:
   «1. Аня (вы) — владелец», кнопка «Пригласить». Аня — владелец.
2. Аня жмёт «Пригласить» → выбор чата Telegram → Петя получает ссылку
   `https://t.me/<bot>/<app>?startapp=movienight_join_<код>` (без `Bot:MiniAppShortName` —
   `https://t.me/<bot>?startapp=movienight_join_<код>`, основное Mini App бота).
3. Петя открывает ссылку → «Приглашение в группу «Мы с Петей»», участники: Аня → «Вступить» → экран группы
   «1. Аня — владелец, 2. Петя (вы)». Ане в бот: «Петя теперь в группе «Мы с Петей».»
4. Петя открывает ссылку повторно → «Вы уже в этой группе» → «Открыть группу».
5. Ссылка утекла → Аня жмёт «Новая ссылка-приглашение» → старая ссылка перестаёт работать.
6. Аня исключает Васю → Вася получает в бот «Вас исключили из группы «…».», остальные — «Вася больше не в группе «…».»
7. Аня выходит из группы → владельцем становится Петя (следующий по порядку), ему приходит
   «Аня больше не в группе «…». Теперь вы владелец группы.»

## Критерии приёмки

### Модуль
- **AC-1** Given реальный `Program.cs` с `builder.AddModule<MovieNightModule>();` When старт на пустой БД Then применена
  миграция `MovieNightDbContext`: в `sqlite_master` есть таблицы `movienight_groups`, `movienight_group_members`,
  `movienight_revoked_invite_codes`, `__ef_migrations_movienight` (≥ 1 строка); таблицы `__EFMigrationsHistory` нет;
  `GET /api/movienight/groups` без заголовка `Authorization` → 401 `auth_missing`.
  `typeof(MovieNightModule).Assembly.GetReferencedAssemblies()` не содержит `Bot.Host` и сборок `Modules.*`
  (кроме себя). Все тесты `Bot.Platform.Tests` остаются зелёными (регрессия: `Program` теперь с модулем).
  - **AC-1a** `MovieNightModule.Name == "movienight"`; модуль не переопределяет `MapBot` (бот — в 005):
    `typeof(MovieNightModule).GetInterfaceMap(typeof(IBotModule))` для `MapBot` указывает на метод, объявленный в
    `IBotModule` (`BotRouteTable` — `internal`, тестам модуля недоступен).

### Создание и список
- **AC-2** Given пользователь 1 When `POST /api/movienight/groups` `{ "name": "  Мы с Петей  " }` Then 201,
  `GroupDto`: `name = "Мы с Петей"` (trim), `ownerId = 1`, `isOwner = true`,
  `members = [{ userId: 1, position: 1, isMe: true, isOwner: true, displayName/username из IUserDirectory, joinedAt = IClock.UtcNow }]`,
  `invite.code` — 10 символов `[A-Za-z0-9]` (от `IInviteCodeGenerator`),
  `invite.miniAppLink = IDeepLinks.MiniApp("movienight", "join_" + code)`,
  `invite.botLink = IDeepLinks.BotStart("movienight", "join_" + code)` (оба `null`, если `Bot:Username` не задан);
  `createdAt = IClock.UtcNow`; заголовок `Location` = `/api/movienight/groups/{id}` (id — GUID формата `D`).
- **AC-3** Given `name` отсутствует, `null`, не строка, пустой/пробельный после trim, длиннее 64 символов после trim
  (по `string.Length`; граница: 64 — ок, 65 — ошибка), или содержит хотя бы один символ с `char.IsControl == true`
  (`\n`, `\r`, `\t`, `\u0000`, `\u007F`) When создание или переименование Then 400 `validation_failed`,
  `title = "Validation failed"`, `errors.name = ["Название — от 1 до 64 символов, без переводов строк."]`, изменений нет.
  Тело запроса отсутствует, пустое или не JSON-объект → 400 `bad_request`, `title = "Request body must be a JSON
  object."` (технический, пользователю не показывается — Mini App всегда шлёт JSON), изменений нет.
- **AC-4** Given пользователь состоит в 20 группах When создание или вступление в новую для него группу Then 409
  `movienight_group_limit`, `title = "Можно состоять не больше чем в 20 группах."`, изменений нет. Граница: при 19 — ок
  (становится 20). Лимит не проверяется, если пользователь уже участник группы, в которую вступает (AC-16 `joined:false`).
- **AC-5** Given пользователь 1 вступил в группу A, затем в B (время `IClock` может совпадать); пользователь 2 — в C
  When `GET /api/movienight/groups` от 1 Then `[B, A]` (по порядку вступления пользователя 1, новые сверху — по
  `JoinSeq` его членства, см. «Хранение»), `GroupSummaryDto` с `memberCount` и `isOwner`; C не видна.
  Given групп нет Then `[]`.
- **AC-6** Given генератор вернул код, занятый действующей группой или ранее перевыпущенный (есть в
  `movienight_revoked_invite_codes`) When создание или перевыпуск Then берётся следующий код генератора (всего до 5
  попыток; все 5 заняты → 500 `internal_error`, изменений нет). Выданные коды никогда не переиспользуются.

### Просмотр группы
- **AC-7** Given группа с участниками 1 (владелец), 2, 3 (вступили в этом порядке) When `GET /api/movienight/groups/{id}`
  от 2 Then 200, `members` по порядку вступления, `position` 1..N без пропусков, `isMe` только у 2, `isOwner` только у 1,
  `ownerId = 1`, `isOwner = false` на уровне группы; `displayName = PlatformUser.DisplayName`, `username` — из
  `IUserDirectory.FindManyAsync`; нет записи в реестре или `DisplayName` пустой/пробельный → `displayName = "Участник"`
  (`username` — из реестра или `null`). Ссылка-приглашение (`invite`) видна всем участникам.
- **AC-8** Given пользователь не участник группы или группа не существует When любой эндпоинт `/groups/{id}/…`
  Then 404 `movienight_group_not_found`, `title = "Группа не найдена."`; существование чужой группы не раскрывается.
  Проверка членства — раньше проверки владельца (не-участник на действии владельца получает 404, не 403).
  Given `id` не GUID (`/groups/abc`) Then 404 `not_found` (fallback платформы `/api/{**path}`, обработчик модуля не вызван).

### Действия владельца
- **AC-9** Given участник, не владелец When `PUT …/name`, `POST …/invite`, `DELETE …/members/{userId}` Then 403
  `movienight_not_owner`, `title = "Это может сделать только владелец группы."`, изменений нет.
- **AC-10** Given владелец When `PUT /api/movienight/groups/{id}/name` `{ "name": "Киноклуб" }` Then 200 `GroupDto`
  с новым именем; валидация — AC-3. Уведомлений нет. Порядок проверок: членство (404 `movienight_group_not_found`) →
  владелец (403 `movienight_not_owner`) → валидация имени (400 `validation_failed`): не-владелец с невалидным именем
  получает 403, не-участник — 404. Тот же порядок обязателен для `IGroupService.RenameAsync` в 005.
- **AC-11** Given владелец, текущий код `OLDcode123` When `POST /api/movienight/groups/{id}/invite` Then 200 `InviteDto`
  с новым кодом (≠ старому) и ссылками для него; `OLDcode123` записан в `movienight_revoked_invite_codes`;
  `GET /invites/OLDcode123` и `POST /invites/OLDcode123/join` → 404 `movienight_invite_not_found`;
  `GET /invites/{новый}` → 200. Уведомлений нет.
- **AC-12** Given владелец 1, участники 2, 3 When `DELETE /api/movienight/groups/{id}/members/3` Then 204; 3 больше не
  в группе (`GET` группы от 3 → 404, у 3 группа исчезла из списка), позиции 1, 2 пересчитаны; 3 получает
  «Вас исключили из группы «{name}».»; 2 получает «{displayName 3} больше не в группе «{name}».»; владелец-исполнитель
  ничего не получает (правило получателей — AC-16).
- **AC-13** Given владелец When `DELETE …/members/{свой id}` Then 400 `movienight_cannot_remove_self`,
  `title = "Чтобы уйти из группы, используйте «Выйти»."`. Given `userId` — число, но не участник группы Then 404
  `movienight_member_not_found`, `title = "Участник не найден."`. Given `userId` нечисловой или вне `long`
  Then 404 `not_found` (роутинг, ограничение `{userId:long}`).
- **AC-14** Given исключённый участник When открывает ту же ссылку-приглашение Then может вступить снова
  (блок-листа нет; чтобы не пустить — владелец перевыпускает ссылку, AC-11).

### Приглашение и вступление
- **AC-15** Given группа G с участниками Аня, Петя и пользователь 3 не в ней When `GET /api/movienight/invites/{code}`
  от 3 Then 200 `InvitePreviewDto { groupName, memberNames: ["Аня", "Петя"] (по порядку, имена по правилу AC-7),
  memberCount: 2, alreadyMember: false, groupId: null }`. Given запрашивает участник Then `alreadyMember: true`,
  `groupId = G.id`. Given код не существует, перевыпущен, не по формату `^[A-Za-z0-9]{10}$` (в т.ч. другой регистр
  существующего кода — сравнение регистрозависимое), или в группе 0 участников When `GET invites/{code}` или
  `POST invites/{code}/join` Then 404 `movienight_invite_not_found`,
  `title = "Приглашение недействительно или группа больше не существует."`.
- **AC-16** Given пользователь 3 не в группе When `POST /api/movienight/invites/{code}/join` Then 200
  `JoinResultDto { joined: true, group }`, 3 — последний в `members`, `joinedAt = IClock.UtcNow`, владелец не меняется;
  каждому другому участнику-получателю через `IMessageSender.SendAsync(userId, new OutgoingMessage(text), …)` уходит
  «{displayName 3} теперь в группе «{name}».». Порядок проверок: код (404) → уже участник (`joined:false`) → лимит (409).
  Given уже участник Then 200 `{ joined: false, group }`, состав не меняется, уведомлений нет.
  **Правило получателей (для всех уведомлений модуля):** получатель — участник группы (или исключённый, AC-12), у которого
  есть запись в `IUserDirectory` с `CanReceiveMessages = true`; исполнитель действия не получает ничего; одно сообщение
  на получателя. Отправка — после фиксации транзакции, последовательно, с `CancellationToken.None` (данные уже изменены,
  закрытие Mini App не должно терять уведомления). Результат `SendResult` со статусом не `Sent` и любое исключение
  `IMessageSender` не влияют на ответ API и на отправку остальным; логируются (Warning, без текста сообщения и токенов).
- **AC-17** Given пользователь 3 вызывает `join` дважды одновременно Then оба ответа 200 (один `joined:true`, другой
  `joined:false`), участник добавлен один раз, уведомления ушли один раз. Given 2 разных пользователя вступают
  одновременно Then оба в группе, позиции различны. Given пользователь с 19 группами одновременно вступает в 2 новые
  Then ровно одна операция успешна, другая — 409 `movienight_group_limit`.

### Выход и передача владения
- **AC-18** Given группа с 1 (владелец), 2, 3 When `POST /api/movienight/groups/{id}/leave` от 2 Then 204; `members`
  [1, 3]; у 2 группа исчезла; 1 и 3 получают «{displayName 2} больше не в группе «{name}».»
- **AC-19** Given группа с 1 (владелец), 2, 3 When выходит 1 Then 204; владелец — 2 (следующий по порядку вступления);
  2 получает «{displayName 1} больше не в группе «{name}». Теперь вы владелец группы.»; 3 — обычное
  «{displayName 1} больше не в группе «{name}».» (одно сообщение на получателя). Ссылка-приглашение не меняется.
- **AC-20** Given владелец вышел одновременно с выходом следующего по порядку When обе операции завершены Then владелец —
  оставшийся участник с наименьшим порядком. **Инвариант:** пока группа не пуста, `OwnerId` — участник группы с
  наименьшим `JoinSeq` (т.е. всегда `position = 1`).
- **AC-21** Given 2 вышел (или исключён) и снова вступил When `GET` группы Then 2 — последний; владельцем не становится.
- **AC-22** Given единственный участник (владелец) выходит Then 204; группа остаётся в БД без участников и владельца
  (`OwnerId = null`), приглашение → 404 (AC-15); уведомлений нет. Повторный `leave` → 404 `movienight_group_not_found`.

### Mini App
- **AC-23** Given `webapp/src/modules/index.ts` = `[movieNightModule]` (режим одного модуля, `homePath = "/movienight"`)
  When старт без `start_param` Then «Мои группы» на `/movienight`, BackButton скрыта; пусто — текст пустого
  состояния и MainButton «Создать группу»; непусто — список (название, «N участник/участника/участников», пометка
  «вы владелец» при `isOwner`) и MainButton «Создать группу»; нажатие на группу → `/movienight/groups/:id`.
- **AC-24** Given экран создания When поле пустое/пробельное или длиннее 64 после trim Then MainButton «Создать»
  неактивна (`enabled: false`); When валидно и нажата Then `POST` с `{ name }` как введено, MainButton
  `loading: true` до ответа (повторное нажатие во время запроса не шлёт второй запрос), успех —
  `navigate('/movienight/groups/' + id, { replace: true })`; 400 — текст из `errors.name[0]` под полем; 409 — `title`
  ошибки; сеть/5xx — «Не удалось сохранить. Попробуйте ещё раз.».
- **AC-25** Given экран группы, пользователь — **не владелец** When загружен Then название; участники по порядку
  «{position}. {displayName}» с « (вы)» у `isMe` и « — владелец» у `isOwner`; «Порядок — по времени вступления»;
  MainButton «Пригласить» → `telegram.shareUrl(invite.miniAppLink, "Присоединяйся к группе «{name}» в Киновечере")`;
  кнопка «Скопировать ссылку» → `navigator.clipboard.writeText(miniAppLink)` → «Ссылка скопирована»; нет
  `navigator.clipboard` или промис отклонён → «Не удалось скопировать ссылку»; «Выйти из группы» →
  `telegram.showConfirm("Выйти из группы «{name}»?")` → `true`: `POST leave` и `navigate('/movienight', { replace: true })`;
  `false`: запроса нет. Кнопок «Переименовать», «Новая ссылка-приглашение», «Исключить» нет.
  `invite.miniAppLink = null` → текст «Ссылка-приглашение недоступна», MainButton скрыта, «Скопировать ссылку» нет.
  404 → «Группа не найдена», MainButton «К моим группам» → `navigate('/movienight', { replace: true })`.
  Модуль не вызывает `window.open` и не меняет `location` (ссылки — только через `telegram`).
  Сеть/5xx (и любая другая не перечисленная ошибка) на действии («Выйти из группы», а у владельца — AC-26) →
  текст «Не удалось выполнить действие. Попробуйте ещё раз.», экран группы остаётся.
- **AC-26** Given экран группы, пользователь — **владелец** Then всё из AC-25, плюс: «Переименовать» →
  `/movienight/groups/:id/rename`; «Новая ссылка-приглашение» → `showConfirm("Старая ссылка перестанет работать.
  Создать новую?")` → `true`: `POST invite`, ссылка на экране обновлена, текст «Создана новая ссылка»; у каждого
  участника, кроме себя, кнопка «Исключить» → `showConfirm("Исключить {displayName} из группы?")` → `true`: `DELETE`,
  затем перезагрузка группы. Для владельца, если он не единственный, текст подтверждения выхода:
  «Выйти из группы «{name}»? Владельцем станет {displayName участника с position = 2}.» При 403 на любом действии
  (владение сменилось) — `title` ошибки и перезагрузка группы; при 404 `movienight_member_not_found` — перезагрузка
  группы; при 404 `movienight_group_not_found` — состояние «Группа не найдена»; сеть/5xx и прочие ошибки —
  «Не удалось выполнить действие. Попробуйте ещё раз.» (как в AC-25).
- **AC-27** Given `start_param = "movienight_join_AB12cd34EF"` When старт Then `resolveStartParam("join_AB12cd34EF")` →
  `"/movienight/join/AB12cd34EF"`, история `[/movienight, /movienight/join/AB12cd34EF]` (BackButton видна).
  Payload не по `^join_[A-Za-z0-9]{10}$` → `null`; оболочка показывает «Ссылка не распознана — открыта главная.»
  на `/movienight` (поведение `router.tsx`, не модуля).
- **AC-28** Given экран вступления When `alreadyMember = false` Then «Приглашение в группу «{groupName}»», «Участники:
  {memberNames через ", "}», MainButton «Вступить» → `POST join` (MainButton `loading`) →
  `navigate('/movienight/groups/' + group.id, { replace: true })` + `telegram.haptic.notification('success')`.
  `alreadyMember = true` → «Вы уже в этой группе», MainButton «Открыть группу» →
  `navigate('/movienight/groups/' + groupId, { replace: true })`. 404 → «Приглашение недействительно или группа больше
  не существует.», MainButton «К моим группам». 409 → `title` ошибки, MainButton «К моим группам».
- **AC-29** Given экран переименования When открыт Then `GET` группы, поле с текущим именем, MainButton «Сохранить»
  (активность и `loading` — как AC-24); успех → `navigate(-1)`; 403 → «Это может сделать только владелец группы.»;
  404 → «Группа не найдена». Группа загружена с `isOwner = false` (открыт по прямому маршруту) → тот же текст 403,
  MainButton скрыта, `PUT` не отправляется.
- **AC-30** Общие состояния: загрузка — «Загрузка…»; ошибка загрузки (сеть/5xx) — «Не удалось загрузить данные.» и
  MainButton «Повторить» (повторяет запрос). Цвета — только `var(--tg-theme-*)`: существующий
  `webapp/src/platform/__tests__/colors.test.ts` сканирует весь `webapp/src` и должен остаться зелёным.

## Таблица паритета Mini App ↔ бот
Бот реализуется в 005; здесь фиксируется полный список функций этапа (решение пользователя — полный паритет).

| Функция | Mini App (004) | Бот (005) | Кто |
|---|---|---|---|
| Список групп | «Мои группы» (AC-23) | `/groups` | все |
| Создать группу | «Новая группа» (AC-24) | `/newgroup [имя]` | все |
| Участники, порядок, владелец | «Группа» (AC-25) | карточка группы | участник |
| Пригласить (поделиться ссылкой) | «Пригласить», «Скопировать» (AC-25) | «Пригласить» | участник |
| Вступить по ссылке | `startapp=movienight_join_…` (AC-27, AC-28) | `start=movienight_join_…` | по ссылке |
| Выйти (с передачей владения) | «Выйти из группы» (AC-25, AC-26) | «Выйти из группы» | участник |
| Переименовать | «Переименование» (AC-29) | «Переименовать» | владелец |
| Перевыпустить ссылку | «Новая ссылка-приглашение» (AC-26) | «Новая ссылка» | владелец |
| Исключить участника | «Исключить» (AC-26) | «Участники» → «Исключить» | владелец |

## Контракт API
Префикс `/api/movienight` (группа создаётся платформой, уже с `RequireAuthorization()` по initData). Ошибки —
ProblemDetails через `Bot.Platform.Api.ApiErrors` (`Problem(status, code, title)`, `Validation(errors)`,
`Forbidden(code, title)`, `NotFound(code, title)`); JSON — camelCase (настройки ASP.NET по умолчанию).
Пользователь — `HttpContext.GetTelegramUserId()`.

| Метод | Маршрут | Request | Response | Ошибки |
|---|---|---|---|---|
| GET | `/groups` | — | `GroupSummaryDto[]` | 401 |
| POST | `/groups` | `CreateGroupRequest` | 201 `GroupDto` + `Location` | 400 `validation_failed`, 401, 409 `movienight_group_limit` |
| GET | `/groups/{id:guid}` | — | `GroupDto` | 401, 404 `movienight_group_not_found` |
| PUT | `/groups/{id:guid}/name` | `RenameGroupRequest` | `GroupDto` | 400, 401, 403 `movienight_not_owner`, 404 |
| POST | `/groups/{id:guid}/invite` | — | `InviteDto` | 401, 403, 404 |
| DELETE | `/groups/{id:guid}/members/{userId:long}` | — | 204 | 400 `movienight_cannot_remove_self`, 401, 403, 404 `movienight_group_not_found` / `movienight_member_not_found` |
| POST | `/groups/{id:guid}/leave` | — | 204 | 401, 404 |
| GET | `/invites/{code}` | — | `InvitePreviewDto` | 401, 404 `movienight_invite_not_found` |
| POST | `/invites/{code}/join` | — | `JoinResultDto` | 401, 404, 409 `movienight_group_limit` |

Маршрут не совпал (не-GUID `id`, нечисловой `userId`) → 404 `not_found` от fallback платформы.

```csharp
// src/Modules/MovieNight/Api/GroupDtos.cs, namespace Modules.MovieNight.Api
public sealed record CreateGroupRequest(string? Name);
public sealed record RenameGroupRequest(string? Name);
public sealed record GroupSummaryDto(Guid Id, string Name, int MemberCount, bool IsOwner);
public sealed record MemberDto(long UserId, string DisplayName, string? Username, int Position, DateTimeOffset JoinedAt,
    bool IsMe, bool IsOwner);
public sealed record InviteDto(string Code, string? MiniAppLink, string? BotLink);
public sealed record GroupDto(Guid Id, string Name, DateTimeOffset CreatedAt, long OwnerId, bool IsOwner,
    IReadOnlyList<MemberDto> Members, InviteDto Invite);
public sealed record InvitePreviewDto(string GroupName, IReadOnlyList<string> MemberNames, int MemberCount,
    bool AlreadyMember, Guid? GroupId);
public sealed record JoinResultDto(bool Joined, GroupDto Group);
```
```ts
// webapp/src/modules/movie-night/api.ts (поверх platform/api.ts: пути без "/api", напр. api.get('/movienight/groups'))
export type CreateGroupRequest = { name: string };
export type RenameGroupRequest = { name: string };
export type GroupSummaryDto = { id: string; name: string; memberCount: number; isOwner: boolean };
export type MemberDto = { userId: number; displayName: string; username: string | null; position: number; joinedAt: string; isMe: boolean; isOwner: boolean };
export type InviteDto = { code: string; miniAppLink: string | null; botLink: string | null };
export type GroupDto = { id: string; name: string; createdAt: string; ownerId: number; isOwner: boolean; members: MemberDto[]; invite: InviteDto };
export type InvitePreviewDto = { groupName: string; memberNames: string[]; memberCount: number; alreadyMember: boolean; groupId: string | null };
export type JoinResultDto = { joined: boolean; group: GroupDto };

export const movieNightApi: {
  listGroups(): Promise<GroupSummaryDto[]>;
  createGroup(req: CreateGroupRequest): Promise<GroupDto>;
  getGroup(id: string): Promise<GroupDto>;
  renameGroup(id: string, req: RenameGroupRequest): Promise<GroupDto>;
  regenerateInvite(id: string): Promise<InviteDto>;
  removeMember(id: string, userId: number): Promise<void>;
  leaveGroup(id: string): Promise<void>;
  getInvite(code: string): Promise<InvitePreviewDto>;      // code — через encodeURIComponent
  joinByInvite(code: string): Promise<JoinResultDto>;
};
```
Ошибки приходят как `ApiError` из `platform/api.ts` (`status`, `code`, `title`, `errors`); сеть — `status 0`,
`code "network_error"`.

| Код | HTTP | `title` (показывается в Mini App) |
|---|---|---|
| `validation_failed` | 400 | `Validation failed` (текст для пользователя — `errors.name[0]`) |
| `bad_request` | 400 | `Request body must be a JSON object.` (технический, в Mini App не показывается) |
| `movienight_cannot_remove_self` | 400 | `Чтобы уйти из группы, используйте «Выйти».` |
| `movienight_not_owner` | 403 | `Это может сделать только владелец группы.` |
| `movienight_group_not_found` | 404 | `Группа не найдена.` |
| `movienight_member_not_found` | 404 | `Участник не найден.` |
| `movienight_invite_not_found` | 404 | `Приглашение недействительно или группа больше не существует.` |
| `movienight_group_limit` | 409 | `Можно состоять не больше чем в 20 группах.` |

## Контракты модуля (C#) — общие для API (004) и бота (005)

```csharp
// src/Modules/MovieNight/Modules.MovieNight.csproj (Microsoft.NET.Sdk + FrameworkReference Microsoft.AspNetCore.App,
// ProjectReference только на Bot.Platform; Microsoft.EntityFrameworkCore.Design — PrivateAssets=all, для миграций)
public sealed class MovieNightModule : IBotModule
{
    public string Name => "movienight";
    public void AddServices(IServiceCollection services, IConfiguration config);
        // services.AddModuleDbContext<MovieNightDbContext>(config, "movienight"); IGroupService (scoped);
        // IInviteCodeGenerator (singleton)
    public void MapApi(IEndpointRouteBuilder api);   // GroupEndpoints.Map(api)
    // MapBot не переопределяется (default interface method) — бот в 005
}

// Domain (без EF/ASP.NET/Telegram.Bot)
public sealed class Group { Guid Id; string Name; string InviteCode; long? OwnerId; long CreatedBy; DateTimeOffset CreatedAt; }
public sealed class GroupMember { long JoinSeq; Guid GroupId; long UserId; DateTimeOffset JoinedAt; }
public sealed class RevokedInviteCode { string Code; Guid GroupId; DateTimeOffset RevokedAt; }   // неповторяемость кодов
public static class GroupRules
{
    public const int NameMaxLength = 64;
    public const int MaxGroupsPerUser = 20;
    public const int InviteCodeLength = 10;
    public const string UnknownMemberName = "Участник";
    public static bool TryNormalizeName(string? raw, out string name);   // trim + правила AC-3
    public static bool IsValidInviteCode(string? code);                  // ^[A-Za-z0-9]{10}$
}

// Application
public interface IInviteCodeGenerator { string Next(); }   // реализация — RandomNumberGenerator.GetString, base62
public enum GroupError { GroupNotFound, MemberNotFound, InviteNotFound, NotOwner, CannotRemoveSelf, GroupLimit, InvalidName }
public sealed class GroupException(GroupError error) : Exception { public GroupError Error { get; } }
public sealed record MemberInfo(long UserId, long JoinSeq, string DisplayName, string? Username, int Position,
    DateTimeOffset JoinedAt, bool IsOwner);           // JoinSeq — стабильный id членства (нужен 005 для callback data)
public sealed record GroupDetails(Guid Id, string Name, DateTimeOffset CreatedAt, long OwnerId, IReadOnlyList<MemberInfo> Members,
    string InviteCode, string? MiniAppLink, string? BotLink);
public sealed record GroupSummary(Guid Id, string Name, int MemberCount, bool IsOwner);
public sealed record InviteInfo(string Code, string? MiniAppLink, string? BotLink);
public sealed record InvitePreview(string GroupName, IReadOnlyList<string> MemberNames, bool AlreadyMember, Guid? GroupId);
public sealed record JoinResult(bool Joined, GroupDetails Group);

public interface IGroupService   // scoped; ошибки бизнес-правил — GroupException; уведомления шлёт сам сервис (AC-16)
{
    Task<IReadOnlyList<GroupSummary>> ListAsync(long userId, CancellationToken ct);
    Task<GroupDetails> CreateAsync(long userId, string? name, CancellationToken ct);
    Task<GroupDetails> GetAsync(long userId, Guid groupId, CancellationToken ct);
    Task<GroupDetails> RenameAsync(long userId, Guid groupId, string? name, CancellationToken ct);       // владелец
    Task<InviteInfo> RegenerateInviteAsync(long userId, Guid groupId, CancellationToken ct);            // владелец
    Task RemoveMemberAsync(long userId, Guid groupId, long memberUserId, CancellationToken ct);          // владелец
    Task LeaveAsync(long userId, Guid groupId, CancellationToken ct);
    Task<InvitePreview> GetInviteAsync(long userId, string code, CancellationToken ct);
    Task<JoinResult> JoinAsync(long userId, string code, CancellationToken ct);
}

// Persistence (internal)
internal sealed class MovieNightDbContext(DbContextOptions<MovieNightDbContext> options) : DbContext(options);
internal sealed class MovieNightDbContextFactory : IDesignTimeDbContextFactory<MovieNightDbContext>;
    // как PlatformDbContextFactory: UseSqlite("Data Source=design-time.db", MigrationsHistoryTable("__ef_migrations_movienight"))
```

### Хранение
- `movienight_groups`: PK `Id` (GUID), `Name`, `InviteCode` (уникальный индекс), `OwnerId` (nullable), `CreatedBy`,
  `CreatedAt`.
- `movienight_group_members`: PK `JoinSeq` — `INTEGER PRIMARY KEY AUTOINCREMENT` (глобально возрастающий, не
  переиспользуется после удаления строк — поэтому именно `AUTOINCREMENT`, не голый rowid); уникальный индекс
  `(GroupId, UserId)`; индекс `UserId`. Порядок участников в группе — `JoinSeq` по возрастанию, `position` = индекс + 1.
  Список групп пользователя — по `JoinSeq` его членства по убыванию. Так порядок не зависит от совпадения времени
  `IClock` и не требует `ORDER BY` по `DateTimeOffset` (провайдер SQLite EF Core его не транслирует).
- `movienight_revoked_invite_codes`: PK `Code`.
- Выход/исключение удаляют строку участника; повторное вступление создаёт новую строку (новый `JoinSeq` → в конец).
- **Конкурентность (SQLite).** Все изменяющие сценарии (create, rename, invite, remove, leave, join) выполняются в одной
  транзакции, которая берёт блокировку записи в начале (SQLite `BEGIN IMMEDIATE`; в Microsoft.Data.Sqlite — не-deferred
  транзакция; конкретный вызов EF Core сверить по документации, см. «Сверка документации»): «прочитать → проверить →
  записать» сериализуется, что даёт AC-17, AC-20 и лимит без гонок. Уникальные индексы — вторая линия защиты:
  конфликт `(GroupId, UserId)` при вступлении → `joined:false`. `SqliteException` с кодом `SQLITE_BUSY` (5),
  `SQLITE_LOCKED` (6) или `SQLITE_CONSTRAINT` (19) (в т.ч. во `InnerException`, напр. `DbUpdateException`) → повтор
  всей операции, всего до 3 попыток, затем исключение (500). Для join повтор после конфликта `(GroupId, UserId)`
  видит пользователя участником → `joined:false` (без уведомлений).
- Время — `IClock`, имена/`CanReceiveMessages` — `IUserDirectory.FindManyAsync` (реестр платформы, отдельный
  `PlatformDbContext`; join между контекстами не делается), уведомления — `IMessageSender.SendAsync`, ссылки — `IDeepLinks`.

## Используемые контракты платформы (фактический код после 003)
| Контракт | Как использует 004 |
|---|---|
| `IBotModule { Name; AddServices(services, config); MapApi(api); MapBot(bot) = default }` | `MovieNightModule`; `MapApi` получает группу `/api/movienight` с авторизацией |
| `PlatformHostExtensions.AddModule<T>()` | `Program.cs`: заменить строку-комментарий на `builder.AddModule<MovieNightModule>();` + `using Modules.MovieNight;` (или `<Using>` в `Bot.Host.csproj`); `ProjectReference` Host → модуль |
| `ModuleDbContextExtensions.AddModuleDbContext<T>(config, "movienight")` | SQLite из `ConnectionStrings:Default`, история `__ef_migrations_movienight`, миграции при старте Host |
| `TelegramHttpContextExtensions.GetTelegramUserId()` | id текущего пользователя |
| `ApiErrors.Problem/Validation/Forbidden/NotFound` | все ошибки (таблица кодов выше) |
| `IClock.UtcNow` (singleton) | `CreatedAt`, `JoinedAt`, `RevokedAt` |
| `IUserDirectory` (scoped): `FindAsync`, `FindManyAsync` → `PlatformUser { DisplayName, Username, CanReceiveMessages }` | имена и получатели |
| `IMessageSender` (singleton): `SendAsync(chatId, OutgoingMessage(text), ct)` → `SendResult(Status, MessageId)` | уведомления; `SendStatus.Blocked` уже помечает пользователя в реестре — модулю делать ничего не нужно |
| `IDeepLinks` (singleton): `MiniApp(module, payload)`, `BotStart(module, payload)` → `string?`; `ArgumentException` при невалидных аргументах | `payload = "join_" + code` — всегда валиден (26 символов в параметре ≤ 64) |

Реестр пользователей пополняется только платформой: `GET /api/platform/me` (Mini App вызывает при каждом старте;
`allows_write_to_pm = true` → `CanReceiveMessages = true`) и любое личное сообщение/колбэк боту
(`CanReceiveMessages = true`). Модуль реестр не пишет.

## Контракт бота
Команд в этой спеке нет (005). Сообщения, которые шлёт сервер (`OutgoingMessage(text)`, без parse mode, без клавиатуры),
получатели — по правилу AC-16:
- вступление: «{displayName} теперь в группе «{name}».»
- выход / исключение (остальным): «{displayName} больше не в группе «{name}».»
- выход владельца (новому владельцу): «{displayName} больше не в группе «{name}». Теперь вы владелец группы.»
- исключённому: «Вас исключили из группы «{name}».»

## Экраны Mini App
Модуль: `webapp/src/modules/movie-night/` (`api.ts`, `routes.tsx`, `index.ts`, `screens/`), экспорт
`movieNightModule: WebModule = { name: "movienight", title: "Киновечер", basePath: "/movienight", routes, resolveStartParam }`;
регистрируется в `webapp/src/modules/index.ts`. Маршруты — дочерние к `basePath` (относительные пути):
`{ index: true }`, `groups/new`, `groups/:id`, `groups/:id/rename`, `join/:code`. Кнопки — `useMainButton`/
`telegram.*` из `platform/`; BackButton модуль не трогает — её ведёт оболочка (`Shell`: скрыта на `homePath`, иначе
по истории).

| Экран | Маршрут | Содержимое | MainButton | BackButton |
|---|---|---|---|---|
| Мои группы | `/movienight` | «Мои группы»; список (название, «N участник/участника/участников», «вы владелец»); пусто: «У вас пока нет групп. Создайте группу и пригласите друзей по ссылке.» | «Создать группу» → `/movienight/groups/new` | скрыта (главная) |
| Новая группа | `/movienight/groups/new` | «Новая группа», поле «Название» (`maxLength` 64), ошибка под полем | «Создать» | оболочка |
| Группа | `/movienight/groups/:id` | Название; «Участники» — «1. Аня (вы) — владелец»…; «Порядок — по времени вступления»; «Скопировать ссылку», «Выйти из группы»; владельцу дополнительно «Переименовать», «Новая ссылка-приглашение», «Исключить» у участников | «Пригласить» → `shareUrl` (скрыта без ссылки) | оболочка |
| Переименование | `/movienight/groups/:id/rename` | «Название группы», поле с текущим | «Сохранить» | оболочка |
| Вступление | `/movienight/join/:code` | см. AC-28 | «Вступить» / «Открыть группу» / «К моим группам» | оболочка |
| Группа не найдена | (состояние экранов группы/переименования) | «Группа не найдена» | «К моим группам» → `navigate('/movienight', { replace: true })` | оболочка |

Склонение: `n % 10 == 1 && n % 100 != 11` — «участник»; `n % 10 ∈ 2..4 && n % 100 ∉ 12..14` — «участника»;
иначе — «участников» (1, 21 — участник; 2, 22 — участника; 5, 11, 12, 14 — участников).
Стили — только `var(--tg-theme-*)` и классы из `app.css`/своего CSS модуля без цветовых литералов.

## Сверка документации (Context7 и запасной путь)
По `CLAUDE.md` каждое неочевидное использование API библиотек сверяется через Context7 (`resolve-library-id` →
`query-docs`, версия из `Directory.Packages.props` / `webapp/package.json`). **Сейчас Context7 недоступен (Invalid API
key).** Сначала попробовать Context7; при ошибке — запасной путь, с отметкой в разделе `DOCS` отчёта
(«Context7 недоступен, сверено по …»):
- **NuGet-пакеты** — XML-документация из кэша NuGet: `~/.nuget/packages/<id в нижнем регистре>/<версия>/lib/<tfm>/*.xml`
  (EF Core 10.0.15: `microsoft.entityframeworkcore`, `microsoft.entityframeworkcore.relational`,
  `microsoft.entityframeworkcore.sqlite.core`, `microsoft.data.sqlite.core`; xUnit 2.9.3; NSubstitute 6.2.0;
  `microsoft.aspnetcore.mvc.testing` 10.0.15).
- **Shared framework ASP.NET Core** (minimal API, `Results`, route constraints) — XML рядом со сборками
  `<dotnet root>/packs/Microsoft.AspNetCore.App.Ref/10.0.*/ref/net10.0/*.xml`; затем learn.microsoft.com.
- **npm** (react-router 8, vitest 5, @testing-library/react 16) — типы и JSDoc в `webapp/node_modules/<pkg>/**/*.d.ts`
  и `README.md`; затем официальные сайты (reactrouter.com, vitest.dev, testing-library.com).
- **Telegram WebApp** — https://core.telegram.org/bots/webapps и локальный `webapp/public/telegram-web-app.js`.

Что обязательно сверить: EF Core SQLite — миграции и `AUTOINCREMENT` для `long`-ключа, уникальные индексы, способ открыть
`BEGIN IMMEDIATE`-транзакцию (`Database.BeginTransactionAsync` / `SqliteConnection.BeginTransaction(deferred:)`),
коды `SqliteException` (`SQLITE_CONSTRAINT` = 19, `SQLITE_BUSY` = 5), ограничения трансляции `DateTimeOffset`;
minimal API — `Results.Created`, `{id:guid}`, `{userId:long}`, привязка тела к `record` с nullable-полями;
`WebApplicationFactory<Program>.ConfigureTestServices`; react-router — относительные дочерние маршруты, `useNavigate`
с `{ replace: true }` и `navigate(-1)`, `useParams`; Vitest — `vi.mock`, `vi.stubGlobal` (для `navigator.clipboard`).

## Задачи
### Backend
- **BE-1** Проект `src/Modules/MovieNight/Modules.MovieNight.csproj` (ссылается только на `Bot.Platform`), в
  `MultiBot.slnx`; `ProjectReference` из `Bot.Host`; `Program.cs`: `builder.AddModule<MovieNightModule>();`. Папки по
  скиллу (`Domain`, `Application`, `Api`, `Persistence`; `Telegram/` — в 005). (AC-1, AC-1a)
- **BE-2** `MovieNightDbContext` через `AddModuleDbContext(config, "movienight")`, префикс `movienight_`, таблицы/индексы
  из «Хранение» (`JoinSeq` — `AUTOINCREMENT`), design-time factory, миграция в `Persistence/Migrations`. (AC-1, AC-5, AC-6, AC-17)
- **BE-3** Domain `GroupRules`, `IInviteCodeGenerator` (+ реализация на `RandomNumberGenerator`). (AC-2, AC-3, AC-6, AC-15)
- **BE-4** `IGroupService`: создание, список, просмотр, членство, лимит, вступление (порядок проверок, идемпотентность,
  гонки через `BEGIN IMMEDIATE`), выход с передачей владения (инвариант AC-20). (AC-2..AC-8, AC-15..AC-22)
- **BE-5** Действия владельца: переименование, перевыпуск кода (старый — в revoked), исключение. (AC-9..AC-14)
- **BE-6** Уведомления по правилу получателей: после commit, `CancellationToken.None`, ошибки/статусы не влияют на
  ответ, лог Warning без текста. (AC-12, AC-16, AC-18, AC-19)
- **BE-7** Endpoints и DTO, маппинг `GroupException` → ProblemDetails (таблица кодов и `title`), `Location` при 201. (все API-AC)
- **BE-8** README: модуль MovieNight, что ссылки-приглашения требуют `Bot:Username` (и желательно `Bot:MiniAppShortName`).

### Frontend
- **FE-1** Каркас модуля `webapp/src/modules/movie-night/` (`api.ts`, `routes.tsx` с `resolveStartParam`, `index.ts`),
  регистрация в `modules/index.ts`. (AC-23, AC-27)
- **FE-2** «Мои группы» + склонение. (AC-23, AC-30)
- **FE-3** «Новая группа» и «Переименование». (AC-24, AC-29)
- **FE-4** «Группа» для участника: список, «Пригласить», копирование (с отказом clipboard), выход, 404. (AC-25)
- **FE-5** «Группа» для владельца: переименование, новая ссылка, исключение, текст выхода с передачей, 403/404. (AC-26)
- **FE-6** «Вступление». (AC-28)

### Tester
- **TEST-1** Проект `tests/Modules.MovieNight.Tests` (Sdk.Web, xUnit, NSubstitute, Mvc.Testing; ссылки на `Bot.Host`,
  `Bot.Platform`, модуль), в `.slnx`. Инфраструктура с учётом фактического кода:
  - `IBotApi`, `BotApiMock`, `BotHarness`, `DemoHost.BotApi` — `internal` (`InternalsVisibleTo` только
    `Bot.Platform.Tests`): в тестах модуля не используются. Публичные хелперы `TestInitData`, `FakeClock`,
    `LogCapture`, `ApiCall`, `TempDir` подключаются ссылкой на исходники
    (`<Compile Include="..\Bot.Platform.Tests\Helpers\X.cs" Link="Helpers\Shared\X.cs" />`), а не ссылкой на тестовый проект.
  - Своя `MovieNightFactory : WebApplicationFactory<Program>` по образцу `PlatformFactory` (реальный `Program` с
    модулем): `Bot:Token = TestInitData.BotToken`, `Bot:WebhookSecret`, временный SQLite (`Pooling=False`),
    `WebApp:DistPath` — несуществующая папка, `Bot:PublicUrl` не задан (→ `BotSetupService` не ходит в Telegram),
    `Bot:Username = "MovieNightBot"`, `Bot:MiniAppShortName = "app"` по умолчанию (вариант без `Username` — для
    ссылок `null`). В `ConfigureTestServices`: `IClock` → `FakeClock`, `IMessageSender` → NSubstitute-захват
    (возвращает `Sent` / настраиваемо `Blocked`/`Failed`/исключение), `IInviteCodeGenerator` → заданная последовательность.
    `IDeepLinks` — реальный (конфигурацией); ожидаемые ссылки в тестах — литералы из AC-13 спеки 003.
  - Посев пользователей в реестр — через публичный API: `GET /api/platform/me` с
    `TestInitData.For(id, firstName: "Аня", lastName: null, allowsWriteToPm: true)` (для «нет в реестре» — не вызывать;
    для `CanReceiveMessages = false` — `allowsWriteToPm: null` у нового пользователя).
  - `Bot.Platform.Tests`: `PlatformFactory` теперь поднимает `Program` с модулем MovieNight — прогнать весь набор;
    тесты, чей смысл «Host без модулей» (например, `ModuleTests.AC4_HostWithoutModules_…`), перевести на
    `DemoHost.StartAsync(includeDemoModule: false)` или переименовать.
- **TEST-2** Модуль, создание, список: AC-1, AC-1a, AC-2..AC-6 (каждый невалидный name, граница 64/65, `\u007F`, тело
  не JSON, лимит 19/20, коллизия кода, коллизия с перевыпущенным кодом, 5 коллизий → 500, сортировка при одинаковом
  времени `FakeClock`, `isOwner`, ссылки `null` без `Bot:Username`, ссылки без `MiniAppShortName`).
- **TEST-3** Просмотр и доступ: AC-7, AC-8 (чужая, несуществующая, не-GUID → `not_found`, нет в реестре, пустой
  `DisplayName`).
- **TEST-4** Действия владельца: AC-9..AC-14 (каждое действие не-владельцем → 403, чужая группа → 404 раньше 403,
  старый код после перевыпуска и запись в revoked, исключение себя, не-участника, нечисловой `userId` → `not_found`,
  повторное вступление исключённого, уведомления исключённому и остальным, не исполнителю).
- **TEST-5** Вступление и выход: AC-15..AC-22 (код другого регистра, повторное и параллельное вступление, параллельное
  вступление на границе лимита, порядок проверок «уже участник» раньше лимита, правило получателей: нет в реестре /
  `CanReceiveMessages = false` / исполнитель; `Blocked`/`Failed`/исключение отправителя не ломает ответ и не мешает
  остальным получателям; передача владения и текст новому владельцу; параллельный выход владельца и следующего;
  последний участник; повторный `leave`).
- **TEST-6** Frontend (`webapp/src/modules/movie-night/__tests__/`): AC-23..AC-30. Моки: `vi.mock` на
  `platform/telegram` с `platform/__tests__/fakeTelegram.ts` (`tg.pressMain`, `tg.setConfirmResult`, `shareUrl`,
  `haptic`), `stubFetch`/`jsonResponse`/`problemResponse` из `platform/__tests__/testUtils.tsx`, `navigator.clipboard`
  через `vi.stubGlobal` (успех, отказ, отсутствие). Проверить: `resolveStartParam` (валидный/невалидный payload, граница
  длины кода 9/10/11), сборку истории через `createAppRouter` с `[movieNightModule]`, MainButton активность/`loading`/
  отсутствие двойного запроса, `replace`, `showConfirm` true/false, ссылка `null`, набор кнопок владелец/не владелец,
  403 с перезагрузкой, текст выхода владельца, склонение 1/2/5/11/12/21/22, `window.open` не вызывается
  (закрывает остаточное замечание 003 №1 для модуля).

## Вне скоупа
- Удаление группы, ручная передача владения, несколько администраторов, блок-лист исключённых, срок действия ссылки.
- Изменение порядка участников (бриф: в MVP нельзя), текущий ход «По очереди» (Этап 3).
- Коллекции, тайтлы (Этап 2). Функции групп в боте — 005.
- `requestWriteAccess` для получения уведомлений от бота у тех, кто не запускал бота.
- Остаточные MINOR 001 вне модуля: `EnsureDirectory` при каждом создании DbContext, формулировка README о пути БД.

## Открытые вопросы
Нет.

## Принятые решения (ответы пользователя)
- **Права — вариант B: создатель-владелец.** Только владелец переименовывает группу, перевыпускает ссылку-приглашение
  и исключает участников. При выходе владельца владение переходит к следующему по порядку вступления.
  (AC-9..AC-14, AC-19..AC-22, AC-26)
- **Ссылка не постоянная:** владелец может перевыпустить её, прежняя ссылка сразу перестаёт работать (AC-11).
- **Паритет — полный:** все функции этапа дублируются в боте (005); таблица паритета — в этой спеке и в 005.
- **Режим бота — личный чат** (inline-режим и групповые чаты не делаются) — касается 005.
- **Порядок:** 003 → 004 → 005, каждая отдельным циклом `/feature`.

## Допущения
- Вступление — только после подтверждения на экране приглашения.
- Ссылкой могут делиться все участники (видна всем), перевыпускает только владелец. Код — 10 символов base62 из
  криптографического ГСЧ; выданные коды не переиспользуются; сравнение кода регистрозависимое.
- Исключение не запрещает повторное вступление по действующей ссылке; защита — перевыпуск ссылки владельцем.
- Повторное вступление после выхода/исключения разрешено, участник встаёт в конец порядка и владельцем не становится.
- Последний участник выходит — группа остаётся в БД (для будущей истории) без владельца, приглашение не работает.
- Лимит — 20 групп на пользователя; на число участников лимита нет (бриф).
- Уведомления о вступлении/выходе/исключении шлются ботом; исполнителю действия — не шлются; пользователям без записи
  в реестре или с `CanReceiveMessages = false` — не шлются.
- Уведомления отправляются синхронно в запросе после commit (группы маленькие; фоновая очередь — вне MVP).
- Список групп — по порядку вступления текущего пользователя, новые сверху.
- `memberNames` в превью доступны по коду не-участнику: имеющий ссылку и так может вступить.
- Основная ссылка-приглашение — Mini App (`startapp`, бриф); ссылка на бота (`start`) отдаётся рядом для 005.
- Имя — `PlatformUser.DisplayName` из реестра платформы; нет записи или имя пустое — «Участник».
- Модуль не пишет в реестр пользователей: имена актуализируются платформой при открытии Mini App или сообщении боту.
- (для 005) `JoinSeq` — глобальный счётчик по всем группам (`AUTOINCREMENT`), а не номер внутри группы. Callback data
  `kickok:{GUID}:{joinSeq}` (7 + 36 + 1 + до 9 цифр = до 53 байт) укладывается в лимит Telegram 64 байта, пока
  `JoinSeq ≤ 999 999 999`; при приближении к пределу формат callback data в 005 нужно пересмотреть (вне MVP).

## Решения по замечаниям critic, круг 1
Реализация признана разумной и зафиксирована как контракт; правок кода не требуется.
1. **Ошибка действия на экране группы** (AC-25, AC-26): сеть/5xx и любые ошибки, не перечисленные явно (403,
   404 `movienight_group_not_found`, 404 `movienight_member_not_found`), → «Не удалось выполнить действие.
   Попробуйте ещё раз.»
2. **Повтор записи** («Хранение»): до 3 попыток при `SQLITE_BUSY`, `SQLITE_LOCKED`, `SQLITE_CONSTRAINT` (с поиском по
   цепочке `InnerException`); конфликт `(GroupId, UserId)` при join после повтора → `joined:false`. Конфликт
   уникального `InviteCode` тоже повторяется (генератор выдаёт новый код; лимит 5 попыток AC-6 — внутри операции).
3. **Порядок проверок при переименовании** (AC-10): членство (404) → владелец (403) → валидация (400). Обязателен и для 005.
4. **Тело не JSON / пустое** (AC-3): 400 `bad_request`, `title = "Request body must be a JSON object."`.
   `title` оставлен английским: он технический (как `Validation failed` платформы), Mini App всегда шлёт
   корректный JSON и этот `title` пользователю не показывает; бот (005) HTTP API не использует. BE-правки нет.

## Изменения ред. 2 (сверка с кодом после 003)
1. `JoinSeq` — глобальный `AUTOINCREMENT`-ключ членства вместо «max+1 в группе с повтором»: порядок не зависит от
   совпадающего времени `FakeClock`, нет `ORDER BY DateTimeOffset` (SQLite), нет гонки за номер. Смысл для 005
   («стабильный id членства») сохранён.
2. Конкурентность: изменяющие сценарии — в `BEGIN IMMEDIATE`-транзакции; повтор при `SQLITE_BUSY`; добавлен AC-17
   на гонку у границы лимита.
3. Нечисловой `userId` и не-GUID `id` → 404 `not_found` (fallback платформы), а не код модуля (AC-8, AC-13).
4. Зафиксированы `title` всех кодов ошибок, `title` валидации (`Validation failed`), 400 на тело не-JSON.
5. Порядок проверок при вступлении: код → уже участник → лимит (AC-4, AC-16).
6. Правило получателей уточнено по `IUserDirectory`/`IMessageSender`: нужна запись в реестре, отправка с
   `CancellationToken.None`, `SendResult` не `Sent` и исключения не мешают остальным; пустой `DisplayName` → «Участник».
7. Ссылки: оба формата `IDeepLinks.MiniApp` (с `MiniAppShortName` и без), `null` без `Bot:Username`.
8. Mini App: точный текст оболочки «Ссылка не распознана — открыта главная.»; маршруты модуля — относительные дочерние;
   BackButton ведёт оболочка; отказ/отсутствие `navigator.clipboard`; защита от двойного нажатия MainButton; 404
   `movienight_member_not_found` при исключении → перезагрузка; запрет `window.open`.
9. Инвариант владельца: всегда участник с `position = 1` (AC-20); «следующий» в тексте выхода — `position = 2`.
10. Тестовая инфраструктура приведена к коду: `internal` `IBotApi`/`BotApiMock` недоступны тестам модуля — подмена
    `IMessageSender`; хелперы подключаются ссылкой на исходники; фабрика над реальным `Program`; посев реестра через
    `/api/platform/me`; регрессия `Bot.Platform.Tests` после подключения модуля к `Program`.
11. AC-1 конкретизирован (таблицы, отсутствие `__EFMigrationsHistory`, проверка ссылок сборки), добавлен AC-1a.
12. Раздел «Сверка документации» с запасным путём на время недоступности Context7.

## Остаточные замечания (MINOR, не блокируют)
Critic: `APPROVED`, круг 4 (дополнительный круг после 3-го разрешён пользователем для дописывания тестов).
1. Логика сценариев (проверки владельца и лимита, передача владения, правило получателей, уведомления) лежит в
   `Persistence/EfGroupService`, в `Application` — только интерфейс. Чище перенести сценарии в `Application`, оставив
   в `Persistence` доступ к данным (учесть при 005 или отдельным рефакторингом).
2. Тело запроса разбирается до проверки членства: не-участник с не-JSON телом на `PUT /groups/{id}/name` получает
   400 `bad_request` вместо 404. Утечки данных нет, но AC-8 требует «любой эндпоинт → 404».
3. Лишнее `NameEquals("name")` в `GroupEndpoints.ReadRequestAsync`.
4. `movieNightApi` в webapp не аннотирован типом контракта.
5. `LoadDetailsAsync` после commit выполняется вне транзакции — узкое окно ответа 404 после успешной операции
   (при параллельном выходе/исключении).
6. Кнопки «Переименовать» и «Скопировать ссылку» без `disabled={busy}`.
7. Context7 был недоступен (Invalid API key) — сверка API библиотек выполнена по XML-документации пакетов NuGet.
