import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { telegram } from '../telegram';

// Спека 003, TEST-6: AC-20 (openTelegramLink/shareUrl), AC-21 (showConfirm). Реальный адаптер поверх стаба WebApp.
type Win = { Telegram?: unknown };
const setWebApp = (stub: unknown) => ((window as unknown as Win).Telegram = { WebApp: stub });

let openTelegramLink: ReturnType<typeof vi.fn>;

beforeEach(() => {
  openTelegramLink = vi.fn();
  setWebApp({ initData: 'x=1', initDataUnsafe: {}, openTelegramLink });
});
afterEach(() => {
  delete (window as unknown as Win).Telegram;
});

describe('telegram.openTelegramLink / shareUrl (003 AC-20)', () => {
  it('AC20: openTelegramLink passes url to WebApp.openTelegramLink', () => {
    telegram.openTelegramLink('https://t.me/b?start=x_1');
    expect(openTelegramLink).toHaveBeenCalledExactlyOnceWith('https://t.me/b?start=x_1');
  });

  it('AC20: shareUrl builds t.me/share/url with both params encoded', () => {
    telegram.shareUrl('https://t.me/b?start=x_1', 'Привет & пока');
    const expected =
      'https://t.me/share/url?url=' +
      encodeURIComponent('https://t.me/b?start=x_1') +
      '&text=' +
      encodeURIComponent('Привет & пока');
    expect(openTelegramLink).toHaveBeenCalledExactlyOnceWith(expected);
    expect(expected).toContain('url=https%3A%2F%2Ft.me%2Fb%3Fstart%3Dx_1&text=%D0%9F');
  });

  it('AC20: special characters (&, =, #, spaces, newline) cannot break out of the text parameter', () => {
    telegram.shareUrl('https://t.me/b?start=x_1&evil=1', 'a&b=c#d e\nf');
    const link = String(openTelegramLink.mock.calls[0]?.[0]);
    const parsed = new URL(link);
    expect([...parsed.searchParams.keys()]).toEqual(['url', 'text']);
    expect(parsed.searchParams.get('url')).toBe('https://t.me/b?start=x_1&evil=1');
    expect(parsed.searchParams.get('text')).toBe('a&b=c#d e\nf');
  });

  it('AC20: empty text still produces text= parameter', () => {
    telegram.shareUrl('https://t.me/b', '');
    expect(openTelegramLink).toHaveBeenCalledWith('https://t.me/share/url?url=' + encodeURIComponent('https://t.me/b') + '&text=');
  });

  it('AC20: outside Telegram both are no-ops without exceptions', () => {
    delete (window as unknown as Win).Telegram;
    expect(() => telegram.openTelegramLink('https://t.me/b')).not.toThrow();
    expect(() => telegram.shareUrl('https://t.me/b', 'x')).not.toThrow();
    expect(openTelegramLink).not.toHaveBeenCalled();
  });
});

describe('telegram.showConfirm (003 AC-21)', () => {
  it.each([true, false])('AC21: resolves with callback value %s', async (value) => {
    const showConfirm = vi.fn((_m: string, cb: (ok: boolean) => void) => cb(value));
    setWebApp({ initData: 'x=1', initDataUnsafe: {}, showConfirm });

    await expect(telegram.showConfirm('Выйти?')).resolves.toBe(value);
    expect(showConfirm).toHaveBeenCalledWith('Выйти?', expect.any(Function));
  });

  it('AC21: stays pending until the callback fires (async callback)', async () => {
    let cb: ((ok: boolean) => void) | undefined;
    setWebApp({ initData: 'x=1', initDataUnsafe: {}, showConfirm: (_m: string, c: (ok: boolean) => void) => (cb = c) });

    let settled = false;
    const p = telegram.showConfirm('?').then((v) => ((settled = true), v));
    await Promise.resolve();
    expect(settled).toBe(false);
    cb?.(true);
    await expect(p).resolves.toBe(true);
  });

  it('AC21: outside Telegram resolves false', async () => {
    delete (window as unknown as Win).Telegram;
    await expect(telegram.showConfirm('?')).resolves.toBe(false);
  });

  it('AC21: WebApp without showConfirm resolves false without exception', async () => {
    setWebApp({ initData: 'x=1', initDataUnsafe: {} });
    await expect(telegram.showConfirm('?')).resolves.toBe(false);
  });
});

describe('WebApp present but initData empty = outside Telegram (003 AC-20/21, critic round 1)', () => {
  it('AC20: openTelegramLink and shareUrl do not call WebApp.openTelegramLink', () => {
    setWebApp({ initData: '', initDataUnsafe: {}, openTelegramLink });

    expect(() => telegram.openTelegramLink('https://t.me/b')).not.toThrow();
    expect(() => telegram.shareUrl('https://t.me/b', 'x')).not.toThrow();

    expect(telegram.isAvailable()).toBe(false);
    expect(openTelegramLink).not.toHaveBeenCalled();
  });

  it('AC21: showConfirm resolves false without calling WebApp.showConfirm', async () => {
    const showConfirm = vi.fn((_m: string, cb: (ok: boolean) => void) => cb(true));
    setWebApp({ initData: '', initDataUnsafe: {}, showConfirm });

    await expect(telegram.showConfirm('?')).resolves.toBe(false);
    expect(showConfirm).not.toHaveBeenCalled();
  });
});
