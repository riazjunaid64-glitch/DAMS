import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
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
