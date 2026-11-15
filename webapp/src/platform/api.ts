import { telegram } from './telegram';

export type HealthDto = { status: 'ok' };
export type MeDto = {
  id: number;
  firstName: string;
  lastName: string | null;
  username: string | null;
  languageCode: string | null;
};
export type ProblemDetails = {
  type?: string;
  title: string;
  status: number;
  detail?: string;
  code: string;
  errors?: Record<string, string[]>;
};

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly title: string;
  readonly errors?: Record<string, string[]>;

  constructor(status: number, code: string, title: string, errors?: Record<string, string[]>) {
    super(title);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.title = title;
    this.errors = errors;
  }
}

function isProblem(body: unknown): body is Partial<ProblemDetails> & { code: string } {
  return typeof body === 'object' && body !== null && typeof (body as { code?: unknown }).code === 'string';
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    Authorization: `tma ${telegram.initData()}`,
  };
  const init: RequestInit = { method, headers };
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
    init.body = JSON.stringify(body);
  }

  let response: Response;
  try {
    response = await fetch(`/api${path}`, init);
  } catch {
    throw new ApiError(0, 'network_error', 'Network error');
  }

  if (response.ok) {
    if (response.status === 204) return undefined as T;
    try {
      return (await response.json()) as T;
    } catch {
      throw new ApiError(response.status, 'unknown', response.statusText || 'Invalid response');
    }
  }

  let problem: unknown;
  try {
    problem = await response.json();
  } catch {
    problem = undefined;
  }
  if (isProblem(problem)) {
    throw new ApiError(
      response.status,
      problem.code,
      problem.title ?? (response.statusText || 'Request failed'),
      problem.errors,
    );
  }
  throw new ApiError(response.status, 'unknown', response.statusText || 'Request failed');
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body),
  put: <T>(path: string, body?: unknown) => request<T>('PUT', path, body),
  del: <T>(path: string) => request<T>('DELETE', path),
};
