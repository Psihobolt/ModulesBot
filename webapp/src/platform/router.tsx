import { useState, useSyncExternalStore, type ReactElement } from 'react';
import {
  createMemoryRouter,
  Navigate,
  Outlet,
  useLocation,
  useNavigate,
  type DataRouter,
  type RouteObject,
} from 'react-router';
import type { MeDto } from './api';
import { useBackButton } from './buttons';
import { HomeScreen, NotFoundScreen } from './screens';

export type WebModule = {
  name: string;
  title: string;
  basePath: string;
  routes: RouteObject[];
  resolveStartParam?: (payload: string) => string | null;
};

const START_PARAM_RE = /^([a-z][a-z0-9]{1,31})_([A-Za-z0-9_-]+)$/;
const START_PARAM_MAX_LENGTH = 64;

export function parseStartParam(raw: string | undefined): { module: string; payload: string } | null {
  if (!raw || raw.length > START_PARAM_MAX_LENGTH) return null;
  const match = START_PARAM_RE.exec(raw);
  if (!match) return null;
  return { module: match[1]!, payload: match[2]! };
}

/** Следит за стеком истории memory-роутера, чтобы знать, есть ли куда идти назад. */
export class HistoryTracker {
  private keys: string[];
  private index: number;
  private readonly listeners = new Set<() => void>();
  private canGoBack: boolean;
  private readonly unsubscribe: () => void;

  constructor(router: DataRouter, initialDepth: number) {
    this.keys = Array.from({ length: initialDepth }, (_, i) => `__initial_${i}`);
    this.keys.push(router.state.location.key);
    this.index = this.keys.length - 1;
    this.canGoBack = this.index > 0;
    this.unsubscribe = router.subscribe((state) => {
      const key = state.location.key;
      if (key === this.keys[this.index]) return;
      if (state.historyAction === 'PUSH') {
        this.keys = [...this.keys.slice(0, this.index + 1), key];
        this.index += 1;
      } else if (state.historyAction === 'REPLACE') {
        this.keys[this.index] = key;
      } else {
        const found = this.keys.indexOf(key);
        this.index = found >= 0 ? found : Math.max(0, this.index - 1);
      }
      this.update();
    });
  }

  subscribe = (cb: () => void): (() => void) => {
    this.listeners.add(cb);
    return () => this.listeners.delete(cb);
  };

  getSnapshot = (): boolean => this.canGoBack;

  dispose(): void {
    this.unsubscribe();
    this.listeners.clear();
  }

  private update(): void {
    const next = this.index > 0;
    if (next === this.canGoBack) return;
    this.canGoBack = next;
    this.listeners.forEach((l) => l());
  }
}

function Shell(props: {
  tracker: HistoryTracker;
  homePath: string;
  notice: boolean;
}): ReactElement {
  const navigate = useNavigate();
  const location = useLocation();
  const canGoBack = useSyncExternalStore(props.tracker.subscribe, props.tracker.getSnapshot);
  // На главной BackButton скрыта всегда; на остальных маршрутах — по индексу истории.
  const showBack = canGoBack && location.pathname !== props.homePath;
  useBackButton(showBack ? () => void navigate(-1) : null);
  // Одноразовый флаг: после первой смены location уведомление скрыто навсегда (в т.ч. при POP назад).
  const [firstKey] = useState(location.key);
  const [dismissed, setDismissed] = useState(false);
  if (!dismissed && location.key !== firstKey) setDismissed(true);
  const showNotice = props.notice && !dismissed;
  return (
    <>
      {showNotice && (
        <p className="notice" role="status">
          Ссылка не распознана — открыта главная.
        </p>
      )}
      <Outlet />
    </>
  );
}

export type AppRouter = { router: DataRouter };

/** Строит memory-роутер: главная, маршруты модулей, обработка start_param, режим одного модуля. */
export function createAppRouter(opts: {
  modules: WebModule[];
  me: MeDto;
  startParam: string | undefined;
}): AppRouter {
  const { modules, me, startParam } = opts;
  const single = modules.length === 1 ? modules[0]! : null;
  const homePath = single ? single.basePath : '/';

  let target: string | null = null;
  let notice = false;
  if (startParam !== undefined && startParam !== '') {
    const parsed = parseStartParam(startParam);
    const module = parsed ? modules.find((m) => m.name === parsed.module) : undefined;
    const resolved = parsed && module?.resolveStartParam ? module.resolveStartParam(parsed.payload) : null;
    if (resolved && resolved.startsWith('/')) target = resolved;
    else notice = true;
  }

  const entries =
    target && target !== homePath
      ? [{ pathname: homePath, key: 'home' }, { pathname: target, key: 'target' }]
      : [{ pathname: homePath, key: 'home' }];

  // tracker создаётся после роутера, но нужен Shell в route — доступ через замыкание.
  const holder: { tracker?: HistoryTracker } = {};
  const routes: RouteObject[] = [
    {
      element: <ShellHost holder={holder} homePath={homePath} notice={notice} />,
      children: [
        {
          path: '/',
          element: single ? (
            <Navigate to={homePath} replace />
          ) : (
            <HomeScreen firstName={me.firstName} modules={modules} />
          ),
        },
        ...modules.map((m): RouteObject => ({ path: m.basePath, children: m.routes })),
        { path: '*', element: <NotFoundScreen homePath={homePath} /> },
      ],
    },
  ];

  const router = createMemoryRouter(routes, {
    initialEntries: entries,
    initialIndex: entries.length - 1,
  });
  holder.tracker = new HistoryTracker(router, entries.length - 1);
  return { router };
}

function ShellHost(props: {
  holder: { tracker?: HistoryTracker };
  homePath: string;
  notice: boolean;
}): ReactElement {
  if (!props.holder.tracker) throw new Error('History tracker is not initialized');
  return (
    <Shell tracker={props.holder.tracker} homePath={props.homePath} notice={props.notice} />
  );
}
