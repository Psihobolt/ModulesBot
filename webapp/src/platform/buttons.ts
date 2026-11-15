import { useEffect, useRef } from 'react';
import { telegram, type MainButtonParams } from './telegram';

/** null → кнопка скрыта. Обработчик берётся из ref, поэтому смена onClick не плодит подписки. */
export function useMainButton(params: MainButtonParams | null): void {
  const onClickRef = useRef<(() => void) | null>(null);
  useEffect(() => {
    onClickRef.current = params?.onClick ?? null;
  });

  const visible = params !== null;
  const text = params?.text;
  const enabled = params?.enabled;
  const loading = params?.loading;

  useEffect(() => {
    if (!visible || text === undefined) return;
    return telegram.mainButton.show({
      text,
      enabled,
      loading,
      onClick: () => onClickRef.current?.(),
    });
  }, [visible, text, enabled, loading]);
}

/** null → кнопка скрыта. */
export function useBackButton(onBack: (() => void) | null): void {
  const onBackRef = useRef<(() => void) | null>(null);
  useEffect(() => {
    onBackRef.current = onBack;
  });

  const visible = onBack !== null;
  useEffect(() => {
    if (!visible) return;
    return telegram.backButton.show(() => onBackRef.current?.());
  }, [visible]);
}
