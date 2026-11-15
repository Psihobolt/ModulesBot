import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useBackButton, useMainButton } from '../buttons';
import type { MainButtonParams } from '../telegram';
import { tg } from './fakeTelegram';

vi.mock('../telegram', async () => ({ telegram: (await import('./fakeTelegram')).fakeTelegram }));

function MainProbe({ params }: { params: MainButtonParams | null }) {
  useMainButton(params);
  return null;
}

function BackProbe({ onBack }: { onBack: (() => void) | null }) {
  useBackButton(onBack);
  return null;
}

describe('useMainButton / useBackButton (AC-26)', () => {
  beforeEach(() => tg.reset());
  afterEach(cleanup);

  it('AC26: useMainButton shows the button with text and press calls the handler', () => {
    const onClick = vi.fn();
    render(<MainProbe params={{ text: 'Готово', onClick }} />);

    expect(tg.mainVisible).toBe(true);
    expect(tg.mainText).toBe('Готово');
    act(() => tg.pressMain());
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('AC26: useMainButton(null) keeps the button hidden', () => {
    render(<MainProbe params={null} />);
    expect(tg.mainVisible).toBe(false);
    expect(tg.mainHandlerCount).toBe(0);
  });

  it('AC26: useMainButton unmount hides the button and unsubscribes the handler', () => {
    const onClick = vi.fn();
    const { unmount } = render(<MainProbe params={{ text: 'Go', onClick }} />);

    unmount();

    expect(tg.mainVisible).toBe(false);
    expect(tg.mainHandlerCount).toBe(0);
    act(() => tg.pressMain());
    expect(onClick).not.toHaveBeenCalled();
  });

  it('AC26: switching useMainButton params to null hides the button and unsubscribes', () => {
    const onClick = vi.fn();
    const { rerender } = render(<MainProbe params={{ text: 'Go', onClick }} />);

    rerender(<MainProbe params={null} />);

    expect(tg.mainVisible).toBe(false);
    expect(tg.mainHandlerCount).toBe(0);
    act(() => tg.pressMain());
    expect(onClick).not.toHaveBeenCalled();
  });

  it('AC26: useMainButton param changes do not accumulate handlers; only the newest handler fires', () => {
    const first = vi.fn();
    const second = vi.fn();
    const third = vi.fn();
    const { rerender } = render(<MainProbe params={{ text: 'A', onClick: first }} />);
    rerender(<MainProbe params={{ text: 'B', onClick: second }} />);
    rerender(<MainProbe params={{ text: 'B', onClick: third }} />);
    rerender(<MainProbe params={{ text: 'C', onClick: third, loading: true }} />);

    expect(tg.mainHandlerCount).toBe(1);
    expect(tg.mainText).toBe('C');
    act(() => tg.pressMain());
    expect(first).not.toHaveBeenCalled();
    expect(second).not.toHaveBeenCalled();
    expect(third).toHaveBeenCalledTimes(1);
  });

  it('AC26: re-render with a new onClick of the same button does not break the press (latest handler wins)', () => {
    const first = vi.fn();
    const second = vi.fn();
    const { rerender } = render(<MainProbe params={{ text: 'Go', onClick: first }} />);
    rerender(<MainProbe params={{ text: 'Go', onClick: second }} />);

    act(() => tg.pressMain());

    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledTimes(1);
    expect(tg.mainHandlerCount).toBe(1);
  });

  it('AC26: useBackButton shows the button and press calls the handler', () => {
    const onBack = vi.fn();
    render(<BackProbe onBack={onBack} />);

    expect(tg.backVisible).toBe(true);
    act(() => tg.pressBack());
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it('AC26: useBackButton(null) keeps the button hidden', () => {
    render(<BackProbe onBack={null} />);
    expect(tg.backVisible).toBe(false);
    expect(tg.backHandlerCount).toBe(0);
  });

  it('AC26: useBackButton unmount hides the button and unsubscribes the handler', () => {
    const onBack = vi.fn();
    const { unmount } = render(<BackProbe onBack={onBack} />);

    unmount();

    expect(tg.backVisible).toBe(false);
    expect(tg.backHandlerCount).toBe(0);
    act(() => tg.pressBack());
    expect(onBack).not.toHaveBeenCalled();
  });

  it('AC26: useBackButton handler changes do not accumulate; only the newest fires', () => {
    const first = vi.fn();
    const second = vi.fn();
    const { rerender } = render(<BackProbe onBack={first} />);
    rerender(<BackProbe onBack={second} />);

    expect(tg.backHandlerCount).toBe(1);
    act(() => tg.pressBack());
    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledTimes(1);
  });

  it('AC26: useBackButton switching to null hides the button', () => {
    const onBack = vi.fn();
    const { rerender } = render(<BackProbe onBack={onBack} />);

    rerender(<BackProbe onBack={null} />);

    expect(tg.backVisible).toBe(false);
    act(() => tg.pressBack());
    expect(onBack).not.toHaveBeenCalled();
  });
});
