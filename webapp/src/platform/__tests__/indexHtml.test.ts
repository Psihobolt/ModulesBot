import { existsSync, readFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// Спека 003, TEST-8: AC-22 (исходники), AC-23 (артефакты сборки; тест не запускает build сам).
const root = resolve(__dirname, '../../..');
const read = (p: string) => readFileSync(resolve(root, p), 'utf8');

type ScriptTag = { tag: string; src: string | undefined; index: number };
function scripts(html: string): ScriptTag[] {
  return [...html.matchAll(/<script\b[^>]*>/gi)].map((m) => ({
    tag: m[0],
    src: /\bsrc\s*=\s*["']([^"']*)["']/i.exec(m[0])?.[1],
    index: m.index ?? 0,
  }));
}

function assertLocalScriptFirst(html: string) {
  const all = scripts(html);
  const local = all.filter((s) => s.src === '/telegram-web-app.js');
  expect(local).toHaveLength(1);
  const tag = local[0]!;
  expect(tag.tag).not.toMatch(/\basync\b/i);
  expect(tag.tag).not.toMatch(/\bdefer\b/i);
  expect(tag.tag).not.toMatch(/type\s*=\s*["']module["']/i);
  const bundle = all.find((s) => /type\s*=\s*["']module["']/i.test(s.tag));
  expect(bundle).toBeDefined();
  expect(tag.index).toBeLessThan(bundle!.index);
  return tag;
}

describe('webapp/index.html (003 AC-22)', () => {
  const html = read('index.html');

  it('AC22: exactly one sync local telegram-web-app.js in <head>, before the module bundle', () => {
    const tag = assertLocalScriptFirst(html);
    const headEnd = html.search(/<\/head>/i);
    expect(headEnd).toBeGreaterThan(0);
    expect(tag.index).toBeLessThan(headEnd);
  });

  it('AC22: index.html has no telegram.org reference', () => {
    expect(html).not.toContain('telegram.org');
  });

  it('AC22: public/telegram-web-app.js exists and is not empty', () => {
    const p = resolve(root, 'public/telegram-web-app.js');
    expect(existsSync(p)).toBe(true);
    expect(statSync(p).size).toBeGreaterThan(0);
  });
});

describe('webapp/dist (003 AC-23)', () => {
  const need = (p: string) => {
    if (!existsSync(resolve(root, p))) throw new Error(`${p} is missing: run npm run build first`);
  };

  it('AC23: dist/telegram-web-app.js is byte-identical to public/telegram-web-app.js', () => {
    need('dist/telegram-web-app.js');
    const a = readFileSync(resolve(root, 'dist/telegram-web-app.js'));
    const b = readFileSync(resolve(root, 'public/telegram-web-app.js'));
    expect(a.equals(b)).toBe(true);
  });

  it('AC23: dist/index.html keeps sync local script before the bundle and has no telegram.org', () => {
    need('dist/index.html');
    const html = read('dist/index.html');
    assertLocalScriptFirst(html);
    expect(html).toContain('src="/telegram-web-app.js"');
    expect(html).not.toContain('telegram.org');
  });
});
