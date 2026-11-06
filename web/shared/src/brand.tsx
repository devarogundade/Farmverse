import { useId } from 'react';

/** FARM token mark. */
export function FarmIcon({ size = 20 }: { size?: number }) {
  const id = useId();
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true">
      <defs>
        <linearGradient id={id} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#ffe36a" />
          <stop offset="1" stopColor="#f2a600" />
        </linearGradient>
      </defs>
      <path d="M12 1.5 21 6.75v10.5L12 22.5 3 17.25V6.75Z" fill={`url(#${id})`} stroke="#170d06" strokeWidth="1.2" strokeLinejoin="round" />
      <path d="M12 18V8.2M12 12.4c-2.6 0-3.8-1.6-3.9-3.6 2.4 0 3.9 1.2 3.9 3.6Zm0-1.8c2.6 0 3.8-1.6 3.9-3.6-2.4 0-3.9 1.2-3.9 3.6Zm0 4.6c-2.2 0-3.3-1.3-3.4-3.1 2 0 3.4 1 3.4 3.1Zm0 0c2.2 0 3.3-1.3 3.4-3.1-2 0-3.4 1-3.4 3.1Z" fill="none" stroke="#7a3d00" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
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
