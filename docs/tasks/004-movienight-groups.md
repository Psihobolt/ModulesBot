# 004 — MovieNight: модуль и группы (API + Mini App)

**Статус:** READY
**Модуль:** movienight (new)   **Бриф:** docs/product/movie-night.md (Этап 1, часть 1 из 2)
**Зависит от:** 003 (`IDeepLinks` для ссылок-приглашений, `telegram.shareUrl`/`showConfirm`).
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
2. Аня жмёт «Пригласить» → выбор чата Telegram → Петя получает ссылку `t.me/<bot>/<app>?startapp=movienight_join_<код>`.
3. Петя открывает ссылку → «Приглашение в группу «Мы с Петей»», участники: Аня → «Вступить» → экран группы
   «1. Аня — владелец, 2. Петя (вы)». Ане в бот: «Петя теперь в группе «Мы с Петей».»
4. Петя открывает ссылку повторно → «Вы уже в этой группе» → «Открыть группу».
5. Ссылка утекла → Аня жмёт «Новая ссылка-приглашение» → старая ссылка перестаёт работать.
6. Аня исключает Васю → Вася получает в бот «Вас исключили из группы «…».», остальные — «Вася больше не в группе «…».»
7. Аня выходит из группы → владельцем становится Петя (следующий по порядку), ему приходит «Теперь вы владелец группы «…».»

## Критерии приёмки

### Модуль
- **AC-1** Given Host (`Program.cs` + `builder.AddModule<MovieNightModule>()`) When старт на пустой БД Then применена
  миграция `MovieNightDbContext` (таблицы `movienight_groups`, `movienight_group_members`, история
  `__ef_migrations_movienight`); `GET /api/movienight/groups` без авторизации → 401 `auth_missing`.
  Проект модуля не ссылается на `Bot.Host` и другие модули (проверка ссылок проекта в тесте).

### Создание и список
- **AC-2** Given пользователь 1 When `POST /api/movienight/groups` `{ "name": "  Мы с Петей  " }` Then 201,
  `GroupDto`: `name = "Мы с Петей"` (trim), `ownerId = 1`, `isOwner = true`,
  `members = [{ userId: 1, position: 1, isMe: true, isOwner: true, displayName из реестра }]`,
  `invite.code` — 10 символов `[A-Za-z0-9]` (от `IInviteCodeGenerator`), `invite.miniAppLink`/`botLink` — из
  `IDeepLinks.MiniApp/BotStart("movienight", "join_<code>")`; `createdAt = IClock.UtcNow`; заголовок `Location`
  `/api/movienight/groups/{id}`.
- **AC-3** Given `name` отсутствует, `null`, пустой/пробельный после trim, длиннее 64 символов (граница: 64 — ок,
  после trim по `string.Length`), содержит управляющие символы (`\n`, `\t`, `\u0000`) When создание или переименование
  Then 400 `validation_failed`, `errors.name = ["Название — от 1 до 64 символов, без переводов строк."]`, изменений нет.
- **AC-4** Given пользователь состоит в 20 группах When создание или вступление Then 409 `movienight_group_limit`,
  `title = "Можно состоять не больше чем в 20 группах."`. Граница: при 19 — ок.
- **AC-5** Given пользователь 1 в группах A (вступил раньше) и B, пользователь 2 — в C When `GET /api/movienight/groups`
  от 1 Then `[B, A]` (по времени вступления пользователя 1, новые сверху), `GroupSummaryDto` с `memberCount` и
  `isOwner`; C не видна. Given групп нет Then `[]`.
- **AC-6** Given коллизия кода (генератор вернул занятый код — в том числе код, ранее выданный любой группе и
  перевыпущенный) When создание или перевыпуск Then код перегенерируется (до 5 попыток, далее 500); выданные коды
  никогда не переиспользуются.

### Просмотр группы
- **AC-7** Given группа с участниками 1 (владелец), 2, 3 (вступили в этом порядке) When `GET /api/movienight/groups/{id}`
  от 2 Then 200, `members` по порядку вступления, `position` 1..N без пропусков, `isMe` только у 2, `isOwner` только у 1,
  `ownerId = 1`, `isOwner = false` на уровне группы; `displayName`/`username` из `IUserDirectory`; нет записи в реестре →
  `displayName = "Участник"`, `username = null`. Ссылка-приглашение видна всем участникам.
- **AC-8** Given пользователь не участник группы, группа не существует или `id` не GUID When любой эндпоинт
  `/groups/{id}/…` Then 404 `movienight_group_not_found` (для не-GUID допустим 404 `not_found` от роутинга);
  существование чужой группы не раскрывается. Проверка членства — раньше проверки владельца.

### Действия владельца
- **AC-9** Given участник, не владелец When `PUT …/name`, `POST …/invite`, `DELETE …/members/{userId}` Then 403
  `movienight_not_owner`, `title = "Это может сделать только владелец группы."`, изменений нет.
- **AC-10** Given владелец When `PUT /api/movienight/groups/{id}/name` `{ "name": "Киноклуб" }` Then 200 `GroupDto`
  с новым именем; валидация — AC-3. Уведомлений нет.
- **AC-11** Given владелец, текущий код `OLDcode123` When `POST /api/movienight/groups/{id}/invite` Then 200 `InviteDto`
  с новым кодом (≠ старому) и ссылками; `GET /invites/OLDcode123` и `POST /invites/OLDcode123/join` → 404
  `movienight_invite_not_found`; `GET /invites/{новый}` → 200. Уведомлений нет.
- **AC-12** Given владелец 1, участники 2, 3 When `DELETE /api/movienight/groups/{id}/members/3` Then 204; 3 больше не
  в группе (`GET` группы от 3 → 404, у 3 группа исчезла из списка), позиции 1, 2 пересчитаны; 3 получает
  «Вас исключили из группы «{name}».»; остальные, кроме владельца-исполнителя (по правилу получателей AC-16), —
  «{displayName} больше не в группе «{name}».»
- **AC-13** Given владелец When `DELETE …/members/{свой id}` Then 400 `movienight_cannot_remove_self`
  (`title = "Чтобы уйти из группы, используйте «Выйти»."`). Given `userId` не участник (или нечисловой) Then 404
  `movienight_member_not_found`.
- **AC-14** Given исключённый участник When открывает ту же ссылку-приглашение Then может вступить снова
  (блок-листа нет; чтобы не пустить — владелец перевыпускает ссылку, AC-11).

### Приглашение и вступление
- **AC-15** Given группа G с участниками Аня, Петя и пользователь 3 не в ней When `GET /api/movienight/invites/{code}`
  от 3 Then 200 `InvitePreviewDto { groupName, memberNames: ["Аня", "Петя"] (по порядку), memberCount: 2,
  alreadyMember: false, groupId: null }`. Given запрашивает участник Then `alreadyMember: true`, `groupId = G.id`.
  Given код не существует, перевыпущен, не по формату `^[A-Za-z0-9]{10}$`, или в группе 0 участников When
  `GET invites/{code}` или `POST invites/{code}/join` Then 404 `movienight_invite_not_found`.
- **AC-16** Given пользователь 3 не в группе When `POST /api/movienight/invites/{code}/join` Then 200
  `JoinResultDto { joined: true, group }`, 3 — последний в `members`, `joinedAt = IClock.UtcNow`, владелец не меняется;
  каждому другому участнику с `CanReceiveMessages = true` через `IMessageSender.SendAsync` уходит
  «{displayName} теперь в группе «{name}».». **Правило получателей (для всех уведомлений модуля):** не шлётся
  исполнителю действия и участникам с `CanReceiveMessages = false`; результат отправки (`Blocked`, `Failed`, исключение
  `IMessageSender`) не влияет на ответ API и логируется; отправка — после фиксации транзакции.
  Given уже участник Then 200 `{ joined: false, group }`, состав не меняется, уведомлений нет.
- **AC-17** Given пользователь 3 вызывает `join` дважды одновременно Then оба 200, участник добавлен один раз,
  уведомления ушли один раз. Given 2 разных пользователя вступают одновременно Then оба в группе, позиции различны.

### Выход и передача владения
- **AC-18** Given группа с 1 (владелец), 2, 3 When `POST /api/movienight/groups/{id}/leave` от 2 Then 204; `members`
  [1, 3]; у 2 группа исчезла; 1 и 3 получают «{displayName} больше не в группе «{name}».»
- **AC-19** Given группа с 1 (владелец), 2, 3 When выходит 1 Then 204; владелец — 2 (следующий по порядку вступления);
  2 получает «{displayName 1} больше не в группе «{name}». Теперь вы владелец группы.»; 3 — обычное
  «… больше не в группе «{name}».» (одно сообщение на получателя). Ссылка-приглашение не меняется.
- **AC-20** Given владелец вышел одновременно с выходом следующего по порядку When обе операции завершены Then владелец —
  оставшийся участник с наименьшим порядком (владелец всегда участник группы, пока она не пуста).
- **AC-21** Given 2 вышел (или исключён) и снова вступил When `GET` группы Then 2 — последний; владельцем не становится.
- **AC-22** Given единственный участник (владелец) выходит Then 204; группа остаётся в БД без участников и владельца
  (`OwnerId = null`), приглашение → 404 (AC-15); уведомлений нет.

### Mini App
- **AC-23** Given модуль зарегистрирован во фронтенде (`modules = [movieNightModule]`, режим одного модуля) When старт
  без `start_param` Then «Мои группы» на `/movienight`; пусто — текст пустого состояния и MainButton «Создать группу»;
  непусто — список (название, «N участник(а/ов)», пометка «вы владелец» при `isOwner`), нажатие → `/movienight/groups/:id`.
- **AC-24** Given экран создания When поле пустое/пробельное или длиннее 64 Then MainButton «Создать» неактивна;
  When валидно и нажата Then `POST`, MainButton в `loading`, успех — `navigate('/movienight/groups/:id', { replace: true })`;
  400 — текст из `errors.name` под полем; 409 — `title` ошибки.
- **AC-25** Given экран группы, пользователь — **не владелец** When загружен Then название; участники по порядку
  «{position}. {displayName}» с «(вы)» и «— владелец»; MainButton «Пригласить» →
  `telegram.shareUrl(invite.miniAppLink, "Присоединяйся к группе «{name}» в Киновечере")`; «Скопировать ссылку» →
  `navigator.clipboard.writeText(miniAppLink)` + «Ссылка скопирована»; «Выйти из группы» → `showConfirm("Выйти из
  группы «{name}»?")` → `true`: `POST leave` и `navigate('/movienight', { replace: true })`; `false`: ничего.
  Кнопок «Переименовать», «Новая ссылка-приглашение», «Исключить» нет. `miniAppLink = null` → «Ссылка-приглашение
  недоступна», MainButton скрыта, «Скопировать» нет. 404 → «Группа не найдена», MainButton «К моим группам».
- **AC-26** Given экран группы, пользователь — **владелец** Then всё из AC-25, плюс: «Переименовать» →
  `/movienight/groups/:id/rename`; «Новая ссылка-приглашение» → `showConfirm("Старая ссылка перестанет работать.
  Создать новую?")` → `true`: `POST invite`, ссылка на экране обновлена, текст «Создана новая ссылка»; у каждого
  участника, кроме себя, кнопка «Исключить» → `showConfirm("Исключить {displayName} из группы?")` → `true`: `DELETE`,
  список обновлён. Для владельца, если он не единственный, текст подтверждения выхода: «Выйти из группы «{name}»?
  Владельцем станет {displayName следующего}.» При 403 на любом действии (владение сменилось) — `title` ошибки и
  перезагрузка группы.
- **AC-27** Given `start_param = "movienight_join_AB12cd34EF"` When старт Then `resolveStartParam("join_AB12cd34EF")` →
  `/movienight/join/AB12cd34EF`, история `[/movienight, /movienight/join/AB12cd34EF]`. Payload не по
  `^join_[A-Za-z0-9]{10}$` → `null` (оболочка: «Ссылка не распознана»).
- **AC-28** Given экран вступления When `alreadyMember = false` Then «Приглашение в группу «{groupName}»», «Участники:
  {memberNames через запятую}», MainButton «Вступить» → `POST join` → `navigate('/movienight/groups/:id', { replace: true })`
  + `haptic.notification('success')`. `alreadyMember = true` → «Вы уже в этой группе», MainButton «Открыть группу».
  404 → «Приглашение недействительно или группа больше не существует.», MainButton «К моим группам».
  409 → `title` ошибки, MainButton «К моим группам».
- **AC-29** Given экран переименования When открыт Then поле с текущим именем, MainButton «Сохранить» (активность — как
  AC-24); успех → `navigate(-1)`; 403 → «Это может сделать только владелец группы.»; 404 → «Группа не найдена».
  Экран открыт не владельцем (по прямому маршруту) → тот же текст 403 без запроса изменения.
- **AC-30** Общие состояния: загрузка — «Загрузка…»; сетевая/5xx — «Не удалось загрузить данные.» и MainButton
  «Повторить». Цвета — только `var(--tg-theme-*)` (проверка 001 AC-29 распространяется на модуль).

## Таблица паритета Mini App ↔ бот
Бот реализуется в 005; здесь фиксируется полный список функций этапа.

| Функция | Mini App (004) | Бот (005) | Кто |
|---|---|---|---|
| Список групп | «Мои группы» (AC-23) | `/groups` | все |
| Создать группу | «Новая группа» (AC-24) | `/newgroup [имя]` | все |
| Участники, порядок, владелец | «Группа» (AC-25) | карточка группы | участник |
| Пригласить (поделиться ссылкой) | «Пригласить», «Скопировать» | «Пригласить» | участник |
| Вступить по ссылке | `startapp=movienight_join_…` (AC-28) | `start=movienight_join_…` | по ссылке |
| Выйти (с передачей владения) | «Выйти из группы» | «Выйти из группы» | участник |
| Переименовать | «Переименование» (AC-29) | «Переименовать» | владелец |
| Перевыпустить ссылку | «Новая ссылка-приглашение» (AC-26) | «Новая ссылка» | владелец |
| Исключить участника | «Исключить» (AC-26) | «Участники» → «Исключить» | владелец |

## Контракт API
Префикс `/api/movienight`, авторизация initData (платформа). Ошибки — ProblemDetails 001 + коды модуля.

| Метод | Маршрут | Request | Response | Ошибки |
|---|---|---|---|---|
| GET | `/groups` | — | `GroupSummaryDto[]` | 401 |
| POST | `/groups` | `CreateGroupRequest` | 201 `GroupDto` | 400, 401, 409 `movienight_group_limit` |
| GET | `/groups/{id:guid}` | — | `GroupDto` | 401, 404 `movienight_group_not_found` |
| PUT | `/groups/{id:guid}/name` | `RenameGroupRequest` | `GroupDto` | 400, 401, 403 `movienight_not_owner`, 404 |
| POST | `/groups/{id:guid}/invite` | — | `InviteDto` | 401, 403, 404 |
| DELETE | `/groups/{id:guid}/members/{userId:long}` | — | 204 | 400 `movienight_cannot_remove_self`, 401, 403, 404 `movienight_group_not_found` / `movienight_member_not_found` |
| POST | `/groups/{id:guid}/leave` | — | 204 | 401, 404 |
| GET | `/invites/{code}` | — | `InvitePreviewDto` | 401, 404 `movienight_invite_not_found` |
| POST | `/invites/{code}/join` | — | `JoinResultDto` | 401, 404, 409 `movienight_group_limit` |

```csharp
// Modules.MovieNight/Api/GroupDtos.cs
public sealed record CreateGroupRequest(string? Name);
public sealed record RenameGroupRequest(string? Name);
public sealed record GroupSummaryDto(Guid Id, string Name, int MemberCount, bool IsOwner);
public sealed record MemberDto(long UserId, string DisplayName, string? Username, int Position, DateTimeOffset JoinedAt,
    bool IsMe, bool IsOwner);
public sealed record InviteDto(string Code, string? MiniAppLink, string? BotLink);
public sealed record GroupDto(Guid Id, string Name, DateTimeOffset CreatedAt, long OwnerId, bool IsOwner,
    IReadOnlyList<MemberDto> Members, InviteDto Invite);
public sealed record InvitePreviewDto(string GroupName, IReadOnlyList<string> MemberNames, int MemberCount, bool AlreadyMember, Guid? GroupId);
public sealed record JoinResultDto(bool Joined, GroupDto Group);
```
```ts
// webapp/src/modules/movie-night/api.ts
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
  getInvite(code: string): Promise<InvitePreviewDto>;
  joinByInvite(code: string): Promise<JoinResultDto>;
};
```
Коды ошибок модуля: `movienight_group_not_found` (404), `movienight_member_not_found` (404),
`movienight_invite_not_found` (404), `movienight_not_owner` (403), `movienight_cannot_remove_self` (400),
`movienight_group_limit` (409), `validation_failed` (400, `errors.name`).

## Контракты модуля (C#) — общие для API (004) и бота (005)

```csharp
// src/Modules/MovieNight/Modules.MovieNight.csproj, namespace Modules.MovieNight.*
public sealed class MovieNightModule : IBotModule { public string Name => "movienight"; /* AddServices, MapApi; MapBot — в 005 */ }

// Domain
public sealed class Group { Guid Id; string Name; string InviteCode; long? OwnerId; long CreatedBy; DateTimeOffset CreatedAt; }
public sealed class GroupMember { Guid GroupId; long UserId; long JoinSeq; DateTimeOffset JoinedAt; }
public sealed class RevokedInviteCode { string Code; Guid GroupId; DateTimeOffset RevokedAt; }   // для неповторяемости кодов
public static class GroupRules
{
    public const int NameMaxLength = 64;
    public const int MaxGroupsPerUser = 20;
    public const int InviteCodeLength = 10;
    public static bool TryNormalizeName(string? raw, out string name);   // trim + правила AC-3
}

// Application
public interface IInviteCodeGenerator { string Next(); }   // реализация — RandomNumberGenerator, base62
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

public interface IGroupService   // scoped; все методы бросают GroupException
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
```
Хранение: `movienight_groups` (уникальный индекс `InviteCode`), `movienight_group_members`
(PK `(GroupId, UserId)`, уникальный `(GroupId, JoinSeq)`; `JoinSeq = max+1` в группе, при конфликте — повтор),
`movienight_revoked_invite_codes` (PK `Code`). Выход/исключение удаляют строку участника; выход владельца и смена
`OwnerId` — в одной транзакции. Время — `IClock`, имена — `IUserDirectory`, уведомления — `IMessageSender`,
ссылки — `IDeepLinks`.

## Контракт бота
Команд в этой спеке нет (005). Сообщения, которые шлёт сервер (без parse mode), получатели — по правилу AC-16:
- вступление: «{displayName} теперь в группе «{name}».»
- выход / исключение (остальным): «{displayName} больше не в группе «{name}».»
- выход владельца (новому владельцу): «{displayName} больше не в группе «{name}». Теперь вы владелец группы.»
- исключённому: «Вас исключили из группы «{name}».»

## Экраны Mini App
Модуль: `webapp/src/modules/movie-night/`, `name = "movienight"`, `title = "Киновечер"`, `basePath = "/movienight"`;
регистрируется в `webapp/src/modules/index.ts` (режим одного модуля → главная = `/movienight`).

| Экран | Маршрут | Содержимое | MainButton | BackButton |
|---|---|---|---|---|
| Мои группы | `/movienight` | «Мои группы»; список (название, «N участник/участника/участников», «вы владелец»); пусто: «У вас пока нет групп. Создайте группу и пригласите друзей по ссылке.»; кнопка «Создать группу» при непустом списке | «Создать группу» → `/movienight/groups/new` | скрыта (главная) |
| Новая группа | `/movienight/groups/new` | «Новая группа», поле «Название» (maxLength 64), ошибка под полем | «Создать» | авто |
| Группа | `/movienight/groups/:id` | Название; «Участники» — «1. Аня (вы) — владелец»…; «Порядок — по времени вступления»; «Скопировать ссылку», «Выйти из группы»; владельцу дополнительно «Переименовать», «Новая ссылка-приглашение», «Исключить» у участников | «Пригласить» → `shareUrl` (скрыта без ссылки) | авто |
| Переименование | `/movienight/groups/:id/rename` | «Название группы», поле с текущим | «Сохранить» | авто |
| Вступление | `/movienight/join/:code` | см. AC-28 | «Вступить» / «Открыть группу» / «К моим группам» | авто |
| Группа не найдена | (состояние экранов группы) | «Группа не найдена» | «К моим группам» → `navigate('/movienight', { replace: true })` | авто |

Склонение: 1, 21, 31… — «участник»; 2–4, 22–24… — «участника»; иначе (в т.ч. 11–14) — «участников».

## Задачи
### Backend
- **BE-1** Проект `src/Modules/MovieNight/Modules.MovieNight.csproj` (ссылается только на `Bot.Platform`), в
  `MultiBot.slnx`; ссылка из `Bot.Host`; `Program.cs`: `builder.AddModule<MovieNightModule>();`. Папки по скиллу. (AC-1)
- **BE-2** `MovieNightDbContext`, префикс `movienight_`, таблицы/индексы из контракта, миграция. (AC-1, AC-6, AC-17)
- **BE-3** Domain `GroupRules`, `IInviteCodeGenerator` (+ реализация на `RandomNumberGenerator`). (AC-2, AC-3, AC-6)
- **BE-4** `IGroupService`: создание, список, просмотр, членство, лимит, вступление (идемпотентность, гонки),
  выход с передачей владения. (AC-2..AC-8, AC-15..AC-22)
- **BE-5** Действия владельца: переименование, перевыпуск кода (старый — в revoked), исключение. (AC-9..AC-14)
- **BE-6** Уведомления по правилу получателей, после commit. (AC-12, AC-16, AC-18, AC-19)
- **BE-7** Endpoints и DTO, маппинг `GroupException` → ProblemDetails, `Location` при 201. (все API-AC)

### Frontend
- **FE-1** Каркас модуля `webapp/src/modules/movie-night/` (`api.ts`, `routes.tsx` с `resolveStartParam`, `index.ts`),
  регистрация в `modules/index.ts`. (AC-23, AC-27)
- **FE-2** «Мои группы» + склонение. (AC-23, AC-30)
- **FE-3** «Новая группа» и «Переименование». (AC-24, AC-29)
- **FE-4** «Группа» для участника: список, «Пригласить», копирование, выход, 404. (AC-25)
- **FE-5** «Группа» для владельца: переименование, новая ссылка, исключение, текст выхода с передачей, 403. (AC-26)
- **FE-6** «Вступление». (AC-28)

### Tester
- **TEST-1** Проект `tests/Modules.MovieNight.Tests` (xUnit, NSubstitute, Mvc.Testing; ссылки на Host, Platform, модуль),
  в `.slnx`; фабрика Host с модулем, `TestInitData`, `FakeClock`, фейки `IInviteCodeGenerator` (заданная
  последовательность), `IMessageSender` (захват), `IDeepLinks`; посев пользователей в реестр.
- **TEST-2** Модуль, создание, список: AC-1..AC-6 (каждый невалидный name, граница 64, лимит 19/20, коллизия кода,
  коллизия с перевыпущенным кодом, сортировка, `isOwner`).
- **TEST-3** Просмотр и доступ: AC-7, AC-8 (чужая, несуществующая, не-GUID, нет в реестре).
- **TEST-4** Действия владельца: AC-9..AC-14 (каждое действие не-владельцем → 403, чужая группа → 404 раньше 403,
  старый код после перевыпуска, исключение себя, не-участника, повторное вступление исключённого, уведомления).
- **TEST-5** Вступление и выход: AC-15..AC-22 (повторное и параллельное вступление, правило получателей, `Blocked`/исключение
  отправителя не ломает ответ, передача владения, параллельный выход владельца и следующего, последний участник).
- **TEST-6** Frontend (`webapp/src/modules/movie-night/__tests__/`): AC-23..AC-30 (моки `telegram`, `fetch`;
  `resolveStartParam`; MainButton активность/loading; `replace`; `showConfirm` true/false; ссылка `null`; набор кнопок
  владелец/не владелец; 403 с перезагрузкой; текст выхода владельца; склонение 1/2/5/11/21/22).

## Вне скоупа
- Удаление группы, ручная передача владения, несколько администраторов, блок-лист исключённых, срок действия ссылки.
- Изменение порядка участников (бриф: в MVP нельзя), текущий ход «По очереди» (Этап 3).
- Коллекции, тайтлы (Этап 2). Функции групп в боте — 005.
- `requestWriteAccess` для получения уведомлений от бота у тех, кто не запускал бота.

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
  криптографического ГСЧ; выданные коды не переиспользуются.
- Исключение не запрещает повторное вступление по действующей ссылке; защита — перевыпуск ссылки владельцем.
- Повторное вступление после выхода/исключения разрешено, участник встаёт в конец порядка и владельцем не становится.
- Последний участник выходит — группа остаётся в БД (для будущей истории) без владельца, приглашение не работает.
- Лимит — 20 групп на пользователя; на число участников лимита нет (бриф).
- Уведомления о вступлении/выходе/исключении шлются ботом; исполнителю действия — не шлются.
- Список групп — по времени вступления текущего пользователя, новые сверху.
- `memberNames` в превью доступны по коду не-участнику: имеющий ссылку и так может вступить.
- Основная ссылка-приглашение — Mini App (`startapp`, бриф); ссылка на бота (`start`) отдаётся рядом для 005.
- Имя из реестра платформы; если пользователя там нет — «Участник».
