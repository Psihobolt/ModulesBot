import type { ReactElement } from 'react';
import { useNavigate } from 'react-router';
import { useMainButton } from '../../../platform/buttons';
import { MessageScreen } from '../../../platform/screens';

export const HOME = '/movienight';
export const SAVE_FAILED = 'Не удалось сохранить. Попробуйте ещё раз.';
export const ACTION_FAILED = 'Не удалось выполнить действие. Попробуйте ещё раз.';
export const NOT_OWNER = 'Это может сделать только владелец группы.';
export const INVITE_NOT_FOUND = 'Приглашение недействительно или группа больше не существует.';

/** Сообщение с MainButton «К моим группам». */
export function BackToGroupsScreen(props: { title: string }): ReactElement {
  const navigate = useNavigate();
  useMainButton({ text: 'К моим группам', onClick: () => void navigate(HOME, { replace: true }) });
  return <MessageScreen title={props.title} />;
}

export function GroupNotFoundScreen(): ReactElement {
  return <BackToGroupsScreen title="Группа не найдена" />;
}
