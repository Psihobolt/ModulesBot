import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RouterProvider } from 'react-router/dom';
import { App } from '../../App';
import { createAppRouter, type WebModule } from '../router';
import { pressBackWhenReady, pressMainWhenReady, tg } from './fakeTelegram';
import {
  ME,
  deferred,
  demoModule,
  expectText,
  hasText,
  headerOf,
  jsonResponse,
  otherModule,
  problemResponse,
  stubFetch,
} from './testUtils';

vi.mock('../telegram', async () => ({ telegram: (await import('./fakeTelegram')).fakeTelegram }));

const SESSION_EXPIRED = 'Сессия устарела. Закройте приложение и откройте его снова.';
const LOAD_ERROR = 'Не удалось загрузить данные.';
const NOTICE = 'Ссылка не распознана — открыта главная.';
const EMPTY = 'Здесь пока ничего нет — разделы появятся скоро.';

const twoModules: WebModule[] = [demoModule, otherModule];

function okMe() {
  return stubFetch(() => jsonResponse(ME));
}

beforeEach(() => {
  tg.reset();
  delete document.documentElement.dataset.colorScheme;
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('outside Telegram (AC-20)', () => {
  it('AC20: isAvailable() === false shows "Откройте приложение в Telegram" and does not call the API', async () => {
    tg.setAvailable(false);
    const fetchMock = okMe();

    render(<App modules={twoModules} />);

    await expectText('Откройте приложение в Telegram');
    expect(fetchMock).not.toHaveBeenCalled();
    await waitFor(() => expect(tg.mainVisible).toBe(false));
  });

  it('AC20: outside Telegram does not call ready()/expand()', async () => {
    tg.setAvailable(false);
    okMe();
    render(<App modules={[]} />);
    await expectText('Откройте приложение в Telegram');
    expect(vi.mocked(await import('../telegram').then((m) => m.telegram.ready))).not.toHaveBeenCalled();
  });
});

describe('startup in Telegram (AC-21)', () => {
  it('AC21: shows "Загрузка…" while the request is pending, then the greeting from the server firstName', async () => {
    const pending = deferred<Response>();
    stubFetch(() => pending.promise as unknown as Response);
    tg.setInitData('init=1&hash=zz');

    render(<App modules={twoModules} />);

    await expectText('Загрузка…');
    expect(hasText('Привет')).toBe(false);

    await act(async () => {
      pending.resolve(jsonResponse(ME));
      await pending.promise;
    });

    await expectText('Привет, Иван!');
    expect(hasText('Загрузка…')).toBe(false);
  });

  it('AC21: ready() and expand() are called exactly once each', async () => {
    okMe();
    const { telegram } = await import('../telegram');

    render(<App modules={twoModules} />);
    await expectText('Привет, Иван!');

    expect(telegram.ready).toHaveBeenCalledTimes(1);
    expect(telegram.expand).toHaveBeenCalledTimes(1);
  });

  it('AC21: ready()/expand() stay at one call even after a retry', async () => {
    let calls = 0;
    stubFetch(() => (++calls === 1 ? problemResponse(500, 'internal_error') : jsonResponse(ME)));
    const { telegram } = await import('../telegram');

    render(<App modules={twoModules} />);
    await expectText(LOAD_ERROR);
    await pressMainWhenReady('Повторить');
    await expectText('Привет, Иван!');

    expect(telegram.ready).toHaveBeenCalledTimes(1);
    expect(telegram.expand).toHaveBeenCalledTimes(1);
  });

  it('AC21: GET /api/platform/me with Authorization: tma <initData>', async () => {
    const fetchMock = okMe();
    tg.setInitData('query_id=AAH&auth_date=1&hash=abc');

    render(<App modules={twoModules} />);
    await expectText('Привет, Иван!');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe('/api/platform/me');
    expect((init?.method ?? 'GET').toUpperCase()).toBe('GET');
    expect(headerOf(init, 'Authorization')).toBe('tma query_id=AAH&auth_date=1&hash=abc');
  });

  it('AC21: greeting uses firstName from the server response', async () => {
    stubFetch(() => jsonResponse({ ...ME, firstName: 'Server-Name' }));
    render(<App modules={twoModules} />);
    await expectText('Привет, Server-Name!');
  });

  it('AC21: home screen has MainButton and BackButton hidden', async () => {
    okMe();
    render(<App modules={twoModules} />);
    await expectText('Привет, Иван!');
    await waitFor(() => expect(tg.mainVisible).toBe(false));
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });
});

describe('home screen (AC-22)', () => {
  it('AC22: no modules shows the empty placeholder', async () => {
    okMe();
    render(<App modules={[]} />);
    await expectText('Привет, Иван!');
    await expectText(EMPTY);
  });

  it('AC22: two modules list their titles and no placeholder', async () => {
    okMe();
    render(<App modules={twoModules} />);
    await expectText('Привет, Иван!');
    expect(hasText('Демо')).toBe(true);
    expect(hasText('Другой')).toBe(true);
    expect(hasText(EMPTY)).toBe(false);
  });

  it('AC22: clicking a module title opens its basePath', async () => {
    okMe();
    render(<App modules={twoModules} />);
    await expectText('Привет, Иван!');

    fireEvent.click(screen.getByText('Демо'));

    await expectText('Главная демо-модуля');
    expect(hasText('Привет, Иван!')).toBe(false);
  });

  it('AC22: exactly one module and no start_param opens its basePath right away', async () => {
    okMe();
    render(<App modules={[demoModule]} />);
    await expectText('Главная демо-модуля');
    expect(hasText(EMPTY)).toBe(false);
  });

  it('AC22: single module opened as "home" has no BackButton', async () => {
    okMe();
    render(<App modules={[demoModule]} />);
    await expectText('Главная демо-модуля');
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });
});

describe('errors (AC-23)', () => {
  it.each(['auth_expired', 'auth_invalid', 'auth_missing'])(
    'AC23: 401 %s shows "Сессия устарела…" without MainButton',
    async (code) => {
      stubFetch(() => problemResponse(401, code));
      render(<App modules={twoModules} />);

      await expectText(SESSION_EXPIRED);
      expect(tg.mainVisible).toBe(false);
      expect(tg.mainHandlerCount).toBe(0);
      expect(hasText('Привет')).toBe(false);
    },
  );

  it('AC23: 401 session-expired does not offer retry or call API again', async () => {
    const fetchMock = stubFetch(() => problemResponse(401, 'auth_expired'));
    render(<App modules={twoModules} />);
    await expectText(SESSION_EXPIRED);
    act(() => tg.pressMain()); // кнопки нет намеренно: нажатие не должно ничего вызвать
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('AC23: network error shows "Не удалось загрузить данные." and MainButton "Повторить"', async () => {
    stubFetch(() => {
      throw new TypeError('Failed to fetch');
    });
    render(<App modules={twoModules} />);

    await expectText(LOAD_ERROR);
    await waitFor(() => expect(tg.mainVisible).toBe(true));
    await waitFor(() => expect(tg.mainText).toBe('Повторить'));
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });

  it('AC23: 5xx shows the load-error screen with "Повторить"', async () => {
    stubFetch(() => problemResponse(503, 'internal_error'));
    render(<App modules={twoModules} />);

    await expectText(LOAD_ERROR);
    await waitFor(() => expect(tg.mainText).toBe('Повторить'));
  });

  it('AC23: 5xx with HTML body (no ProblemDetails) is also a load error', async () => {
    stubFetch(() => new Response('<html>502</html>', { status: 502, headers: { 'Content-Type': 'text/html' } }));
    render(<App modules={twoModules} />);
    await expectText(LOAD_ERROR);
    await waitFor(() => expect(tg.mainText).toBe('Повторить'));
  });

  it('AC23: pressing "Повторить" repeats the request; on success shows home and hides MainButton', async () => {
    let calls = 0;
    const fetchMock = stubFetch(() => {
      if (++calls === 1) throw new TypeError('offline');
      return jsonResponse(ME);
    });
    render(<App modules={twoModules} />);
    await expectText(LOAD_ERROR);

    await pressMainWhenReady('Повторить');

    await expectText('Привет, Иван!');
    expect(fetchMock).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(tg.mainVisible).toBe(false));
    await waitFor(() => expect(tg.mainHandlerCount).toBe(0));
    expect(hasText(LOAD_ERROR)).toBe(false);
  });

  it('AC23: retry that fails again stays on the error screen with MainButton', async () => {
    const fetchMock = stubFetch(() => problemResponse(500, 'internal_error'));
    render(<App modules={twoModules} />);
    await expectText(LOAD_ERROR);

    await pressMainWhenReady('Повторить');
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));

    await expectText(LOAD_ERROR);
    await waitFor(() => expect(tg.mainText).toBe('Повторить'));
  });

  it('AC23: retry that returns 401 switches to the session-expired screen', async () => {
    let calls = 0;
    stubFetch(() => (++calls === 1 ? problemResponse(500, 'internal_error') : problemResponse(401, 'auth_expired')));
    render(<App modules={twoModules} />);
    await expectText(LOAD_ERROR);

    await pressMainWhenReady('Повторить');

    await expectText(SESSION_EXPIRED);
    await waitFor(() => expect(tg.mainVisible).toBe(false));
  });
});

describe('start_param routing (AC-24)', () => {
  it('AC24: "demo_abc" opens /demo/items/abc and BackButton leads home', async () => {
    okMe();
    tg.setStartParam('demo_abc');
    render(<App modules={twoModules} />);

    await expectText('Элемент abc');
    await waitFor(() => expect(tg.backVisible).toBe(true));

    await pressBackWhenReady();

    await expectText('Привет, Иван!');
    expect(hasText('Элемент abc')).toBe(false);
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });

  it('AC24: valid start_param wins over single-module "home" shortcut', async () => {
    okMe();
    tg.setStartParam('demo_abc');
    render(<App modules={[demoModule]} />);

    await expectText('Элемент abc');
    await waitFor(() => expect(tg.backVisible).toBe(true));
  });

  it.each([
    ['"demo" (no payload)', 'demo'],
    ['"Demo_x" (uppercase)', 'Demo_x'],
    ['"demo_" (empty payload)', 'demo_'],
    ['"demo_a b" (space)', 'demo_a b'],
    ['longer than 64', 'demo_' + 'a'.repeat(60)],
    ['unregistered module "ghost_abc"', 'ghost_abc'],
    ['resolveStartParam returned null', 'demo_bad'],
  ])('AC24: start_param %s -> home with notice', async (_name, param) => {
    okMe();
    tg.setStartParam(param);
    render(<App modules={twoModules} />);

    await expectText('Привет, Иван!');
    await expectText(NOTICE);
    expect(hasText('Элемент')).toBe(false);
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });

  it('AC24: no start_param -> no notice', async () => {
    okMe();
    render(<App modules={twoModules} />);
    await expectText('Привет, Иван!');
    expect(hasText(NOTICE)).toBe(false);
  });

  it('AC24: unrecognized start_param with a single module shows the notice over homePath (= basePath)', async () => {
    okMe();
    tg.setStartParam('ghost_abc');
    render(<App modules={[demoModule]} />);

    await expectText(NOTICE);
    await expectText('Главная демо-модуля');
  });
});

describe('navigation (AC-26, AC-27)', () => {
  it('AC26: nested route shows BackButton; pressing it goes one step back in history', async () => {
    okMe();
    render(<App modules={twoModules} />);
    await expectText('Привет, Иван!');
    await waitFor(() => expect(tg.backVisible).toBe(false));

    fireEvent.click(screen.getByText('Другой'));
    await expectText('Экран другого модуля');
    await waitFor(() => expect(tg.backVisible).toBe(true));

    await pressBackWhenReady();

    await expectText('Привет, Иван!');
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });

  it('AC26: BackButton handlers do not accumulate across navigation', async () => {
    okMe();
    render(<App modules={twoModules} />);
    await expectText('Привет, Иван!');

    fireEvent.click(screen.getByText('Демо'));
    await expectText('Главная демо-модуля');
    await pressBackWhenReady();
    await expectText('Привет, Иван!');
    fireEvent.click(screen.getByText('Другой'));
    await expectText('Экран другого модуля');

    await waitFor(() => expect(tg.backHandlerCount).toBe(1));
  });

  it('AC27: unknown in-app route shows "Страница не найдена" with MainButton "На главную"', async () => {
    okMe();
    const ghost: WebModule = { ...demoModule, resolveStartParam: () => '/no/such/page' };
    tg.setStartParam('demo_x');
    render(<App modules={[ghost, otherModule]} />);

    await expectText('Страница не найдена');
    await waitFor(() => expect(tg.mainVisible).toBe(true));
    await waitFor(() => expect(tg.mainText).toBe('На главную'));
  });

  it('AC27: pressing "На главную" opens home and hides the MainButton', async () => {
    okMe();
    const ghost: WebModule = { ...demoModule, resolveStartParam: () => '/no/such/page' };
    tg.setStartParam('demo_x');
    render(<App modules={[ghost, otherModule]} />);
    await expectText('Страница не найдена');

    await pressMainWhenReady();

    await expectText('Привет, Иван!');
    await waitFor(() => expect(tg.mainVisible).toBe(false));
    expect(hasText('Страница не найдена')).toBe(false);
  });
});

describe('theme (AC-29)', () => {
  it('AC29: colorScheme "dark" at startup sets data-color-scheme="dark" on <html>', async () => {
    tg.setScheme('dark');
    okMe();
    render(<App modules={twoModules} />);

    await waitFor(() => expect(document.documentElement.dataset.colorScheme).toBe('dark'));
  });

  it('AC29: themeChanged to "light" updates the attribute', async () => {
    tg.setScheme('dark');
    okMe();
    render(<App modules={twoModules} />);
    await waitFor(() => expect(document.documentElement.dataset.colorScheme).toBe('dark'));

    act(() => tg.emitThemeChanged('light'));

    await waitFor(() => expect(document.documentElement.dataset.colorScheme).toBe('light'));
  });

  it('AC29: multiple theme changes follow the latest scheme', async () => {
    okMe();
    render(<App modules={twoModules} />);
    await expectText('Привет, Иван!');

    act(() => tg.emitThemeChanged('dark'));
    act(() => tg.emitThemeChanged('light'));
    act(() => tg.emitThemeChanged('dark'));

    await waitFor(() => expect(document.documentElement.dataset.colorScheme).toBe('dark'));
  });
});

/** Рендерит оболочку с прямым доступом к роутеру (для проверки PUSH/REPLACE и pathname). */
function renderRouter(modules: WebModule[], startParam?: string) {
  const { router } = createAppRouter({ modules, me: ME, startParam });
  render(<RouterProvider router={router} />);
  return router;
}

const ghostModule: WebModule = { ...demoModule, resolveStartParam: () => '/no/such/page' };

describe('home = homePath, Back/Main buttons (AC-26, AC-27)', () => {
  it('AC26: PUSH navigation to the home path hides BackButton even though history has previous entries', async () => {
    const router = renderRouter(twoModules);
    await expectText('Привет, Иван!');

    await act(async () => { await router.navigate('/demo'); });
    await expectText('Главная демо-модуля');
    await waitFor(() => expect(tg.backVisible).toBe(true));

    await act(async () => { await router.navigate('/'); });

    await expectText('Привет, Иван!');
    expect(router.state.historyAction).toBe('PUSH');
    await waitFor(() => expect(tg.backVisible).toBe(false));
    await waitFor(() => expect(tg.backHandlerCount).toBe(0));
  });

  it('AC26: after Demo -> Other (PUSH) -> home (PUSH) BackButton stays hidden on home and visible again on a module', async () => {
    const router = renderRouter(twoModules);
    await expectText('Привет, Иван!');
    await act(async () => { await router.navigate('/demo'); });
    await act(async () => { await router.navigate('/other'); });
    await act(async () => { await router.navigate('/'); });
    await waitFor(() => expect(tg.backVisible).toBe(false));
    await act(async () => { await router.navigate('/other'); });
    await waitFor(() => expect(tg.backVisible).toBe(true));
  });

  it('AC26: single-module mode: BackButton is hidden on basePath even after PUSH from a nested route', async () => {
    const router = renderRouter([demoModule]);
    await expectText('Главная демо-модуля');
    await act(async () => { await router.navigate('/demo/items/1'); });
    await expectText('Элемент 1');
    await waitFor(() => expect(tg.backVisible).toBe(true));

    await act(async () => { await router.navigate('/demo'); });

    await expectText('Главная демо-модуля');
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });

  it('AC27: "На главную" uses REPLACE: history does not grow, Back and Main are hidden, no return to NotFound', async () => {
    tg.setStartParam('demo_x');
    const router = renderRouter([ghostModule, otherModule], 'demo_x');
    await expectText('Страница не найдена');
    await waitFor(() => expect(tg.backVisible).toBe(true)); // история [home, notfound]

    await pressMainWhenReady();

    await expectText('Привет, Иван!');
    expect(router.state.historyAction).toBe('REPLACE');
    expect(router.state.location.pathname).toBe('/');
    await waitFor(() => expect(tg.backVisible).toBe(false));
    await waitFor(() => expect(tg.mainVisible).toBe(false));
    await waitFor(() => expect(tg.backHandlerCount).toBe(0));
    await waitFor(() => expect(tg.mainHandlerCount).toBe(0));

    // назад на NotFound вернуться нельзя: запись заменена, step back приводит на главную, не на «Страница не найдена»
    await act(async () => { await router.navigate(-1); });
    expect(hasText('Страница не найдена')).toBe(false);
    expect(router.state.location.pathname).toBe('/');
  });

  it('AC27: unknown route reached by PUSH and "На главную": history length stays the same (one step back lands on home, not NotFound)', async () => {
    const router = renderRouter(twoModules);
    await expectText('Привет, Иван!');
    await act(async () => { await router.navigate('/nope'); });
    await expectText('Страница не найдена');
    await waitFor(() => expect(tg.mainText).toBe('На главную'));

    await pressMainWhenReady();

    await expectText('Привет, Иван!');
    expect(router.state.historyAction).toBe('REPLACE');
    await act(async () => { await router.navigate(-1); });
    expect(hasText('Страница не найдена')).toBe(false);
    expect(router.state.location.pathname).toBe('/');
  });

  it('AC27: single-module mode: "На главную" replaces with basePath; Back and Main hidden', async () => {
    const router = renderRouter([demoModule]);
    await expectText('Главная демо-модуля');
    await act(async () => { await router.navigate('/nope'); });
    await expectText('Страница не найдена');
    await waitFor(() => expect(tg.mainText).toBe('На главную'));

    await pressMainWhenReady();

    await expectText('Главная демо-модуля');
    expect(router.state.historyAction).toBe('REPLACE');
    expect(router.state.location.pathname).toBe('/demo');
    await waitFor(() => expect(tg.backVisible).toBe(false));
    await waitFor(() => expect(tg.mainVisible).toBe(false));
  });
});

describe('single-module mode (AC-22, AC-24)', () => {
  it('AC22: "/" is replaced with basePath and the platform home list is not shown', async () => {
    const router = renderRouter([demoModule]);
    await expectText('Главная демо-модуля');

    await act(async () => { await router.navigate('/'); });

    await expectText('Главная демо-модуля');
    expect(router.state.location.pathname).toBe('/demo');
    expect(hasText('Привет, Иван!')).toBe(false);
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });

  it('AC24: unrecognized start_param with a single module opens homePath (= basePath) with the notice', async () => {
    tg.setStartParam('ghost_abc');
    okMe();
    render(<App modules={[demoModule]} />);

    await expectText('Главная демо-модуля');
    await expectText(NOTICE);
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });

  it('AC24: single-module notice disappears after the first navigation and does not come back', async () => {
    const router = renderRouter([demoModule], 'ghost_abc');
    await expectText(NOTICE);
    expect(router.state.location.pathname).toBe('/demo');

    await act(async () => { await router.navigate('/demo/items/1'); });
    await expectText('Элемент 1');
    expect(hasText(NOTICE)).toBe(false);

    await act(async () => { await router.navigate('/demo'); });
    await expectText('Главная демо-модуля');
    expect(hasText(NOTICE)).toBe(false);
  });

  it('AC24: notice (multi-module) disappears after navigation and does not reappear on return to "/"', async () => {
    const router = renderRouter(twoModules, 'ghost_abc');
    await expectText(NOTICE);

    await act(async () => { await router.navigate('/demo'); });
    await expectText('Главная демо-модуля');
    expect(hasText(NOTICE)).toBe(false);

    await act(async () => { await router.navigate('/'); });
    await expectText('Привет, Иван!');
    expect(hasText(NOTICE)).toBe(false);
  });

  it('AC24: single module + valid deep link: history is [homePath, target], Back leads to basePath and is hidden there', async () => {
    const router = renderRouter([demoModule], 'demo_abc');
    await expectText('Элемент abc');
    await waitFor(() => expect(tg.backVisible).toBe(true));

    await pressBackWhenReady();

    await expectText('Главная демо-модуля');
    expect(router.state.location.pathname).toBe('/demo');
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });

  it('AC24: single module + resolveStartParam returning basePath: history is [homePath], Back hidden', async () => {
    const same: WebModule = { ...demoModule, resolveStartParam: () => '/demo' };
    const router = renderRouter([same], 'demo_x');
    await expectText('Главная демо-модуля');
    expect(hasText(NOTICE)).toBe(false);
    await waitFor(() => expect(tg.backVisible).toBe(false));
    await act(async () => { await router.navigate(-1); });
    expect(router.state.location.pathname).toBe('/demo');
  });
});

describe('notice does not reappear on POP (AC-24)', () => {
  it('AC24: multi-module: invalid start_param -> module (PUSH) -> BackButton (POP) -> home without notice', async () => {
    const router = renderRouter(twoModules, 'ghost_abc');
    await expectText(NOTICE);

    await act(async () => { await router.navigate('/demo'); });
    await expectText('Главная демо-модуля');
    expect(hasText(NOTICE)).toBe(false);
    await waitFor(() => expect(tg.backVisible).toBe(true));

    await pressBackWhenReady();

    await expectText('Привет, Иван!');
    expect(router.state.historyAction).toBe('POP');
    expect(router.state.location.pathname).toBe('/');
    expect(hasText(NOTICE)).toBe(false);
  });

  it('AC24: single module: notice on /demo -> /demo/items/1 (PUSH) -> BackButton (POP) -> /demo without notice', async () => {
    const router = renderRouter([demoModule], 'ghost_abc');
    await expectText(NOTICE);

    await act(async () => { await router.navigate('/demo/items/1'); });
    await expectText('Элемент 1');
    expect(hasText(NOTICE)).toBe(false);
    await waitFor(() => expect(tg.backVisible).toBe(true));

    await pressBackWhenReady();

    await expectText('Главная демо-модуля');
    expect(router.state.historyAction).toBe('POP');
    expect(router.state.location.pathname).toBe('/demo');
    expect(hasText(NOTICE)).toBe(false);
  });
});
