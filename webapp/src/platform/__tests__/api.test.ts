import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError } from '../api';
import { tg } from './fakeTelegram';
import { headerOf, jsonResponse, problemResponse, stubFetch } from './testUtils';

vi.mock('../telegram', async () => ({ telegram: (await import('./fakeTelegram')).fakeTelegram }));

async function caught(promise: Promise<unknown>): Promise<unknown> {
  try {
    await promise;
  } catch (e) {
    return e;
  }
  throw new Error('Ожидалась ошибка, но промис выполнился');
}

describe('api client (AC-28)', () => {
  beforeEach(() => {
    tg.reset();
    tg.setInitData('raw-init-data=1&hash=abc');
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('AC28: 2xx with JSON returns parsed object', async () => {
    stubFetch(() => jsonResponse({ id: 1, name: 'x' }));
    await expect(api.get<{ id: number; name: string }>('/things/1')).resolves.toEqual({ id: 1, name: 'x' });
  });

  it('AC28: 204 returns undefined', async () => {
    stubFetch(() => new Response(null, { status: 204 }));
    await expect(api.del('/things/1')).resolves.toBeUndefined();
  });

  it('AC28: requests go to /api<path> with Authorization: tma <initData>', async () => {
    const fetchMock = stubFetch(() => jsonResponse({}));
    await api.get('/platform/me');
    await api.get('/things?x=1&y=2');

    expect(fetchMock.mock.calls[0]![0]).toBe('/api/platform/me');
    expect(fetchMock.mock.calls[1]![0]).toBe('/api/things?x=1&y=2');
    for (const call of fetchMock.mock.calls) {
      expect(headerOf(call[1], 'Authorization')).toBe('tma raw-init-data=1&hash=abc');
    }
  });

  it('AC28: initData is read at call time (not cached at import)', async () => {
    const fetchMock = stubFetch(() => jsonResponse({}));
    await api.get('/a');
    tg.setInitData('second=2');
    await api.get('/b');
    expect(headerOf(fetchMock.mock.calls[1]![1], 'Authorization')).toBe('tma second=2');
  });

  it.each([
    ['get', 'GET'],
    ['del', 'DELETE'],
  ] as const)('AC28: %s uses HTTP %s without body', async (method, verb) => {
    const fetchMock = stubFetch(() => jsonResponse({}));
    await api[method]('/x');
    const init = fetchMock.mock.calls[0]![1];
    expect((init?.method ?? 'GET').toUpperCase()).toBe(verb);
    expect(init?.body ?? undefined).toBeUndefined();
  });

  it.each([
    ['post', 'POST'],
    ['put', 'PUT'],
  ] as const)('AC28: %s sends JSON body with %s and Authorization', async (method, verb) => {
    const fetchMock = stubFetch(() => jsonResponse({ ok: true }));
    await api[method]('/x', { title: 'Привет', n: 1 });
    const init = fetchMock.mock.calls[0]![1];
    expect(init?.method?.toUpperCase()).toBe(verb);
    expect(JSON.parse(String(init?.body))).toEqual({ title: 'Привет', n: 1 });
    expect(headerOf(init, 'Content-Type')).toContain('application/json');
    expect(headerOf(init, 'Authorization')).toBe('tma raw-init-data=1&hash=abc');
  });

  it('AC28: post without body works', async () => {
    const fetchMock = stubFetch(() => jsonResponse({}));
    await api.post('/x');
    expect(fetchMock.mock.calls[0]![1]?.method?.toUpperCase()).toBe('POST');
  });

  it('AC28: ProblemDetails response throws ApiError with status, code, title, errors', async () => {
    stubFetch(() =>
      problemResponse(400, 'validation_failed', { title: 'Validation failed', errors: { name: ['too long'] } }),
    );
    const e = await caught(api.post('/x', {}));
    expect(e).toBeInstanceOf(ApiError);
    const err = e as ApiError;
    expect(err.status).toBe(400);
    expect(err.code).toBe('validation_failed');
    expect(err.title).toBe('Validation failed');
    expect(err.errors).toEqual({ name: ['too long'] });
  });

  it('AC28: ProblemDetails 401 keeps code', async () => {
    stubFetch(() => problemResponse(401, 'auth_expired'));
    const err = (await caught(api.get('/platform/me'))) as ApiError;
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(401);
    expect(err.code).toBe('auth_expired');
    expect(err.errors).toBeUndefined();
  });

  it('AC28: error without JSON body (HTML) gives ApiError with code "unknown"', async () => {
    stubFetch(() => new Response('<html>Bad gateway</html>', { status: 502, headers: { 'Content-Type': 'text/html' } }));
    const err = (await caught(api.get('/x'))) as ApiError;
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(502);
    expect(err.code).toBe('unknown');
  });

  it('AC28: error with empty body gives ApiError with code "unknown"', async () => {
    stubFetch(() => new Response(null, { status: 500 }));
    const err = (await caught(api.get('/x'))) as ApiError;
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(500);
    expect(err.code).toBe('unknown');
  });

  it('AC28: network failure gives ApiError status 0 code "network_error"', async () => {
    stubFetch(() => {
      throw new TypeError('Failed to fetch');
    });
    const err = (await caught(api.get('/x'))) as ApiError;
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(0);
    expect(err.code).toBe('network_error');
  });

  it('AC28: ApiError is an Error', () => {
    expect(Object.getPrototypeOf(ApiError.prototype)).toBe(Error.prototype);
  });
});
