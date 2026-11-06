import { sprite } from './gameTheme';

/** FARM token mark: the clover, as on chain (the token's icon), in the game and in the favicon. */
export function FarmIcon({ size = 20 }: { size?: number }) {
  return <img src={sprite('Icon_Clover')} width={size} height={size} alt="" aria-hidden="true" style={{ display: 'inline-block', verticalAlign: 'middle', objectFit: 'contain', flexShrink: 0 }} />;
}

/** Farmverse sprout mark (black on the white logo disc). */
export function LogoMark({ size = 26, color = '#111' }: { size?: number; color?: string }) {
  return (
    <svg viewBox="0 0 32 32" width={size} height={size} aria-hidden="true">
      <path d="M16 27V11" stroke={color} strokeWidth="3" strokeLinecap="round" />
      <path d="M16 17c-4.6 0-6.8-2.9-7-6.5 4.3 0 7 2.2 7 6.5Zm0-3.2c4.6 0 6.8-2.9 7-6.5-4.3 0-7 2.2-7 6.5Zm0 9c-4 0-5.9-2.4-6.1-5.5 3.7 0 6.1 1.8 6.1 5.5Zm0 0c4 0 5.9-2.4 6.1-5.5-3.7 0-6.1 1.8-6.1 5.5Z" fill={color} />
    </svg>
  );
}
