import { ramp } from './anim';
import { SceneCtx, useNow } from './clock';
import { DISSOLVE, HEIGHT, SCENES, WIDTH, sceneDur, sceneEnd } from './timeline';

/**
 * The whole trailer at 1920 × 1080. Every scene stays mounted (their art and 3D models load once,
 * up front); the clock decides which are on screen. A scene dissolves in over the one before it.
 */
export function Trailer() {
  const now = useNow();
  return (
    <div className="trailer" style={{ width: WIDTH, height: HEIGHT }}>
      {SCENES.map((s, i) => {
        const live = now >= s.start && now < sceneEnd(i);
        const t = Math.max(0, now - s.start);
        const opacity = !live ? 0 : i === 0 ? 1 : ramp(t, 0, DISSOLVE, 'inOut');
        return (
          <SceneCtx.Provider key={s.id} value={{ t, dur: sceneDur(i), live }}>
            <div className="scene" data-scene={s.id} style={{ opacity, visibility: live ? 'visible' : 'hidden', zIndex: i }}>
              <s.C />
            </div>
          </SceneCtx.Provider>
        );
      })}
    </div>
  );
}
