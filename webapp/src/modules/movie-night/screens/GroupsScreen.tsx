import { useCallback, type ReactElement } from 'react';
import { useNavigate } from 'react-router';
import { useMainButton } from '../../../platform/buttons';
import { LoadErrorScreen, LoadingScreen } from '../../../platform/screens';
import { movieNightApi, type GroupSummaryDto } from '../api';
import { pluralMembers } from '../plural';
import { useLoad } from '../useLoad';

function GroupsView(props: { groups: GroupSummaryDto[] }): ReactElement {
  const navigate = useNavigate();
  useMainButton({ text: 'Создать группу', onClick: () => void navigate('/movienight/groups/new') });
  return (
    <main className="screen">
      <h1 className="screen__title">Мои группы</h1>
      {props.groups.length === 0 ? (
        <p className="screen__text">У вас пока нет групп. Создайте группу и пригласите друзей по ссылке.</p>
      ) : (
        <ul className="module-list">
          {props.groups.map((g) => (
            <li key={g.id}>
              <button
                type="button"
                className="module-list__item"
                onClick={() => void navigate(`/movienight/groups/${encodeURIComponent(g.id)}`)}
              >
                <span className="mn-row__title">{g.name}</span>
                <span className="mn-row__hint">
                  {pluralMembers(g.memberCount)}
                  {g.isOwner ? ' · вы владелец' : ''}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}

export function GroupsScreen(): ReactElement {
  const load = useCallback(() => movieNightApi.listGroups(), []);
  const { state, reload } = useLoad(load);
  if (state.status === 'loading') return <LoadingScreen />;
  if (state.status === 'error') return <LoadErrorScreen onRetry={reload} />;
  return <GroupsView groups={state.data} />;
}
