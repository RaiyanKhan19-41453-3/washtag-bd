import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const target = process.env.VITE_API_TARGET || 'http://localhost:8080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target, changeOrigin: true },
      '/hubs': { target, changeOrigin: true, ws: true },
      '/swagger': { target, changeOrigin: true }
    }
  },
  build: {
    outDir: 'dist',
    sourcemap: false
  }
});
