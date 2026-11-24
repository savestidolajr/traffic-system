/** @type {import('tailwindcss').Config} */
export default {
    darkMode: ['class'],
    content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
    theme: {
        extend: {
            colors: {
                border: 'hsl(var(--border))',
                input: 'hsl(var(--input))',
                ring: 'hsl(var(--ring))',
                background: 'hsl(var(--background))',
                foreground: 'hsl(var(--foreground))',
                primary: {
                    DEFAULT: 'hsl(var(--primary))',
                    'on-tint': 'hsl(var(--primary-on-tint))',
                    foreground: 'hsl(var(--primary-foreground))',
                },
                secondary: {
                    DEFAULT: 'hsl(var(--secondary))',
                    foreground: 'hsl(var(--secondary-foreground))',
                },
                destructive: {
                    DEFAULT: 'hsl(var(--destructive))',
                    'on-tint': 'hsl(var(--destructive-on-tint))',
                    foreground: 'hsl(var(--destructive-foreground))',
                },
                muted: {
                    DEFAULT: 'hsl(var(--muted))',
                    foreground: 'hsl(var(--muted-foreground))',
                    // De-emphasised body text. Exists because
                    // `text-muted-foreground/60` must NOT — opacity on a text
                    // token is a contrast bug by construction (in light, every
                    // step from /80 down fails AA: /70 = 3.11:1, /60 = 2.57:1,
                    // /50 = 2.14:1). Banned in .oxlintrc.json; use this.
                    'foreground-subtle': 'hsl(var(--muted-foreground-subtle))',
                },
                accent: {
                    DEFAULT: 'hsl(var(--accent))',
                    foreground: 'hsl(var(--accent-foreground))',
                },
                popover: {
                    DEFAULT: 'hsl(var(--popover))',
                    foreground: 'hsl(var(--popover-foreground))',
                },
                card: {
                    DEFAULT: 'hsl(var(--card))',
                    foreground: 'hsl(var(--card-foreground))',
                },
                success: {
                    DEFAULT: 'hsl(var(--success))',
                    foreground: 'hsl(var(--success-foreground))',
                },
                warning: {
                    DEFAULT: 'hsl(var(--warning))',
                    foreground: 'hsl(var(--warning-foreground))',
                },
                processing: {
                    DEFAULT: 'hsl(var(--processing))',
                    'on-tint': 'hsl(var(--processing-on-tint))',
                    foreground: 'hsl(var(--processing-foreground))',
                },
                rejected: {
                    DEFAULT: 'hsl(var(--rejected))',
                    'on-tint': 'hsl(var(--rejected-on-tint))',
                    foreground: 'hsl(var(--rejected-foreground))',
                },
                info: {
                    DEFAULT: 'hsl(var(--info))',
                    foreground: 'hsl(var(--info-foreground))',
                },
                // `<name>-on-tint` is the chip foreground (`text-status-x-on-tint`
                // on `bg-status-x/10`). Identical to the base in light; lighter in
                // dark, where a fill needs to stay dark for white text but chip
                // text has to out-run the tint. See src/index.css.
                status: {
                    requested: 'hsl(var(--status-requested))',
                    'requested-on-tint': 'hsl(var(--status-requested-on-tint))',
                    hereFetched: 'hsl(var(--status-here-fetched))',
                    incidents: 'hsl(var(--status-incidents))',
                    'incidents-on-tint': 'hsl(var(--status-incidents-on-tint))',
                    scriptReady: 'hsl(var(--status-script-ready))',
                    'scriptReady-on-tint':
                        'hsl(var(--status-script-ready-on-tint))',
                    audioReady: 'hsl(var(--status-audio-ready))',
                    'audioReady-on-tint':
                        'hsl(var(--status-audio-ready-on-tint))',
                    waiting: 'hsl(var(--status-waiting))',
                    approved: 'hsl(var(--status-approved))',
                    rejected: 'hsl(var(--status-rejected))',
                    'rejected-on-tint': 'hsl(var(--status-rejected-on-tint))',
                    published: 'hsl(var(--status-published))',
                    fallback: 'hsl(var(--status-fallback))',
                    failed: 'hsl(var(--status-failed))',
                    'failed-on-tint': 'hsl(var(--status-failed-on-tint))',
                },
                overlay: 'hsl(var(--overlay))',
                sidebar: {
                    DEFAULT: 'hsl(var(--sidebar-background))',
                    foreground: 'hsl(var(--sidebar-foreground))',
                    primary: 'hsl(var(--sidebar-primary))',
                    'primary-foreground':
                        'hsl(var(--sidebar-primary-foreground))',
                    accent: 'hsl(var(--sidebar-accent))',
                    'accent-foreground':
                        'hsl(var(--sidebar-accent-foreground))',
                    border: 'hsl(var(--sidebar-border))',
                    ring: 'hsl(var(--sidebar-ring))',
                },
            },
            borderRadius: {
                sm: 'var(--radius-sm)',
                md: 'var(--radius-md)',
                lg: 'var(--radius-lg)',
                xl: 'var(--radius-xl)',
            },
            fontFamily: {
                // Archivo is the approved open fallback until ARN supplies
                // licensed Obviously webfont files. Aptos remains the body face.
                sans: [
                    'Aptos',
                    'Inter',
                    'system-ui',
                    '-apple-system',
                    'sans-serif',
                ],
                display: [
                    'Archivo',
                    'Arial Narrow',
                    'Aptos Display',
                    'sans-serif',
                ],
                mono: [
                    'JetBrains Mono',
                    'Fira Code',
                    'ui-monospace',
                    'monospace',
                ],
            },
            fontSize: {
                '2xs': ['0.6875rem', { lineHeight: '1rem' }],
                '3xs': ['0.625rem', { lineHeight: '0.875rem' }],
            },
            keyframes: {
                'accordion-down': {
                    from: {
                        height: '0',
                    },
                    to: {
                        height: 'var(--radix-accordion-content-height)',
                    },
                },
                'accordion-up': {
                    from: {
                        height: 'var(--radix-accordion-content-height)',
                    },
                    to: {
                        height: '0',
                    },
                },
                shimmer: {
                    '0%': {
                        backgroundPosition: '-200% 0',
                    },
                    '100%': {
                        backgroundPosition: '200% 0',
                    },
                },
                'studio-in': {
                    '0%': {
                        opacity: '0',
                        transform: 'translateY(28px) scale(0.95)',
                    },
                    '60%': {
                        opacity: '1',
                        transform: 'translateY(-3px) scale(1.004)',
                    },
                    '100%': {
                        opacity: '1',
                        transform: 'translateY(0) scale(1)',
                    },
                },
                'beam-grow': {
                    '0%': { transform: 'scaleY(0)' },
                    '100%': { transform: 'scaleY(1)' },
                },
                'aurora-drift': {
                    '0%, 100%': { transform: 'translateX(-10%)' },
                    '50%': { transform: 'translateX(10%)' },
                },
            },
            animation: {
                'accordion-down': 'accordion-down 0.2s ease-out',
                'accordion-up': 'accordion-up 0.2s ease-out',
                shimmer: 'shimmer 2s linear infinite',
                'studio-in':
                    'studio-in 0.32s cubic-bezier(0.21, 1.02, 0.73, 1) both',
                'beam-grow':
                    'beam-grow 0.9s cubic-bezier(0.16, 1, 0.3, 1) both',
                'aurora-drift': 'aurora-drift 60s ease-in-out infinite',
            },
            boxShadow: {
                soft: '0 2px 15px -3px rgba(0, 0, 0, 0.07), 0 10px 20px -2px rgba(0, 0, 0, 0.04)',
                medium: '0 4px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)',
                large: '0 10px 40px -10px rgba(0, 0, 0, 0.1), 0 4px 25px -5px rgba(0, 0, 0, 0.1)',
                glow: '0 0 15px 0 hsl(var(--primary) / 0.15)',
                'inner-light': 'inset 0 1px 0 rgba(255, 255, 255, 0.1)',
            },
        },
    },
    plugins: [require('tailwindcss-animate')],
}
