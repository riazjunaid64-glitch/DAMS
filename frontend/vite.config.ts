import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    target: "es2020",
    // Split rarely-changing vendor code into its own long-cached chunk so app updates
    // don't force users to re-download React/Router on every deploy.
    rollupOptions: {
      output: {
        manualChunks: {
          "react-vendor": ["react", "react-dom", "react-router-dom"],
        },
      },
    },
  },
  server: {
    proxy: {
      // Matches BACKEND/DAMS.Api launchSettings "http" profile (applicationUrl)
      "/api": {
        target: "http://localhost:5219",
        changeOrigin: true,
      },
      // Static uploads live on the API (wwwroot/uploads); same-origin as SPA in dev
      "/uploads": {
        target: "http://localhost:5219",
        changeOrigin: true,
      },
    },
  },
})
