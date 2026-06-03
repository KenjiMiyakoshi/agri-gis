import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  // D'202 (WD'2): admin-style.html を分離エントリ化
  build: {
    rollupOptions: {
      input: {
        main: resolve(__dirname, 'index.html'),
        adminStyle: resolve(__dirname, 'admin-style.html')
      }
    }
  },
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
