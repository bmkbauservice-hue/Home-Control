import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Alle Aufrufe an /api werden an das .NET-Backend weitergeleitet.
    // So braucht das Frontend keine feste Backend-Adresse und kein CORS.
    // Standard ist das Entwicklungs-Backend. Gegen den Dienst nur ausdrücklich, denn dort sind es echte Daten:
    // HC_BACKEND=http://127.0.0.1:5200 npm run dev
    proxy: {
      '/api': process.env.HC_BACKEND ?? 'http://localhost:5111',
    },
  },
})
