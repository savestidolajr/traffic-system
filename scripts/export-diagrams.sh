#!/usr/bin/env bash
set -euo pipefail

# ── Export all architecture diagrams ──
# Usage: ./scripts/export-diagrams.sh
#
# Two pipelines, one model (see architecture/renders/README.md):
#   1. LikeC4 auto-layout PNGs for every view (interactive/exploratory)
#   2. Manual orthogonal renders for presentation views (renders/views/*.yaml),
#      validated against the model export — fails on drift.
# Presentation renders overwrite the auto-layout PNG of the same name.
# Run after any change to .c4 files or render specs.

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCH_DIR="$REPO_ROOT/architecture"
OUT_DIR="$ARCH_DIR/images"

echo "── Exporting architecture diagrams ──"

# Run from inside the project dir so a single project resolves (no phantom "default").
# --notation bakes the legend in; --description adds the title and description banner.
EXPORT_LOG="$(mktemp)"
if ( cd "$ARCH_DIR" && npx likec4 export png --theme dark --notation --description -o "$OUT_DIR" . ) >"$EXPORT_LOG" 2>&1; then
    grep -E '(export/|exported|Error)' "$EXPORT_LOG" || cat "$EXPORT_LOG"
else
    cat "$EXPORT_LOG"
    rm -f "$EXPORT_LOG"
    exit 1
fi
rm -f "$EXPORT_LOG"

echo ""
echo "── Rendering presentation diagrams (manual layout) ──"
MODEL_JSON="$(mktemp --suffix=.json)"
( cd "$ARCH_DIR" && npx likec4 export json -o "$MODEL_JSON" . ) >/dev/null 2>&1
python3 "$ARCH_DIR/renders/render.py" --model "$MODEL_JSON" --out "$OUT_DIR" \
    "$ARCH_DIR"/renders/views/*.yaml
rm -f "$MODEL_JSON"

# Rasterise manual renders so document references (*.png) pick them up.
if command -v rsvg-convert >/dev/null 2>&1; then
    for svg in "$OUT_DIR"/*.svg; do
        [ -e "$svg" ] || continue
        rsvg-convert -z 2 "$svg" -o "${svg%.svg}.png"
    done
else
    echo "warning: rsvg-convert not found — skipping SVG->PNG rasterisation" >&2
fi

echo ""
echo "Exported to $OUT_DIR:"
ls -1 "$OUT_DIR"/*.png "$OUT_DIR"/*.svg 2>/dev/null | while read f; do
    SIZE=$(stat -c%s "$f" 2>/dev/null || stat -f%z "$f" 2>/dev/null || echo "?")
    echo "  $(basename "$f") (${SIZE} bytes)"
done
