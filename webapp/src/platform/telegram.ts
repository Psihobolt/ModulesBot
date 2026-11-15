// Единственное место, где трогается window.Telegram.

export type ColorScheme = 'light' | 'dark';
export type MainButtonParams = { text: string; onClick: () => void; enabled?: boolean; loading?: boolean };

export interface TelegramPlatform {
  isAvailable(): boolean;
  initData(): string;
  startParam(): string | undefined;
  colorScheme(): ColorScheme;
  ready(): void;
  expand(): void;
  openTelegramLink(url: string): void;
  shareUrl(url: string, text: string): void;
  showConfirm(message: string): Promise<boolean>;
  onThemeChanged(cb: () => void): () => void;
  mainButton: { show(p: MainButtonParams): () => void; hide(): void };
  backButton: { show(onClick: () => void): () => void; hide(): void };
  haptic: {
    impact(style: 'light' | 'medium' | 'heavy'): void;
    notification(type: 'success' | 'warning' | 'error'): void;
    selection(): void;
  };
}

// Минимальные типы используемой части Telegram.WebApp.
type Handler = () => void;
interface TgMainButton {
  setParams(p: { text?: string; is_active?: boolean; is_visible?: boolean }): void;
  onClick(cb: Handler): void;
  offClick(cb: Handler): void;
  show(): void;
  hide(): void;
  showProgress(leaveActive?: boolean): void;
  hideProgress(): void;
}
interface TgBackButton {
  onClick(cb: Handler): void;
  offClick(cb: Handler): void;
  show(): void;
  hide(): void;
}
interface TgWebApp {
  initData: string;
  initDataUnsafe: { start_param?: string };
  colorScheme: ColorScheme;
  ready(): void;
  expand(): void;
  openTelegramLink(url: string): void;
  showConfirm?(message: string, cb: (ok: boolean) => void): void;
  onEvent(event: 'themeChanged', cb: Handler): void;
  offEvent(event: 'themeChanged', cb: Handler): void;
  MainButton: TgMainButton;
  BackButton: TgBackButton;
  HapticFeedback: {
    impactOccurred(style: 'light' | 'medium' | 'heavy'): void;
    notificationOccurred(type: 'success' | 'warning' | 'error'): void;
    selectionChanged(): void;
  };
}
declare global {
  interface Window {
    Telegram?: { WebApp?: TgWebApp };
  }
}

const webApp = (): TgWebApp | undefined => window.Telegram?.WebApp;
const noop = (): void => {};

// Текущий владелец кнопок: cleanup устаревшего вызова не должен скрывать кнопку, показанную позже.
let mainOwner: symbol | null = null;
let mainHandler: Handler | null = null;
let backOwner: symbol | null = null;
let backHandler: Handler | null = null;

function hideMain(w: TgWebApp): void {
  if (mainHandler) w.MainButton.offClick(mainHandler);
  mainHandler = null;
  mainOwner = null;
  w.MainButton.hideProgress();
  w.MainButton.hide();
}

function hideBack(w: TgWebApp): void {
  if (backHandler) w.BackButton.offClick(backHandler);
  backHandler = null;
  backOwner = null;
  w.BackButton.hide();
}

export const telegram: TelegramPlatform = {
  isAvailable() {
    const w = webApp();
    return !!w && typeof w.initData === 'string' && w.initData !== '';
  },
  initData() {
    return webApp()?.initData ?? '';
  },
  startParam() {
    const v = webApp()?.initDataUnsafe?.start_param;
    return v ? v : undefined;
  },
  colorScheme() {
    return webApp()?.colorScheme === 'dark' ? 'dark' : 'light';
  },
  ready() {
    webApp()?.ready();
  },
  expand() {
    webApp()?.expand();
  },
  openTelegramLink(url) {
    if (telegram.isAvailable()) webApp()?.openTelegramLink(url);
  },
  shareUrl(url, text) {
    if (!telegram.isAvailable()) return;
    webApp()?.openTelegramLink(
      `https://t.me/share/url?url=${encodeURIComponent(url)}&text=${encodeURIComponent(text)}`,
    );
  },
  showConfirm(message) {
    const w = webApp();
    if (!telegram.isAvailable() || !w || typeof w.showConfirm !== 'function') return Promise.resolve(false);
    return new Promise<boolean>((resolve) => {
      try {
        w.showConfirm!(message, (ok) => resolve(ok === true));
      } catch {
        resolve(false);
      }
    });
  },
  onThemeChanged(cb) {
    const w = webApp();
    if (!w) return noop;
    w.onEvent('themeChanged', cb);
    return () => w.offEvent('themeChanged', cb);
  },
  mainButton: {
    show(p) {
      const w = webApp();
      if (!w) return noop;
      if (mainHandler) w.MainButton.offClick(mainHandler);
      const owner = Symbol('main');
      mainOwner = owner;
      mainHandler = p.onClick;
      w.MainButton.setParams({ text: p.text, is_active: p.enabled !== false, is_visible: true });
      w.MainButton.onClick(p.onClick);
      if (p.loading) w.MainButton.showProgress(false);
      else w.MainButton.hideProgress();
      w.MainButton.show();
      return () => {
        if (mainOwner === owner) hideMain(w);
      };
    },
    hide() {
      const w = webApp();
      if (w) hideMain(w);
    },
  },
  backButton: {
    show(onClick) {
      const w = webApp();
      if (!w) return noop;
      if (backHandler) w.BackButton.offClick(backHandler);
      const owner = Symbol('back');
      backOwner = owner;
      backHandler = onClick;
      w.BackButton.onClick(onClick);
      w.BackButton.show();
      return () => {
        if (backOwner === owner) hideBack(w);
      };
    },
    hide() {
      const w = webApp();
      if (w) hideBack(w);
    },
  },
  haptic: {
    impact: (style) => webApp()?.HapticFeedback?.impactOccurred(style),
    notification: (type) => webApp()?.HapticFeedback?.notificationOccurred(type),
    selection: () => webApp()?.HapticFeedback?.selectionChanged(),
  },
};
