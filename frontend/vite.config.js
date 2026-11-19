import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// Point the dev proxy at the running app's nginx (it forwards /api and /hubs
// to the API). Override with VITE_API_TARGET in frontend/.env or your shell.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'VITE_');
  const target = env.VITE_API_TARGET || 'http://localhost:8081';

  return {
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
  };
});
