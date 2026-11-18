import { act, cleanup, fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fakeTelegram, pressMainWhenReady, tg } from '../../../platform/__tests__/fakeTelegram';
import { deferred, expectText, hasText, jsonResponse } from '../../../platform/__tests__/testUtils';
import { LINK, groupsList, makeGroup, mockApi, problem, renderAt } from './helpers';

vi.mock('../../../platform/telegram', async () => ({
  telegram: (await import('../../../platform/__tests__/fakeTelegram')).fakeTelegram,
}));

const NOT_OWNER = 'Это может сделать только владелец группы.';
let openSpy: ReturnType<typeof vi.spyOn>;
const href = window.location.href;

beforeEach(() => {
  tg.reset();
  openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);
});
afterEach(() => {
  expect(openSpy).not.toHaveBeenCalled();
  expect(window.location.href).toBe(href);
  openSpy.mockRestore();
  cleanup();
  vi.unstubAllGlobals();
});

const group = (meIndex: number, over = {}) => jsonResponse(makeGroup(meIndex, over));

describe('"Группа" для участника (AC-25)', () => {
  it('AC25: shows name, ordered members with (вы)/владелец marks and the order hint', async () => {
    mockApi({ 'GET /movienight/groups/g1': group(1) });
    renderAt('/movienight/groups/g1');

    await expectText('Мы с Петей');
    await expectText('1. Аня — владелец');
    expect(hasText('2. Петя (вы)')).toBe(true);
    expect(hasText('3. Вася')).toBe(true);
    expect(hasText('Порядок — по времени вступления')).toBe(true);
  });

  it('AC25: owner viewing own group sees "1. Аня (вы) — владелец"', async () => {
    mockApi({ 'GET /movienight/groups/g1': group(0) });
    renderAt('/movienight/groups/g1');

    await expectText('1. Аня (вы) — владелец');
  });

  it('AC25: non-owner has no owner-only buttons', async () => {
    mockApi({ 'GET /movienight/groups/g1': group(1) });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    expect(screen.queryByText('Переименовать')).toBeNull();
    expect(screen.queryByText('Новая ссылка-приглашение')).toBeNull();
    expect(screen.queryByText('Исключить')).toBeNull();
    expect(screen.getByText('Скопировать ссылку')).toBeInTheDocument();
    expect(screen.getByText('Выйти из группы')).toBeInTheDocument();
  });

  it('AC25: MainButton "Пригласить" shares the link through telegram.shareUrl with the group text', async () => {
    mockApi({ 'GET /movienight/groups/g1': group(1) });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');
    await waitFor(() => expect(tg.mainText).toBe('Пригласить'));

    await pressMainWhenReady();

    expect(fakeTelegram.shareUrl).toHaveBeenCalledWith(LINK, 'Присоединяйся к группе «Мы с Петей» в Киновечере');
  });

  it('AC25: "Скопировать ссылку" writes miniAppLink to the clipboard and confirms', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    mockApi({ 'GET /movienight/groups/g1': group(1) });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Скопировать ссылку'));

    await expectText('Ссылка скопирована');
    expect(writeText).toHaveBeenCalledWith(LINK);
  });

  it('AC25: clipboard rejection shows "Не удалось скопировать ссылку"', async () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn(() => Promise.reject(new Error('denied'))) } });
    mockApi({ 'GET /movienight/groups/g1': group(1) });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Скопировать ссылку'));

    await expectText('Не удалось скопировать ссылку');
    expect(hasText('Ссылка скопирована')).toBe(false);
  });

  it('AC25: missing navigator.clipboard shows "Не удалось скопировать ссылку"', async () => {
    vi.stubGlobal('navigator', {});
    mockApi({ 'GET /movienight/groups/g1': group(1) });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Скопировать ссылку'));

    await expectText('Не удалось скопировать ссылку');
  });

  it('AC25: leave confirmed -> POST leave and navigate to /movienight with replace', async () => {
    tg.setConfirmResult(true);
    const api = mockApi({
      'GET /movienight/groups/g1': group(1),
      'POST /movienight/groups/g1/leave': new Response(null, { status: 204 }),
      'GET /movienight/groups': groupsList(),
    });
    const router = renderAt('/movienight', '/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Выйти из группы'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/movienight'));
    expect(router.state.historyAction).toBe('REPLACE');
    expect(fakeTelegram.showConfirm).toHaveBeenCalledWith('Выйти из группы «Мы с Петей»?');
    expect(api.count('POST', '/movienight/groups/g1/leave')).toBe(1);
  });

  it('AC25: leave declined -> no request, stays on the screen', async () => {
    tg.setConfirmResult(false);
    const api = mockApi({ 'GET /movienight/groups/g1': group(1) });
    const router = renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Выйти из группы'));

    await waitFor(() => expect(fakeTelegram.showConfirm).toHaveBeenCalled());
    expect(api.calls.filter((c) => c.method === 'POST')).toHaveLength(0);
    expect(router.state.location.pathname).toBe('/movienight/groups/g1');
  });

  it('AC25: miniAppLink null -> "Ссылка-приглашение недоступна", no MainButton, no copy button', async () => {
    mockApi({
      'GET /movienight/groups/g1': group(1, { invite: { code: 'AB12cd34EF', miniAppLink: null, botLink: null } }),
    });
    renderAt('/movienight/groups/g1');

    await expectText('Ссылка-приглашение недоступна');
    await waitFor(() => expect(tg.mainVisible).toBe(false));
    expect(screen.queryByText('Скопировать ссылку')).toBeNull();
  });

  it('AC25: 404 shows "Группа не найдена" and MainButton "К моим группам" navigates home with replace', async () => {
    mockApi({
      'GET /movienight/groups/g1': problem(404, 'movienight_group_not_found', 'Группа не найдена.'),
      'GET /movienight/groups': groupsList(),
    });
    const router = renderAt('/movienight', '/movienight/groups/g1');

    await expectText('Группа не найдена');
    await waitFor(() => expect(tg.mainText).toBe('К моим группам'));
    await pressMainWhenReady();

    await waitFor(() => expect(router.state.location.pathname).toBe('/movienight'));
    expect(router.state.historyAction).toBe('REPLACE');
  });

  it('AC25: leave while request is in flight does not send a second request on double click', async () => {
    tg.setConfirmResult(true);
    const pending = deferred<Response>();
    const api = mockApi({
      'GET /movienight/groups/g1': group(1),
      'POST /movienight/groups/g1/leave': () => pending.promise,
      'GET /movienight/groups': groupsList(),
    });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Выйти из группы'));
    await waitFor(() => expect(api.count('POST', '/movienight/groups/g1/leave')).toBe(1));
    fireEvent.click(screen.getByText('Выйти из группы'));
    await act(async () => {
      pending.resolve(new Response(null, { status: 204 }));
    });

    expect(api.count('POST', '/movienight/groups/g1/leave')).toBe(1);
  });
});

describe('"Группа" для владельца (AC-26)', () => {
  it('AC26: owner sees rename, new invite, kick buttons for others only, plus the member buttons', async () => {
    mockApi({ 'GET /movienight/groups/g1': group(0) });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    expect(screen.getByText('Переименовать')).toBeInTheDocument();
    expect(screen.getByText('Новая ссылка-приглашение')).toBeInTheDocument();
    expect(screen.getAllByText('Исключить')).toHaveLength(2);
    expect(screen.getByText('Скопировать ссылку')).toBeInTheDocument();
    expect(screen.getByText('Выйти из группы')).toBeInTheDocument();
    await waitFor(() => expect(tg.mainText).toBe('Пригласить'));
  });

  it('AC26: "Переименовать" navigates to /movienight/groups/:id/rename', async () => {
    mockApi({ 'GET /movienight/groups/g1': group(0) });
    const router = renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Переименовать'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/movienight/groups/g1/rename'));
  });

  it('AC26: new invite confirmed -> POST invite, link on screen updated, notice shown', async () => {
    tg.setConfirmResult(true);
    const newLink = 'https://t.me/MovieNightBot/app?startapp=movienight_join_NEWcode123';
    const api = mockApi({
      'GET /movienight/groups/g1': group(0),
      'POST /movienight/groups/g1/invite': jsonResponse({ code: 'NEWcode123', miniAppLink: newLink, botLink: null }),
    });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Новая ссылка-приглашение'));

    await expectText('Создана новая ссылка');
    expect(fakeTelegram.showConfirm).toHaveBeenCalledWith('Старая ссылка перестанет работать. Создать новую?');
    expect(api.count('POST', '/movienight/groups/g1/invite')).toBe(1);
    await pressMainWhenReady();
    expect(fakeTelegram.shareUrl).toHaveBeenCalledWith(newLink, expect.any(String));
  });

  it('AC26: new invite declined -> no request', async () => {
    tg.setConfirmResult(false);
    const api = mockApi({ 'GET /movienight/groups/g1': group(0) });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Новая ссылка-приглашение'));

    await waitFor(() => expect(fakeTelegram.showConfirm).toHaveBeenCalled());
    expect(api.calls.filter((c) => c.method === 'POST')).toHaveLength(0);
  });

  it('AC26: kick confirmed -> DELETE member then group reloaded', async () => {
    tg.setConfirmResult(true);
    let reloaded = false;
    const api = mockApi({
      'GET /movienight/groups/g1': () => {
        const g = makeGroup(0);
        return jsonResponse(reloaded ? { ...g, members: g.members.filter((m) => m.userId !== 2) } : g);
      },
      'DELETE /movienight/groups/g1/members/2': () => {
        reloaded = true;
        return new Response(null, { status: 204 });
      },
    });
    renderAt('/movienight/groups/g1');
    await expectText('2. Петя');

    fireEvent.click(screen.getAllByText('Исключить')[0]!);

    await waitFor(() => expect(hasText('2. Петя')).toBe(false));
    expect(fakeTelegram.showConfirm).toHaveBeenCalledWith('Исключить Петя из группы?');
    expect(api.count('DELETE', '/movienight/groups/g1/members/2')).toBe(1);
    expect(api.count('GET', '/movienight/groups/g1')).toBe(2);
  });

  it('AC26: kick declined -> no DELETE', async () => {
    tg.setConfirmResult(false);
    const api = mockApi({ 'GET /movienight/groups/g1': group(0) });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getAllByText('Исключить')[0]!);

    await waitFor(() => expect(fakeTelegram.showConfirm).toHaveBeenCalled());
    expect(api.calls.filter((c) => c.method === 'DELETE')).toHaveLength(0);
  });

  it('AC26: owner leave confirm names the next owner (position 2)', async () => {
    tg.setConfirmResult(false);
    mockApi({ 'GET /movienight/groups/g1': group(0) });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Выйти из группы'));

    await waitFor(() =>
      expect(fakeTelegram.showConfirm).toHaveBeenCalledWith('Выйти из группы «Мы с Петей»? Владельцем станет Петя.'),
    );
  });

  it('AC26: sole owner leave confirm has no successor sentence', async () => {
    tg.setConfirmResult(false);
    const solo = makeGroup(0);
    mockApi({ 'GET /movienight/groups/g1': jsonResponse({ ...solo, members: [solo.members[0]!] }) });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Выйти из группы'));

    await waitFor(() => expect(fakeTelegram.showConfirm).toHaveBeenCalledWith('Выйти из группы «Мы с Петей»?'));
  });

  it('AC26: non-owner leave confirm has no successor sentence', async () => {
    tg.setConfirmResult(false);
    mockApi({ 'GET /movienight/groups/g1': group(2) });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Выйти из группы'));

    await waitFor(() => expect(fakeTelegram.showConfirm).toHaveBeenCalledWith('Выйти из группы «Мы с Петей»?'));
  });

  it.each([
    ['invite', 'Новая ссылка-приглашение', 'POST /movienight/groups/g1/invite'],
    ['kick', 'Исключить', 'DELETE /movienight/groups/g1/members/2'],
  ])('AC26: 403 on %s shows the error title and reloads the group', async (_n, button, key) => {
    tg.setConfirmResult(true);
    const api = mockApi({
      'GET /movienight/groups/g1': group(0),
      [key]: problem(403, 'movienight_not_owner', NOT_OWNER),
    });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getAllByText(button)[0]!);

    await expectText(NOT_OWNER);
    expect(api.count('GET', '/movienight/groups/g1')).toBe(2);
  });

  it('AC26: 404 movienight_member_not_found on kick reloads the group', async () => {
    tg.setConfirmResult(true);
    const api = mockApi({
      'GET /movienight/groups/g1': group(0),
      'DELETE /movienight/groups/g1/members/2': problem(404, 'movienight_member_not_found', 'Участник не найден.'),
    });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getAllByText('Исключить')[0]!);

    await waitFor(() => expect(api.count('GET', '/movienight/groups/g1')).toBe(2));
    expect(hasText('Группа не найдена')).toBe(false);
  });

  it('AC26: 404 movienight_group_not_found on an action shows "Группа не найдена"', async () => {
    tg.setConfirmResult(true);
    mockApi({
      'GET /movienight/groups/g1': group(0),
      'POST /movienight/groups/g1/invite': problem(404, 'movienight_group_not_found', 'Группа не найдена.'),
    });
    renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Новая ссылка-приглашение'));

    await expectText('Группа не найдена');
    await waitFor(() => expect(tg.mainText).toBe('К моим группам'));
  });
});

const ACTION_FAILED = 'Не удалось выполнить действие. Попробуйте ещё раз.';
const failures: [string, () => Response | Promise<Response>][] = [
  ['network error', () => Promise.reject(new TypeError('Failed to fetch'))],
  ['5xx', () => problem(500, 'internal_error', 'Internal Server Error')],
  ['409', () => problem(409, 'conflict', 'Conflict')],
  ['400', () => problem(400, 'bad_request', 'Bad request')],
];

describe('Неперечисленные ошибки действий: "Не удалось выполнить действие" (AC-25/26)', () => {
  it.each(failures)('AC25: leave fails with %s -> message shown, screen stays, no navigation', async (_n, fail) => {
    tg.setConfirmResult(true);
    const api = mockApi({
      'GET /movienight/groups/g1': group(1),
      'POST /movienight/groups/g1/leave': fail,
      'GET /movienight/groups': groupsList(),
    });
    const router = renderAt('/movienight', '/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Выйти из группы'));

    await expectText(ACTION_FAILED);
    expect(api.count('POST', '/movienight/groups/g1/leave')).toBe(1);
    expect(router.state.location.pathname).toBe('/movienight/groups/g1');
    expect(hasText('Участники')).toBe(true);
    expect(hasText('Группа не найдена')).toBe(false);
    await waitFor(() => expect(tg.mainText).toBe('Пригласить'));
  });

  it.each(failures)('AC26: new invite fails with %s -> message shown, group not reloaded', async (_n, fail) => {
    tg.setConfirmResult(true);
    const api = mockApi({
      'GET /movienight/groups/g1': group(0),
      'POST /movienight/groups/g1/invite': fail,
    });
    const router = renderAt('/movienight/groups/g1');
    await expectText('Участники');

    fireEvent.click(screen.getByText('Новая ссылка-приглашение'));

    await expectText(ACTION_FAILED);
    expect(api.count('POST', '/movienight/groups/g1/invite')).toBe(1);
    expect(api.count('GET', '/movienight/groups/g1')).toBe(1);
    expect(router.state.location.pathname).toBe('/movienight/groups/g1');
    expect(hasText('Создана новая ссылка')).toBe(false);
    await waitFor(() => expect(tg.mainText).toBe('Пригласить'));
  });

  it.each(failures)('AC26: kick fails with %s -> message shown, group not reloaded', async (_n, fail) => {
    tg.setConfirmResult(true);
    const api = mockApi({
      'GET /movienight/groups/g1': group(0),
      'DELETE /movienight/groups/g1/members/2': fail,
    });
    renderAt('/movienight/groups/g1');
    await expectText('2. Петя');

    fireEvent.click(screen.getAllByText('Исключить')[0]!);

    await expectText(ACTION_FAILED);
    expect(api.count('DELETE', '/movienight/groups/g1/members/2')).toBe(1);
    expect(api.count('GET', '/movienight/groups/g1')).toBe(1);
    expect(hasText('2. Петя')).toBe(true);
  });
});
