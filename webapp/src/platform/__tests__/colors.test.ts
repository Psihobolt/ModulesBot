import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

// AC-29: в webapp/src/**/*.css и *.tsx нет цветовых литералов. Единственное исключение — platform/theme.css:
// литерал допустим только как значение --tg-theme-* в правиле с селектором ровно `:root` или `:root[data-color-scheme='dark']`.
// Файлы читаются через fs: Vitest подменяет импорт CSS пустыми строками, поэтому import.meta.glob('?raw') не годится.
const SRC = join(import.meta.dirname, '..', '..');
const THEME = 'platform/theme.css';

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const full = join(dir, name);
    return statSync(full).isDirectory() ? walk(full) : [full];
  });
}

const files = walk(SRC)
  .filter((f) => /\.(css|tsx)$/.test(f))
  .map((f) => relative(SRC, f).split('\\').join('/'))
  .filter((p) => !p.includes('__tests__/') && !/\.test\./.test(p) && !p.startsWith('test/'))
  .map((p) => [p, readFileSync(join(SRC, p), 'utf8')] as const);

const NAMED_COLORS =
  'white|black|red|green|blue|yellow|orange|purple|pink|gray|grey|silver|maroon|navy|teal|aqua|lime|olive|fuchsia|brown|cyan|magenta|gold|violet|indigo|crimson|coral|tomato|salmon|khaki|lavender|beige|ivory';

const PATTERNS: [string, RegExp][] = [
  ['hex color', /#[0-9a-fA-F]{3,8}\b/g],
  ['rgb()/rgba()', /\brgba?\s*\(/gi],
  ['hsl()/hsla()', /\bhsla?\s*\(/gi],
  [
    'named color in color/background',
    new RegExp(`\\b(?:background(?:-color)?|color)\\s*:\\s*['"]?(?:${NAMED_COLORS})\\b`, 'gi'),
  ],
];

function violations(text: string): string[] {
  return PATTERNS.flatMap(([name, re]) => [...text.matchAll(re)].map((m) => `${name}: ${m[0]}`));
}

const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '');

/** Допустимые селекторы fallback-блоков (кавычки ' или "). */
const ALLOWED_SELECTOR = /^:root(?:\[data-color-scheme=(['"])dark\1\])?$/;

/** Разбор плоского CSS (без вложенности) на правила: селектор + объявления. */
function parseRules(css: string): { selector: string; body: string }[] {
  return [...stripComments(css).matchAll(/([^{}]+)\{([^{}]*)\}/g)].map((m) => ({
    selector: m[1]!.trim().replace(/\s+/g, ' '),
    body: m[2]!,
  }));
}

/** Нарушения в theme.css: литерал вне допустимого правила/вне --tg-theme-* значения. */
function themeViolations(css: string): string[] {
  const out: string[] = [];
  for (const rule of parseRules(css)) {
    const allowedRule = ALLOWED_SELECTOR.test(rule.selector);
    for (const decl of rule.body.split(';')) {
      const d = decl.trim();
      if (!d) continue;
      const found = violations(d);
      if (found.length === 0) continue;
      const isThemeVar = /^--tg-theme-[a-z0-9-]+\s*:/.test(d);
      if (!(allowedRule && isThemeVar)) out.push(...found.map((f) => `${rule.selector} { ${d} } -> ${f}`));
    }
  }
  return out;
}

// запрещено даже в допустимом блоке theme.css (формы с альфа-каналом)
const FORBIDDEN_EVERYWHERE: [string, RegExp][] = [
  ['#rrggbbaa', /#[0-9a-fA-F]{8}\b/g],
  ['rgba(', /\brgba\s*\(/gi],
  ['hsla(', /\bhsla\s*\(/gi],
];

describe('no color literals (AC-29)', () => {
  it('AC29: real CSS content is read (non-empty), not Vitest stubs', () => {
    const theme = files.find(([p]) => p === THEME);
    expect(theme, 'platform/theme.css must be found').toBeDefined();
    expect(theme![1].length).toBeGreaterThan(100);
    expect(theme![1]).toContain('--tg-theme-bg-color');
    const cssFiles = files.filter(([p]) => p.endsWith('.css'));
    expect(cssFiles.length).toBeGreaterThan(0);
    for (const [p, c] of cssFiles) expect(c.trim().length, `${p} is empty`).toBeGreaterThan(0);
  });

  it('AC29: source scan finds tsx components too', () => {
    expect(files.filter(([p]) => p.endsWith('.tsx')).length).toBeGreaterThan(1);
  });

  it('AC29: no color literals in webapp/src/**/*.css and *.tsx except theme.css', () => {
    const report = files
      .filter(([p]) => p !== THEME)
      .flatMap(([p, c]) => violations(stripComments(c)).map((v) => `${p} -> ${v}`));
    expect(report).toEqual([]);
  });

  it("AC29: theme.css literals only as --tg-theme-* values in `:root` / `:root[data-color-scheme='dark']`", () => {
    const theme = files.find(([p]) => p === THEME)![1];
    expect(themeViolations(theme)).toEqual([]);
  });

  it('AC29: theme.css has no #rrggbbaa, rgba(, hsla( anywhere', () => {
    const theme = stripComments(files.find(([p]) => p === THEME)![1]);
    const found = FORBIDDEN_EVERYWHERE.flatMap(([n, re]) => [...theme.matchAll(re)].map((m) => `${n}: ${m[0]}`));
    expect(found).toEqual([]);
  });

  it('AC29: theme.css defines both fallback blocks (light and dark)', () => {
    const selectors = parseRules(files.find(([p]) => p === THEME)![1]).map((r) => r.selector);
    expect(selectors.some((s) => s === ':root')).toBe(true);
    expect(selectors.some((s) => /^:root\[data-color-scheme=(['"])dark\1\]$/.test(s))).toBe(true);
  });

  it('AC29: detector self-check catches literals', () => {
    expect(violations('a { color: #fff }')).not.toEqual([]);
    expect(violations('a { color: #11223344 }')).not.toEqual([]);
    expect(violations('a { background: rgb(1,2,3) }')).not.toEqual([]);
    expect(violations('a { background: rgba(1,2,3,.5) }')).not.toEqual([]);
    expect(violations('a { color: hsl(1 2% 3%) }')).not.toEqual([]);
    expect(violations('a { color: hsla(1 2% 3% / .5) }')).not.toEqual([]);
    expect(violations('a { color: red }')).not.toEqual([]);
    expect(violations("<div style={{ background: 'white' }} />")).not.toEqual([]);
    expect(violations('a { color: var(--tg-theme-text-color) }')).toEqual([]);
  });

  it('AC29: theme rule self-check (allowed vs forbidden placement)', () => {
    expect(themeViolations(':root { --tg-theme-bg-color: #fff; }')).toEqual([]);
    expect(themeViolations(":root[data-color-scheme='dark'] { --tg-theme-bg-color: #212121; }")).toEqual([]);
    expect(themeViolations(':root[data-color-scheme="dark"] { --tg-theme-bg-color: #212121; }')).toEqual([]);
    // литерал вне --tg-theme-* переменной
    expect(themeViolations(':root { --my-color: #fff; }')).not.toEqual([]);
    expect(themeViolations(':root { color: #fff; }')).not.toEqual([]);
    // другой селектор
    expect(themeViolations('body { --tg-theme-bg-color: #fff; }')).not.toEqual([]);
    expect(themeViolations(':root.x { --tg-theme-bg-color: #fff; }')).not.toEqual([]);
    expect(themeViolations(":root[data-color-scheme='light'] { --tg-theme-bg-color: #fff; }")).not.toEqual([]);
    expect(themeViolations('.a { color: rgb(1,2,3); }')).not.toEqual([]);
  });
});
