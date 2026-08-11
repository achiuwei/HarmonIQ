import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  define: { 'process.env.NODE_ENV': JSON.stringify('production') },
  build: {
    lib: { entry: 'src/main.ts', formats: ['iife'], name: 'HarmonIQModule', fileName: () => 'harmoniq-module.js' },
    outDir: '../backend/HarmonIQ.Api/wwwroot/embed',
    emptyOutDir: true,
  },
  server: { proxy: { '/api': 'http://localhost:5080' } },
});
