import { Bounds, ContactShadows, OrbitControls, PerspectiveCamera, View } from '@react-three/drei';
import { Canvas, useFrame } from '@react-three/fiber';
import { Component, Suspense, useRef, type ReactNode } from 'react';
import type { Group } from 'three';
import type { NftMetadata } from '../nft/metadata';
import { Character } from './Character';
import { Item } from './Item';

/**
 * One WebGL canvas for the whole page; every <NftStage> is a drei <View> (a scissored viewport
 * that tracks its DOM box). Browsers cap WebGL contexts at ~16, so one canvas per card would not
 * survive a grid.
 */
export function SharedCanvas() {
  return (
    <Canvas
      className="gl-layer"
      eventSource={document.getElementById('root')!}
      dpr={[1, 2]}
      flat
      gl={{ antialias: true, alpha: true }}
    >
      <View.Port />
    </Canvas>
  );
}

/** The 3D model an NFT's metadata describes. */
export function NftModel({ meta, clip, playing = true }: { meta: NftMetadata; clip?: string; playing?: boolean }) {
  if (meta.type === 'worker') return <Character outfit={meta.outfit} clip={clip} playing={playing} />;
  if (!meta.item_id) return null;
  return <Item itemId={meta.item_id} quantity={meta.quantity} size={meta.type === 'good' ? 1.9 : 2.3} playing={playing} />;
}

/** Spins while active; eases in and out instead of starting/stopping dead. */
function Turntable({ speed, active, children }: { speed: number; active: boolean; children: ReactNode }) {
  const g = useRef<Group>(null);
  const v = useRef(0);
  useFrame((_, dt) => {
    v.current += ((active ? speed : 0) - v.current) * Math.min(1, dt * 4);
    if (g.current && Math.abs(v.current) > 1e-4) g.current.rotation.y += dt * v.current;
  });
  return <group ref={g}>{children}</group>;
}

class ModelBoundary extends Component<{ children: ReactNode; resetKey: string }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() {
    return { failed: true };
  }
  componentDidUpdate(prev: { resetKey: string }) {
    if (prev.resetKey !== this.props.resetKey && this.state.failed) this.setState({ failed: false });
  }
  render() {
    return this.state.failed ? null : this.props.children;
  }
}

interface StageProps {
  meta: NftMetadata;
  className?: string;
  /** Orbit + zoom with the mouse (hero / studio). */
  interactive?: boolean;
  /** Camera framing: hero is tall and close, card is square. */
  framing?: 'hero' | 'card';
  clip?: string;
  /** Play animation / spin. List cards pass their hover state; hero and studio leave it on. */
  animate?: boolean;
}

export function NftStage({ meta, className, interactive = false, framing = 'card', clip, animate = true }: StageProps) {
  const worker = meta.type === 'worker';
  const target: [number, number, number] = worker ? [0, framing === 'hero' ? 0.84 : 1.05, 0] : [0, 0.55, 0];
  const camPos: [number, number, number] = worker
    ? framing === 'hero' ? [0.8, 1.15, 4.9] : [0.6, 1.25, 3.3]
    : framing === 'hero' ? [3.6, 2.6, 5.2] : [3.2, 2.4, 4.3];

  return (
    <View className={className}>
      <PerspectiveCamera makeDefault position={camPos} fov={framing === 'hero' ? 26 : 30} onUpdate={(c) => c.lookAt(...target)} />
      {/* Tone mapping is off (flat) to keep the palette atlases true, so keep the light sum near 1. */}
      <hemisphereLight args={['#fff4e6', '#3a4a52', 0.75]} />
      <directionalLight position={[3, 6, 4]} intensity={1.35} />
      <directionalLight position={[-4, 2, -3]} intensity={0.45} color="#bfe6ff" />
      <ModelBoundary resetKey={JSON.stringify(meta)}>
        <Suspense fallback={null}>
          {worker ? (
            <NftModel meta={meta} clip={clip} playing={animate} />
          ) : interactive ? (
            // Items vary in shape (a wide tractor, a tall silo) and stages vary in aspect: fit each one.
            <Bounds fit clip observe margin={1.15}>
              <NftModel meta={meta} clip={clip} playing={animate} />
            </Bounds>
          ) : (
            <Turntable speed={0.6} active={animate}>
              <NftModel meta={meta} clip={clip} playing={animate} />
            </Turntable>
          )}
        </Suspense>
      </ModelBoundary>
      <ContactShadows position={[0, 0.001, 0]} opacity={0.45} scale={6} blur={2.4} far={2.5} resolution={256} />
      {interactive && (
        <OrbitControls
          makeDefault
          target={target}
          enablePan={false}
          minDistance={2.2}
          maxDistance={9}
          minPolarAngle={0.2}
          maxPolarAngle={Math.PI / 2 - 0.05}
          autoRotate={!worker}
          autoRotateSpeed={1.2}
        />
      )}
    </View>
  );
}
