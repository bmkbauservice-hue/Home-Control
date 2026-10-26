import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Alle Aufrufe an /api werden an das .NET-Backend weitergeleitet.
    // So braucht das Frontend keine feste Backend-Adresse und kein CORS.
    proxy: {
      '/api': 'http://localhost:5111',
    },
  },
})
