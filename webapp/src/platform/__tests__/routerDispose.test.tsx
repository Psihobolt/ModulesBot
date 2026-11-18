import { cleanup, render, waitFor } from '@testing-library/react';
import { StrictMode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { createAppRouter } from '../router';
import { pressBackWhenReady, tg } from './fakeTelegram';
import { ME, demoModule, expectText, jsonResponse, stubFetch } from './testUtils';

vi.mock('../telegram', async () => ({ telegram: (await import('./fakeTelegram')).fakeTelegram }));

// Спека 003, FE-2 (остаточное 001): dispose роутера/HistoryTracker при размонтировании оболочки.
type Created = { router: { dispose: () => void; subscribe: (cb: never) => () => void }; dispose: () => void };
const created: { app: Created; routerDispose: ReturnType<typeof vi.fn>; appDispose: ReturnType<typeof vi.fn> }[] = [];
let activeSubscriptions = 0;

vi.mock('../router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../router')>();
  return {
    ...actual,
    createAppRouter: (opts: Parameters<typeof actual.createAppRouter>[0]) => {
      const app = actual.createAppRouter(opts);
      // Считаем подписки на роутер: HistoryTracker подписывается в конструкторе и обязан отписаться в dispose().
      const subscribe = app.router.subscribe.bind(app.router);
      app.router.subscribe = ((cb: never) => {
        activeSubscriptions += 1;
        const off = subscribe(cb);
        let done = false;
        return () => {
          if (!done) activeSubscriptions -= 1;
          done = true;
          off();
        };
      }) as typeof app.router.subscribe;
      const routerDispose = vi.spyOn(app.router, 'dispose');
      const appDispose = vi.spyOn(app, 'dispose');
      created.push({ app: app as unknown as Created, routerDispose, appDispose });
      return app;
    },
  };
});

beforeEach(() => {
  tg.reset();
  created.length = 0;
  activeSubscriptions = 0;
  stubFetch(() => jsonResponse(ME));
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('shell unmount disposes router (FE-2)', () => {
  it('FE2: unmounting the app calls dispose of the router and the history tracker', async () => {
    const { unmount } = render(<App modules={[demoModule]} />);
    await expectText('Главная демо-модуля');
    expect(created).toHaveLength(1);
    expect(created[0]!.appDispose).not.toHaveBeenCalled();

    unmount();

    expect(created[0]!.appDispose).toHaveBeenCalledTimes(1);
    expect(created[0]!.routerDispose).toHaveBeenCalledTimes(1);
  });

  it('FE2: after unmount no router subscriptions remain (tracker unsubscribed) and no buttons leak', async () => {
    const { unmount } = render(<App modules={[demoModule]} />);
    await expectText('Главная демо-модуля');
    expect(activeSubscriptions).toBeGreaterThan(0);

    unmount();

    expect(activeSubscriptions).toBe(0);
    await waitFor(() => expect(tg.backHandlerCount).toBe(0));
    await waitFor(() => expect(tg.backVisible).toBe(false));
  });

  it('FE2: HistoryTracker.dispose is idempotent-safe and stops reacting to navigation', async () => {
    const app = createAppRouter({ modules: [demoModule], me: ME, startParam: 'demo_abc' });
    app.dispose();
    expect(() => app.dispose()).not.toThrow();
  });
});

describe('StrictMode double mount (FE-2)', () => {
  it('FE2: every router created by StrictMode but the last is disposed; the shell renders', async () => {
    const { unmount } = render(
      <StrictMode>
        <App modules={[demoModule]} />
      </StrictMode>,
    );
    await expectText('Главная демо-модуля');

    expect(created.length).toBeGreaterThanOrEqual(1);
    created.slice(0, -1).forEach((c) => expect(c.appDispose).toHaveBeenCalledTimes(1));
    expect(created.at(-1)!.appDispose).not.toHaveBeenCalled();

    unmount();
    created.forEach((c) => expect(c.appDispose).toHaveBeenCalledTimes(1));
    expect(activeSubscriptions).toBe(0);
  });

  it('FE2: StrictMode does not break navigation and BackButton (deep link -> back -> home)', async () => {
    tg.setStartParam('demo_abc');
    render(
      <StrictMode>
        <App modules={[demoModule, { ...demoModule, name: 'x', title: 'X', basePath: '/x', routes: [] }]} />
      </StrictMode>,
    );
    await expectText('Элемент abc');
    await waitFor(() => expect(tg.backVisible).toBe(true));
    await waitFor(() => expect(tg.backHandlerCount).toBe(1));

    await pressBackWhenReady();

    await expectText('Привет, Иван!');
    await waitFor(() => expect(tg.backVisible).toBe(false));
    await waitFor(() => expect(tg.backHandlerCount).toBe(0));
  });
});
