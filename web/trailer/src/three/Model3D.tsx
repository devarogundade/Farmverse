import { NftModel } from '@farmverse/shared/three/NftStage';
import type { NftMetadata } from '@farmverse/shared/nft/metadata';
import { Center, ContactShadows, PerspectiveCamera } from '@react-three/drei';
import { Canvas } from '@react-three/fiber';
import { Suspense, useState, type CSSProperties } from 'react';
import { useT } from '../clock';

interface Props {
  meta: NftMetadata;
  className?: string;
  style?: CSSProperties;
  /** Turntable speed in radians a second (items only). */
  spin?: number;
  /** Starting angle. */
  angle?: number;
  clip?: string;
  /** Pull the camera back (1 = as the market frames it). */
  distance?: number;
}

/**
 * One NFT in 3D, drawn from the trailer clock. Each has its own small canvas so it fades and
 * stacks with the page like any element; the clock advances every canvas together
 * (frameloop "never"), so the renderer gets the same frame every time.
 */
export function Model3D({ meta, className, style, spin = 0.45, angle = -0.5, clip, distance = 1 }: Props) {
  const t = useT();
  // Items are centred on the origin; the shadow goes under whatever height the model turns out to be.
  const [floor, setFloor] = useState(-0.8);
  const worker = meta.type === 'worker';
  // Workers stand on the floor, framed head to toe; items are centred and turn about their middle.
  const target: [number, number, number] = worker ? [0, 0.95, 0] : [0, 0, 0];
  const base: [number, number, number] = worker ? [0.4, 1.12, 4.1] : [3.2, 2.4, 4.3];
  const cam = base.map((v, i) => (i === 1 ? target[1] + (v - target[1]) * distance : v * distance)) as [number, number, number];
  return (
    <div className={className} style={style}>
      <Canvas frameloop="never" flat dpr={2} resize={{ offsetSize: true, scroll: false }} gl={{ antialias: true, alpha: true, preserveDrawingBuffer: true }}>
        <PerspectiveCamera makeDefault position={cam} fov={worker ? 26 : 30} onUpdate={(c) => c.lookAt(...target)} />
        <hemisphereLight args={['#fff4e6', '#3a4a52', 0.75]} />
        <directionalLight position={[3, 6, 4]} intensity={1.35} />
        <directionalLight position={[-4, 2, -3]} intensity={0.45} color="#bfe6ff" />
        <Suspense fallback={null}>
          {worker ? (
            <group rotation-y={angle}>
              <NftModel meta={meta} clip={clip} />
            </group>
          ) : (
            <group rotation-y={angle + t * spin}>
              <Center onCentered={({ height }) => setFloor(-height / 2)}>
                <NftModel meta={meta} clip={clip} />
              </Center>
            </group>
          )}
        </Suspense>
        <ContactShadows position={[0, worker ? -0.06 : floor - 0.01, 0]} opacity={0.45} scale={6} blur={2.4} far={2.5} resolution={256} />
      </Canvas>
    </div>
  );
}
