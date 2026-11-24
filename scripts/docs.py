#!/usr/bin/env python3
"""Documentation harness for Arn.

Source files are Markdown docs and universal SKILL.md files. Generated indexes
are derived from those sources and should not be edited by hand.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from datetime import date, datetime
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TODAY = date.today()

DOC_REQUIRED = [
    "title",
    "description",
    "tags",
    "purpose",
    "status",
    "last_verified",
    "review_after",
    "volatility",
    "source_of_truth",
    "verified_by",
    "aliases",
    "common_queries",
]
PURPOSES = {"overview", "reference", "decision-log", "runbook"}
STATUSES = {
    "current",
    "draft",
    "needs-review",
    "stale",
    "archived",
    "captured",
    "promoted",
    "partially-promoted",
}
VOLATILITIES = {"low", "medium", "high"}
# Subdirectories under docs/ to ignore during validation.
# These contain third-party content (e.g. cloned ElevenLabs repos) that
# doesn't follow the Arn frontmatter schema and is not maintained here.
IGNORED_PREFIXES = {
    "elevenlabs",
    "archive",
}
# Third-party skills installed from external marketplaces ship their own
# structure (examples/, troubleshooting/, LICENSE.txt, *.prompt.md) and are not
# maintained to Arn's universal skill contract — exempt them from the structure
# check, mirroring IGNORED_PREFIXES for cloned third-party docs.
THIRD_PARTY_SKILLS = {
    "appinsights-instrumentation",
    "azure-diagnostics",
    "dotnet-code-testing-agent",
}
GENERATED_FILES = [
    Path("docs/_manifest.yaml"),
    Path("docs/_search.jsonl"),
    Path("docs/_sections.yaml"),
    Path("docs/_data.js"),
    Path("llms.txt"),
]
LINE_BUDGETS = {
    Path("AGENTS.md"): 150,
    # Per-file budgets override the generic doc/skill thresholds below, for files
    # where the generic limit is inappropriate. Runaway growth still warns.
    Path("docs/_manifest.yaml"): 2500,  # machine-generated index; grows with doc count
    Path("docs/canonical/decisions.md"): 800,  # append-only ADR log; section-routed via _sections.yaml
    Path("docs/CODEMAPS/FILES.md"): 1050,  # generated codemap; 448 backend files across 53 folders
    # (one mandated `| File | Purpose |` table per folder) + preserved narrative sections
    # push the true floor to ~1005 lines even with zero blank-line padding — 900 predates
    # the `codemap` generator catching real file-count drift; revisit if it grows further.
}
# Always-on agent context file: exceeding this is an ERROR, not a warning.
HARD_LINE_BUDGETS = {
    Path("AGENTS.md"): 150,
}
HARD_LINE_LENGTH: dict[Path, int] = {}
BACKEND_PROJECTS = ["Domain", "Application", "Infrastructure", "Functions", "Workflow", "Workflow.Hosting"]
CODEMAP_DOC = Path("docs/CODEMAPS/FILES.md")
CODEMAP_SUMMARY_MAX_CHARS = 140
CODEMAP_TYPE_DECL_RE = re.compile(
    r"^\s*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|"
    r"readonly|unsafe|new|virtual|override)\s+)*(?:class|interface|record|struct|enum)\b"
)
CODEMAP_ATTRIBUTE_LINE_RE = re.compile(r"^\s*\[.*\]\s*$")
CODEMAP_XML_TAG_RE = re.compile(r"<[^>]+>")
CODEMAP_WHITESPACE_RE = re.compile(r"\s+")
STOP_WORDS = {
    "a",
    "an",
    "and",
    "are",
    "as",
    "at",
    "be",
    "by",
    "for",
    "from",
    "how",
    "i",
    "in",
    "is",
    "it",
    "of",
    "on",
    "or",
    "the",
    "to",
    "what",
    "when",
    "where",
    "which",
    "with",
}


@dataclass
class MarkdownFile:
    path: Path
    meta: dict[str, object]
    body: str
    body_start_line: int


def rel(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def clean_value(value: str) -> str:
    return value.strip().strip('"').strip("'").replace('\\"', '"')


def parse_scalar(value: str) -> object:
    value = value.strip()
    if value.startswith("[") and value.endswith("]"):
        inner = value[1:-1].strip()
        if not inner:
            return []
        return [clean_value(item) for item in inner.split(",")]
    return clean_value(value)


def parse_markdown(path: Path) -> MarkdownFile:
    text = read_text(path)
    lines = text.splitlines()
    if not lines or lines[0] != "---":
        return MarkdownFile(path, {}, text, 1)

    meta: dict[str, object] = {}
    current_key: str | None = None
    end_index = 0

    for index, line in enumerate(lines[1:], start=1):
        if line == "---":
            end_index = index
            break

        list_match = re.match(r"^\s*-\s+(.*)$", line)
        if list_match and current_key:
            meta.setdefault(current_key, [])
            value = clean_value(list_match.group(1))
            if isinstance(meta[current_key], list):
                meta[current_key].append(value)
            continue

        key_match = re.match(r"^([A-Za-z0-9_-]+):(?:\s*(.*))?$", line)
        if key_match:
            key, raw_value = key_match.group(1), key_match.group(2) or ""
            if raw_value == "":
                meta[key] = []
                current_key = key
            else:
                meta[key] = parse_scalar(raw_value)
                current_key = None
            continue

        current_key = None

    body = "\n".join(lines[end_index + 1 :])
    if body:
        body += "\n"
    return MarkdownFile(path, meta, body, end_index + 2)


def yaml_quote(value: object) -> str:
    text = str(value)
    escaped = text.replace("\\", "\\\\").replace('"', '\\"')
    return f'"{escaped}"'


def yaml_scalar(value: object) -> str:
    text = str(value)
    if re.fullmatch(r"\d{4}-\d{2}-\d{2}", text):
        return text
    return yaml_quote(text)


def yaml_block_list(key: str, values: object, indent: int = 0) -> list[str]:
    prefix = " " * indent
    if not values:
        return []
    if isinstance(values, str):
        values = [values]
    lines = [f"{prefix}{key}:"]
    for value in values:
        lines.append(f"{prefix}  - {yaml_scalar(value)}")
    return lines


def dump_frontmatter(meta: dict[str, object]) -> str:
    lines = ["---"]
    for key, value in meta.items():
        if isinstance(value, list):
            lines.extend(yaml_block_list(key, value))
        else:
            lines.append(f"{key}: {yaml_scalar(value)}")
    lines.append("---")
    return "\n".join(lines) + "\n"


def iter_docs() -> list[MarkdownFile]:
    docs = []
    for path in sorted((ROOT / "docs").rglob("*.md")):
        rel = path.relative_to(ROOT / "docs")
        if rel.parts[0] in IGNORED_PREFIXES:
            continue
        docs.append(parse_markdown(path))
    return docs


def iter_skills() -> list[MarkdownFile]:
    skills_root = ROOT / ".agents/skills"
    if not skills_root.exists():
        return []
    return [parse_markdown(path) for path in sorted(skills_root.glob("*/SKILL.md"))]


def as_list(value: object) -> list[str]:
    if value is None:
        return []
    if isinstance(value, list):
        return [str(item) for item in value if str(item)]
    if str(value):
        return [str(value)]
    return []


def headings(markdown: MarkdownFile) -> list[dict[str, object]]:
    result = []
    in_code = False
    for offset, line in enumerate(
        markdown.body.splitlines(), start=markdown.body_start_line
    ):
        if line.startswith("```"):
            in_code = not in_code
            continue
        if in_code:
            continue
        match = re.match(r"^(#{1,6})\s+(.+)$", line)
        if match:
            result.append(
                {
                    "level": len(match.group(1)),
                    "heading": match.group(2).strip(),
                    "line": offset,
                }
            )
    return result


def summary(markdown: MarkdownFile) -> str:
    paragraphs = []
    current: list[str] = []
    in_code = False
    for line in markdown.body.splitlines():
        if line.startswith("```"):
            in_code = not in_code
            continue
        if (
            in_code
            or line.startswith("#")
            or line.startswith("|")
            or line.startswith("- ")
        ):
            continue
        if not line.strip():
            if current:
                paragraphs.append(" ".join(current))
                current = []
            continue
        current.append(line.strip())
    if current:
        paragraphs.append(" ".join(current))
    return paragraphs[0] if paragraphs else str(markdown.meta.get("description", ""))


def doc_entry(doc: MarkdownFile) -> dict[str, object]:
    meta = doc.meta
    entry: dict[str, object] = {
        "title": meta.get("title", ""),
        "description": meta.get("description", ""),
        "tags": as_list(meta.get("tags")),
        "aliases": as_list(meta.get("aliases")),
        "purpose": meta.get("purpose", ""),
        "status": meta.get("status", ""),
        "last_verified": meta.get("last_verified", ""),
        "review_after": meta.get("review_after", ""),
        "volatility": meta.get("volatility", ""),
        "source_of_truth": meta.get("source_of_truth", ""),
        "verified_by": as_list(meta.get("verified_by")),
        "related_code": as_list(meta.get("related_code")),
        "common_queries": as_list(meta.get("common_queries")),
        "see_also": as_list(meta.get("see_also")),
        "file": rel(doc.path),
    }
    return {key: value for key, value in entry.items() if value not in ("", [], None)}


def yaml_manifest(entries: list[dict[str, object]]) -> str:
    lines = [
        "# Arn Documentation Manifest",
        "# Generated from live Markdown docs. Do not edit by hand.",
        "# Agents should match title/description/tags/aliases/common_queries/related_code against the task, then read only relevant files.",
        "# Regenerate with: ./scripts/docs.py generate",
        "",
    ]
    scalar_keys = [
        "title",
        "description",
        "purpose",
        "status",
        "last_verified",
        "review_after",
        "volatility",
        "source_of_truth",
        "file",
    ]
    list_keys = [
        "tags",
        "aliases",
        "verified_by",
        "related_code",
        "common_queries",
        "see_also",
    ]
    for entry in entries:
        lines.append(f"- title: {yaml_quote(entry.get('title', ''))}")
        for key in scalar_keys[1:]:
            if key in entry:
                lines.append(f"  {key}: {yaml_scalar(entry[key])}")
        for key in list_keys:
            lines.extend(yaml_block_list(key, entry.get(key), indent=2))
        lines.append("")
    return "\n".join(lines)


def search_jsonl(docs: list[MarkdownFile]) -> str:
    rows = []
    for doc in docs:
        row = doc_entry(doc)
        row["headings"] = headings(doc)
        row["summary"] = summary(doc)
        rows.append(json.dumps(row, ensure_ascii=False, sort_keys=True))
    return "\n".join(rows) + ("\n" if rows else "")


def sections_yaml(docs: list[MarkdownFile]) -> str:
    lines = [
        "# Arn Documentation Sections",
        "# Generated heading index for section-level routing. Do not edit by hand.",
        "# Regenerate with: ./scripts/docs.py generate",
        "",
    ]
    for doc in docs:
        meta = doc.meta
        for heading in headings(doc):
            lines.append(f"- file: {rel(doc.path)}")
            lines.append(f"  title: {yaml_quote(meta.get('title', ''))}")
            lines.append(f"  heading: {yaml_quote(heading['heading'])}")
            lines.append(f"  level: {heading['level']}")
            lines.append(f"  line: {heading['line']}")
            lines.extend(yaml_block_list("tags", as_list(meta.get("tags")), indent=2))
            lines.append("")
    return "\n".join(lines)



def llms_txt(docs: list[MarkdownFile]) -> str:
    groups: dict[str, list[MarkdownFile]] = {}
    for doc in docs:
        groups.setdefault(str(doc.meta.get("purpose", "reference")), []).append(doc)

    lines = [
        "# Arn",
        "",
        "Arn is the productionisation repository for ARN's AI Traffic Bulletin platform.",
        "",
        "## Start Here",
        "",
        "- `AGENTS.md` - canonical agent instructions",
        "- `docs/CODEMAPS/ARCHITECTURE.md` - how the platform fits together",
        "- `docs/_manifest.yaml` - generated doc routing index",
        "- `docs/_search.jsonl` - generated search index for agents and tools",
        "- `docs/_sections.yaml` - generated section-level routing index",
        "",
        "## Generated Indexes",
        "",
        "- `docs/_manifest.yaml`",
        "- `docs/_search.jsonl`",
        "- `docs/_sections.yaml`",
        "- `docs/_data.js`",
        "- `llms.txt`",
        "",
    ]

    for purpose in ["overview", "decision-log", "reference", "runbook"]:
        items = groups.get(purpose, [])
        if not items:
            continue
        lines.append(f"## {purpose.title()} Docs")
        lines.append("")
        for doc in items:
            lines.append(f"- `{rel(doc.path)}` - {doc.meta.get('description', '')}")
        lines.append("")

    lines.extend(
        [
            "## Maintenance",
            "",
            "- Use `./scripts/docs.py new-doc`, `add-decision`, or `new-skill` for source creation.",
            "- Regenerate indexes with `./scripts/docs.py generate`.",
            "- Validate health with `./scripts/docs.py check`.",
            "",
        ]
    )
    return "\n".join(lines)


def data_js(docs: list[MarkdownFile]) -> str:
    """Doc-portal data bundle consumed by docs/index.html (window.DOCS_DATA).

    Mirrors the search rows but includes the full body for in-portal rendering.
    """
    rows = []
    for doc in docs:
        row = doc_entry(doc)
        row["headings"] = headings(doc)
        row["summary"] = summary(doc)
        row["body"] = doc.body
        rows.append(row)
    payload = json.dumps(rows, ensure_ascii=False, indent=2)
    # Escape "</" so a doc body containing "</script>" can't close the host <script> tag.
    payload = payload.replace("</", "<\\/")
    return "window.DOCS_DATA=" + payload + ";\n"


def generated_contents() -> dict[Path, str]:
    docs = iter_docs()
    entries = [doc_entry(doc) for doc in docs]
    return {
        Path("docs/_manifest.yaml"): yaml_manifest(entries),
        Path("docs/_search.jsonl"): search_jsonl(docs),
        Path("docs/_sections.yaml"): sections_yaml(docs),
        Path("docs/_data.js"): data_js(docs),
        Path("llms.txt"): llms_txt(docs),
    }


def command_generate(_: argparse.Namespace) -> int:
    changed = []
    for path, content in generated_contents().items():
        full_path = ROOT / path
        if full_path.exists() and read_text(full_path) == content:
            continue
        write_text(full_path, content)
        changed.append(str(path))
    if changed:
        print("Updated generated indexes:")
        for path in changed:
            print(f"- {path}")
    else:
        print("[OK] Generated indexes are current")
    return 0


def codemap_clip(text: str) -> str:
    text = CODEMAP_WHITESPACE_RE.sub(" ", text).strip().replace("|", "\\|")
    if len(text) <= CODEMAP_SUMMARY_MAX_CHARS:
        return text
    truncated = text[:CODEMAP_SUMMARY_MAX_CHARS].rsplit(" ", 1)[0].rstrip(",.;:")
    return truncated + "…"


def codemap_first_type_decl(lines: list[str]) -> int | None:
    for index, line in enumerate(lines):
        if CODEMAP_TYPE_DECL_RE.match(line):
            return index
    return None


def codemap_type_summary(lines: list[str], decl_index: int) -> str | None:
    """First content line of the `<summary>` doc comment directly above `decl_index`."""
    doc_lines: list[str] = []
    cursor = decl_index - 1
    while cursor >= 0:
        raw = lines[cursor]
        stripped = raw.strip()
        if stripped.startswith("///"):
            doc_lines.insert(0, stripped[3:].strip())
            cursor -= 1
            continue
        if stripped == "" or CODEMAP_ATTRIBUTE_LINE_RE.match(raw):
            cursor -= 1
            continue
        break
    if not doc_lines:
        return None
    match = re.search(r"<summary>(.*?)(?:</summary>|$)", "\n".join(doc_lines), re.DOTALL)
    if not match:
        return None
    first_line = next((line.strip() for line in match.group(1).splitlines() if line.strip()), "")
    if not first_line:
        return None
    return CODEMAP_XML_TAG_RE.sub("", first_line)


def codemap_fallback_comment(lines: list[str]) -> str | None:
    """First top-of-file `//` (non-doc) comment line, before any real code."""
    for line in lines:
        stripped = line.strip()
        if not stripped:
            continue
        if stripped.startswith("using ") or stripped.startswith("namespace "):
            continue
        if stripped.startswith("///"):
            continue
        if stripped.startswith("//"):
            text = stripped[2:].strip()
            if text:
                return CODEMAP_XML_TAG_RE.sub("", text)
            continue
        if CODEMAP_ATTRIBUTE_LINE_RE.match(line):
            continue
        break
    return None


def codemap_purpose(path: Path) -> str:
    """Purpose = first `<summary>` line of the file's first type decl, else the first
    top-of-file `//` comment, else an em-dash placeholder."""
    lines = read_text(path).splitlines()
    decl_index = codemap_first_type_decl(lines)
    if decl_index is not None:
        summary = codemap_type_summary(lines, decl_index)
        if summary:
            return codemap_clip(summary)
    fallback = codemap_fallback_comment(lines)
    if fallback:
        return codemap_clip(fallback)
    return "—"


def codemap_tracked_files(project: str) -> list[Path]:
    prefix = f"src/ArnBulletinGenerator.{project}"
    output = subprocess.run(
        ["git", "ls-files", "--", prefix],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=True,
    ).stdout.splitlines()
    files = []
    for entry in output:
        if not entry.endswith(".cs"):
            continue
        if "obj" in Path(entry).parts or "bin" in Path(entry).parts:
            continue
        path = ROOT / entry
        if path.exists():
            files.append(path)
    return files


def codemap_group_by_folder(project: str, files: list[Path]) -> dict[str, list[Path]]:
    prefix = f"ArnBulletinGenerator.{project}/"
    groups: dict[str, list[Path]] = {}
    for path in files:
        inner = rel(path).split(prefix, 1)[1]
        folder = "/".join(inner.split("/")[:-1]) or "Root"
        groups.setdefault(folder, []).append(path)
    return groups


def codemap_project_block(project: str) -> tuple[str, int, int]:
    """Render one project's marker-region body: one `| File | Purpose |` table per
    folder plus a trailing total. Returns (block_text, file_count, no_summary_count)."""
    groups = codemap_group_by_folder(project, codemap_tracked_files(project))
    order = sorted(groups, key=lambda key: (key != "Root", key))
    lines: list[str] = []
    total = 0
    dashes = 0
    for folder in order:
        heading = "### Root" if folder == "Root" else f"### `{folder}/`"
        lines.append(heading)
        lines.append("| File | Purpose |")
        lines.append("|---|---|")
        for path in sorted(groups[folder], key=lambda p: p.name):
            purpose = codemap_purpose(path)
            if purpose == "—":
                dashes += 1
            lines.append(f"| `{path.name}` | {purpose} |")
            total += 1
    lines.append("")
    lines.append(f"**Total:** {total} files")
    return "\n".join(lines).rstrip() + "\n", total, dashes


def codemap_blocks() -> dict[str, tuple[str, int, int]]:
    return {project: codemap_project_block(project) for project in BACKEND_PROJECTS}


def codemap_marker(project: str, kind: str) -> str:
    return f"<!-- codemap:{kind} {project} -->"


def apply_codemap_markers(
    text: str, blocks: dict[str, tuple[str, int, int]]
) -> tuple[str, list[str], bool]:
    """Replace the marker-delimited codemap regions in `text` with fresh `blocks`.

    Returns (new_text, notes, changed). `notes` records any project whose marker
    pair was missing and had to be inserted under its `## ` heading. `changed` is
    True if any marker region's content differs from what's already there.
    """
    notes: list[str] = []
    changed = False
    for project, (block, _, _) in blocks.items():
        begin, end = codemap_marker(project, "begin"), codemap_marker(project, "end")
        pattern = re.compile(re.escape(begin) + r"(.*?)" + re.escape(end), re.DOTALL)
        match = pattern.search(text)
        body = f"\n{block}"
        if match:
            if match.group(1) != body:
                changed = True
            text = text[: match.start()] + begin + body + end + text[match.end() :]
            continue
        heading_re = re.compile(
            r"^(## `src/ArnBulletinGenerator\." + re.escape(project) + r"/`[ \t]*)\n",
            re.MULTILINE,
        )
        heading_match = heading_re.search(text)
        if not heading_match:
            raise RuntimeError(
                f"is missing a '## `src/ArnBulletinGenerator.{project}/`' heading "
                "to anchor the codemap block"
            )
        insert_at = heading_match.end()
        text = text[:insert_at] + f"\n{begin}{body}{end}\n" + text[insert_at:]
        notes.append(
            f"{project}: no existing marker pair; inserted a new block directly under "
            f"its `## `src/ArnBulletinGenerator.{project}/`` heading"
        )
        changed = True
    return text, notes, changed


def bump_codemap_dates(text: str) -> str:
    today = TODAY.isoformat()
    text = re.sub(r"(?m)^last_verified:\s*.*$", f"last_verified: {today}", text, count=1)
    text = re.sub(r"(?m)^(\*\*Last Updated\*\*:\s*).*$", rf"\g<1>{today}", text, count=1)
    return text


def command_codemap(_: argparse.Namespace) -> int:
    path = ROOT / CODEMAP_DOC
    blocks = codemap_blocks()
    updated, notes, changed = apply_codemap_markers(read_text(path), blocks)
    for note in notes:
        print(f"[INFO] {rel(path)}: {note}")
    for project, (_, total, dashes) in blocks.items():
        pct = (dashes / total * 100) if total else 0.0
        print(f"  {project}: {total} files, {dashes} without an extracted summary ({pct:.1f}%)")
    if not changed:
        print(f"[OK] {rel(path)} codemap tables are current")
        return 0
    write_text(path, bump_codemap_dates(updated))
    print(f"Updated {rel(path)} codemap tables")
    return 0


def parse_date(value: object) -> date | None:
    try:
        return datetime.strptime(str(value), "%Y-%m-%d").date()
    except ValueError:
        return None


def validate_docs(errors: list[str], warnings: list[str]) -> None:
    for doc in iter_docs():
        path = rel(doc.path)
        if doc.path.name.endswith(".prompt.md"):
            errors.append(
                f"{path} is a prompt artifact; move it under .agents/prompts/"
            )
        if not doc.meta:
            errors.append(f"{path} is missing YAML frontmatter")
            continue
        for key in DOC_REQUIRED:
            value = doc.meta.get(key)
            if value in (None, "", []):
                errors.append(f"{path} missing {key} frontmatter")

        purpose = str(doc.meta.get("purpose", ""))
        status = str(doc.meta.get("status", ""))
        volatility = str(doc.meta.get("volatility", ""))
        if purpose and purpose not in PURPOSES:
            errors.append(f"{path} has invalid purpose: {purpose}")
        if status and status not in STATUSES:
            errors.append(f"{path} has invalid status: {status}")
        if volatility and volatility not in VOLATILITIES:
            errors.append(f"{path} has invalid volatility: {volatility}")

        for key in ["last_verified", "review_after"]:
            value = doc.meta.get(key)
            if value and not parse_date(value):
                errors.append(f"{path} has invalid {key} date: {value}")

        review_after = parse_date(doc.meta.get("review_after", ""))
        if review_after and review_after < TODAY and status == "current":
            errors.append(f"{path} review_after has passed; verify it or mark it stale")

        for key in ["see_also", "related_code"]:
            for ref in as_list(doc.meta.get(key)):
                if ref.startswith(("http://", "https://")):
                    continue
                check_ref = ref.split("#", 1)[0]
                if "*" in check_ref or not check_ref:
                    continue
                if not (ROOT / check_ref).exists():
                    errors.append(f"{path} {key} reference does not exist: {ref}")


def validate_generated(errors: list[str]) -> None:
    expected = generated_contents()
    for path, content in expected.items():
        actual_path = ROOT / path
        if not actual_path.exists():
            errors.append(f"{path} is missing; run ./scripts/docs.py generate")
            continue
        if read_text(actual_path) != content:
            errors.append(f"{path} is stale; run ./scripts/docs.py generate")


def validate_skills(errors: list[str]) -> None:
    skills_root = ROOT / ".agents/skills"
    if not skills_root.exists():
        return
    for skill in iter_skills():
        path = rel(skill.path)
        expected_name = skill.path.parent.name
        name = str(skill.meta.get("name", ""))
        description = str(skill.meta.get("description", ""))
        if not name:
            errors.append(f"{path} missing skill name")
        if name and name != expected_name:
            errors.append(
                f"{path} name '{name}' does not match directory '{expected_name}'"
            )
        if not description:
            errors.append(f"{path} missing skill description")
        if "TODO" in description:
            errors.append(f"{path} still contains TODO description")

    for entry in sorted(skills_root.glob("*/*")):
        if entry.parent.name in THIRD_PARTY_SKILLS:
            continue
        if entry.name not in {"SKILL.md", "scripts", "references", "assets"}:
            errors.append(
                f"{rel(entry)} is not part of the universal skill contract; "
                "use SKILL.md plus optional scripts/, references/, and assets/"
            )


def validate_decisions(errors: list[str]) -> None:
    path = ROOT / "docs/canonical/decisions.md"
    if not path.exists():
        return
    text = parse_markdown(path).body
    matches = list(re.finditer(r"^## \d{4}-\d{2}-\d{2} .*$", text, re.MULTILINE))
    required = [
        "**Status:**",
        "**Context:**",
        "**Decision:**",
        "**Rationale:**",
        "**Consequences:**",
    ]
    for index, match in enumerate(matches):
        start = match.end()
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        block = text[start:end]
        heading = match.group(0)
        for marker in required:
            if marker not in block:
                errors.append(f"docs/canonical/decisions.md {heading} missing {marker}")


def line_count(path: Path) -> int:
    return len(read_text(path).splitlines())


def validate_hard_budgets(errors: list[str]) -> None:
    """Always-on context files get ERRORs: they are read in every agent session,
    so runaway growth (or single lines long enough to truncate in file reads)
    directly degrades every future session. Fix by rewriting the file in place
    rather than appending a dated log."""
    for path, budget in HARD_LINE_BUDGETS.items():
        full = ROOT / path
        if full.exists() and line_count(full) > budget:
            errors.append(
                f"{path} is {line_count(full)} lines; hard budget is {budget} — "
                "rewrite in place, move dated history to docs/archive/context-history.md"
            )
    for path, max_len in HARD_LINE_LENGTH.items():
        full = ROOT / path
        if not full.exists():
            continue
        for number, line in enumerate(read_text(full).splitlines(), start=1):
            if len(line) > max_len:
                errors.append(
                    f"{path}:{number} line is {len(line)} chars (max {max_len}) — "
                    "long lines truncate in agent file reads; wrap or split into sub-bullets"
                )


def validate_budgets(warnings: list[str]) -> None:
    budgeted = set(LINE_BUDGETS)
    for path, budget in LINE_BUDGETS.items():
        full = ROOT / path
        if full.exists() and line_count(full) > budget:
            warnings.append(
                f"{path} is {line_count(full)} lines; soft budget is {budget}"
            )
    for skill in iter_skills():
        if Path(rel(skill.path)) in budgeted:
            continue
        count = line_count(skill.path)
        if count > 120:
            warnings.append(f"{rel(skill.path)} is {count} lines; soft budget is 120")
    for doc in iter_docs():
        if Path(rel(doc.path)) in budgeted:
            continue
        count = line_count(doc.path)
        if count > 500:
            warnings.append(
                f"{rel(doc.path)} is {count} lines; consider splitting or section routing"
            )


def validate_codemap(errors: list[str]) -> None:
    path = ROOT / CODEMAP_DOC
    if not path.exists():
        return
    try:
        _, _, changed = apply_codemap_markers(read_text(path), codemap_blocks())
    except RuntimeError as exc:
        errors.append(f"{rel(path)} {exc}")
        return
    if changed:
        errors.append(
            f"{rel(path)} codemap tables are stale; run ./scripts/docs.py codemap"
        )


def command_check(_: argparse.Namespace) -> int:
    errors: list[str] = []
    warnings: list[str] = []
    validate_docs(errors, warnings)
    validate_generated(errors)
    validate_codemap(errors)
    validate_skills(errors)
    validate_decisions(errors)
    validate_hard_budgets(errors)
    validate_budgets(warnings)

    for warning in warnings:
        print(f"WARN: {warning}", file=sys.stderr)
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    if errors:
        return 1
    print("[OK] Documentation harness checks passed")
    return 0


def tokenize(value: str) -> list[str]:
    return [
        token
        for token in re.findall(r"[a-z0-9]+", value.lower())
        if token not in STOP_WORDS
    ]


def weighted_text(doc: MarkdownFile) -> list[tuple[str, int]]:
    meta = doc.meta
    fields: list[tuple[str, int]] = [
        (str(meta.get("title", "")), 8),
        (str(meta.get("description", "")), 5),
        (" ".join(as_list(meta.get("aliases"))), 8),
        (" ".join(as_list(meta.get("tags"))), 6),
        (" ".join(as_list(meta.get("common_queries"))), 5),
        (" ".join(as_list(meta.get("related_code"))), 4),
        (" ".join(heading["heading"] for heading in headings(doc)), 3),
        (summary(doc), 2),
    ]
    return fields


def weighted_skill_text(skill: MarkdownFile) -> list[tuple[str, int]]:
    meta = skill.meta
    return [
        (str(meta.get("name", "")), 8),
        (str(meta.get("description", "")), 6),
        (skill.body, 2),
    ]


def route_score(query_tokens: list[str], doc: MarkdownFile) -> tuple[int, list[str]]:
    score = 0
    matched: set[str] = set()
    for text, weight in weighted_text(doc):
        tokens = set(tokenize(text))
        for token in query_tokens:
            if token in tokens:
                score += weight
                matched.add(token)
            else:
                partials = [
                    candidate
                    for candidate in tokens
                    if token in candidate or candidate in token
                ]
                if partials and len(token) >= 4:
                    score += max(1, weight // 2)
                    matched.add(token)
    return score, sorted(matched)


def skill_route_score(
    query_tokens: list[str], skill: MarkdownFile
) -> tuple[int, list[str]]:
    score = 0
    matched: set[str] = set()
    for text, weight in weighted_skill_text(skill):
        tokens = set(tokenize(text))
        for token in query_tokens:
            if token in tokens:
                score += weight
                matched.add(token)
            else:
                partials = [
                    candidate
                    for candidate in tokens
                    if token in candidate or candidate in token
                ]
                if partials and len(token) >= 4:
                    score += max(1, weight // 2)
                    matched.add(token)
    return score, sorted(matched)


def ranked_headings(
    query_tokens: list[str], doc: MarkdownFile, limit: int
) -> list[dict[str, object]]:
    scored = []
    for heading in headings(doc):
        tokens = set(tokenize(str(heading["heading"])))
        score = sum(1 for token in query_tokens if token in tokens)
        if score > 0:
            scored.append((score, heading))
    if scored:
        scored.sort(key=lambda item: (-item[0], int(item[1]["line"])))
        return [heading for _, heading in scored[:limit]]
    return headings(doc)[:limit]


def command_route(args: argparse.Namespace) -> int:
    query = " ".join(args.query).strip()
    query_tokens = tokenize(query)
    if not query_tokens:
        print("ERROR: provide a search query", file=sys.stderr)
        return 1

    ranked = []
    for doc in iter_docs():
        score, matched = route_score(query_tokens, doc)
        if score > 0:
            ranked.append((score, matched, doc))
    ranked.sort(key=lambda item: (-item[0], rel(item[2].path)))

    ranked_skills = []
    for skill in iter_skills():
        score, matched = skill_route_score(query_tokens, skill)
        if score > 0:
            ranked_skills.append((score, matched, skill))
    ranked_skills.sort(key=lambda item: (-item[0], rel(item[2].path.parent)))

    if not ranked and not ranked_skills:
        print(
            "No indexed docs or skills matched. Try broader terms or inspect docs/_search.jsonl."
        )
        return 1

    print(f"Route query: {query}")
    if ranked_skills:
        print("Use these skills if applicable:")
        for score, matched, skill in ranked_skills[: args.skills]:
            print(f"- {rel(skill.path.parent)}")
            print(f"  score: {score}; matched: {', '.join(matched)}")
            print(f"  why: {skill.meta.get('description', '')}")
    if ranked:
        print("Open these docs first:")
        for score, matched, doc in ranked[: args.limit]:
            meta = doc.meta
            print(f"- {rel(doc.path)}")
            print(f"  score: {score}; matched: {', '.join(matched)}")
            print(f"  why: {meta.get('description', '')}")
            top_headings = ranked_headings(query_tokens, doc, args.sections)
            if top_headings:
                print("  sections:")
                for heading in top_headings:
                    print(f"    - L{heading['line']}: {heading['heading']}")
    return 0


def slugify(value: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    return slug or "untitled"


def split_csv(value: str | None) -> list[str]:
    if not value:
        return []
    return [item.strip() for item in value.split(",") if item.strip()]


def next_review_after(volatility: str) -> str:
    days = {"high": 14, "medium": 30, "low": 90}.get(volatility, 30)
    return date.fromordinal(TODAY.toordinal() + days).isoformat()


def command_new_doc(args: argparse.Namespace) -> int:
    purpose_dir = {
        "overview": "docs/canonical",
        "decision-log": "docs/canonical",
        "reference": "docs/reference",
        "runbook": "docs/operations",
    }[args.purpose]
    path = ROOT / (args.path or f"{purpose_dir}/{slugify(args.title)}.md")
    if path.exists() and not args.force:
        print(f"ERROR: {rel(path)} already exists", file=sys.stderr)
        return 1

    volatility = args.volatility
    meta: dict[str, object] = {
        "title": args.title,
        "description": args.description,
        "tags": split_csv(args.tags),
        "aliases": split_csv(args.aliases) or [args.title],
        "purpose": args.purpose,
        "status": args.status,
        "last_verified": args.last_verified or TODAY.isoformat(),
        "review_after": args.review_after or next_review_after(volatility),
        "volatility": volatility,
        "source_of_truth": args.source_of_truth,
        "verified_by": split_csv(args.verified_by) or ["manual review"],
        "related_code": split_csv(args.related_code),
        "common_queries": args.common_query or [f"When should I read {args.title}?"],
        "see_also": split_csv(args.see_also),
    }
    body = f"\n# {args.title}\n\n{args.description}\n\n## Notes\n\n- Add durable facts here.\n"
    write_text(path, dump_frontmatter(meta) + body)
    print(f"Created source doc: {rel(path)}")
    command_generate(args)
    return 0


def command_add_decision(args: argparse.Namespace) -> int:
    path = ROOT / "docs/canonical/decisions.md"
    doc = parse_markdown(path)
    entry = (
        f"\n## {args.date or TODAY.isoformat()} — {args.title}\n\n"
        f"**Status:** {args.status}\n\n"
        f"**Context:** {args.context}\n\n"
        f"**Decision:** {args.decision}\n\n"
        f"**Rationale:** {args.rationale}\n\n"
        f"**Consequences:** {args.consequences}\n"
    )
    write_text(path, read_text(path).rstrip() + "\n" + entry)
    print(f"Appended decision: {rel(path)}")
    command_generate(args)
    return 0


def command_new_skill(args: argparse.Namespace) -> int:
    name = slugify(args.name)
    path = ROOT / ".agents/skills" / name / "SKILL.md"
    if path.exists() and not args.force:
        print(f"ERROR: {rel(path)} already exists", file=sys.stderr)
        return 1
    body = (
        f"---\nname: {name}\ndescription: {args.description}\n---\n\n"
        f"# {args.title or name.replace('-', ' ').title()}\n\n"
        "Use this skill for repeatable Arn-specific work that should load on demand.\n\n"
        "## Workflow\n\n"
        "1. Read `docs/_search.jsonl` or `docs/_manifest.yaml` to route.\n"
        "2. Open only the docs relevant to the task.\n"
        "3. Do the work, then update matching docs if behavior or workflow changed.\n"
    )
    write_text(path, body)
    print(f"Created source skill: {rel(path)}")
    command_generate(args)
    return 0


def command_doctor(args: argparse.Namespace) -> int:
    return command_check(args)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Single interface for Arn documentation sources, generated indexes, and portable skills"
    )
    sub = parser.add_subparsers(dest="command", required=True)

    generate = sub.add_parser(
        "generate", help="Regenerate all derived indexes from source docs and skills"
    )
    generate.set_defaults(func=command_generate)

    codemap = sub.add_parser(
        "codemap",
        help="Regenerate the backend per-project file tables in docs/CODEMAPS/FILES.md",
    )
    codemap.set_defaults(func=command_codemap)

    route = sub.add_parser(
        "route",
        help="Suggest relevant docs, sections, and portable skills for a task",
        description="Route a task summary to the smallest useful set of docs, sections, and skills.",
    )
    route.add_argument(
        "query", nargs="+", help="Task summary, file path, concept, or user request"
    )
    route.add_argument("--limit", type=int, default=5, help="Maximum docs to return")
    route.add_argument(
        "--sections", type=int, default=6, help="Maximum sections to show per doc"
    )
    route.add_argument(
        "--skills", type=int, default=3, help="Maximum portable skills to return"
    )
    route.set_defaults(func=command_route)

    check = sub.add_parser(
        "check", help="Validate source docs, generated indexes, decisions, and skills"
    )
    check.set_defaults(func=command_check)

    doctor = sub.add_parser("doctor", help="Alias for check")
    doctor.set_defaults(func=command_doctor)

    new_doc = sub.add_parser(
        "new-doc", help="Create a source Markdown doc with complete frontmatter"
    )
    new_doc.add_argument("--title", required=True)
    new_doc.add_argument("--description", required=True)
    new_doc.add_argument("--tags", required=True, help="Comma-separated tags")
    new_doc.add_argument("--purpose", required=True, choices=sorted(PURPOSES))
    new_doc.add_argument("--source-of-truth", required=True)
    new_doc.add_argument("--status", default="current", choices=sorted(STATUSES))
    new_doc.add_argument("--volatility", default="medium", choices=sorted(VOLATILITIES))
    new_doc.add_argument("--aliases", default="")
    new_doc.add_argument("--related-code", default="")
    new_doc.add_argument("--see-also", default="")
    new_doc.add_argument("--verified-by", default="")
    new_doc.add_argument("--review-after")
    new_doc.add_argument("--last-verified")
    new_doc.add_argument("--common-query", action="append")
    new_doc.add_argument("--path")
    new_doc.add_argument("--force", action="store_true")
    new_doc.set_defaults(func=command_new_doc)

    decision = sub.add_parser(
        "add-decision", help="Append a complete decision-log entry"
    )
    decision.add_argument("--title", required=True)
    decision.add_argument(
        "--status", default="accepted", choices=["proposed", "accepted", "superseded"]
    )
    decision.add_argument("--context", required=True)
    decision.add_argument("--decision", required=True)
    decision.add_argument("--rationale", required=True)
    decision.add_argument("--consequences", required=True)
    decision.add_argument("--date")
    decision.set_defaults(func=command_add_decision)

    skill = sub.add_parser("new-skill", help="Create a universal portable SKILL.md")
    skill.add_argument("name")
    skill.add_argument("--description", required=True)
    skill.add_argument("--title")
    skill.add_argument("--force", action="store_true")
    skill.set_defaults(func=command_new_skill)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
