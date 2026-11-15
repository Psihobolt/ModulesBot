import { describe, expect, it } from 'vitest';
import { parseStartParam } from '../router';

describe('parseStartParam (AC-25)', () => {
  it('AC25: "movienight_join_AB12-cd" -> module + payload', () => {
    expect(parseStartParam('movienight_join_AB12-cd')).toEqual({ module: 'movienight', payload: 'join_AB12-cd' });
  });

  it('AC25: simple "demo_abc"', () => {
    expect(parseStartParam('demo_abc')).toEqual({ module: 'demo', payload: 'abc' });
  });

  it('AC25: payload may contain digits, "-" and "_"', () => {
    expect(parseStartParam('demo2_a-B_9')).toEqual({ module: 'demo2', payload: 'a-B_9' });
  });

  it('AC25: total length exactly 64 is accepted', () => {
    const raw = 'demo_' + 'a'.repeat(59);
    expect(raw).toHaveLength(64);
    expect(parseStartParam(raw)).toEqual({ module: 'demo', payload: 'a'.repeat(59) });
  });

  it.each([
    ['undefined', undefined],
    ['empty string', ''],
    ['no payload part: "demo"', 'demo'],
    ['uppercase module: "Demo_x"', 'Demo_x'],
    ['empty payload: "demo_"', 'demo_'],
    ['space in payload: "demo_a b"', 'demo_a b'],
    ['65 chars', 'demo_' + 'a'.repeat(60)],
    ['module starts with digit: "1demo_x"', '1demo_x'],
    ['one-char module: "a_x"', 'a_x'],
    ['module longer than 32', 'a'.repeat(33) + '_x'],
    ['forbidden char in payload: "demo_a/b"', 'demo_a/b'],
    ['cyrillic payload', 'demo_привет'],
    ['leading underscore', '_demo_x'],
  ])('AC25: invalid value (%s) -> null', (_name, raw) => {
    expect(parseStartParam(raw)).toBeNull();
  });
});
