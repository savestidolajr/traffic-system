#!/usr/bin/env node
// =============================================================================
// UI inventory — a GENERATED, machine-readable index of every shared component
// in `web/`: what it is, the props it requires, the variants it offers, and how
// widely it is already used.
//
// Why it exists: an agent that cannot see what the codebase already has will
// build a second one. The 2026-07-27 accessibility sweep is the worked example —
// 28 `<SelectTrigger>` call sites had shipped with no accessible name because
// nothing told anyone the primitive needed one. That specific hole is now closed
// in the type system (`select.tsx`), which is strictly better than a document;
// this inventory closes the general one: "what do we already have, and what does
// it demand of me?", answered from the source rather than from memory.
//
// Generated, never hand-edited — a hand-written inventory drifts, and a drifted
// inventory is worse than none. `--check` fails when the committed file no
// longer matches the source, which is what keeps that promise (verify.sh runs it).
//
// Usage:
//   node scripts/ui-inventory.mjs           # regenerate docs/UI-INVENTORY.md (+ .json)
//   node scripts/ui-inventory.mjs --check   # exit 1 if the committed file is stale
// =============================================================================
import {
    readFileSync,
    writeFileSync,
    existsSync,
    readdirSync,
    statSync,
} from 'fs'
import { resolve, dirname, relative, join } from 'path'
import { fileURLToPath } from 'url'
import ts from 'typescript'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const SRC = resolve(ROOT, 'src')
const OUT_MD = resolve(ROOT, 'docs', 'UI-INVENTORY.md')
const OUT_JSON = resolve(ROOT, 'docs', 'ui-inventory.json')
const CHECK = process.argv.includes('--check')

// Two tiers, because they answer different questions. Primitives are the
// vocabulary you must not re-invent; shared components are the assembled
// patterns you should reach for before composing your own.
const GROUPS = [
    {
        id: 'primitives',
        title: 'Primitives — `src/components/ui/`',
        blurb: 'shadcn/Radix-derived building blocks. Restyle these; never fork one into a feature folder.',
        dir: resolve(SRC, 'components', 'ui'),
        recursive: true,
    },
    {
        id: 'shared',
        title: 'Shared components — `src/components/`',
        blurb: 'App-level patterns assembled from the primitives and reused across features.',
        dir: resolve(SRC, 'components'),
        recursive: false,
    },
]

const rel = (p) => relative(ROOT, p).split('\\').join('/')

function tsxFiles(dir, recursive) {
    if (!existsSync(dir)) return []
    const out = []
    for (const name of readdirSync(dir).sort()) {
        const full = join(dir, name)
        if (statSync(full).isDirectory()) {
            if (recursive) out.push(...tsxFiles(full, true))
            continue
        }
        if (!name.endsWith('.tsx')) continue
        if (name.endsWith('.test.tsx')) continue
        out.push(full)
    }
    return out
}

/** First sentence of the leading JSDoc — the one-line "what is this". */
function summaryOf(node, source) {
    const ranges = ts.getLeadingCommentRanges(source, node.pos) ?? []
    const doc = ranges
        .map((r) => source.slice(r.pos, r.end))
        .filter((c) => c.startsWith('/**'))
        .pop()
    if (!doc) return null
    const body = doc
        .replace(/^\/\*\*/, '')
        .replace(/\*\/$/, '')
        .split('\n')
        .map((l) => l.replace(/^\s*\*ractice?\s?/, '').replace(/^\s*\*\s?/, ''))
        .join(' ')
        .replace(/\s+/g, ' ')
        .trim()
    if (!body) return null
    const stop = body.search(/\.(\s|$)/)
    return (stop === -1 ? body : body.slice(0, stop + 1)).trim()
}

/**
 * Props of an exported component, from the CHECKER rather than the syntax — so
 * an intersection, a `ComponentPropsWithRef<typeof RadixThing>`, or a
 * discriminated union all resolve to the real surface a caller must satisfy.
 *
 * A union props type (the shape `select.tsx` uses to demand one of `aria-label`
 * / `aria-labelledby`) has no single member list; the constituents are reported
 * so the requirement survives into the doc.
 */
function propsOf(checker, signature) {
    const param = signature?.parameters?.[0]
    if (!param) return { required: [], alternatives: [] }
    const type = checker.getTypeOfSymbolAtLocation(
        param,
        param.valueDeclaration ?? param.declarations?.[0]
    )
    const collect = (t) =>
        t
            .getProperties()
            .filter((p) => !(p.flags & ts.SymbolFlags.Optional))
            .map((p) => p.getName())
            // Framework noise every component inherits — not part of the contract.
            .filter((n) => !['key', 'ref', 'children'].includes(n))
            .sort()

    if (type.isUnion()) {
        const alternatives = type.types.map(collect).filter((a) => a.length)
        // Anything present in EVERY branch is unconditionally required.
        const required = alternatives.length
            ? alternatives[0].filter((n) =>
                  alternatives.every((a) => a.includes(n))
              )
            : []
        return {
            required,
            alternatives: alternatives
                .map((a) => a.filter((n) => !required.includes(n)))
                .filter((a) => a.length),
        }
    }
    return { required: collect(type), alternatives: [] }
}

/**
 * cva variant groups, keyed by the component they belong to.
 *
 * A file can declare several (`alertVariants`, `badgeVariants`, …) and the
 * convention here is `<component>Variants = cva(…)`, so the declaration name is
 * what attributes the options — otherwise every export in `alert.tsx` claims
 * Alert's `variant` prop, which is worse than reporting nothing.
 */
function variantsIn(source) {
    const out = {}
    const decl = /(?:const|let)\s+([A-Za-z_$][\w$]*)Variants\s*=\s*cva\(/g
    let d
    while ((d = decl.exec(source))) {
        const base = d[1]
        const start = source.indexOf('variants: {', d.index)
        if (start === -1) continue
        // Balance braces from `variants: {` so nested option objects are included.
        let depth = 0
        let end = start
        for (let i = source.indexOf('{', start); i < source.length; i++) {
            if (source[i] === '{') depth++
            else if (source[i] === '}') {
                depth--
                if (depth === 0) {
                    end = i
                    break
                }
            }
        }
        // Blank out every string literal first: the option VALUES are Tailwind
        // class strings full of `hover:` / `dark:` / `pointer-coarse:`, and a
        // key regex run over them reads those variant prefixes as option names.
        const block = source
            .slice(start, end)
            .replace(/'[^'\n]*'|"[^"\n]*"|`[^`]*`/g, "''")
        const groups = {}
        // One nesting level down: `name: { option: '…', option2: '…' }`
        const re = /(\w+):\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}/g
        let m
        while ((m = re.exec(block))) {
            if (m[1] === 'variants') continue
            const opts = [
                ...m[2].matchAll(/(?:^|[\s,{])['"]?([\w-]+)['"]?\s*:/g),
            ].map((o) => o[1])
            if (opts.length) groups[m[1]] = opts
        }
        if (Object.keys(groups).length)
            out[base[0].toUpperCase() + base.slice(1)] = groups
    }
    return out
}

function build() {
    const configPath = resolve(ROOT, 'tsconfig.json')
    const program = ts.createProgram({
        rootNames: GROUPS.flatMap((g) => tsxFiles(g.dir, g.recursive)),
        options: {
            jsx: ts.JsxEmit.ReactJSX,
            target: ts.ScriptTarget.ESNext,
            module: ts.ModuleKind.ESNext,
            moduleResolution: ts.ModuleResolutionKind.Bundler,
            strict: true,
            skipLibCheck: true,
            noEmit: true,
            baseUrl: ROOT,
            paths: { '@/*': ['src/*'] },
            configFilePath: existsSync(configPath) ? configPath : undefined,
        },
    })
    const checker = program.getTypeChecker()

    // Usage counts: how many times each exported name appears as a JSX tag
    // anywhere under src/, excluding its own definition file.
    const allSources = []
    const walkSrc = (dir) => {
        for (const name of readdirSync(dir)) {
            const full = join(dir, name)
            if (statSync(full).isDirectory()) walkSrc(full)
            else if (/\.(tsx|ts)$/.test(name)) allSources.push(full)
        }
    }
    walkSrc(SRC)
    const bodies = new Map(allSources.map((f) => [f, readFileSync(f, 'utf8')]))

    // cva declarations live beside the component as often as inside it
    // (`button-variants.ts`), so build one project-wide map before walking.
    const cvaByComponent = {}
    for (const [, body] of bodies)
        Object.assign(cvaByComponent, variantsIn(body))

    const groups = []
    for (const g of GROUPS) {
        const components = []
        for (const file of tsxFiles(g.dir, g.recursive)) {
            const sf = program.getSourceFile(file)
            if (!sf) continue
            const text = sf.getFullText()
            const moduleSymbol = checker.getSymbolAtLocation(sf)
            if (!moduleSymbol) continue

            for (const exp of checker.getExportsOfModule(moduleSymbol)) {
                const name = exp.getName()
                // Components only: PascalCase exports that are callable.
                if (!/^[A-Z]/.test(name)) continue
                const decl =
                    exp.valueDeclaration ??
                    exp.declarations?.[0] ??
                    checker.getAliasedSymbol?.(exp)?.valueDeclaration
                if (!decl) continue
                const type = checker.getTypeOfSymbolAtLocation(exp, decl)
                const sig = type.getCallSignatures()[0]
                if (!sig) continue

                const { required, alternatives } = propsOf(checker, sig)
                // `<` closes the set because a generic component is written
                // `<ScopeGroupedList<FillerRow> …>` — omitting it undercounted
                // every generic to zero and filed them as dead.
                const usages = [...bodies.entries()].filter(
                    ([f, body]) =>
                        f !== file && new RegExp(`<${name}[\\s/><]`).test(body)
                )
                components.push({
                    name,
                    file: rel(file),
                    summary: summaryOf(decl, text),
                    required,
                    alternatives,
                    variants: cvaByComponent[name] ?? {},
                    usageCount: usages.length,
                    usedIn: usages.slice(0, 3).map(([f]) => rel(f)),
                })
            }
        }
        components.sort((a, b) => a.name.localeCompare(b.name))
        groups.push({ ...g, dir: rel(g.dir), components })
    }
    return groups
}

function render(groups) {
    const esc = (s) => String(s).replace(/\|/g, '\\|')
    let md = `# UI inventory

**Generated — do not edit.** Regenerate with \`npm run ui:inventory\`; \`scripts/verify.sh\` fails when this file is stale.

The vocabulary this app already speaks. Read it before building a new component: if something here covers the job, use it. Companion to \`docs/DESIGN-SYSTEM.md\` (icons, type, colour) — that one governs *how things look*, this one governs *what exists*.

Columns: **Requires** is the props a caller MUST pass (\`children\`/\`ref\`/\`key\` elided). **or** marks a props union — satisfy exactly one branch. **Variants** are the cva options declared in the same file. **Used** counts distinct files that render the component.

`
    for (const g of groups) {
        md += `## ${g.title}\n\n${g.blurb}\n\n`
        md += `| Component | Requires | Variants | Used | What it is |\n|---|---|---|---|---|\n`
        for (const c of g.components) {
            const req = [
                c.required.map((r) => `\`${r}\``).join(' '),
                ...c.alternatives.map(
                    (a) => `(${a.map((r) => `\`${r}\``).join(' ')})`
                ),
            ]
                .filter(Boolean)
                .join(c.alternatives.length > 1 ? ' **or** ' : ' ')
            const vars = Object.entries(c.variants)
                .map(([k, v]) => `${k}: ${v.join('/')}`)
                .join('<br>')
            md += `| **${c.name}**<br><sub>${c.file.replace('src/components/', '')}</sub> | ${req || '—'} | ${esc(vars) || '—'} | ${c.usageCount} | ${esc(c.summary ?? '')} |\n`
        }
        md += `\n`
    }
    const unused = groups.flatMap((g) =>
        g.components.filter((c) => c.usageCount === 0).map((c) => c.name)
    )
    md += `## Exported but never rendered\n\n${
        unused.length
            ? `${unused.map((n) => `\`${n}\``).join(', ')}\n\nEach is either a sub-part consumed through a sibling export, or dead. \`npm run knip\` decides which.\n`
            : 'None.\n'
    }`
    return md
}

const groups = build()
const md = render(groups)
const json = JSON.stringify(groups, null, 2)

if (CHECK) {
    const stale = !existsSync(OUT_MD) || readFileSync(OUT_MD, 'utf8') !== md
    if (stale) {
        console.error(
            `✗ ${rel(OUT_MD)} is stale — run "npm run ui:inventory" and commit the result.`
        )
        process.exit(1)
    }
    console.log(`✓ ${rel(OUT_MD)} matches src/components/`)
    process.exit(0)
}

writeFileSync(OUT_MD, md)
writeFileSync(OUT_JSON, json)
const total = groups.reduce((n, g) => n + g.components.length, 0)
console.log(
    `✓ ${total} components across ${groups.length} groups → ${rel(OUT_MD)} + ${rel(OUT_JSON)}`
)
