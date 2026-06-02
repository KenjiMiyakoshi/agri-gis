import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true
      },
      // D301 (WD3): Phase D サーバラスタタイル経路
      '/tiles': {
        target: 'http://localhost:5080',
        changeOrigin: true
      }
    }
  }
});
