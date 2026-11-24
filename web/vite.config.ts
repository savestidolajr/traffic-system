import { defineConfig, configDefaults } from 'vitest/config'
import react from '@vitejs/plugin-react'
import path from 'path'

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [react()],
    test: {
        // Unit tests live under src/*.test.ts(x); the e2e/*.spec.ts files are
        // Playwright suites and must NOT be collected by vitest.
        exclude: [...configDefaults.exclude, 'e2e/**'],
        // Repairs `window.localStorage` when the host Node shadows jsdom's -
        // see the shim for why. No-op on the CI-pinned Node.
        setupFiles: ['./src/test/storage-shim.ts'],
        // Vitest's 5-second default is a machine-speed gate, not a behavioural one:
        // three component suites (verified pronunciations, filler statements, review
        // rewrite) timed out at 5s on a loaded 16-core box while passing in 2s idle,
        // and CI runs on two cores. These budgets fail a genuinely hung test and stop
        // failing a merely slow agent.
        testTimeout: 20_000,
        hookTimeout: 20_000,
    },
    build: {
        rollupOptions: {
            output: {
                manualChunks(id) {
                    if (!id.includes('node_modules')) return undefined
                    // Match a package by its DIRECTORY name. The previous
                    // substring form (`id.includes('/react/')`) would also
                    // claim `node_modules/@scope/react/…` for vendor-react —
                    // correct today only by luck of the dependency tree.
                    const pkg = (name: string) =>
                        id.includes(`/node_modules/${name}/`) ||
                        id.includes(`/node_modules/.pnpm/${name}@`)

                    // KNOWN, UNFIXED (2026-07-27): `vendor-map` (162 kB raw /
                    // 47 kB gz) is a STATIC import of the entry chunk, so all
                    // twelve routes download leaflet and ten of them never
                    // render a map — Lighthouse reports 38 kB of it unused on
                    // each. The entry takes exactly ONE binding from it (a
                    // leaflet LatLng/project helper), so some module in the
                    // eager graph — everything `main.tsx` → `App.tsx` reaches,
                    // which includes CommandPalette and its entity hooks — pulls
                    // leaflet in. Every route component IS lazy, so this is not
                    // a routing problem. Naming the culprit needs a bundle
                    // analyzer (rollup-plugin-visualizer); guessing at it risks
                    // breaking the map for a 47 kB win. Do that before editing
                    // this function again.
                    if (
                        pkg('react') ||
                        pkg('react-dom') ||
                        pkg('react-router') ||
                        pkg('react-router-dom') ||
                        pkg('scheduler')
                    )
                        return 'vendor-react'
                    if (pkg('leaflet') || pkg('react-leaflet'))
                        return 'vendor-map'
                    if (id.includes('/node_modules/@tanstack/'))
                        return 'vendor-tanstack'
                    if (
                        id.includes('/node_modules/@radix-ui/') ||
                        pkg('cmdk') ||
                        pkg('sonner')
                    )
                        return 'vendor-ui'
                    return undefined
                },
            },
        },
    },
    resolve: {
        alias: {
            '@': path.resolve(__dirname, './src'),
        },
    },
    server: {
        proxy: {
            '/api': {
                target: 'http://localhost:7071',
                changeOrigin: true,
                secure: false,
                // Local dev only: the backend AuthorizationMiddleware requires an
                // X-MS-CLIENT-PRINCIPAL header (injected by Azure Easy Auth in prod).
                // Nothing injects it when the SPA talks to the local Functions host, so
                // the dev proxy simulates an Admin principal — matching scripts/*.sh.
                // Decoded: {"auth_typ":"aad","role_typ":"roles","claims":[
                //   {"typ":"name","val":"Local Dev"},{"typ":"roles","val":"Admin"}, ...]}
                configure: (proxy) => {
                    const DEV_PRINCIPAL =
                        'eyJhdXRoX3R5cCI6ImFhZCIsIm5hbWVfdHlwIjoibmFtZSIsInJvbGVfdHlwIjoicm9sZXMiLCJjbGFpbXMiOlt7InR5cCI6Im5hbWUiLCJ2YWwiOiJMb2NhbCBEZXYifSx7InR5cCI6InJvbGVzIiwidmFsIjoiQWRtaW4ifSx7InR5cCI6Imh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vaWRlbnRpdHkvY2xhaW1zL29iamVjdGlkZW50aWZpZXIiLCJ2YWwiOiIwMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDAifV19'
                    proxy.on(
                        'proxyReq',
                        (proxyReq: {
                            setHeader: (n: string, v: string) => void
                        }) => {
                            proxyReq.setHeader(
                                'X-MS-CLIENT-PRINCIPAL',
                                DEV_PRINCIPAL
                            )
                        }
                    )
                },
            },
        },
    },
})
