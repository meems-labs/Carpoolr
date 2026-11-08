import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      strategies: 'injectManifest',
      srcDir: 'src',
      filename: 'sw.ts',
      registerType: 'prompt',
      injectRegister: false,
      includeAssets: ['icons/icon.svg', 'icons/apple-touch-icon.png'],
      includeManifestIcons: true,
      manifest: {
        id: '/',
        name: 'Carpoolr – Fahrgemeinschaften fürs Team',
        short_name: 'Carpoolr',
        description: 'Fahrgemeinschaften unter Kollegen planen und Fahrtkosten teilen.',
        lang: 'de',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        orientation: 'any',
        theme_color: '#176c56',
        background_color: '#f3f7f5',
        icons: [
          { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
          { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
          {
            src: '/icons/icon-maskable-512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable',
          },
        ],
      },
      injectManifest: {
        rollupFormat: 'iife',
        globPatterns: [
          'index.html',
          'theme-init.js',
          'assets/**/*.{js,css,woff,woff2}',
          'icons/*.{png,svg}',
          'manifest.webmanifest',
        ],
        globIgnores: ['**/*.map', '**/sw.js'],
        maximumFileSizeToCacheInBytes: 2 * 1024 * 1024,
      },
      devOptions: { enabled: false },
    }),
  ],
  server: {
    port: 5173,
    strictPort: true,
  },
})
