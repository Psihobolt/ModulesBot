# NNN — <Название задачи>

**Статус:** DRAFT | NEEDS_CLARIFICATION | READY | DONE
**Модуль:** <movienight | platform | …>   **Бриф:** <docs/product/… или —>

## Исходное описание
<дословно>

## Цель
<1–2 предложения>

## Сценарии
1. …

## Критерии приёмки
- **AC-1** Given … When … Then …

## Контракт API
| Метод | Маршрут | Request | Response | Ошибки |
|---|---|---|---|---|
| POST | /api/movienight/rooms | `CreateRoomRequest` | `RoomDto` | 400, 401 |

```csharp
public sealed record CreateRoomRequest(string Title);
public sealed record RoomDto(Guid Id, string Title, IReadOnlyList<MemberDto> Members);
```
```ts
export type CreateRoomRequest = { title: string };
export type RoomDto = { id: string; title: string; members: MemberDto[] };
```

## Контракт бота
- Команды: …
- Callback data: `<module>:<action>:<payload>`
- Сообщения: …

## Экраны Mini App
| Экран | Содержимое | MainButton | BackButton |
|---|---|---|---|

## Задачи
### Backend
- **BE-1** … (AC-1)
### Frontend
- **FE-1** … (AC-1)
### Tester
- **TEST-1** … (AC-1; негативы: …)

## Вне скоупа
- …

## Открытые вопросы
1. …

## Принятые решения / допущения
- …
