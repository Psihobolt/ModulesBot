import type { ReactElement } from 'react';
import { useNavigate } from 'react-router';
import { useMainButton } from './buttons';
import type { WebModule } from './router';

export function MessageScreen(props: { title: string; text?: string }): ReactElement {
  return (
    <main className="screen screen--center">
      <h1 className="screen__title">{props.title}</h1>
      {props.text && <p className="screen__text">{props.text}</p>}
    </main>
  );
}

export function OutsideTelegramScreen(): ReactElement {
  return (
    <MessageScreen
      title="Откройте приложение в Telegram"
      text="Это мини-приложение работает внутри Telegram. Откройте его через бота."
    />
  );
}

export function LoadingScreen(): ReactElement {
  return (
    <main className="screen screen--center" role="status">
      <p className="screen__text">Загрузка…</p>
    </main>
  );
}

export function SessionExpiredScreen(): ReactElement {
  return <MessageScreen title="Сессия устарела. Закройте приложение и откройте его снова." />;
}

export function LoadErrorScreen(props: { onRetry: () => void }): ReactElement {
  useMainButton({ text: 'Повторить', onClick: props.onRetry });
  return <MessageScreen title="Не удалось загрузить данные." />;
}

export function HomeScreen(props: { firstName: string; modules: WebModule[] }): ReactElement {
  const navigate = useNavigate();
  return (
    <main className="screen">
      <h1 className="screen__title">Привет, {props.firstName}!</h1>
      {props.modules.length === 0 ? (
        <p className="screen__text">Здесь пока ничего нет — разделы появятся скоро.</p>
      ) : (
        <ul className="module-list">
          {props.modules.map((m) => (
            <li key={m.name}>
              <button type="button" className="module-list__item" onClick={() => void navigate(m.basePath)}>
                {m.title}
              </button>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}

export function NotFoundScreen(props: { homePath: string }): ReactElement {
  const navigate = useNavigate();
  useMainButton({ text: 'На главную', onClick: () => void navigate(props.homePath, { replace: true }) });
  return <MessageScreen title="Страница не найдена" />;
}
