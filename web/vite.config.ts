/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const api = process.env.TREASURYKIT_API ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // The Api serves /api and /dev; proxying avoids CORS in development.
    proxy: { '/api': api, '/dev': api },
  },
  build: {
    // Served by TreasuryKit.Api in production (single deployable).
    outDir: '../src/Host/TreasuryKit.Api/wwwroot',
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
