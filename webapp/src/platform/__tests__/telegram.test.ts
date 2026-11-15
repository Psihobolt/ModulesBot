import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { telegram } from '../telegram';

// Реальный адаптер (без vi.mock) поверх стаба window.Telegram.WebApp.
function makeStub(initData = 'query_id=1&hash=abc') {
  const mainHandlers = new Set<() => void>();
  const backHandlers = new Set<() => void>();
  const stub = {
    initData,
    initDataUnsafe: {} as { start_param?: string },
    colorScheme: 'light' as 'light' | 'dark',
    ready: vi.fn(),
    expand: vi.fn(),
    onEvent: vi.fn(),
    offEvent: vi.fn(),
    MainButton: {
      setParams: vi.fn(),
      onClick: vi.fn((cb: () => void) => void mainHandlers.add(cb)),
      offClick: vi.fn((cb: () => void) => void mainHandlers.delete(cb)),
      show: vi.fn(),
      hide: vi.fn(),
      showProgress: vi.fn(),
      hideProgress: vi.fn(),
    },
    BackButton: {
      onClick: vi.fn((cb: () => void) => void backHandlers.add(cb)),
      offClick: vi.fn((cb: () => void) => void backHandlers.delete(cb)),
      show: vi.fn(),
      hide: vi.fn(),
    },
    HapticFeedback: { impactOccurred: vi.fn(), notificationOccurred: vi.fn(), selectionChanged: vi.fn() },
  };
  return { stub, mainHandlers, backHandlers };
}

type Stub = ReturnType<typeof makeStub>;
let s: Stub;

function install(initData?: string) {
  s = makeStub(initData);
  (window as unknown as { Telegram?: unknown }).Telegram = { WebApp: s.stub };
}

beforeEach(() => install());
afterEach(() => {
  // сбрасываем внутреннее состояние владельцев кнопок
  if (window.Telegram?.WebApp) {
    telegram.mainButton.hide();
    telegram.backButton.hide();
  }
  delete (window as unknown as { Telegram?: unknown }).Telegram;
});

describe('telegram adapter: availability (AC-20)', () => {
  it('AC20: no window.Telegram -> isAvailable() === false', () => {
    delete (window as unknown as { Telegram?: unknown }).Telegram;
    expect(telegram.isAvailable()).toBe(false);
    expect(telegram.initData()).toBe('');
  });

  it('AC20: window.Telegram without WebApp -> false', () => {
    (window as unknown as { Telegram?: unknown }).Telegram = {};
    expect(telegram.isAvailable()).toBe(false);
  });

  it('AC20: empty initData -> false', () => {
    install('');
    expect(telegram.isAvailable()).toBe(false);
  });

  it('AC20: non-empty initData -> true and initData() returns it', () => {
    install('user=1&hash=z');
    expect(telegram.isAvailable()).toBe(true);
    expect(telegram.initData()).toBe('user=1&hash=z');
  });

  it('AC20: other methods are safe no-ops when WebApp is absent', () => {
    delete (window as unknown as { Telegram?: unknown }).Telegram;
    const cb = vi.fn();
    expect(() => {
      telegram.ready();
      telegram.expand();
      telegram.haptic.impact('light');
      telegram.haptic.notification('success');
      telegram.haptic.selection();
      telegram.mainButton.show({ text: 'x', onClick: cb })();
      telegram.backButton.show(cb)();
      telegram.mainButton.hide();
      telegram.backButton.hide();
      telegram.onThemeChanged(cb)();
    }).not.toThrow();
    expect(telegram.startParam()).toBeUndefined();
    expect(telegram.colorScheme()).toBe('light');
  });
});

describe('telegram adapter: MainButton (AC-26)', () => {
  it('AC26: show() registers one handler and shows the button; cleanup offClick + hide', () => {
    const onClick = vi.fn();
    const cleanup = telegram.mainButton.show({ text: 'Go', onClick });

    expect(s.stub.MainButton.setParams).toHaveBeenCalledWith(expect.objectContaining({ text: 'Go' }));
    expect(s.stub.MainButton.show).toHaveBeenCalled();
    expect(s.mainHandlers.size).toBe(1);

    cleanup();

    expect(s.stub.MainButton.offClick).toHaveBeenCalledWith(onClick);
    expect(s.stub.MainButton.hide).toHaveBeenCalled();
    expect(s.mainHandlers.size).toBe(0);
  });

  it('AC26: repeated show() does not accumulate handlers', () => {
    for (let i = 0; i < 5; i++) telegram.mainButton.show({ text: `t${i}`, onClick: vi.fn() });
    expect(s.mainHandlers.size).toBe(1);
  });

  it('AC26: stale cleanup does not hide a button shown later', () => {
    const first = telegram.mainButton.show({ text: 'A', onClick: vi.fn() });
    const second = telegram.mainButton.show({ text: 'B', onClick: vi.fn() });
    s.stub.MainButton.hide.mockClear();
    s.stub.MainButton.offClick.mockClear();

    first();

    expect(s.stub.MainButton.hide).not.toHaveBeenCalled();
    expect(s.mainHandlers.size).toBe(1);

    second();
    expect(s.stub.MainButton.hide).toHaveBeenCalledTimes(1);
    expect(s.mainHandlers.size).toBe(0);
  });

  it('AC26: cleanup called twice hides only once', () => {
    const c = telegram.mainButton.show({ text: 'A', onClick: vi.fn() });
    s.stub.MainButton.hide.mockClear();
    c();
    c();
    expect(s.stub.MainButton.hide).toHaveBeenCalledTimes(1);
  });

  it('AC26: enabled=false -> inactive; loading=true -> progress shown', () => {
    telegram.mainButton.show({ text: 'A', onClick: vi.fn(), enabled: false, loading: true });
    expect(s.stub.MainButton.setParams).toHaveBeenCalledWith(expect.objectContaining({ is_active: false }));
    expect(s.stub.MainButton.showProgress).toHaveBeenCalled();
  });

  it('AC26: hide() removes the handler and hides the button', () => {
    telegram.mainButton.show({ text: 'A', onClick: vi.fn() });
    telegram.mainButton.hide();
    expect(s.mainHandlers.size).toBe(0);
    expect(s.stub.MainButton.hide).toHaveBeenCalled();
  });
});

describe('telegram adapter: BackButton (AC-26)', () => {
  it('AC26: show() registers one handler and shows; cleanup offClick + hide', () => {
    const onClick = vi.fn();
    const cleanup = telegram.backButton.show(onClick);

    expect(s.stub.BackButton.show).toHaveBeenCalled();
    expect(s.backHandlers.size).toBe(1);

    cleanup();

    expect(s.stub.BackButton.offClick).toHaveBeenCalledWith(onClick);
    expect(s.stub.BackButton.hide).toHaveBeenCalled();
    expect(s.backHandlers.size).toBe(0);
  });

  it('AC26: repeated show() does not accumulate handlers', () => {
    for (let i = 0; i < 5; i++) telegram.backButton.show(vi.fn());
    expect(s.backHandlers.size).toBe(1);
  });

  it('AC26: stale cleanup does not hide a button shown later', () => {
    const first = telegram.backButton.show(vi.fn());
    const second = telegram.backButton.show(vi.fn());
    s.stub.BackButton.hide.mockClear();

    first();

    expect(s.stub.BackButton.hide).not.toHaveBeenCalled();
    expect(s.backHandlers.size).toBe(1);

    second();
    expect(s.stub.BackButton.hide).toHaveBeenCalledTimes(1);
    expect(s.backHandlers.size).toBe(0);
  });

  it('AC26: Main and Back owners are independent', () => {
    const m = telegram.mainButton.show({ text: 'A', onClick: vi.fn() });
    telegram.backButton.show(vi.fn());
    s.stub.MainButton.hide.mockClear();
    s.stub.BackButton.hide.mockClear();

    m();

    expect(s.stub.MainButton.hide).toHaveBeenCalledTimes(1);
    expect(s.stub.BackButton.hide).not.toHaveBeenCalled();
    expect(s.backHandlers.size).toBe(1);
  });
});

describe('telegram adapter: misc', () => {
  it('startParam() returns start_param, empty -> undefined', () => {
    s.stub.initDataUnsafe.start_param = 'demo_abc';
    expect(telegram.startParam()).toBe('demo_abc');
    s.stub.initDataUnsafe.start_param = '';
    expect(telegram.startParam()).toBeUndefined();
  });

  it('AC29: onThemeChanged subscribes and unsubscribes via onEvent/offEvent', () => {
    const cb = vi.fn();
    const off = telegram.onThemeChanged(cb);
    expect(s.stub.onEvent).toHaveBeenCalledWith('themeChanged', cb);
    off();
    expect(s.stub.offEvent).toHaveBeenCalledWith('themeChanged', cb);
  });

  it('colorScheme() maps dark/other', () => {
    s.stub.colorScheme = 'dark';
    expect(telegram.colorScheme()).toBe('dark');
    s.stub.colorScheme = 'light';
    expect(telegram.colorScheme()).toBe('light');
  });

  it('ready()/expand() delegate to WebApp', () => {
    telegram.ready();
    telegram.expand();
    expect(s.stub.ready).toHaveBeenCalledTimes(1);
    expect(s.stub.expand).toHaveBeenCalledTimes(1);
  });
});
