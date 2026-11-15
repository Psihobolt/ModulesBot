import { useCallback, useRef, useState, type ReactElement } from 'react';
import { useNavigate, useParams } from 'react-router';
import { useMainButton } from '../../../platform/buttons';
import { LoadErrorScreen, LoadingScreen } from '../../../platform/screens';
import { telegram } from '../../../platform/telegram';
import { movieNightApi, type InvitePreviewDto } from '../api';
import { toApiError, useLoad } from '../useLoad';
import { ACTION_FAILED, BackToGroupsScreen, INVITE_NOT_FOUND } from './common';

function JoinView(props: {
  code: string;
  preview: InvitePreviewDto;
  onBlocked: (title: string) => void;
}): ReactElement {
  const { code, preview, onBlocked } = props;
  const navigate = useNavigate();
  const [joining, setJoining] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const busy = useRef(false);

  const open = (id: string): void => void navigate(`/movienight/groups/${encodeURIComponent(id)}`, { replace: true });

  const join = async (): Promise<void> => {
    if (busy.current) return;
    busy.current = true;
    setJoining(true);
    setMessage(null);
    try {
      const result = await movieNightApi.joinByInvite(code);
      telegram.haptic.notification('success');
      open(result.group.id);
    } catch (e) {
      const err = toApiError(e);
      if (err?.status === 404) onBlocked(INVITE_NOT_FOUND);
      else if (err?.status === 409) onBlocked(err.title);
      else setMessage(ACTION_FAILED);
      busy.current = false;
      setJoining(false);
    }
  };

  const member = preview.alreadyMember && preview.groupId !== null;
  useMainButton(
    member
      ? { text: 'Открыть группу', onClick: () => open(preview.groupId!) }
      : { text: 'Вступить', loading: joining, onClick: () => void join() },
  );

  return (
    <main className="screen">
      {member ? (
        <>
          <h1 className="screen__title">Вы уже в этой группе</h1>
          <p className="screen__text">«{preview.groupName}»</p>
        </>
      ) : (
        <>
          <h1 className="screen__title">Приглашение в группу «{preview.groupName}»</h1>
          <p className="screen__text">Участники: {preview.memberNames.join(', ')}</p>
        </>
      )}
      {message && (
        <p className="mn-error" role="alert">
          {message}
        </p>
      )}
    </main>
  );
}

export function JoinScreen(): ReactElement {
  const { code = '' } = useParams();
  const load = useCallback(() => movieNightApi.getInvite(code), [code]);
  const { state, reload } = useLoad(load);
  const [blocked, setBlocked] = useState<string | null>(null);

  if (blocked !== null) return <BackToGroupsScreen title={blocked} />;
  if (state.status === 'loading') return <LoadingScreen />;
  if (state.status === 'error') {
    if (state.error?.status === 404) return <BackToGroupsScreen title={INVITE_NOT_FOUND} />;
    return <LoadErrorScreen onRetry={reload} />;
  }
  return <JoinView code={code} preview={state.data} onBlocked={setBlocked} />;
}
