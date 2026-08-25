import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The dev server proxies the api and the hub to the bot. `changeOrigin` stays
// off on purpose: the OAuth redirect is built from the Host header, and in
// development it has to point back at the Vite origin.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: false,
      },
      '/hub': {
        target: 'http://localhost:5000',
        changeOrigin: false,
        ws: true,
      },
    },
  },
})
