/** Saved (hearted) items: a per-browser convenience kept in localStorage. */
import { useSyncExternalStore } from 'react';

const KEY = 'fv_saved';
const read = (): string[] => {
  try {
    const v = JSON.parse(localStorage.getItem(KEY) ?? '[]');
    return Array.isArray(v) ? v.filter((x) => typeof x === 'string') : [];
  } catch {
    return [];
  }
};

let saved = read();
const listeners = new Set<() => void>();

export function toggleSaved(id: string): boolean {
  const on = !saved.includes(id);
  saved = on ? [...saved, id] : saved.filter((x) => x !== id);
  try {
    localStorage.setItem(KEY, JSON.stringify(saved));
  } catch {
    // storage blocked: still works for this visit
  }
  listeners.forEach((l) => l());
  return on;
}

export const useSaved = () =>
  useSyncExternalStore(
    (l) => {
      listeners.add(l);
      return () => listeners.delete(l);
    },
    () => saved,
  );
