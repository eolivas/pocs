import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The application frontend talks only to the Application BFF. In dev, /api is
// proxied to the Application BFF origin. Runs on 5174 (Portal Frontend uses 5173).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5174,
    proxy: {
      '/api': {
        target: process.env.VITE_BFF_ORIGIN ?? 'http://localhost:5101',
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
  },
});
