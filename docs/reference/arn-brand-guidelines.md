---
title: "ARN Brand Guidelines (UI reference)"
description: "ARN branding the operator UI must follow — colours, typography, logo/Light Portal, iconography, and design principles distilled from the ARN Brand Guidelines for application to the Arn operator dashboard"
tags:
  - "brand"
  - "ui"
  - "design"
  - "frontend"
  - "branding"
purpose: "reference"
status: stale
last_verified: 2026-07-17
review_after: 2026-09-01
volatility: "low"
source_of_truth: "ARN Brand Guidelines (brand creative guidelines), archived at docs/reference/assets/ARN_Brand_Guidelines.pdf"
verified_by:
  - "read from ARN Brand Guidelines PDF (2026-06-02)"
  - "web/src/index.css and web/tailwind.config.js (2026-07-17)"
aliases:
  - "ARN brand"
  - "brand guidelines"
  - "ARN colours"
  - "ARN typography"
common_queries:
  - "What colours and fonts should the UI use?"
  - "What is ARN Purple?"
  - "How do we brand the operator dashboard for ARN?"
see_also:
  - "docs/operations/solution-overview.md"
  - "docs/reference/assets/ARN_Brand_Guidelines.pdf"
---

# ARN Brand Guidelines (UI reference)

Distilled from the ARN Brand Guidelines (archived: [`docs/reference/assets/ARN_Brand_Guidelines.pdf`](assets/ARN_Brand_Guidelines.pdf)). This captures the parts the **operator dashboard** (`web/`) must follow. The guideline's own mantra is **"Simplify to elevate"**; an operator tool is a functional surface, so apply ARN branding as a confident, restrained accent over a clean, legible layout — not a marketing canvas.

## Colour

### Primary palette
| Token | Name | Hex | Notes |
|---|---|---|---|
| Core | **ARN Purple** | `#691FD0` | The signature colour — creativity / the power of sound. Primary brand + primary action colour. |
| Accent | Secondary Purple | `#8C5FFD` | Lighter purple; hovers, highlights, focus rings. |
| Neutral | ARN Black | `#000000` | Contrast, typography, dark surfaces. |
| Neutral | White | `#FFFFFF` | Contrast, light surfaces, text on purple/dark. |

### Gradients (brand devices, use sparingly)
- **Dynamic Gradient:** `#802FF4 → #491CB7`
- **Light Leak Gradient:** `#000000 → #691FD0` (the signature "purple glow" — good for headers, the login screen, empty states).

### Secondary palette (sub-brand colours — use sparingly, never to overpower purple)
ARN Pink `#FF1F8F` / `#F9008F` / `#C9009A` · ARN Yellow `#FFAA00` / `#FBC540` / `#F7941D` · ARN Red `#FF0022` / `#DD0030` / `#AA0038` · ARN Blue `#5294FF` / `#4A83FF` / `#3351FF`.

### Recommended shadcn CSS-variable mapping (`web/src/index.css`)
The app already themes via shadcn CSS variables; ARN branding is mostly a token change, not a rewrite. Suggested mapping (verify WCAG AA contrast for each text/background pair before locking):
- `--primary` → ARN Purple `#691FD0`; `--primary-foreground` → white.
- `--ring` / focus → Secondary Purple `#8C5FFD`.
- `--background` / `--card` → white (light) or near-black (dark, leaning into the "purple glow" aesthetic); `--foreground` → near-black / white.
- **Status colours are functional, not brand-decorative** — keep them unambiguous and accessible. ARN Red (`#DD0030`) suits destructive/error; ARN Yellow (`#F7941D`) suits warning; ARN Blue (`#4A83FF`) suits info. "Success/OK" has no brand green — use an accessible green or a confident purple/blue affirmative; do not force a brand colour at the cost of legibility. This matters for the **colour-coded deadline countdown** (W2) — urgency must read instantly.

## Typography
| Role | Family | Weight | Notes |
|---|---|---|---|
| Display / headings | **Obviously** | SemiBold (hero); Bold for headlines | Wide, confident. Headlines: ~80% leading, -50 optical tracking. |
| Body / subheads | **Aptos** | Regular (body), Bold (subheads) | ~90% leading. |

Caveats for web use:
- **Obviously** is a commercial typeface (OHno Type Co). The available Adobe Type OTF identifies a Typekit EULA but does not prove permission for self-hosted web embedding, so it is not shipped. ARN must supply the web-font licence and WOFF/WOFF2 files before the app can use it.
- The app self-hosts **Archivo** as the approved interim display face under the SIL Open Font License (`web/src/assets/fonts/archivo-semibold-bold.woff2`; licence alongside it). The display stack is `"Archivo", "Arial Narrow", "Aptos Display", sans-serif`; prepend Obviously only after the licensed web assets arrive.
- **Aptos** is Microsoft's font; retain web-safe body fallbacks: `"Aptos", "Inter", system-ui, sans-serif`.
- Text rules: write **"ARN" in uppercase** always; product/section headings lowercase (per ARN style) — but for an operator tool, prioritise scannability and sentence case for data-dense UI; reserve the lowercase styling for brand headers.

## Logo & the Light Portal
- **Logo:** purple is the preferred colourway; **white logo on purple/dark backgrounds**, black on purple. Maintain clearspace ≥ the height of the "A". Use only **approved vector files**.
- **The Light Portal** is ARN's signature device (a gateway/sound-wave-light motif), aligns to the "A", may rotate ±15°, has an outline variant, approved colours only. Good for the app header, login, loading, and empty states.
- **Asset gap (request from ARN):** we do not yet hold approved **logo SVGs** or the **Light Portal** vector — only the raster guidelines PDF. These are needed before final branding.

## Iconography
Outline icons only (not filled). The app already uses **lucide-react** (outline) — this matches ARN's icon rule, so no change needed. Brand-specific icons may use an outer circle in a brand colour.

## Design principles & tone
- **Sound First** · **Entertainment, Evolved** · **Inclusive Storytelling** · **Genuine Confidence** · "Simplify to elevate."
- Tone of voice: **genuine, confident, entertaining**; personality: **curious, creative, experts**. For operator-facing copy (labels, errors, confirmations): clear and confident, no jargon, no fluff.

## Application notes for the operator dashboard
- Lead with a clean, legible, data-dense layout; use ARN Purple as the primary action/brand accent and the Light-Leak gradient for hero/empty surfaces.
- Accessibility is non-negotiable for a tool used under time pressure (the ~8-minute approval window): verify WCAG AA contrast, never rely on colour alone for status (pair with icon/label).
- A dark theme aligns naturally with the "purple glow / light leak" aesthetic and is easy on the eyes for long monitoring shifts; offer light + dark via the existing `next-themes` wiring.

## Outstanding brand assets to request from ARN
1. Approved **logo** vector files (purple / white / black colourways).
2. The **Light Portal** vector + usage lock-ups.
3. **Obviously** self-hosted web-font licence + WOFF/WOFF2 files (and confirmation of Aptos web usage). The Adobe Type OTF available in SharePoint is not sufficient evidence of web embedding rights.
4. Any existing **ARN web/design-system tokens** (so we align rather than re-derive).
