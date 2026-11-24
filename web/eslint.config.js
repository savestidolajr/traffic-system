import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import prettierConfig from 'eslint-config-prettier'
import prettierPlugin from 'eslint-plugin-prettier'
import tseslint from 'typescript-eslint'

export default tseslint.config(
    { ignores: ['dist', 'src/lib/api/mock/live-traffic.ts'] },
    {
        extends: [
            js.configs.recommended,
            ...tseslint.configs.recommended,
            prettierConfig,
        ],
        files: ['**/*.{ts,tsx}'],
        languageOptions: {
            ecmaVersion: 2020,
            globals: globals.browser,
        },
        plugins: {
            'react-hooks': reactHooks,
            'react-refresh': reactRefresh,
            prettier: prettierPlugin,
        },
        rules: {
            ...reactHooks.configs.recommended.rules,
            // React Compiler readiness rules (new in react-hooks 7). This app
            // does NOT run the React Compiler (plain `react()` in vite.config),
            // so these "Compilation Skipped" advisories have zero runtime effect
            // and the only "fix" would be deleting beneficial manual memoization
            // or rewriting safe react-hook-form usage. Scoped out until/unless the
            // compiler is adopted; every behavioural hooks rule stays at error.
            'react-hooks/preserve-manual-memoization': 'off',
            'react-hooks/incompatible-library': 'off',
            'react-refresh/only-export-components': [
                'warn',
                { allowConstantExport: true },
            ],
            'prettier/prettier': 'error',
        },
    },
    {
        // Playwright e2e suites: `use()` is the fixture callback, not the React
        // `use` hook, so the React-hooks rule is a false positive here.
        files: ['e2e/**/*.{ts,tsx}'],
        rules: {
            'react-hooks/rules-of-hooks': 'off',
        },
    }
)
