import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [react()],
  // 3D models live in the shared package so the landing page can use them too.
  publicDir: '../shared/public',
  server: { port: 5174 },
  // the Privy wallet's sign page (social login) is a second page next to the market
  build: { rollupOptions: { input: { main: 'index.html', sign: 'sign.html' } } },
});
