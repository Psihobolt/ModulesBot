import { render } from '@testing-library/react';
import { createMemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { vi } from 'vitest';
import { fakeTelegram } from '../../../platform/__tests__/fakeTelegram';
import { jsonResponse, stubFetch } from '../../../platform/__tests__/testUtils';
import type { GroupDto, MemberDto } from '../api';
import { movieNightModule } from '../index';

export const LINK = 'https://t.me/MovieNightBot/app?startapp=movienight_join_AB12cd34EF';

export function problem(status: number, code: string, title: string, errors?: Record<string, string[]>): Response {
  return jsonResponse({ title, status, code, ...(errors ? { errors } : {}) }, status, 'application/problem+json');
}

export const member = (over: Partial<MemberDto> & Pick<MemberDto, 'userId' | 'displayName' | 'position'>): MemberDto => ({
  username: null,
  joinedAt: '2026-01-15T12:00:00Z',
  isMe: false,
  isOwner: false,
  ...over,
});

/** Группа «Мы с Петей»: Аня (1, владелец), Петя (2), Вася (3). meIndex — кто «я» (0..2). */
export function makeGroup(meIndex = 0, over: Partial<GroupDto> = {}): GroupDto {
  const names = ['Аня', 'Петя', 'Вася'];
  return {
    id: 'g1',
    name: 'Мы с Петей',
    createdAt: '2026-01-15T12:00:00Z',
    ownerId: 1,
    isOwner: meIndex === 0,
    members: names.map((n, i) => member({ userId: i + 1, displayName: n, position: i + 1, isMe: i === meIndex, isOwner: i === 0 })),
    invite: { code: 'AB12cd34EF', miniAppLink: LINK, botLink: 'https://t.me/MovieNightBot?start=movienight_join_AB12cd34EF' },
    ...over,
  };
}

export type Call = { method: string; path: string; body: unknown };
type Handler = (call: Call) => Response | Promise<Response>;

/** Роутинг fetch по ключу "METHOD /path" (без "/api"). Не найден — 500 unhandled. */
export function mockApi(handlers: Record<string, Handler | Response>) {
  const calls: Call[] = [];
  const fetchMock = stubFetch((url, init) => {
    const method = init?.method ?? 'GET';
    const path = url.replace(/^\/api/, '');
    const body = typeof init?.body === 'string' ? (JSON.parse(init.body) as unknown) : undefined;
    const call = { method, path, body };
    calls.push(call);
    const h = handlers[`${method} ${path}`];
    if (!h) return problem(500, 'unhandled', `unhandled ${method} ${path}`);
    return typeof h === 'function' ? (h(call) as Response) : h.clone();
  });
  return {
    fetchMock,
    calls,
    count: (method: string, path: string) => calls.filter((c) => c.method === method && c.path === path).length,
  };
}

/** Монтирует маршруты модуля под /movienight. Последний entry — текущий; предыдущие — история. */
export function renderAt(...entries: string[]) {
  const router = createMemoryRouter(
    [
      { path: '/movienight', children: movieNightModule.routes },
      { path: '*', element: <div>fallback</div> },
    ],
    { initialEntries: entries, initialIndex: entries.length - 1 },
  );
  render(<RouterProvider router={router} />);
  return router;
}

export function lastMain() {
  const call = vi.mocked(fakeTelegram.mainButton.show).mock.lastCall;
  return call?.[0] ?? null;
}

export const groupsList = (): Response => jsonResponse([]);
