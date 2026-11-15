import { useCallback, useState, type ReactElement } from 'react';
import { useNavigate, useParams } from 'react-router';
import { ApiError } from '../../../platform/api';
import { useMainButton } from '../../../platform/buttons';
import { LoadErrorScreen, LoadingScreen } from '../../../platform/screens';
import { telegram } from '../../../platform/telegram';
import { movieNightApi, type GroupDto, type MemberDto } from '../api';
import { toApiError, useLoad } from '../useLoad';
import { ACTION_FAILED, GroupNotFoundScreen, HOME } from './common';

function memberLabel(m: MemberDto): string {
  return `${m.position}. ${m.displayName}${m.isMe ? ' (вы)' : ''}${m.isOwner ? ' — владелец' : ''}`;
}

function GroupView(props: {
  group: GroupDto;
  onGroup: (g: GroupDto) => void;
  onRefresh: () => Promise<void>;
  onGone: (e: ApiError) => void;
}): ReactElement {
  const { group, onGroup, onRefresh, onGone } = props;
  const navigate = useNavigate();
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const link = group.invite.miniAppLink;

  useMainButton(
    link === null
      ? null
      : {
          text: 'Пригласить',
          onClick: () => telegram.shareUrl(link, `Присоединяйся к группе «${group.name}» в Киновечере`),
        },
  );

  const fail = async (e: unknown): Promise<void> => {
    const err = toApiError(e);
    if (err?.status === 404 && err.code === 'movienight_group_not_found') {
      onGone(err);
    } else if (err?.status === 403) {
      setMessage(err.title);
      await onRefresh();
    } else if (err?.status === 404 && err.code === 'movienight_member_not_found') {
      await onRefresh();
    } else {
      setMessage(ACTION_FAILED);
    }
  };

  const run = async (action: () => Promise<void>): Promise<void> => {
    if (busy) return;
    setBusy(true);
    setMessage(null);
    try {
      await action();
    } catch (e) {
      await fail(e);
    } finally {
      setBusy(false);
    }
  };

  const copy = async (): Promise<void> => {
    if (link === null) return;
    try {
      if (typeof navigator === 'undefined' || !navigator.clipboard) throw new Error('no clipboard');
      await navigator.clipboard.writeText(link);
      setMessage('Ссылка скопирована');
    } catch {
      setMessage('Не удалось скопировать ссылку');
    }
  };

  const leave = async (): Promise<void> => {
    const next = group.members.find((m) => m.position === 2);
    const text =
      group.isOwner && next
        ? `Выйти из группы «${group.name}»? Владельцем станет ${next.displayName}.`
        : `Выйти из группы «${group.name}»?`;
    if (!(await telegram.showConfirm(text))) return;
    await run(async () => {
      await movieNightApi.leaveGroup(group.id);
      void navigate(HOME, { replace: true });
    });
  };

  const regenerate = async (): Promise<void> => {
    if (!(await telegram.showConfirm('Старая ссылка перестанет работать. Создать новую?'))) return;
    await run(async () => {
      const invite = await movieNightApi.regenerateInvite(group.id);
      onGroup({ ...group, invite });
      setMessage('Создана новая ссылка');
    });
  };

  const kick = async (m: MemberDto): Promise<void> => {
    if (!(await telegram.showConfirm(`Исключить ${m.displayName} из группы?`))) return;
    await run(async () => {
      await movieNightApi.removeMember(group.id, m.userId);
      await onRefresh();
    });
  };

  return (
    <main className="screen">
      <h1 className="screen__title">{group.name}</h1>
      {message && (
        <p className="notice" role="status">
          {message}
        </p>
      )}
      <section className="mn-section">
        <h2 className="mn-section__title">Участники</h2>
        <ul className="mn-members">
          {group.members.map((m) => (
            <li key={m.userId} className="mn-members__item">
              <span>{memberLabel(m)}</span>
              {group.isOwner && !m.isMe && (
                <button type="button" className="mn-link mn-link--danger" disabled={busy} onClick={() => void kick(m)}>
                  Исключить
                </button>
              )}
            </li>
          ))}
        </ul>
        <p className="screen__text">Порядок — по времени вступления</p>
      </section>
      <section className="mn-actions">
        {link === null ? (
          <p className="screen__text">Ссылка-приглашение недоступна</p>
        ) : (
          <button type="button" className="mn-btn" onClick={() => void copy()}>
            Скопировать ссылку
          </button>
        )}
        {group.isOwner && (
          <>
            <button
              type="button"
              className="mn-btn"
              onClick={() => void navigate(`/movienight/groups/${encodeURIComponent(group.id)}/rename`)}
            >
              Переименовать
            </button>
            <button type="button" className="mn-btn" disabled={busy} onClick={() => void regenerate()}>
              Новая ссылка-приглашение
            </button>
          </>
        )}
        <button type="button" className="mn-btn mn-btn--danger" disabled={busy} onClick={() => void leave()}>
          Выйти из группы
        </button>
      </section>
    </main>
  );
}

export function GroupScreen(): ReactElement {
  const { id = '' } = useParams();
  const load = useCallback(() => movieNightApi.getGroup(id), [id]);
  const { state, setState, reload } = useLoad(load);

  const refresh = useCallback(async (): Promise<void> => {
    try {
      setState({ status: 'ready', data: await movieNightApi.getGroup(id) });
    } catch (e) {
      const err = toApiError(e);
      if (err?.status === 404) setState({ status: 'error', error: err });
    }
  }, [id, setState]);

  if (state.status === 'loading') return <LoadingScreen />;
  if (state.status === 'error') {
    if (state.error?.status === 404) return <GroupNotFoundScreen />;
    return <LoadErrorScreen onRetry={reload} />;
  }
  return (
    <GroupView
      group={state.data}
      onGroup={(data) => setState({ status: 'ready', data })}
      onRefresh={refresh}
      onGone={(error) => setState({ status: 'error', error })}
    />
  );
}
