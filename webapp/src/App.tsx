import { useCallback, useEffect, useRef, useState, type ReactElement } from 'react';
import { RouterProvider } from 'react-router/dom';
import { api, ApiError, type MeDto } from './platform/api';
import { createAppRouter, type WebModule } from './platform/router';
import {
  LoadErrorScreen,
  LoadingScreen,
  OutsideTelegramScreen,
  SessionExpiredScreen,
} from './platform/screens';
import { telegram } from './platform/telegram';
import { useTheme } from './platform/theme';

type AppRouter = ReturnType<typeof createAppRouter>;

type MeState =
  | { status: 'loading' }
  | { status: 'ready'; me: MeDto }
  | { status: 'session-expired' }
  | { status: 'error' };

const AUTH_CODES = new Set(['auth_expired', 'auth_invalid', 'auth_missing']);

function Ready({ me, modules }: { me: MeDto; modules: WebModule[] }): ReactElement {
  const [router, setRouter] = useState<AppRouter['router'] | null>(null);
  // Роутер создаётся в эффекте и освобождается в cleanup: двойной mount StrictMode даёт новый роутер.
  useEffect(() => {
    const app = createAppRouter({ modules, me, startParam: telegram.startParam() });
    // eslint-disable-next-line react-hooks/set-state-in-effect -- роутер: внешний ресурс с dispose, создаётся только в эффекте
    setRouter(app.router);
    return () => {
      app.dispose();
      setRouter(null);
    };
  }, [modules, me]);
  if (!router) return <LoadingScreen />;
  return <RouterProvider router={router} />;
}

async function fetchMe(): Promise<MeState> {
  try {
    return { status: 'ready', me: await api.get<MeDto>('/platform/me') };
  } catch (e) {
    if (e instanceof ApiError && e.status === 401 && AUTH_CODES.has(e.code)) {
      return { status: 'session-expired' };
    }
    return { status: 'error' };
  }
}

function TelegramApp({ modules }: { modules: WebModule[] }): ReactElement {
  const [state, setState] = useState<MeState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);
  const initialized = useRef(false);

  useEffect(() => {
    if (initialized.current) return;
    initialized.current = true;
    telegram.ready();
    telegram.expand();
  }, []);

  useEffect(() => {
    let cancelled = false;
    void fetchMe().then((next) => {
      if (!cancelled) setState(next);
    });
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  const retry = useCallback(() => {
    setState({ status: 'loading' });
    setReloadKey((k) => k + 1);
  }, []);

  switch (state.status) {
    case 'loading':
      return <LoadingScreen />;
    case 'session-expired':
      return <SessionExpiredScreen />;
    case 'error':
      return <LoadErrorScreen onRetry={retry} />;
    case 'ready':
      return <Ready me={state.me} modules={modules} />;
  }
}

export function App({ modules }: { modules: WebModule[] }): ReactElement {
  useTheme();
  if (!telegram.isAvailable()) return <OutsideTelegramScreen />;
  return <TelegramApp modules={modules} />;
}
