import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [react()],
  // Game sprites and fonts live in the shared package.
  publicDir: '../shared/public',
  server: { port: 5177 },
});
