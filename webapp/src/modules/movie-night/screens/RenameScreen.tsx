import { useCallback, useState, type ReactElement } from 'react';
import { useNavigate, useParams } from 'react-router';
import { LoadErrorScreen, LoadingScreen, MessageScreen } from '../../../platform/screens';
import { movieNightApi, type GroupDto } from '../api';
import { useLoad } from '../useLoad';
import { GroupNotFoundScreen, NOT_OWNER } from './common';
import { NameForm } from './NameForm';

function RenameView(props: { group: GroupDto; onGone: (notOwner: boolean) => void }): ReactElement {
  const navigate = useNavigate();
  const { group, onGone } = props;
  return (
    <NameForm
      title="Название группы"
      label="Название"
      initial={group.name}
      submitText="Сохранить"
      onSubmit={async (name) => {
        await movieNightApi.renameGroup(group.id, { name });
        void navigate(-1);
      }}
      onFail={(e) => {
        if (e.status === 404 && e.code === 'movienight_group_not_found') {
          onGone(false);
          return true;
        }
        if (e.status === 403) {
          onGone(true);
          return true;
        }
        return false;
      }}
    />
  );
}

export function RenameScreen(): ReactElement {
  const { id = '' } = useParams();
  const load = useCallback(() => movieNightApi.getGroup(id), [id]);
  const { state, reload } = useLoad(load);
  const [gone, setGone] = useState<'none' | 'notFound' | 'notOwner'>('none');

  if (state.status === 'loading') return <LoadingScreen />;
  if (state.status === 'error') {
    if (state.error?.status === 404) return <GroupNotFoundScreen />;
    if (state.error?.status === 403) return <MessageScreen title={NOT_OWNER} />;
    return <LoadErrorScreen onRetry={reload} />;
  }
  if (gone === 'notFound') return <GroupNotFoundScreen />;
  if (gone === 'notOwner' || !state.data.isOwner) return <MessageScreen title={NOT_OWNER} />;
  return <RenameView group={state.data} onGone={(notOwner) => setGone(notOwner ? 'notOwner' : 'notFound')} />;
}
