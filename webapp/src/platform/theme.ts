import { useEffect } from 'react';
import { telegram } from './telegram';

function applyColorScheme(): void {
  document.documentElement.dataset.colorScheme = telegram.colorScheme();
}

/** Ставит data-color-scheme на <html> и следит за themeChanged. */
export function useTheme(): void {
  useEffect(() => {
    applyColorScheme();
    return telegram.onThemeChanged(applyColorScheme);
  }, []);
}
