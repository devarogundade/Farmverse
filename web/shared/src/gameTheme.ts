import { useEffect, useState } from 'react';

/** The game's four colour themes (GameTheme.Ids); Orchard is the default, as in the game. */
export const THEMES = [
  { id: 'meadow', name: 'Meadow', swatch: '#2f9a3a' },
  { id: 'harvest', name: 'Harvest', swatch: '#c06a1f' },
  { id: 'orchard', name: 'Orchard', swatch: '#22b69d' },
  { id: 'mix', name: 'Mix', swatch: '#b8742a' },
] as const;
export type ThemeId = (typeof THEMES)[number]['id'];

const KEY = 'fv_theme'; // same name as the game's PlayerPrefs key

function read(): ThemeId {
  try {
    const v = localStorage.getItem(KEY);
    if (THEMES.some((t) => t.id === v)) return v as ThemeId;
  } catch {
    /* storage blocked: use the default */
  }
  return 'orchard';
}

/** Applies the saved theme to <html data-theme> and returns [theme, setTheme]. */
export function useGameTheme(): [ThemeId, (id: ThemeId) => void] {
  const [theme, setTheme] = useState<ThemeId>(read);
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try {
      localStorage.setItem(KEY, theme);
    } catch {
      /* fine: the theme just will not be remembered */
    }
  }, [theme]);
  return [theme, setTheme];
}

/** URL of a game UI sprite (web/shared/public/ui). */
export const sprite = (name: string) => `/ui/${name}.webp`;
