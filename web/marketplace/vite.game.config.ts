import { defineConfig } from 'vite';

/**
 * privy-near.js: the "Email or Google" wallet for the game page (src/privy-near-game.ts), one ES module next to
 * farmverse-page.js. deployment/vercel.sh builds it into the game's folder:
 *   npx vite build --config vite.game.config.ts --outDir <game folder>
 */
export default defineConfig({
  publicDir: false,
  build: {
    emptyOutDir: false,
    lib: { entry: 'src/privy-near-game.ts', formats: ['es'], fileName: () => 'privy-near.js' },
    // one file: the game page loads it by name
    rollupOptions: { output: { codeSplitting: false } as never },
    minify: true,
    target: 'es2022',
  },
});
