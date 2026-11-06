import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [react()],
  // 3D models live in the shared package so the landing page can use them too.
  publicDir: '../shared/public',
  server: { port: 5174 },
});
