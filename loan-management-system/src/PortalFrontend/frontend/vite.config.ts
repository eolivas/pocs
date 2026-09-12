import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The portal talks only to the BFF. In dev, /api is proxied to the BFF origin.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.VITE_BFF_ORIGIN ?? 'http://localhost:5100',
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
  },
});
