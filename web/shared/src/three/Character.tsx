import { useAnimations, useGLTF } from '@react-three/drei';
import { useEffect, useLayoutEffect, useMemo, useRef } from 'react';
import { Box3, Group, LinearFilter, Mesh, MeshStandardMaterial, Object3D, Vector3 } from 'three';
import { clone } from 'three/examples/jsm/utils/SkeletonUtils.js';
import { CHARACTER_URL, type Outfit } from '../nft/metadata';

/** Costumes that are a whole body: the base body is hidden under them. */
const FULL_BODY = new Set(['Mascot_002']);
/** A face is needed unless a costume covers the head. */
const DEFAULT_FACE = 'Male_emotion_usual_001';

interface Props {
  outfit?: Outfit;
  clip?: string;
  /** World height of the body, in metres. */
  height?: number;
  /** false holds the first frame of the clip (list cards animate only on hover). */
  playing?: boolean;
}

/**
 * A worker NFT. character.glb holds one rig and every outfit part as its own skinned mesh
 * (node extras `slot`); this shows the body plus exactly the parts `outfit` names.
 */
export function Character({ outfit = {}, clip = 'Idle_Relaxed', height = 1.8, playing = true }: Props) {
  const { scene, animations } = useGLTF(CHARACTER_URL);
  // SkeletonUtils.clone keeps each instance's skeleton separate, so many cards can animate at once.
  const model = useMemo(() => {
    const m = clone(scene);
    m.traverse((o) => {
      const mat = (o as Mesh).material as MeshStandardMaterial | undefined;
      // Palette atlas: mipmaps blur neighbouring colour cells together, so sample the base level only.
      if (mat?.map && mat.map.generateMipmaps) {
        mat.map.generateMipmaps = false;
        mat.map.minFilter = LinearFilter;
        mat.map.magFilter = LinearFilter;
        mat.map.needsUpdate = true;
      }
    });
    return m;
  }, [scene]);
  const root = useRef<Group>(null);
  const { actions } = useAnimations(animations, root);

  useLayoutEffect(() => {
    const costume = outfit.costume;
    const shown = new Set(Object.values(outfit).filter(Boolean) as string[]);
    if (!costume && !outfit.face) shown.add(DEFAULT_FACE);
    model.traverse((o: Object3D) => {
      const slot = o.userData?.slot as string | undefined;
      if (!slot) return;
      o.visible = slot === 'body' ? !(costume && FULL_BODY.has(costume)) : shown.has(o.name);
    });
  }, [model, outfit]);

  // Fit: scale so the base body is `height` tall, feet on y = 0, centred.
  const fit = useMemo(() => {
    let body: Object3D | undefined;
    model.traverse((o) => {
      if (!body && o.userData?.slot === 'body') body = o;
    });
    model.updateMatrixWorld(true);
    const box = new Box3().setFromObject(body ?? model, true);
    const size = box.getSize(new Vector3());
    const s = height / (size.y || 1);
    const c = box.getCenter(new Vector3());
    return { scale: s, position: [-c.x * s, -box.min.y * s, -c.z * s] as [number, number, number] };
  }, [model, height]);

  useEffect(() => {
    const action = actions[clip] ?? Object.values(actions)[0];
    action?.reset().fadeIn(0.25).play();
    return () => {
      action?.fadeOut(0.25);
    };
  }, [actions, clip]);

  // Runs after the effect above (which resets `paused`), so a still card stays still.
  useEffect(() => {
    const action = actions[clip] ?? Object.values(actions)[0];
    if (action) action.paused = !playing;
  }, [actions, clip, playing]);

  return (
    <group ref={root} position={fit.position} scale={fit.scale}>
      <primitive object={model} />
    </group>
  );
}

useGLTF.preload(CHARACTER_URL);
