import { useRef, useState, type ReactElement } from 'react';
import { useMainButton } from '../../../platform/buttons';
import { ApiError } from '../../../platform/api';
import { SAVE_FAILED } from './common';

export const NAME_MAX = 64;

/** Поле названия + MainButton. onSubmit бросает ApiError; onFail может перехватить ошибку (вернуть true). */
export function NameForm(props: {
  title: string;
  label: string;
  initial: string;
  submitText: string;
  onSubmit: (name: string) => Promise<void>;
  onFail?: (e: ApiError) => boolean;
}): ReactElement {
  const [name, setName] = useState(props.initial);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const busy = useRef(false);

  const trimmed = name.trim();
  const valid = trimmed.length >= 1 && trimmed.length <= NAME_MAX;

  const submit = async (): Promise<void> => {
    if (busy.current || !valid) return;
    busy.current = true;
    setSaving(true);
    setError(null);
    try {
      await props.onSubmit(name);
    } catch (e) {
      if (e instanceof ApiError) {
        if (props.onFail?.(e)) {
          // обработано снаружи
        } else if (e.status === 400 && e.errors?.name?.[0]) setError(e.errors.name[0]);
        else if (e.status === 409 || e.status === 403) setError(e.title);
        else setError(SAVE_FAILED);
      } else setError(SAVE_FAILED);
      busy.current = false;
      setSaving(false);
    }
  };

  useMainButton({ text: props.submitText, enabled: valid && !saving, loading: saving, onClick: () => void submit() });

  return (
    <main className="screen">
      <h1 className="screen__title">{props.title}</h1>
      <form
        className="mn-form"
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        <label className="mn-form__label" htmlFor="mn-name">
          {props.label}
        </label>
        <input
          id="mn-name"
          className="mn-form__input"
          type="text"
          maxLength={NAME_MAX}
          value={name}
          autoComplete="off"
          onChange={(e) => setName(e.target.value)}
        />
        {error && (
          <p className="mn-error" role="alert">
            {error}
          </p>
        )}
      </form>
    </main>
  );
}
