# Farmverse trailer

The NEAR HACKATHON submission video (2:45, 1920 × 1080, 30 fps), built in the game's own UI kit and rendered
frame by frame to MP4. Script, voiceover and the gameplay shot list: [Docs/marketing/trailer](../../Docs/marketing/trailer/).

## Preview

```bash
npm run dev:trailer        # from web/, then open http://localhost:5179
```

Space plays and pauses, ← → step a second (Shift: one frame), the scene buttons jump. `?t=48` starts at 0:48.
If a voiceover or music file is in `media/audio`, the preview plays it in sync.

## Your media

| Put here | Used for |
|---|---|
| `media/clips/clip1.mp4` … `clip5.mp4` | Gameplay slots (see the shot list). A missing clip shows a labelled placeholder. Trim with `start` in `src/media.ts`. |
| `media/audio/voiceover.wav` (or `.mp3`, `.m4a`) | Mixed in at full level. |
| `media/audio/music.mp3` (or `.wav`, `.m4a`) | Mixed under the voice at 20 % (70 % with no voice), faded out at the end. |
| `media/brand/near-logo.svg` (optional) | Replaces the "NEAR" word on the Built on NEAR card with the official logo. |

Clips and audio are git-ignored.

## Render

With the dev server running:

```bash
node scripts/render.mjs                       # → out/farmverse-trailer.mp4
node scripts/render.mjs --from 48 --to 66     # one part, for checking
node scripts/render.mjs --stills 12,53,101    # PNG frames in out/stills
```

It drives Chrome headless (`--chrome <path>` or `CHROME_PATH` if it isn't in the usual place) and ffmpeg
(`ffmpeg-static`). Every frame is drawn from the trailer clock alone, so a render always comes out the same.

## Changing the timing

Scene start times live in `src/timeline.ts` and match the script. Inside a scene, every beat is a time in
seconds from the scene's start (`useT()`), so moving a beat to match your read is one number.
