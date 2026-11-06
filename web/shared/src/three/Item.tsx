import { useAnimations, useGLTF } from '@react-three/drei';
import { useEffect, useMemo, useRef } from 'react';
import { Box3, Group, Vector3 } from 'three';
import { clone } from 'three/examples/jsm/utils/SkeletonUtils.js';
import { itemModelUrl } from '../nft/metadata';

/** How many copies to draw for a stack of `quantity` units. */
export function stackCount(quantity = 1): number {
  if (quantity < 10) return 1;
  if (quantity < 50) return 3;
  if (quantity < 200) return 5;
  return 7;
}

// Footprint layouts (x, z) in units of one model width.
const LAYOUTS: Record<number, [number, number][]> = {
  1: [[0, 0]],
  3: [[-0.55, 0.3], [0.55, 0.3], [0, -0.55]],
  5: [[0, 0.1], [-1.05, 0.35], [1.05, 0.35], [-0.55, -0.75], [0.55, -0.75]],
  7: [[0, 0.1], [-1.05, 0.25], [1.05, 0.25], [-0.55, -0.8], [0.55, -0.8], [-0.55, 1.0], [0.55, 1.0]],
};

interface Props {
  itemId: string;
  quantity?: number;
  /** Largest dimension of the whole arrangement, in metres. */
  size?: number;
  /** false holds animals still (list cards animate only on hover). */
  playing?: boolean;
}

/** A non-worker NFT: one model per item type, a small pile for goods with a quantity. */
export function Item({ itemId, quantity, size = 2.2, playing = true }: Props) {
  const { scene, animations } = useGLTF(itemModelUrl(itemId));
  const count = stackCount(quantity);

  const { unit, copies } = useMemo(() => {
    const box = new Box3().setFromObject(scene, true);
    const dims = box.getSize(new Vector3());
    const c = box.getCenter(new Vector3());
    const copies = LAYOUTS[count].map(([x, z], i) => {
      const obj = clone(scene);
      obj.position.set(x * dims.x - c.x, -box.min.y, z * dims.z - c.z);
      obj.rotation.y = i === 0 ? 0 : ((i * 37) % 30) * (Math.PI / 180) - 0.25;
      return obj;
    });
    const spread = count === 1 ? 1 : 2.2;
    const unit = size / Math.max(dims.x * spread, dims.y, dims.z * spread, 1e-3);
    return { unit, copies };
  }, [scene, count, size]);

  const root = useRef<Group>(null);
  const { actions } = useAnimations(animations, root);
  useEffect(() => {
    // Animals ship with Eat/Idle/Walk/… clips; prefer Idle.
    const name = Object.keys(actions).find((k) => /_Idle$/.test(k)) ?? Object.keys(actions)[0];
    const action = name ? actions[name] : undefined;
    if (!action) return;
    action.play();
    action.paused = !playing;
  }, [actions, playing]);

  return (
    <group ref={root} scale={unit}>
      {copies.map((o, i) => (
        <primitive key={i} object={o} />
      ))}
    </group>
  );
}
