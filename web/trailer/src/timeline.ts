import type { ComponentType } from 'react';
import { Grow, Island, Open, Tab } from './scenes/Intro';
import { Hands, Redeem } from './scenes/Hands';
import { Market } from './scenes/Market';
import { Mint } from './scenes/Mint';
import { Community, End, Near } from './scenes/Outro';
import { Town } from './scenes/Town';

export const FPS = 30;
export const WIDTH = 1920;
export const HEIGHT = 1080;
/** How long a scene takes to dissolve in over the one before it. */
export const DISSOLVE = 0.5;

export interface Scene {
  id: string;
  label: string;
  /** Start, in seconds. Matches the times in Docs/marketing/trailer/script.md. */
  start: number;
  C: ComponentType;
}

export const SCENES: Scene[] = [
  { id: 'open', label: 'Cold open', start: 0, C: Open },
  { id: 'grow', label: 'Grow', start: 8, C: Grow },
  { id: 'island', label: 'The island grows', start: 22, C: Island },
  { id: 'tab', label: 'Play in a tab', start: 36, C: Tab },
  { id: 'mint', label: 'Mint', start: 48, C: Mint },
  { id: 'market', label: 'The market', start: 66, C: Market },
  { id: 'town', label: 'Market Town', start: 82, C: Town },
  { id: 'hands', label: 'Farmhands', start: 104, C: Hands },
  { id: 'redeem', label: 'Redeem', start: 120, C: Redeem },
  { id: 'near', label: 'Built on NEAR', start: 132, C: Near },
  { id: 'community', label: 'Community', start: 146, C: Community },
  { id: 'end', label: 'End card', start: 153, C: End },
];

export const TOTAL = 165;

export const sceneEnd = (i: number) => (i + 1 < SCENES.length ? SCENES[i + 1].start + DISSOLVE : TOTAL);
export const sceneDur = (i: number) => (i + 1 < SCENES.length ? SCENES[i + 1].start : TOTAL) - SCENES[i].start;
