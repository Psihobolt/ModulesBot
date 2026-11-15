import { act, cleanup, fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fakeTelegram, tg } from '../../../platform/__tests__/fakeTelegram';
import { deferred, expectText, hasText, jsonResponse } from '../../../platform/__tests__/testUtils';
import { groupsList, lastMain, makeGroup, mockApi, problem, renderAt } from './helpers';

vi.mock('../../../platform/telegram', async () => ({
  telegram: (await import('../../../platform/__tests__/fakeTelegram')).fakeTelegram,
}));

const INVITE_GONE = 'Приглашение недействительно или группа больше не существует.';
const NOT_OWNER = 'Это может сделать только владелец группы.';
const PREVIEW = { groupName: 'Мы с Петей', memberNames: ['Аня', 'Петя'], memberCount: 2, alreadyMember: false, groupId: null };
const JOIN = '/movienight/join/AB12cd34EF';

beforeEach(() => tg.reset());
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('"Вступление" (AC-28)', () => {
  it('AC28: shows group name, members and MainButton "Вступить"', async () => {
    mockApi({ 'GET /movienight/invites/AB12cd34EF': jsonResponse(PREVIEW) });
    renderAt('/movienight', JOIN);

    await expectText('Приглашение в группу «Мы с Петей»');
    expect(hasText('Участники: Аня, Петя')).toBe(true);
    await waitFor(() => expect(tg.mainText).toBe('Вступить'));
  });

  it('AC28: join shows loading, sends one POST on repeated presses, then replaces route with the group and fires success haptic', async () => {
    const pending = deferred<Response>();
    const api = mockApi({
      'GET /movienight/invites/AB12cd34EF': jsonResponse(PREVIEW),
      'POST /movienight/invites/AB12cd34EF/join': () => pending.promise,
      'GET /movienight/groups/g1': new Response('{}', { status: 500 }),
    });
    const router = renderAt('/movienight', JOIN);
    await expectText('Приглашение в группу');

    act(() => tg.pressMain());
    await waitFor(() => expect(lastMain()?.loading).toBe(true));
    act(() => tg.pressMain());
    expect(api.count('POST', '/movienight/invites/AB12cd34EF/join')).toBe(1);
    await act(async () => {
      pending.resolve(jsonResponse({ joined: true, group: makeGroup(1) }));
    });

    await waitFor(() => expect(router.state.location.pathname).toBe('/movienight/groups/g1'));
    expect(router.state.historyAction).toBe('REPLACE');
    expect(fakeTelegram.haptic.notification).toHaveBeenCalledWith('success');
  });

  it('AC28: alreadyMember -> "Вы уже в этой группе" and MainButton "Открыть группу" opens the group with replace, no POST', async () => {
    const api = mockApi({
      'GET /movienight/invites/AB12cd34EF': jsonResponse({ ...PREVIEW, alreadyMember: true, groupId: 'g1' }),
      'GET /movienight/groups/g1': new Response('{}', { status: 500 }),
    });
    const router = renderAt('/movienight', JOIN);

    await expectText('Вы уже в этой группе');
    await waitFor(() => expect(tg.mainText).toBe('Открыть группу'));
    act(() => tg.pressMain());

    await waitFor(() => expect(router.state.location.pathname).toBe('/movienight/groups/g1'));
    expect(router.state.historyAction).toBe('REPLACE');
    expect(api.calls.filter((c) => c.method === 'POST')).toHaveLength(0);
  });

  it('AC28: 404 on preview -> invalid-invite text and MainButton "К моим группам"', async () => {
    mockApi({
      'GET /movienight/invites/AB12cd34EF': problem(404, 'movienight_invite_not_found', INVITE_GONE),
      'GET /movienight/groups': groupsList(),
    });
    const router = renderAt('/movienight', JOIN);

    await expectText(INVITE_GONE);
    await waitFor(() => expect(tg.mainText).toBe('К моим группам'));
    act(() => tg.pressMain());
    await waitFor(() => expect(router.state.location.pathname).toBe('/movienight'));
  });

  it('AC28: 404 on join (invite revoked meanwhile) -> invalid-invite text', async () => {
    mockApi({
      'GET /movienight/invites/AB12cd34EF': jsonResponse(PREVIEW),
      'POST /movienight/invites/AB12cd34EF/join': problem(404, 'movienight_invite_not_found', INVITE_GONE),
    });
    renderAt('/movienight', JOIN);
    await expectText('Приглашение в группу');

    act(() => tg.pressMain());

    await expectText(INVITE_GONE);
    await waitFor(() => expect(tg.mainText).toBe('К моим группам'));
  });

  it('AC28: 409 on join shows the error title and MainButton "К моим группам"', async () => {
    mockApi({
      'GET /movienight/invites/AB12cd34EF': jsonResponse(PREVIEW),
      'POST /movienight/invites/AB12cd34EF/join': problem(409, 'movienight_group_limit', 'Можно состоять не больше чем в 20 группах.'),
    });
    renderAt('/movienight', JOIN);
    await expectText('Приглашение в группу');

    act(() => tg.pressMain());

    await expectText('Можно состоять не больше чем в 20 группах.');
    await waitFor(() => expect(tg.mainText).toBe('К моим группам'));
  });

  it('AC28: code is URL-encoded in the request path', async () => {
    const api = mockApi({ 'GET /movienight/invites/a%2Fb': jsonResponse(PREVIEW) });
    renderAt('/movienight', '/movienight/join/a%2Fb');

    await expectText('Приглашение в группу');
    expect(api.calls[0]?.path).toBe('/movienight/invites/a%2Fb');
  });

  it('AC30: network error on preview shows the load error with "Повторить"', async () => {
    mockApi({
      'GET /movienight/invites/AB12cd34EF': () => {
        throw new TypeError('down');
      },
    });
    renderAt('/movienight', JOIN);

    await expectText('Не удалось загрузить данные.');
    await waitFor(() => expect(tg.mainText).toBe('Повторить'));
  });
});

describe('"Переименование" (AC-29)', () => {
  const RENAME = '/movienight/groups/g1/rename';
  const input = () => screen.getByRole('textbox') as HTMLInputElement;

  it('AC29: loads the group, shows the current name in the field and MainButton "Сохранить"', async () => {
    const api = mockApi({ 'GET /movienight/groups/g1': jsonResponse(makeGroup(0)) });
    renderAt('/movienight/groups/g1', RENAME);

    await waitFor(() => expect(input().value).toBe('Мы с Петей'));
    await waitFor(() => expect(tg.mainText).toBe('Сохранить'));
    expect(api.count('GET', '/movienight/groups/g1')).toBe(1);
  });

  it('AC29: empty, whitespace and >64 disable the button; valid enables it', async () => {
    mockApi({ 'GET /movienight/groups/g1': jsonResponse(makeGroup(0)) });
    renderAt(RENAME);
    await waitFor(() => expect(input().value).toBe('Мы с Петей'));

    for (const [value, enabled] of [['', false], ['   ', false], ['Я'.repeat(65), false], ['Я'.repeat(64), true], ['Ок', true]] as const) {
      fireEvent.change(input(), { target: { value } });
      await waitFor(() => expect(lastMain()?.enabled).toBe(enabled));
    }
  });

  it('AC29: save PUTs { name } as typed, loading + no double request, then navigate(-1)', async () => {
    const pending = deferred<Response>();
    const api = mockApi({
      'GET /movienight/groups/g1': jsonResponse(makeGroup(0)),
      'PUT /movienight/groups/g1/name': () => pending.promise,
    });
    const router = renderAt('/movienight', '/movienight/groups/g1', RENAME);
    await waitFor(() => expect(input().value).toBe('Мы с Петей'));
    fireEvent.change(input(), { target: { value: ' Киноклуб ' } });
    await waitFor(() => expect(lastMain()?.enabled).toBe(true));

    act(() => tg.pressMain());
    await waitFor(() => expect(lastMain()?.loading).toBe(true));
    act(() => tg.pressMain());
    expect(api.count('PUT', '/movienight/groups/g1/name')).toBe(1);
    expect(api.calls.find((c) => c.method === 'PUT')?.body).toEqual({ name: ' Киноклуб ' });
    await act(async () => {
      pending.resolve(jsonResponse(makeGroup(0, { name: 'Киноклуб' })));
    });

    await waitFor(() => expect(router.state.location.pathname).toBe('/movienight/groups/g1'));
  });

  it('AC29: 403 on save shows the owner-only text', async () => {
    mockApi({
      'GET /movienight/groups/g1': jsonResponse(makeGroup(0)),
      'PUT /movienight/groups/g1/name': problem(403, 'movienight_not_owner', NOT_OWNER),
    });
    renderAt(RENAME);
    await waitFor(() => expect(input().value).toBe('Мы с Петей'));
    fireEvent.change(input(), { target: { value: 'Новое' } });
    await waitFor(() => expect(lastMain()?.enabled).toBe(true));

    act(() => tg.pressMain());

    await expectText(NOT_OWNER);
  });

  it('AC29: 404 on save shows "Группа не найдена"', async () => {
    mockApi({
      'GET /movienight/groups/g1': jsonResponse(makeGroup(0)),
      'PUT /movienight/groups/g1/name': problem(404, 'movienight_group_not_found', 'Группа не найдена.'),
      'GET /movienight/groups': groupsList(),
    });
    renderAt(RENAME);
    await waitFor(() => expect(input().value).toBe('Мы с Петей'));
    fireEvent.change(input(), { target: { value: 'Новое' } });
    await waitFor(() => expect(lastMain()?.enabled).toBe(true));

    act(() => tg.pressMain());

    await expectText('Группа не найдена');
  });

  it('AC29: 404 on load shows "Группа не найдена" with MainButton "К моим группам"', async () => {
    mockApi({ 'GET /movienight/groups/g1': problem(404, 'movienight_group_not_found', 'Группа не найдена.') });
    renderAt(RENAME);

    await expectText('Группа не найдена');
    await waitFor(() => expect(tg.mainText).toBe('К моим группам'));
  });

  it('AC29: group loaded with isOwner=false shows the 403 text, hides MainButton and never sends PUT', async () => {
    const api = mockApi({ 'GET /movienight/groups/g1': jsonResponse(makeGroup(1)) });
    renderAt(RENAME);

    await expectText(NOT_OWNER);
    await waitFor(() => expect(tg.mainVisible).toBe(false));
    act(() => tg.pressMain());
    expect(api.calls.filter((c) => c.method === 'PUT')).toHaveLength(0);
  });

  it('AC29: 400 on save shows errors.name[0]', async () => {
    mockApi({
      'GET /movienight/groups/g1': jsonResponse(makeGroup(0)),
      'PUT /movienight/groups/g1/name': problem(400, 'validation_failed', 'Validation failed', {
        name: ['Название — от 1 до 64 символов, без переводов строк.'],
      }),
    });
    renderAt(RENAME);
    await waitFor(() => expect(input().value).toBe('Мы с Петей'));
    fireEvent.change(input(), { target: { value: 'Новое' } });
    await waitFor(() => expect(lastMain()?.enabled).toBe(true));

    act(() => tg.pressMain());

    await expectText('Название — от 1 до 64 символов, без переводов строк.');
  });
});
