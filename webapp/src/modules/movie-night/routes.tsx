import type { RouteObject } from 'react-router';
import { GroupScreen } from './screens/GroupScreen';
import { GroupsScreen } from './screens/GroupsScreen';
import { JoinScreen } from './screens/JoinScreen';
import { NewGroupScreen } from './screens/NewGroupScreen';
import { RenameScreen } from './screens/RenameScreen';

export const routes: RouteObject[] = [
  { index: true, element: <GroupsScreen /> },
  { path: 'groups/new', element: <NewGroupScreen /> },
  { path: 'groups/:id', element: <GroupScreen /> },
  { path: 'groups/:id/rename', element: <RenameScreen /> },
  { path: 'join/:code', element: <JoinScreen /> },
];

const JOIN_RE = /^join_([A-Za-z0-9]{10})$/;

export function resolveStartParam(payload: string): string | null {
  const match = JOIN_RE.exec(payload);
  return match ? `/movienight/join/${match[1]}` : null;
}
