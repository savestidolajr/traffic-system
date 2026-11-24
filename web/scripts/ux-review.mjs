#!/usr/bin/env node
// =============================================================================
// UX review — LLM-authored SUBJECTIVE findings, annotated onto the audit
// screenshots. This is the manual counterpart to the machine ux-audit harness:
//
//   ux-audit.mjs   → deterministic capture (screenshots) + axe/overflow pre-scan
//                    → audit/axe-findings.html (machine)
//   ux-review.mjs  → YOU (the reviewing LLM) append your own findings, each with
//                    a box, and it rebuilds audit/review.html (the human review doc)
//
// Workflow (run by an LLM going through the UI):
//   1. npm run audit                         # writes after/<id>.<vp>.png (clean)
//                                            #  + after/<id>.<vp>.marked.png (Set-of-Mark)
//                                            #  + data/<id>.<vp>.marks.json (mark→rect legend)
//   2. node scripts/ux-review.mjs pages      # list reviewable screens
//   3. `read` after/<id>.<vp>.marked.png — every significant element wears a pink
//      numbered badge. Identify issues and note the MARK NUMBER on each — do NOT
//      estimate coordinates (VLMs are unreliable at that; the box is resolved from
//      the element's DOM rect). `marks --page <id> --vp <vp>` lists mark→element.
//   4. node scripts/ux-review.mjs add --json '[{ ...finding... }]'
//   5. open audit/review.html                # auto-rebuilt after every add/merge/rm
//
// Parallel fan-out (full reviews): spawn N `designer` (vision) subagents over
// disjoint page sets; each views the *.marked.png images and WRITES its findings
// array to audit/incoming/<id>.json (NOT via this tool — avoids a write race).
// Then aggregate single-writer:  node scripts/ux-review.mjs merge [--replace]
//
// A finding (prefer `mark`/`marks` over `box`):
//   { "page":"13-guidance", "vp":"mobile", "mark": 12,
//     "confidence":0.9, "note":"optional detail", "tags":["overflow","mobile"] }
//
//   mark   = one Set-of-Mark number → exact box from the DOM rect. BEST.
//   marks  = [n,...] when the issue spans several elements (union box).
//   box    = "x,y,w,h" as a % fallback for a region with no element; eyeballed.
//            Omit all three for a general page-level note (no overlay drawn).
//   vp     = a capture VARIANT, not just a viewport: `desktop` | `mobile` | … are
//            the light theme, `desktop-dark` | `mobile-dark` | … the dark one.
//            Geometry is identical across themes, so a dark finding may cite the
//            marks from either legend — but file it against the variant you
//            actually looked at, so review.html overlays the right screenshot.
// severity: critical | serious | moderate | minor | note
// confidence: 0-1 self-rated certainty this is a GENUINE, objective defect.
//             `merge --min-confidence X` drops incoming findings below X (the
//             noise gate — raise it to keep only high-conviction findings).
//
// Commands: pages | add | merge [--replace] [--min-confidence X] | marks --page id [--vp] | list | rm <id> | clear | build
// =============================================================================
import { existsSync, readFileSync, writeFileSync, readdirSync } from 'fs'
import { resolve, dirname, relative } from 'path'
import { fileURLToPath } from 'url'
import { PNG } from 'pngjs'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'audit')
const SHOTS = resolve(ROOT, 'after')
const STORE = resolve(ROOT, 'review-notes.json')
const OUT = resolve(ROOT, 'review.html')
const INC = resolve(ROOT, 'incoming')
const CWD = process.cwd()

const SEVERITIES = ['critical', 'serious', 'moderate', 'minor', 'note']
// Capture variants the audit produces, narrow→wide then light→dark, for display
// order only. Otherwise the harness is variant-agnostic: any id present in
// audit/after/ is accepted. A `-dark` suffix is just another variant id, which
// is why the theme axis needed no new concept here.
const VP_ORDER = [
    'mobile',
    'mobile-dark',
    'tablet',
    'tablet-dark',
    'laptop',
    'laptop-dark',
    'desktop',
    'desktop-dark',
]
const vpRank = (vp) => {
    const i = VP_ORDER.indexOf(vp)
    return i === -1 ? VP_ORDER.length : i
}
const COLOR = {
    critical: '#ff5c7a',
    serious: '#ff9f43',
    moderate: '#ffd93d',
    minor: '#5cc8ff',
    note: '#9aa7c7',
}

const rel = (p) => relative(CWD, p)
const esc = (s) =>
    String(s ?? '').replace(
        /[&<>"]/g,
        (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]
    )

function loadStore() {
    if (!existsSync(STORE)) return { version: 1, findings: [] }
    try {
        const s = JSON.parse(readFileSync(STORE, 'utf8'))
        if (!Array.isArray(s.findings)) s.findings = []
        return s
    } catch {
        return { version: 1, findings: [] }
    }
}
const saveStore = (s) => writeFileSync(STORE, JSON.stringify(s, null, 2))

const shotName = (page, vp) => `${page}.${vp}.png`
const shotPath = (page, vp) => resolve(SHOTS, shotName(page, vp))

function listShots() {
    if (!existsSync(SHOTS)) return []
    const map = {}
    for (const f of readdirSync(SHOTS)) {
        const m = f.match(/^(.*)\.([a-z0-9-]+)\.png$/)
        if (m && m[2] !== 'marked') (map[m[1]] ??= []).push(m[2])
    }
    return Object.entries(map)
        .map(([page, vps]) => ({
            page,
            vps: vps.sort((a, b) => vpRank(a) - vpRank(b)),
        }))
        .sort((a, b) => a.page.localeCompare(b.page))
}

function dims(page, vp) {
    try {
        const p = PNG.sync.read(readFileSync(shotPath(page, vp)))
        return `${p.width}x${p.height}`
    } catch {
        return '?'
    }
}

function parseArgs(argv) {
    const a = { _: [] }
    for (let i = 0; i < argv.length; i++) {
        const t = argv[i]
        if (t.startsWith('--')) {
            const eq = t.indexOf('=')
            if (eq >= 0) a[t.slice(2, eq)] = t.slice(eq + 1)
            else if (argv[i + 1] !== undefined && !argv[i + 1].startsWith('--'))
                a[t.slice(2)] = argv[++i]
            else a[t.slice(2)] = true
        } else a._.push(t)
    }
    return a
}

function normBox(box) {
    if (box == null) return null
    let b = box
    if (typeof b === 'string') {
        const p = b.split(',').map(Number)
        b = { x: p[0], y: p[1], w: p[2], h: p[3] }
    }
    if (
        ['x', 'y', 'w', 'h'].some(
            (k) => typeof b[k] !== 'number' || Number.isNaN(b[k])
        )
    )
        throw new Error(
            'box must be {x,y,w,h} or "x,y,w,h" in percentages (0-100)'
        )
    const c = (n) => Math.max(0, Math.min(100, +(+n).toFixed(2)))
    return { x: c(b.x), y: c(b.y), w: c(b.w), h: c(b.h) }
}

// Self-rated certainty (0-1) that a finding is a genuine, objective defect.
// Returns a clamped number, or null when absent / unparseable.
function normConfidence(v) {
    if (v == null || v === '' || typeof v === 'boolean') return null
    const n = Number(v)
    if (Number.isNaN(n)) return null
    return Math.max(0, Math.min(1, +n.toFixed(2)))
}

function nextId(store) {
    const used = new Set(store.findings.map((f) => f.id))
    let n = 1
    while (used.has('r' + n)) n++
    return 'r' + n
}

function loadMarks(page, vp) {
    try {
        return JSON.parse(
            readFileSync(
                resolve(ROOT, 'data', `${page}.${vp}.marks.json`),
                'utf8'
            )
        )
    } catch {
        return null
    }
}

// A finding may reference Set-of-Mark numbers instead of a box; resolve the union
// of the marks' DOM rects (exact — no freehand). Returns null if unresolved.
function normMarks(raw) {
    const m = raw.mark != null ? [raw.mark] : raw.marks
    if (m == null) return []
    return (Array.isArray(m) ? m : String(m).split(','))
        .map((n) => parseInt(n, 10))
        .filter((n) => !Number.isNaN(n))
}

function boxFromMarks(page, vp, marks) {
    if (!marks.length) return null
    const legend = loadMarks(page, vp)
    if (!legend) return null
    const boxes = marks
        .map((id) => legend.find((m) => m.mark === id)?.box)
        .filter(Boolean)
    if (!boxes.length) return null
    const x1 = Math.min(...boxes.map((b) => b.x))
    const y1 = Math.min(...boxes.map((b) => b.y))
    const x2 = Math.max(...boxes.map((b) => b.x + b.w))
    const y2 = Math.max(...boxes.map((b) => b.y + b.h))
    const c = (n) => Math.max(0, Math.min(100, +n.toFixed(2)))
    return { x: c(x1), y: c(y1), w: c(x2 - x1), h: c(y2 - y1) }
}

function addFindings(items) {
    const store = loadStore()
    const added = []
    for (const raw of items) {
        const page = raw.page
        const vp = raw.vp || 'desktop'
        if (!page) throw new Error('finding needs a "page"')
        if (!existsSync(shotPath(page, vp)))
            throw new Error(
                `no screenshot ${shotName(page, vp)} in audit/after/ — run "npm run audit" first, or check the id with: node scripts/ux-review.mjs pages`
            )
        const sev = (raw.severity || 'moderate').toLowerCase()
        if (!SEVERITIES.includes(sev))
            throw new Error(
                `severity must be one of: ${SEVERITIES.join(' | ')}`
            )
        if (!raw.title) throw new Error('finding needs a "title"')
        const marks = normMarks(raw)
        const f = {
            id: raw.id || nextId(store),
            page,
            vp,
            box: normBox(raw.box) || boxFromMarks(page, vp, marks),
            marks,
            severity: sev,
            title: String(raw.title),
            note: raw.note ? String(raw.note) : '',
            tags: Array.isArray(raw.tags)
                ? raw.tags
                : raw.tags
                  ? String(raw.tags)
                        .split(',')
                        .map((s) => s.trim())
                        .filter(Boolean)
                  : [],
            confidence: normConfidence(raw.confidence),
            ts: new Date().toISOString(),
        }
        store.findings.push(f)
        added.push(f)
    }
    saveStore(store)
    build()
    return added
}

function build() {
    const store = loadStore()
    const findings = store.findings
    const shots = listShots()
    const reviewed = new Set(findings.map((f) => f.page))
    const tally = {}
    for (const f of findings) tally[f.severity] = (tally[f.severity] || 0) + 1

    // group: page -> vp -> [finding]
    const pages = {}
    for (const f of findings) ((pages[f.page] ??= {})[f.vp] ??= []).push(f)

    let sections = ''
    for (const page of Object.keys(pages).sort()) {
        const vps = pages[page]
        let inner = ''
        let count = 0
        for (const vp of Object.keys(vps).sort((a, b) => vpRank(a) - vpRank(b)))
            if (vps[vp]) {
                let boxes = ''
                let list = ''
                vps[vp].forEach((f, i) => {
                    const n = i + 1
                    count++
                    const col = COLOR[f.severity] || COLOR.note
                    if (f.box)
                        boxes += `<div class="box" data-n="${n}" data-sev="${f.severity}" style="left:${f.box.x}%;top:${f.box.y}%;width:${f.box.w}%;height:${f.box.h}%;--c:${col}"><span class="num">${n}</span></div>`
                    list += `<li class="fi" data-n="${n}" data-sev="${f.severity}" style="--c:${col}"><span class="num">${n}</span><div><span class="sev">${f.severity}</span> <strong>${esc(f.title)}</strong>${f.tags.length ? ` <span class="tags">${f.tags.map((t) => '#' + esc(t)).join(' ')}</span>` : ''}${f.note ? `<div class="note">${esc(f.note)}</div>` : ''}<div class="meta"><code>${f.id}</code>${f.box ? ` · ${f.box.x},${f.box.y} ${f.box.w}×${f.box.h}%` : ' · general'}${f.marks?.length ? ' · marks ' + f.marks.join(',') : ''}${f.confidence != null ? ' · conf ' + f.confidence : ''}${f.by ? ' · ' + esc(f.by) : ''}</div></div></li>`
                })
                inner += `<div class="view"><div class="vpl">${vp}</div><div class="shotwrap"><div class="shot"><img src="after/${page}.${vp}.png" loading="lazy">${boxes}</div></div><ol class="legend">${list}</ol></div>`
            }
        sections += `<section class="page"><h2>${esc(page)} <span class="rt">${count} finding${count === 1 ? '' : 's'}</span></h2>${inner}</section>`
    }

    const tallyStr =
        Object.entries(tally)
            .map(
                ([k, v]) =>
                    `<span class="sev" style="--c:${COLOR[k]}">${v} ${k}</span>`
            )
            .join(' ') || '<span style="color:#778">no findings yet</span>'
    const unreviewed = shots
        .filter((s) => !reviewed.has(s.page))
        .map((s) => s.page)
    const filters = SEVERITIES.map(
        (k) =>
            `<label><input type="checkbox" data-sev="${k}" checked><span class="sev" style="--c:${COLOR[k]}">${k}</span></label>`
    ).join('')

    const html = `<!DOCTYPE html><html><head><meta charset="utf-8"><title>Arn UX Review</title><style>
*{box-sizing:border-box;margin:0;padding:0}
body{background:#0b0e14;color:#dde;font-family:system-ui,sans-serif;padding:1.5rem 2rem 4rem;max-width:1600px;margin:auto}
h1{font-size:1.5rem;color:#fff}.sub{color:#889;font-size:.82rem;margin:.2rem 0 1rem;line-height:1.6}
.controls{position:sticky;top:0;z-index:50;background:rgba(11,14,20,.93);backdrop-filter:blur(6px);padding:.7rem 0;border-bottom:1px solid #232842;display:flex;gap:1rem;flex-wrap:wrap;align-items:center;font-size:.8rem}
.controls label{display:inline-flex;gap:.3rem;align-items:center;cursor:pointer;color:#bcd}
.sev{font-size:.7rem;text-transform:uppercase;letter-spacing:.04em;color:var(--c);font-weight:700}
.page{margin:2rem 0;border-top:1px solid #1c2236;padding-top:1rem}
.page h2{font-size:1rem;color:#a8d8ea}.page .rt{color:#667;font-size:.78rem;font-weight:400}
.view{display:grid;grid-template-columns:minmax(0,1.4fr) minmax(0,1fr);gap:1rem;margin-top:1rem}
@media(max-width:1100px){.view{grid-template-columns:1fr}}
.vpl{grid-column:1/-1;font-size:.7rem;text-transform:uppercase;letter-spacing:.05em;color:#667}
.shotwrap{max-height:82vh;overflow:auto;border:1px solid #232842;border-radius:8px;background:#000}
.shot{position:relative;display:block;width:100%}
.shot img{display:block;width:100%;height:auto}
.box{position:absolute;border:2px solid var(--c);border-radius:6px;box-shadow:0 0 0 2px rgba(0,0,0,.55),0 0 14px var(--c);min-width:10px;min-height:10px;pointer-events:none}
.box .num,.fi .num{flex:none;width:20px;height:20px;border-radius:50%;background:var(--c);color:#000;font-size:.7rem;font-weight:700;display:flex;align-items:center;justify-content:center}
.box .num{position:absolute;top:-11px;left:-11px}
.box.pulse{animation:pulse 1.1s ease-out 2}
@keyframes pulse{0%{box-shadow:0 0 0 2px rgba(0,0,0,.55),0 0 0 0 var(--c)}50%{box-shadow:0 0 0 2px rgba(0,0,0,.55),0 0 0 10px transparent}100%{box-shadow:0 0 0 2px rgba(0,0,0,.55),0 0 14px var(--c)}}
.legend{list-style:none;display:flex;flex-direction:column;gap:.5rem;align-self:start}
.fi{display:flex;gap:.6rem;padding:.5rem .6rem;background:#11141f;border-left:3px solid var(--c);border-radius:0 6px 6px 0;cursor:pointer;font-size:.82rem;line-height:1.45}
.fi:hover{background:#161a2b}
.fi strong{color:#eef;font-weight:600}
.fi .tags{color:#6a86c0;font-size:.72rem}
.fi .note{color:#aab;font-size:.78rem;margin-top:.25rem}
.fi .meta{color:#566;font-size:.68rem;margin-top:.2rem}.fi code{color:#9cf}
.todo{margin-top:2rem;padding:.8rem 1rem;border:1px dashed #2a3350;border-radius:8px;color:#889;font-size:.8rem}
body.hide-critical [data-sev=critical],body.hide-serious [data-sev=serious],body.hide-moderate [data-sev=moderate],body.hide-minor [data-sev=minor],body.hide-note [data-sev=note]{display:none!important}
</style></head><body>
<h1>📝 Arn UX Review <span style="font-size:.8rem;color:#667;font-weight:400">— reviewer findings</span></h1>
<p class="sub">${esc(rel(SHOTS))} screenshots · ${reviewed.size}/${shots.length} screens reviewed · ${findings.length} finding${findings.length === 1 ? '' : 's'} · ${tallyStr}<br>click any finding to flash its box · authored via <code>scripts/ux-review.mjs</code></p>
<div class="controls"><strong style="color:#889">Show</strong>${filters}</div>
${sections || '<p style="color:#778;margin-top:2rem">No findings yet. Add one: <code>node scripts/ux-review.mjs add --json \'[{"page":"…","vp":"desktop","box":"x,y,w,h","severity":"serious","title":"…"}]\'</code></p>'}
${unreviewed.length ? `<div class="todo"><strong>Not yet reviewed (${unreviewed.length}):</strong> ${unreviewed.map(esc).join(' · ')}</div>` : ''}
<script>
document.querySelectorAll('input[data-sev]').forEach(cb=>cb.addEventListener('change',()=>document.body.classList.toggle('hide-'+cb.dataset.sev,!cb.checked)));
document.querySelectorAll('.view').forEach(v=>v.querySelector('.legend').addEventListener('click',e=>{const li=e.target.closest('.fi');if(!li)return;const b=v.querySelector('.box[data-n="'+li.dataset.n+'"]');if(b){b.classList.remove('pulse');void b.offsetWidth;b.classList.add('pulse');b.scrollIntoView({block:'center',behavior:'smooth'});}}));
</script></body></html>`
    writeFileSync(OUT, html)
}

// --- CLI -----------------------------------------------------------------
const args = parseArgs(process.argv.slice(2))
const cmd = args._[0] || 'build'
try {
    if (cmd === 'pages') {
        const shots = listShots()
        const store = loadStore()
        const cnt = {}
        for (const f of store.findings) cnt[f.page] = (cnt[f.page] || 0) + 1
        if (!shots.length) {
            console.log('No screenshots in audit/after/. Run: npm run audit')
        } else {
            console.log(`${shots.length} reviewable screens (audit/after/):\n`)
            for (const s of shots) {
                const marked = existsSync(
                    resolve(SHOTS, `${s.page}.${s.vps[0]}.marked.png`)
                )
                console.log(
                    `  ${s.page.padEnd(22)} [${s.vps.join(', ')}]  ${dims(s.page, s.vps[0])}  findings:${cnt[s.page] || 0}${marked ? '  ✓marked' : ''}`
                )
            }
        }
    } else if (cmd === 'add') {
        let items
        if (args.json) items = JSON.parse(args.json)
        else if (args.file) items = JSON.parse(readFileSync(args.file, 'utf8'))
        else if (args.page)
            items = [
                {
                    page: args.page,
                    vp: args.vp,
                    box: args.box,
                    mark: args.mark,
                    marks: args.marks,
                    severity: args.sev || args.severity,
                    title: args.title,
                    note: args.note,
                    tags: args.tags,
                    confidence: args.confidence,
                },
            ]
        else
            throw new Error(
                "add: pass --json '<finding|array>' (or --file path), or flags --page --vp --box x,y,w,h --sev --title [--note --tags]"
            )
        if (!Array.isArray(items)) items = [items]
        const added = addFindings(items)
        console.log(
            `added ${added.length} finding(s): ${added.map((f) => `${f.id} ${f.page}/${f.vp} [${f.severity}]`).join(', ')}`
        )
        console.log(`→ rebuilt ${rel(OUT)}`)
    } else if (cmd === 'list') {
        const store = loadStore()
        const fs = args.page
            ? store.findings.filter((f) => f.page === args.page)
            : store.findings
        for (const f of fs)
            console.log(
                `${f.id.padEnd(4)} ${(f.page + '/' + f.vp).padEnd(28)} ${f.severity.padEnd(8)} ${f.title}`
            )
        console.log(`\n${fs.length} finding(s)`)
    } else if (cmd === 'rm') {
        const id = args._[1]
        if (!id) throw new Error('rm: pass a finding id (see: list)')
        const store = loadStore()
        const before = store.findings.length
        store.findings = store.findings.filter((f) => f.id !== id)
        saveStore(store)
        build()
        console.log(
            `removed ${before - store.findings.length}; rebuilt ${rel(OUT)}`
        )
    } else if (cmd === 'merge') {
        // Fan-out aggregation: N reviewer agents each write one JSON array to
        // audit/incoming/*.json; this single writer validates + appends them all.
        if (!existsSync(INC))
            throw new Error(
                `no ${rel(INC)}/ dir — reviewers write findings there`
            )
        if (args.replace) saveStore({ version: 1, findings: [] })
        const store = loadStore()
        const minConf = normConfidence(
            args['min-confidence'] ?? args['min-conf'] ?? args.minConfidence
        )
        const files = readdirSync(INC).filter((f) => f.endsWith('.json'))
        const skip = []
        let n = 0
        for (const file of files) {
            let arr
            try {
                arr = JSON.parse(readFileSync(resolve(INC, file), 'utf8'))
            } catch {
                skip.push(`${file}: parse error`)
                continue
            }
            if (!Array.isArray(arr))
                arr = Array.isArray(arr?.findings) ? arr.findings : []
            for (const it of arr) {
                let page = it.page
                let vp = it.vp
                const m = page && String(page).match(/^(.*)\.([a-z0-9-]+)$/)
                if (m) {
                    page = m[1]
                    vp = vp || m[2]
                }
                if (!page || !vp) {
                    skip.push(`${file}: bad page/vp`)
                    continue
                }
                if (!existsSync(shotPath(page, vp))) {
                    skip.push(`${file}: no shot ${page}.${vp}`)
                    continue
                }
                if (!it.title) {
                    skip.push(`${file}: missing title`)
                    continue
                }
                const marks = normMarks(it)
                let box = null
                try {
                    box = normBox(it.box)
                } catch {
                    box = null
                }
                if (!box) box = boxFromMarks(page, vp, marks)
                const sev = String(it.severity || '').toLowerCase()
                const conf = normConfidence(it.confidence)
                if (minConf != null && (conf == null || conf < minConf)) {
                    skip.push(
                        `${file}: confidence ${conf == null ? 'missing' : conf} < ${minConf} — "${String(it.title).slice(0, 40)}"`
                    )
                    continue
                }
                store.findings.push({
                    id: nextId(store),
                    page,
                    vp,
                    box,
                    marks,
                    severity: SEVERITIES.includes(sev) ? sev : 'moderate',
                    confidence: conf,
                    title: String(it.title),
                    note: it.note ? String(it.note) : '',
                    tags: Array.isArray(it.tags)
                        ? it.tags
                        : it.tags
                          ? String(it.tags)
                                .split(',')
                                .map((s) => s.trim())
                                .filter(Boolean)
                          : [],
                    by: file.replace(/\.json$/, ''),
                    ts: new Date().toISOString(),
                })
                n++
            }
        }
        saveStore(store)
        build()
        console.log(
            `merged ${n} finding(s) from ${files.length} file(s); rebuilt ${rel(OUT)}`
        )
        if (skip.length) {
            console.log(`skipped ${skip.length}:`)
            skip.slice(0, 20).forEach((s) => console.log('  - ' + s))
        }
    } else if (cmd === 'marks') {
        const page = args.page
        const vp = args.vp || 'desktop'
        if (!page) throw new Error('marks: --page <id> [--vp desktop|mobile]')
        const legend = loadMarks(page, vp)
        if (!legend)
            throw new Error(
                `no marks for ${page}.${vp} — run "npm run audit" (writes data/<id>.<vp>.marks.json + after/<id>.<vp>.marked.png)`
            )
        for (const mk of legend) {
            const flags = [mk.fixed && 'fixed', mk.clipped && 'clipped']
                .filter(Boolean)
                .join(',')
            console.log(
                `${String(mk.mark).padStart(3)}  ${(mk.role || '').padEnd(10)} ${(mk.name || '').padEnd(40)}${flags ? '  [' + flags + ']' : ''}`
            )
        }
        console.log(
            `\n${legend.length} marks — view audit/after/${page}.${vp}.marked.png`
        )
    } else if (cmd === 'clear') {
        saveStore({ version: 1, findings: [] })
        build()
        console.log(`cleared; rebuilt ${rel(OUT)}`)
    } else if (cmd === 'build') {
        build()
        console.log(`rebuilt ${rel(OUT)} from ${rel(STORE)}`)
    } else {
        console.log(
            'commands: pages | add | merge [--replace] [--min-confidence X] | marks --page id [--vp] | list [--page id] | rm <id> | clear | build'
        )
        process.exit(1)
    }
} catch (e) {
    console.error('error:', e.message)
    process.exit(1)
}
