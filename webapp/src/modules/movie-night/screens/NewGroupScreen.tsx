import type { ReactElement } from 'react';
import { useNavigate } from 'react-router';
import { movieNightApi } from '../api';
import { NameForm } from './NameForm';

export function NewGroupScreen(): ReactElement {
  const navigate = useNavigate();
  return (
    <NameForm
      title="Новая группа"
      label="Название"
      initial=""
      submitText="Создать"
      onSubmit={async (name) => {
        const group = await movieNightApi.createGroup({ name });
        void navigate(`/movienight/groups/${encodeURIComponent(group.id)}`, { replace: true });
      }}
    />
  );
}
