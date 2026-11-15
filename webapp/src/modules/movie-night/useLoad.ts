import { useCallback, useEffect, useState } from 'react';
import { ApiError } from '../../platform/api';

export type LoadState<T> =
  | { status: 'loading' }
  | { status: 'ready'; data: T }
  | { status: 'error'; error: ApiError | null };

export const toApiError = (e: unknown): ApiError | null => (e instanceof ApiError ? e : null);

/** `load` должна быть стабильной (useCallback). */
export function useLoad<T>(load: () => Promise<T>) {
  const [state, setState] = useState<LoadState<T>>({ status: 'loading' });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    load().then(
      (data) => {
        if (!cancelled) setState({ status: 'ready', data });
      },
      (e: unknown) => {
        if (!cancelled) setState({ status: 'error', error: toApiError(e) });
      },
    );
    return () => {
      cancelled = true;
    };
  }, [load, attempt]);

  /** Полная перезагрузка с индикатором загрузки (кнопка «Повторить»). */
  const reload = useCallback(() => {
    setState({ status: 'loading' });
    setAttempt((a) => a + 1);
  }, []);

  return { state, setState, reload };
}
