#!/usr/bin/env python3
"""Manual-layout orthogonal SVG renderer for presentation architecture diagrams.

The LikeC4 model (`architecture/*.c4`) remains the single source of truth for
elements, relationships, and the deployment hierarchy. This renderer is a
presentation layer on top of it: each view spec (`renders/views/*.yaml`)
references model/deployment FQNs and pins exact coordinates for containers,
nodes, and orthogonal edges.

Drift protection — the render FAILS (exit 2) when:
  * a spec references an FQN that no longer exists in the model export
  * a spec edge has no corresponding relationship in the model
    (matched at element-or-descendant level, the same indirection LikeC4
    uses for deployment views)
  * a referenced icon file is missing

Usage:
  npx likec4 export json -o model.json architecture/
  python3 render.py --model model.json --out architecture/images spec.yaml [...]
"""

import argparse
import base64
import json
import sys
from pathlib import Path

import yaml

ICON_DIR = Path(__file__).parent / "icons"

FONT = "Segoe UI, -apple-system, Helvetica, Arial, sans-serif"
MONO = "Cascadia Code, Consolas, monospace"

# ── default styles; view specs may override/extend via `styles:` ──
CONTAINER_STYLES = {
    "subscription": {"fill": "#EFF6FF", "stroke": "#0078D4", "sw": 2, "rx": 10, "shadow": True},
    "region":       {"fill": "#FBFDFF", "stroke": "#94A3B8", "sw": 1.5, "rx": 8, "dash": "6 4"},
    "vnet":         {"fill": "#DBEAFE", "stroke": "#1E40AF", "sw": 2, "rx": 8},
    "subnet-compute": {"fill": "#DCFCE7", "stroke": "#16A34A", "sw": 1.5, "rx": 6},
    "subnet-data":  {"fill": "#EDE9FE", "stroke": "#7C3AED", "sw": 1.5, "rx": 6},
    "saas":         {"fill": "#FEF3C7", "stroke": "#F59E0B", "sw": 2, "rx": 10, "shadow": True},
    "external":     {"fill": "#F3F4F6", "stroke": "#6B7280", "sw": 2, "rx": 10, "shadow": True},
}

EDGE_CLASSES = {
    "auth":  {"color": "#EF4444", "width": 3},
    "api":   {"color": "#2563EB", "width": 3},
    "drop":  {"color": "#F97316", "width": 3},
    "pe":    {"color": "#7C3AED", "width": 2},
    "muted": {"color": "#94A3B8", "width": 2},
}


class Model:
    """Queryable wrapper over a `likec4 export json` document."""

    def __init__(self, path: Path, project: str | None):
        data = json.loads(path.read_text())
        projects = data if isinstance(data, list) else [data]
        if project:
            matches = [p for p in projects if p.get("projectId") == project]
            if not matches:
                ids = [p.get("projectId") for p in projects]
                raise SystemExit(f"project {project!r} not in export (found: {ids})")
            p = matches[0]
        else:
            non_default = [q for q in projects if q.get("projectId") != "default"]
            p = (non_default or projects)[0]
        self.project_id = p.get("projectId")
        self.elements = p["elements"]
        self.relations = p["relations"]
        self.deployment = p["deployments"]["elements"]

    def exists(self, fqn: str) -> bool:
        return fqn in self.deployment or fqn in self.elements

    def entry(self, fqn: str) -> dict | None:
        return self.deployment.get(fqn) or self.elements.get(fqn)

    def model_id(self, fqn: str) -> str | None:
        """Resolve an FQN to its logical model element id."""
        d = self.deployment.get(fqn)
        if d is not None:
            return d.get("element")  # deployed instance → logical; node → None
        if fqn in self.elements:
            return fqn
        return None

    def title(self, fqn: str) -> str:
        d = self.deployment.get(fqn)
        if d is not None:
            logical = d.get("element")
            if logical and logical in self.elements:
                return self.elements[logical].get("title", fqn)
            return d.get("title", fqn)
        if fqn in self.elements:
            return self.elements[fqn].get("title", fqn)
        return fqn

    def has_relation(self, src: str, tgt: str) -> bool:
        """True if any model relationship connects src/tgt or their descendants,
        in either direction."""

        def under(eid: str, base: str) -> bool:
            return eid == base or eid.startswith(base + ".")

        for rel in self.relations.values():
            s = rel["source"]["model"]
            t = rel["target"]["model"]
            if (under(s, src) and under(t, tgt)) or (under(s, tgt) and under(t, src)):
                return True
        return False


class SvgBuilder:
    def __init__(self, w: int, h: int):
        self.w, self.h = w, h
        self.parts: list[str] = []
        self.markers: dict[str, str] = {}

    def rect(self, x, y, w, h, fill, stroke, sw=2, rx=10, dash=None, shadow=False):
        d = f' stroke-dasharray="{dash}"' if dash else ""
        f = ' filter="url(#shadow)"' if shadow else ""
        self.parts.append(
            f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" '
            f'fill="{fill}" stroke="{stroke}" stroke-width="{sw}"{d}{f}/>'
        )

    def text(self, x, y, s, size=13, fill="#0F172A", anchor="middle",
             weight="normal", style="normal", font=FONT, halo=True):
        s = str(s).replace("&", "&amp;").replace("<", "&lt;")
        h = (' paint-order="stroke" stroke="#FFFFFF" stroke-width="3" '
             'stroke-linejoin="round"') if halo else ""
        self.parts.append(
            f'<text x="{x}" y="{y}" font-family="{font}" font-size="{size}" '
            f'fill="{fill}" text-anchor="{anchor}" font-weight="{weight}" '
            f'font-style="{style}"{h}>{s}</text>'
        )

    def icon(self, cx, cy, path: Path, size=64):
        b64 = base64.b64encode(path.read_bytes()).decode()
        half = size // 2
        self.parts.append(
            f'<image href="data:image/svg+xml;base64,{b64}" x="{cx-half}" '
            f'y="{cy-half}" width="{size}" height="{size}"/>'
        )

    @staticmethod
    def _rounded_path(points, radius=10):
        """Polyline -> path with arcs at intermediate vertices."""
        if len(points) < 3:
            return "M " + " L ".join(f"{x} {y}" for x, y in points)
        d = [f"M {points[0][0]} {points[0][1]}"]
        for i in range(1, len(points) - 1):
            px, py = points[i - 1]
            vx, vy = points[i]
            nx, ny = points[i + 1]
            din = ((vx - px) ** 2 + (vy - py) ** 2) ** 0.5
            dout = ((nx - vx) ** 2 + (ny - vy) ** 2) ** 0.5
            r = min(radius, din / 2, dout / 2)
            if r < 1 or din == 0 or dout == 0:
                d.append(f"L {vx} {vy}")
                continue
            ax = vx - (vx - px) / din * r
            ay = vy - (vy - py) / din * r
            bx = vx + (nx - vx) / dout * r
            by = vy + (ny - vy) / dout * r
            d.append(f"L {ax} {ay} Q {vx} {vy} {bx} {by}")
        d.append(f"L {points[-1][0]} {points[-1][1]}")
        return " ".join(d)

    def edge(self, points, color, width=2.5, dash=None):
        marker = self._marker(color)
        d = f' stroke-dasharray="{dash}"' if dash else ""
        path = self._rounded_path(points)
        self.parts.append(
            f'<path d="{path}" fill="none" stroke="{color}" '
            f'stroke-width="{width}" stroke-linecap="butt"{d} '
            f'marker-end="url(#{marker})"/>'
        )

    def line(self, x1, y1, x2, y2, color, width=3, dash=None):
        d = f' stroke-dasharray="{dash}"' if dash else ""
        self.parts.append(
            f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" '
            f'stroke="{color}" stroke-width="{width}"{d}/>'
        )

    def _marker(self, color: str) -> str:
        if color not in self.markers:
            self.markers[color] = f"arr{len(self.markers)}"
        return self.markers[color]

    def render(self) -> str:
        defs = ["<defs>"]
        for color, mid in self.markers.items():
            defs.append(
                f'<marker id="{mid}" markerWidth="11" markerHeight="10" '
                f'refX="9" refY="5" orient="auto" markerUnits="userSpaceOnUse">'
                f'<polygon points="0,0 11,5 0,10" fill="{color}"/></marker>'
            )
        defs.append(
            '<filter id="shadow" width="160%" height="160%" x="-30%" y="-30%">'
            '<feDropShadow dx="3" dy="4" stdDeviation="3" flood-color="#3D4574" '
            'flood-opacity="0.25"/></filter>'
        )
        defs.append("</defs>")
        return (
            f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {self.w} {self.h}" '
            f'width="{self.w}" height="{self.h}">'
            f'<rect width="{self.w}" height="{self.h}" fill="#FFFFFF"/>'
            + "".join(defs) + "".join(self.parts) + "</svg>"
        )


def _text_bbox(x, y, s, size, anchor, weight="normal"):
    per = 0.62 if weight in ("600", "700", "bold") else 0.55
    w = len(str(s)) * size * per
    if anchor == "middle":
        return (x - w / 2, y - size, x + w / 2, y)
    if anchor == "end":
        return (x - w, y - size, x, y)
    return (x, y - size, x + w, y)


def draw_text_item(svg: SvgBuilder, item: dict, default_fill="#0F172A", track=None):
    x, y, s = item["at"][0], item["at"][1], item["text"]
    size = item.get("size", 13)
    anchor = item.get("anchor", "middle")
    weight = item.get("weight", "normal")
    svg.text(
        x, y, s, size=size,
        fill=item.get("color", default_fill),
        anchor=anchor, weight=weight,
        style=item.get("style", "normal"),
        font=MONO if item.get("font") == "mono" else FONT,
    )
    if track is not None:
        track.append((str(s)[:30], _text_bbox(x, y, s, size, anchor, weight)))


def _seg_intersects_rect(seg, rect, pad=0.0):
    """Liang-Barsky: does segment cross the (padded) rect interior?"""
    x1, y1, x2, y2 = seg
    rx0, ry0, rx1, ry1 = rect[0] - pad, rect[1] - pad, rect[2] + pad, rect[3] + pad
    dx, dy = x2 - x1, y2 - y1
    t0, t1 = 0.0, 1.0
    for p, q in ((-dx, x1 - rx0), (dx, rx1 - x1), (-dy, y1 - ry0), (dy, ry1 - y1)):
        if p == 0:
            if q < 0:
                return False
            continue
        t = q / p
        if p < 0:
            t0 = max(t0, t)
        else:
            t1 = min(t1, t)
        if t0 > t1:
            return False
    return (t1 - t0) > 0.02


def _bb_overlap(a, b, pad=0.0):
    return not (a[2] + pad < b[0] or b[2] + pad < a[0]
                or a[3] + pad < b[1] or b[3] + pad < a[1])


def _lint_layout(name, texts, node_rects, segments):
    """Advisory collision checks; prints warnings, never fails the build."""
    warns = []
    for desc, bb in texts:
        for edesc, seg in segments:
            if _seg_intersects_rect(seg, bb):
                warns.append(f"text {desc!r} crossed by edge {edesc}")
                break
    for edesc, seg in segments:
        for ndesc, rect in node_rects:
            near = lambda px, py: (rect[0] - 4 <= px <= rect[2] + 4
                                   and rect[1] - 4 <= py <= rect[3] + 4)
            if near(seg[0], seg[1]) or near(seg[2], seg[3]):
                continue  # attached to this node
            if _seg_intersects_rect(seg, rect):
                warns.append(f"edge {edesc} passes through node {ndesc!r}")
    for i in range(len(texts)):
        for j in range(i + 1, len(texts)):
            if _bb_overlap(texts[i][1], texts[j][1]):
                warns.append(f"texts {texts[i][0]!r} and {texts[j][0]!r} overlap")
    for warn in warns[:20]:
        print(f"  ⚠ {name}: {warn}", file=sys.stderr)


def render_view(spec_path: Path, model: Model, out_dir: Path) -> Path:
    spec = yaml.safe_load(spec_path.read_text())
    errors: list[str] = []

    container_styles = {**CONTAINER_STYLES, **spec.get("styles", {}).get("containers", {})}
    edge_classes = {**EDGE_CLASSES, **spec.get("styles", {}).get("edges", {})}

    w, h = spec["canvas"]
    svg = SvgBuilder(w, h)
    lint_texts: list = []
    lint_nodes: list = []
    lint_segs: list = []

    # ── containers (back layer) ──
    for c in spec.get("containers", []):
        fqn = c.get("id")
        if fqn is not None and not model.exists(fqn):
            errors.append(f"container {fqn!r}: not in model export")
            continue
        style = dict(container_styles.get(c.get("style", ""), {}))
        if not style:
            errors.append(f"container {fqn or c!r}: unknown style {c.get('style')!r}")
            continue
        x, y, cw, ch = c["rect"]
        svg.rect(x, y, cw, ch, style["fill"], style["stroke"],
                 sw=style.get("sw", 2), rx=style.get("rx", 8),
                 dash=style.get("dash"), shadow=style.get("shadow", False))
        labels = c.get("labels", [])
        for item in labels:
            if "text" not in item:
                if fqn is None:
                    errors.append(f"container {c.get('rect')}: label needs text (no model id)")
                    continue
                item = {**item, "text": model.title(fqn)}
            draw_text_item(svg, item, track=lint_texts)

    # ── free-standing texts (title block, captions) — above container fills ──
    for item in spec.get("texts", []):
        draw_text_item(svg, item, track=lint_texts)

    # ── edges (before nodes so icons overlay line ends) ──
    for e in spec.get("edges", []):
        src, tgt = e.get("from"), e.get("to")
        if src is not None and tgt is not None:
            for fqn in (src, tgt):
                if not model.exists(fqn):
                    errors.append(f"edge {src} -> {tgt}: {fqn!r} not in model export")
            src_m, tgt_m = model.model_id(src), model.model_id(tgt)
            if src_m and tgt_m and not model.has_relation(src_m, tgt_m):
                errors.append(
                    f"edge {src} -> {tgt}: no model relationship between "
                    f"{src_m!r} and {tgt_m!r} (or descendants)"
                )
        cls = edge_classes.get(e.get("class", "muted"))
        if cls is None:
            errors.append(f"edge {src} -> {tgt}: unknown class {e.get('class')!r}")
            continue
        svg.edge([tuple(p) for p in e["points"]], cls["color"], cls["width"],
                 dash=cls.get("dash"))
        pts = e["points"]
        edesc = f"{src or '?'}->{tgt or '?'}" if src or tgt else f"@{pts[0]}"
        for i in range(len(pts) - 1):
            lint_segs.append((edesc, (*pts[i], *pts[i + 1])))
        if "label" in e:
            lbl = e["label"]
            draw_text_item(svg, {
                "style": "italic", "weight": "600", "size": 12,
                "color": cls["color"] if e.get("class") != "muted" else "#64748B",
                **lbl,
            }, track=lint_texts)

    # ── nodes ──
    for n in spec.get("nodes", []):
        fqn = n.get("id")
        if fqn is not None and not model.exists(fqn):
            errors.append(f"node {fqn!r}: not in model export")
            continue
        cx, cy = n["at"]
        ndesc = fqn or n.get("label", f"@{n['at']}")
        if "box" in n:
            bw, bh = n["box"]
            svg.rect(cx - bw / 2, cy - bh / 2, bw, bh,
                     n.get("fill", "#FFFFFF"), n.get("stroke", "#64748B"),
                     sw=n.get("sw", 1.5), rx=n.get("rx", 8))
            lint_nodes.append((ndesc, (cx - bw / 2, cy - bh / 2, cx + bw / 2, cy + bh / 2)))
            label = n.get("label") or (model.title(fqn) if fqn else None)
            if label is None:
                errors.append(f"box node at {n['at']}: needs label (no model id)")
                continue
            sub = n.get("sublabel")
            tc = n.get("text_color", "#0F172A")
            if sub:
                svg.text(cx, cy - 2, label, n.get("size", 13), tc, weight="600")
                svg.text(cx, cy + 15, sub, 10, "#64748B")
            else:
                svg.text(cx, cy + 5, label, n.get("size", 13), tc, weight="600")
            continue
        icon_path = ICON_DIR / f"{n['icon']}.svg"
        if not icon_path.exists():
            errors.append(f"node {fqn!r}: icon {icon_path.name} missing")
            continue
        svg.icon(cx, cy, icon_path)
        lint_nodes.append((ndesc, (cx - 32, cy - 32, cx + 32, cy + 32)))
        label = n.get("label") or (model.title(fqn) if fqn else None)
        if label is None:
            errors.append(f"icon node at {n['at']}: needs label (no model id)")
            continue
        sub = n.get("sublabel")
        if n.get("label_pos", "below") == "right":
            svg.text(cx + 42, cy - 2, label, 14, anchor="start", weight="600")
            lint_texts.append((str(label)[:30], _text_bbox(cx + 42, cy - 2, label, 14, "start", "600")))
            if sub:
                svg.text(cx + 42, cy + 15, sub, 11, "#64748B", anchor="start")
                lint_texts.append((str(sub)[:30], _text_bbox(cx + 42, cy + 15, sub, 11, "start")))
        else:
            svg.text(cx, cy + 52, label, 14, weight="600")
            lint_texts.append((str(label)[:30], _text_bbox(cx, cy + 52, label, 14, "middle", "600")))
            if sub:
                svg.text(cx, cy + 69, sub, 11, "#64748B")
                lint_texts.append((str(sub)[:30], _text_bbox(cx, cy + 69, sub, 11, "middle")))

    # ── legend ──
    if "legend" in spec:
        lx, ly = spec["legend"]["at"]
        for label, cls_name in spec["legend"]["items"]:
            cls = edge_classes.get(cls_name, EDGE_CLASSES["muted"])
            svg.line(lx, ly, lx + 26, ly, cls["color"], dash=cls.get("dash"))
            svg.text(lx + 33, ly + 4, label, 11, "#475569", anchor="start")
            lx += 33 + len(label) * 7 + 40

    if errors:
        print(f"✗ {spec_path.name}: {len(errors)} drift error(s)", file=sys.stderr)
        for err in errors:
            print(f"    {err}", file=sys.stderr)
        raise SystemExit(2)

    _lint_layout(spec_path.name, lint_texts, lint_nodes, lint_segs)

    out = out_dir / f"{spec.get('output', spec_path.stem)}.svg"
    out.write_text(svg.render())
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--model", required=True, type=Path,
                    help="path to `likec4 export json` output")
    ap.add_argument("--project", default=None,
                    help="likec4 project id (default: first non-'default')")
    ap.add_argument("--out", required=True, type=Path, help="output directory")
    ap.add_argument("specs", nargs="+", type=Path, help="view spec YAML files")
    args = ap.parse_args()

    model = Model(args.model, args.project)
    args.out.mkdir(parents=True, exist_ok=True)
    for spec in args.specs:
        out = render_view(spec, model, args.out)
        print(f"✓ {spec.name} -> {out}")


if __name__ == "__main__":
    main()
