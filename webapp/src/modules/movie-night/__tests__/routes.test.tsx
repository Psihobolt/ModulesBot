import { act, cleanup, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createAppRouter } from '../../../platform/router';
import { tg } from '../../../platform/__tests__/fakeTelegram';
import { ME, expectText, jsonResponse } from '../../../platform/__tests__/testUtils';
import { App } from '../../../App';
import { movieNightModule } from '../index';
import { modules } from '../../index';
import { mockApi } from './helpers';

vi.mock('../../../platform/telegram', async () => ({
  telegram: (await import('../../../platform/__tests__/fakeTelegram')).fakeTelegram,
}));

beforeEach(() => tg.reset());
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('module registration', () => {
  it('AC23: modules/index.ts is exactly [movieNightModule] with basePath /movienight', () => {
    expect(modules).toEqual([movieNightModule]);
    expect(movieNightModule.name).toBe('movienight');
    expect(movieNightModule.basePath).toBe('/movienight');
  });
});

describe('resolveStartParam (AC-27)', () => {
  const resolve = (p: string) => movieNightModule.resolveStartParam!(p);

  it('AC27: join_<10 alnum> resolves to /movienight/join/<code>', () => {
    expect(resolve('join_AB12cd34EF')).toBe('/movienight/join/AB12cd34EF');
  });

  it.each([
    ['9-character code', 'join_AB12cd34E'],
    ['11-character code', 'join_AB12cd34EFG'],
    ['symbol in code', 'join_AB12cd34E-'],
    ['underscore in code', 'join_AB12cd34E_'],
    ['empty code', 'join_'],
    ['other prefix', 'invite_AB12cd34EF'],
    ['no prefix', 'AB12cd34EF'],
    ['empty payload', ''],
    ['path traversal', 'join_../../etc/pa'],
    ['trailing garbage', 'join_AB12cd34EF/x'],
    ['uppercase prefix', 'JOIN_AB12cd34EF'],
  ])('AC27: %s -> null', (_name, payload) => {
    expect(resolve(payload)).toBeNull();
  });
});

describe('history built by the shell (AC-27)', () => {
  it('AC27: start_param movienight_join_<code> gives history [/movienight, /movienight/join/<code>]', async () => {
    const app = createAppRouter({ modules: [movieNightModule], me: ME, startParam: 'movienight_join_AB12cd34EF' });
    expect(app.router.state.location.pathname).toBe('/movienight/join/AB12cd34EF');
    await act(async () => {
      await app.router.navigate(-1);
    });
    expect(app.router.state.location.pathname).toBe('/movienight');
    app.dispose();
  });

  it('AC27: App with valid start_param shows BackButton on the join screen', async () => {
    tg.setStartParam('movienight_join_AB12cd34EF');
    mockApi({
      'GET /platform/me': jsonResponse(ME),
      'GET /movienight/invites/AB12cd34EF': jsonResponse({
        groupName: 'Мы с Петей',
        memberNames: ['Аня'],
        memberCount: 1,
        alreadyMember: false,
        groupId: null,
      }),
    });

    render(<App modules={[movieNightModule]} />);

    await expectText('Приглашение в группу «Мы с Петей»');
    await waitFor(() => expect(tg.backVisible).toBe(true));
  });

  it.each(['movienight_join_short', 'movienight_join_AB12cd34EFG', 'movienight_other_AB12cd34EF'])(
    'AC27: invalid start_param %s shows the notice and stays on /movienight without BackButton',
    async (param) => {
      tg.setStartParam(param);
      mockApi({ 'GET /platform/me': jsonResponse(ME), 'GET /movienight/groups': jsonResponse([]) });

      render(<App modules={[movieNightModule]} />);

      await expectText('Ссылка не распознана — открыта главная.');
      await expectText('Мои группы');
      await waitFor(() => expect(tg.backVisible).toBe(false));
    },
  );

  it('AC27: start_param of an unknown module is not routed to MovieNight', async () => {
    const app = createAppRouter({ modules: [movieNightModule], me: ME, startParam: 'other_join_AB12cd34EF' });
    expect(app.router.state.location.pathname).toBe('/movienight');
    app.dispose();
  });

  it('AC23: start without start_param opens "Мои группы" on /movienight with BackButton hidden', async () => {
    mockApi({ 'GET /platform/me': jsonResponse(ME), 'GET /movienight/groups': jsonResponse([]) });

    render(<App modules={[movieNightModule]} />);

    await expectText('Мои группы');
    await waitFor(() => expect(tg.backVisible).toBe(false));
    await waitFor(() => expect(tg.mainText).toBe('Создать группу'));
  });
});
