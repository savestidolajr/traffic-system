// @ts-check
import { defineConfig } from 'astro/config'
import starlight from '@astrojs/starlight'

// Sidebar groups are organised by WHO is reading, not by how the documents
// were filed internally. A content operator never needs the incident runbook;
// a platform engineer never needs the review workflow. Page order inside each
// group is set by the sync script via `sidebar.order`.
//
// This list MUST match GROUPS in scripts/build-docs-site.py. A directory that
// is placed there but missing here still builds pages, and they are simply
// unreachable from the navigation; the sync fails the build rather than let
// that ship.
export default defineConfig({
    site: 'https://ibp-docs.arn.com.au',
    outDir: './dist',
    integrations: [
        starlight({
            title: 'ARN Information Bulletin Platform',
            description:
                'Operations documentation for the ARN Information Bulletin Platform.',
            tagline: 'Operations documentation',
            // No logo in the header. This is ARN's operational documentation
            // for a platform they own and run; the supplier's mark belongs in
            // the footer, not on every page of the client's runbook.
            head: [
                {
                    tag: 'meta',
                    attrs: { name: 'robots', content: 'noindex, nofollow' },
                },
            ],
            components: {
                Footer: './src/components/Footer.astro',
                Header: './src/components/Header.astro',
            },
            // The site-wide currency notice is per-page frontmatter in
            // Starlight, not a global option, so scripts/build-docs-site.py
            // emits it on every generated page (see its BANNER constant).
            //
            // `lastUpdated` stays off: a git mtime is a fact about our
            // repository, not about the platform, and the footer already
            // states currency once.
            lastUpdated: false,
            favicon: '/favicon.svg',
            // Geist is self-hosted rather than pulled from a font CDN: this
            // site also ships as an offline bundle with no network, and a
            // reader who opens that bundle must get the same page.
            customCss: [
                '@fontsource-variable/geist',
                '@fontsource-variable/geist-mono',
                './src/styles/tokens.css',
                './src/styles/arn.css',
            ],
            sidebar: [
                {
                    label: 'Start here',
                    items: [{ autogenerate: { directory: 'start-here' } }],
                },
                {
                    label: 'Using the console',
                    items: [{ autogenerate: { directory: 'console' } }],
                },
                {
                    label: 'Running the platform',
                    items: [{ autogenerate: { directory: 'operate' } }],
                },
                {
                    label: 'How it works',
                    items: [{ autogenerate: { directory: 'how-it-works' } }],
                },
                {
                    label: 'Assurance',
                    items: [{ autogenerate: { directory: 'assurance' } }],
                },
                {
                    label: 'Handover',
                    items: [{ autogenerate: { directory: 'handover' } }],
                },
                {
                    label: 'For developers',
                    items: [{ autogenerate: { directory: 'develop' } }],
                },
            ],
        }),
    ],
})
