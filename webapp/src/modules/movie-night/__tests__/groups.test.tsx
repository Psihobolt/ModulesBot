import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { pressMainWhenReady, tg } from '../../../platform/__tests__/fakeTelegram';
import { deferred, expectText, hasText, jsonResponse } from '../../../platform/__tests__/testUtils';
import { lastMain, mockApi, problem, renderAt } from './helpers';

vi.mock('../../../platform/telegram', async () => ({
  telegram: (await import('../../../platform/__tests__/fakeTelegram')).fakeTelegram,
}));

beforeEach(() => tg.reset());
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const summary = (id: string, name: string, memberCount: number, isOwner = false) => ({ id, name, memberCount, isOwner });

describe('"Мои группы" (AC-23)', () => {
  it('AC23: no groups shows the empty text and MainButton "Создать группу"', async () => {
    mockApi({ 'GET /movienight/groups': jsonResponse([]) });
    renderAt('/movienight');

    await expectText('У вас пока нет групп. Создайте группу и пригласите друзей по ссылке.');
    await waitFor(() => expect(tg.mainText).toBe('Создать группу'));
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });

  it('AC23: groups listed with name, member count and "вы владелец" for owned ones; MainButton stays', async () => {
    mockApi({
      'GET /movienight/groups': jsonResponse([summary('a', 'Киноклуб', 3, true), summary('b', 'Семья', 2)]),
    });
    renderAt('/movienight');

    await expectText('Киноклуб');
    expect(hasText('Семья')).toBe(true);
    expect(hasText('3 участника')).toBe(true);
    expect(hasText('2 участника')).toBe(true);
    expect(document.body.textContent?.match(/вы владелец/g)).toHaveLength(1);
    expect(hasText('У вас пока нет групп')).toBe(false);
    await waitFor(() => expect(tg.mainText).toBe('Создать группу'));
  });

  it('AC23: pressing a group opens /movienight/groups/:id', async () => {
    mockApi({
      'GET /movienight/groups': jsonResponse([summary('abc-1', 'Киноклуб', 3)]),
      'GET /movienight/groups/abc-1': new Response('{}', { status: 500 }),
    });
    const router = renderAt('/movienight');
    await expectText('Киноклуб');

    fireEvent.click(screen.getByText('Киноклуб'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/movienight/groups/abc-1'));
  });

  it('AC23: MainButton "Создать группу" navigates to /movienight/groups/new', async () => {
    mockApi({ 'GET /movienight/groups': jsonResponse([]) });
    const router = renderAt('/movienight');
    await expectText('У вас пока нет групп');

    await pressMainWhenReady();

    await waitFor(() => expect(router.state.location.pathname).toBe('/movienight/groups/new'));
  });

  it.each([
    [1, '1 участник'],
    [2, '2 участника'],
    [3, '3 участника'],
    [4, '4 участника'],
    [5, '5 участников'],
    [11, '11 участников'],
    [12, '12 участников'],
    [13, '13 участников'],
    [14, '14 участников'],
    [20, '20 участников'],
    [21, '21 участник'],
    [22, '22 участника'],
    [25, '25 участников'],
    [101, '101 участник'],
    [111, '111 участников'],
  ])('AC23: plural of %i is "%s"', async (n, text) => {
    mockApi({ 'GET /movienight/groups': jsonResponse([summary('g', 'Группа', n)]) });
    renderAt('/movienight');
    await expectText('Группа');

    expect(within(screen.getByRole('button', { name: /Группа/ })).getByText(text)).toBeInTheDocument();
  });
});

describe('общие состояния (AC-30)', () => {
  it('AC30: while loading shows "Загрузка…"', async () => {
    const pending = deferred<Response>();
    mockApi({ 'GET /movienight/groups': () => pending.promise });
    renderAt('/movienight');

    await expectText('Загрузка…');
    pending.resolve(jsonResponse([]));
    await expectText('Мои группы');
  });

  it('AC30: network error shows "Не удалось загрузить данные." and MainButton "Повторить" that retries', async () => {
    let attempt = 0;
    const api = mockApi({
      'GET /movienight/groups': () => {
        attempt += 1;
        if (attempt === 1) throw new TypeError('network down');
        return jsonResponse([summary('a', 'Киноклуб', 2)]);
      },
    });
    renderAt('/movienight');

    await expectText('Не удалось загрузить данные.');
    await waitFor(() => expect(tg.mainText).toBe('Повторить'));
    await pressMainWhenReady();

    await expectText('Киноклуб');
    expect(api.count('GET', '/movienight/groups')).toBe(2);
  });

  it('AC30: 5xx shows the load error screen', async () => {
    mockApi({ 'GET /movienight/groups': problem(500, 'internal_error', 'Internal') });
    renderAt('/movienight');

    await expectText('Не удалось загрузить данные.');
    await waitFor(() => expect(tg.mainText).toBe('Повторить'));
  });

  it('AC30: load error on the group screen offers retry too', async () => {
    mockApi({ 'GET /movienight/groups/g1': problem(500, 'internal_error', 'Internal') });
    renderAt('/movienight/groups/g1');

    await expectText('Не удалось загрузить данные.');
    await waitFor(() => expect(tg.mainText).toBe('Повторить'));
  });
});

describe('"Новая группа" (AC-24)', () => {
  const input = () => screen.getByLabelText('Название') as HTMLInputElement;
  const mainEnabled = () => lastMain()?.enabled;

  it('AC24: renders title, labelled field with maxLength 64 and disabled MainButton "Создать" when empty', async () => {
    renderAt('/movienight/groups/new');

    await expectText('Новая группа');
    expect(input().maxLength).toBe(64);
    await waitFor(() => expect(tg.mainText).toBe('Создать'));
    await waitFor(() => expect(mainEnabled()).toBe(false));
  });

  it('AC24: whitespace-only name keeps MainButton disabled', async () => {
    renderAt('/movienight/groups/new');

    fireEvent.change(input(), { target: { value: '    ' } });

    await waitFor(() => expect(mainEnabled()).toBe(false));
  });

  it('AC24: name longer than 64 after trim keeps MainButton disabled; 64 enables it', async () => {
    renderAt('/movienight/groups/new');

    fireEvent.change(input(), { target: { value: 'Я'.repeat(65) } });
    await waitFor(() => expect(mainEnabled()).toBe(false));
    fireEvent.change(input(), { target: { value: ' ' + 'Я'.repeat(64) + ' ' } });
    await waitFor(() => expect(mainEnabled()).toBe(true));
  });

  it('AC24: valid name POSTs { name } as typed, shows loading, ignores a second press, then navigates with replace', async () => {
    const pending = deferred<Response>();
    const api = mockApi({
      'POST /movienight/groups': () => pending.promise,
      'GET /movienight/groups/new-id': new Response('{}', { status: 500 }),
    });
    const router = renderAt('/movienight', '/movienight/groups/new');
    fireEvent.change(input(), { target: { value: '  Мы с Петей  ' } });
    await waitFor(() => expect(mainEnabled()).toBe(true));

    await pressMainWhenReady();
    await waitFor(() => expect(lastMain()?.loading).toBe(true));
    await pressMainWhenReady();
    await pressMainWhenReady();
    expect(api.count('POST', '/movienight/groups')).toBe(1);
    expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ name: '  Мы с Петей  ' });

    await act(async () => {
      pending.resolve(jsonResponse({ id: 'new-id' }, 201));
    });

    await waitFor(() => expect(router.state.location.pathname).toBe('/movienight/groups/new-id'));
    expect(router.state.historyAction).toBe('REPLACE');
  });

  it('AC24: 400 shows errors.name[0] under the field and allows retry', async () => {
    mockApi({
      'POST /movienight/groups': problem(400, 'validation_failed', 'Validation failed', {
        name: ['Название — от 1 до 64 символов, без переводов строк.'],
      }),
    });
    renderAt('/movienight/groups/new');
    fireEvent.change(input(), { target: { value: 'a' } });
    await waitFor(() => expect(mainEnabled()).toBe(true));

    await pressMainWhenReady();

    await expectText('Название — от 1 до 64 символов, без переводов строк.');
    await waitFor(() => expect(lastMain()?.loading).toBeFalsy());
  });

  it('AC24: 409 shows the error title', async () => {
    mockApi({
      'POST /movienight/groups': problem(409, 'movienight_group_limit', 'Можно состоять не больше чем в 20 группах.'),
    });
    renderAt('/movienight/groups/new');
    fireEvent.change(input(), { target: { value: 'a' } });
    await waitFor(() => expect(mainEnabled()).toBe(true));

    await pressMainWhenReady();

    await expectText('Можно состоять не больше чем в 20 группах.');
  });

  it.each([
    ['5xx', () => problem(500, 'internal_error', 'Internal')],
    [
      'network',
      () => {
        throw new TypeError('down');
      },
    ],
  ])('AC24: %s error shows "Не удалось сохранить. Попробуйте ещё раз."', async (_n, handler) => {
    mockApi({ 'POST /movienight/groups': handler });
    renderAt('/movienight/groups/new');
    fireEvent.change(input(), { target: { value: 'a' } });
    await waitFor(() => expect(mainEnabled()).toBe(true));

    await pressMainWhenReady();

    await expectText('Не удалось сохранить. Попробуйте ещё раз.');
  });

  it('AC24: after an error the second attempt is sent (button is not stuck in loading)', async () => {
    let n = 0;
    const api = mockApi({
      'POST /movienight/groups': () => (++n === 1 ? problem(500, 'internal_error', 'x') : jsonResponse({ id: 'z' }, 201)),
      'GET /movienight/groups/z': new Response('{}', { status: 500 }),
    });
    const router = renderAt('/movienight/groups/new');
    fireEvent.change(input(), { target: { value: 'a' } });
    await waitFor(() => expect(mainEnabled()).toBe(true));
    await pressMainWhenReady();
    await expectText('Не удалось сохранить');

    await pressMainWhenReady();

    await waitFor(() => expect(router.state.location.pathname).toBe('/movienight/groups/z'));
    expect(api.count('POST', '/movienight/groups')).toBe(2);
  });
});
