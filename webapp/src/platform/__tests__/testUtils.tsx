import { vi } from 'vitest';
import { waitFor } from '@testing-library/react';
import { useParams } from 'react-router';
import type { MeDto } from '../api';
import type { WebModule } from '../router';

export const ME: MeDto = { id: 42, firstName: 'Иван', lastName: 'Петров', username: 'ivan', languageCode: 'ru' };

export function jsonResponse(body: unknown, status = 200, contentType = 'application/json'): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } });
}

export function problemResponse(status: number, code: string, extra: Record<string, unknown> = {}): Response {
  return jsonResponse({ title: `title-${code}`, status, code, ...extra }, status, 'application/problem+json');
}

export function stubFetch(impl: (url: string, init?: RequestInit) => Promise<Response> | Response) {
  const fn = vi.fn((input: RequestInfo | URL, init?: RequestInit) => Promise.resolve(impl(String(input), init)));
  vi.stubGlobal('fetch', fn);
  return fn;
}

export function headerOf(init: RequestInit | undefined, name: string): string | null {
  return new Headers(init?.headers).get(name);
}

export function deferred<T>() {
  let resolve!: (v: T) => void;
  let reject!: (e: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** Ждёт, пока текст появится в документе (устойчиво к разбиению текста по элементам). */
export async function expectText(text: string): Promise<void> {
  await waitFor(() => {
    if (!(document.body.textContent ?? '').includes(text)) {
      throw new Error(`Текст не найден: «${text}». В документе: «${document.body.textContent}»`);
    }
  });
}

export function hasText(text: string): boolean {
  return (document.body.textContent ?? '').includes(text);
}

function DemoItem() {
  const { id } = useParams();
  return <div>Элемент {id}</div>;
}

export const demoModule: WebModule = {
  name: 'demo',
  title: 'Демо',
  basePath: '/demo',
  routes: [
    { index: true, element: <div>Главная демо-модуля</div> },
    { path: 'items/:id', element: <DemoItem /> },
  ],
  resolveStartParam: (payload) => (payload === 'bad' ? null : `/demo/items/${payload}`),
};

export const otherModule: WebModule = {
  name: 'other',
  title: 'Другой',
  basePath: '/other',
  routes: [{ index: true, element: <div>Экран другого модуля</div> }],
};
