import { vi } from 'vitest';
import type { ColorScheme, MainButtonParams, TelegramPlatform } from '../telegram';

/**
 * Управляемая подмена `platform/telegram.ts`. Используется через
 * `vi.mock('../telegram', async () => ({ telegram: (await import('./fakeTelegram')).fakeTelegram }))`.
 *
 * Кнопки моделируют контракт: `show()` регистрирует обработчик и возвращает cleanup, который его снимает.
 * `press()` вызывает ВСЕ ещё зарегистрированные обработчики — так «утёкший» обработчик виден в тесте.
 */
type State = {
  available: boolean;
  initData: string;
  startParam: string | undefined;
  scheme: ColorScheme;
};

const initialState = (): State => ({
  available: true,
  initData: 'query_id=AAH&user=%7B%22id%22%3A42%7D&auth_date=1&hash=abc',
  startParam: undefined,
  scheme: 'light',
});

let state = initialState();
const themeListeners = new Set<() => void>();
const mainHandlers = new Set<MainButtonParams>();
const backHandlers = new Set<() => void>();
const main = { visible: false, params: null as MainButtonParams | null };
const back = { visible: false };

export const fakeTelegram: TelegramPlatform = {
  isAvailable: vi.fn(() => state.available),
  initData: vi.fn(() => state.initData),
  startParam: vi.fn(() => state.startParam),
  colorScheme: vi.fn(() => state.scheme),
  ready: vi.fn(),
  expand: vi.fn(),
  onThemeChanged: vi.fn((cb: () => void) => {
    themeListeners.add(cb);
    return () => {
      themeListeners.delete(cb);
    };
  }),
  mainButton: {
    show: vi.fn((params: MainButtonParams) => {
      mainHandlers.add(params);
      main.visible = true;
      main.params = params;
      return () => {
        mainHandlers.delete(params);
        if (main.params === params) {
          main.visible = false;
          main.params = null;
        }
      };
    }),
    hide: vi.fn(() => {
      main.visible = false;
      main.params = null;
    }),
  },
  backButton: {
    show: vi.fn((onClick: () => void) => {
      backHandlers.add(onClick);
      back.visible = true;
      return () => {
        backHandlers.delete(onClick);
        if (backHandlers.size === 0) back.visible = false;
      };
    }),
    hide: vi.fn(() => {
      back.visible = false;
    }),
  },
  haptic: { impact: vi.fn(), notification: vi.fn(), selection: vi.fn() },
};

/** Управление подменой из тестов. */
export const tg = {
  setAvailable(value: boolean) {
    state.available = value;
  },
  setInitData(value: string) {
    state.initData = value;
  },
  setStartParam(value: string | undefined) {
    state.startParam = value;
  },
  setScheme(value: ColorScheme) {
    state.scheme = value;
  },
  /** Меняет схему и вызывает подписчиков themeChanged. */
  emitThemeChanged(value: ColorScheme) {
    state.scheme = value;
    [...themeListeners].forEach((cb) => cb());
  },
  get themeListenerCount() {
    return themeListeners.size;
  },
  get mainVisible() {
    return main.visible;
  },
  get mainText() {
    return main.params?.text ?? null;
  },
  get mainHandlerCount() {
    return mainHandlers.size;
  },
  get backVisible() {
    return back.visible;
  },
  get backHandlerCount() {
    return backHandlers.size;
  },
  pressMain() {
    [...mainHandlers].forEach((p) => p.onClick());
  },
  pressBack() {
    [...backHandlers].forEach((h) => h());
  },
  reset() {
    state = initialState();
    themeListeners.clear();
    mainHandlers.clear();
    backHandlers.clear();
    main.visible = false;
    main.params = null;
    back.visible = false;
    vi.mocked(fakeTelegram.ready).mockClear();
    vi.mocked(fakeTelegram.expand).mockClear();
    vi.mocked(fakeTelegram.mainButton.show).mockClear();
    vi.mocked(fakeTelegram.backButton.show).mockClear();
    vi.mocked(fakeTelegram.mainButton.hide).mockClear();
    vi.mocked(fakeTelegram.backButton.hide).mockClear();
    vi.mocked(fakeTelegram.onThemeChanged).mockClear();
    vi.mocked(fakeTelegram.isAvailable).mockClear();
  },
};
