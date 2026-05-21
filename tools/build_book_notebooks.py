#!/usr/bin/env python3
"""Generate JupyterLab-friendly notebooks from the Quarto book sources.

Each chapter .qmd is split into a sequence of notebook cells:
  - prose runs                -> markdown cells (with .qmd cross-refs rewritten to .ipynb)
  - ```csharp/```fsharp/...   -> code cells, language tagged via cell metadata
  - ```{mermaid} blocks       -> markdown cells with the SVG that Quarto pre-renders
                                 into book/_build/<chapter>.html

We do not wire a real kernel: code cells are non-executable and exist only so
JupyterLab presents the book as a notebook tree with cell-level editing rather
than a wall of static HTML.

Run: python3 tools/build_book_notebooks.py
"""

from __future__ import annotations

import html
import json
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BOOK_DIR = ROOT / "book"
BUILD_DIR = BOOK_DIR / "_build"
OUT_DIR = ROOT / "docs" / "book-notebooks"

CODE_LANGUAGES = {"csharp", "fsharp", "haskell", "rust", "bash", "shell", "json", "yaml", "text"}


@dataclass(frozen=True)
class Chapter:
    source: Path           # absolute .qmd path
    rel: Path              # relative to BOOK_DIR e.g. part1/01-blub-paradox.qmd
    title: str

    @property
    def notebook_path(self) -> Path:
        return OUT_DIR / self.rel.with_suffix(".ipynb")

    @property
    def html_path(self) -> Path:
        return BUILD_DIR / self.rel.with_suffix(".html")


# ── front matter / titles ────────────────────────────────────────────────

def strip_front_matter(text: str) -> tuple[dict[str, str], str]:
    if not text.startswith("---\n"):
        return {}, text
    end = text.find("\n---", 4)
    if end == -1:
        return {}, text
    raw = text[4:end].strip()
    body = text[end + len("\n---"):].lstrip("\n")
    data: dict[str, str] = {}
    for line in raw.splitlines():
        if ":" not in line:
            continue
        key, _, value = line.partition(":")
        data[key.strip()] = value.strip().strip('"').strip("'")
    return data, body


def chapter_paths() -> list[Path]:
    """Read the chapter list out of book/_quarto.yml (in order)."""
    quarto = BOOK_DIR / "_quarto.yml"
    paths: list[Path] = []
    in_chapters = False
    for raw_line in quarto.read_text(encoding="utf-8").splitlines():
        stripped = raw_line.strip()
        if stripped == "chapters:":
            in_chapters = True
            continue
        if in_chapters and stripped and not raw_line.startswith(" "):
            break
        if in_chapters:
            match = re.match(r"-\s+(.+\.qmd)\s*$", stripped)
            if match:
                paths.append(BOOK_DIR / match.group(1))
    return paths


def plain_text(markdown: str) -> str:
    markdown = re.sub(r"`([^`]+)`", r"\1", markdown)
    markdown = re.sub(r"\*\*([^*]+)\*\*", r"\1", markdown)
    markdown = re.sub(r"(?<!\*)\*([^*]+)\*(?!\*)", r"\1", markdown)
    markdown = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", markdown)
    return re.sub(r"\s+", " ", markdown).strip()


def chapter_title(path: Path) -> str:
    metadata, body = strip_front_matter(path.read_text(encoding="utf-8"))
    if metadata.get("title"):
        return metadata["title"]
    for line in body.splitlines():
        match = re.match(r"^#{1,6}\s+(.+?)(?:\s+\{[^}]+\})?\s*$", line)
        if match:
            return plain_text(match.group(1))
    return path.stem


# ── quarto render (for inline SVGs) ──────────────────────────────────────

def ensure_quarto_html(chapters: list[Chapter]) -> None:
    """Render the book to HTML so we have inline SVGs for mermaid diagrams.

    We rely on `mermaid-format: svg` in book/_quarto.yml so each diagram is
    embedded as an `<svg>` element in the rendered .html. Quarto's freeze
    cache keeps repeat renders cheap.
    """
    if shutil.which("quarto") is None:
        print("[notebooks] quarto not on PATH — skipping HTML render; mermaid will appear as source")
        return

    missing = [c for c in chapters if not c.html_path.exists()]
    if not missing:
        return

    print(f"[notebooks] rendering Quarto HTML ({len(missing)} chapters missing) for SVG diagrams...")
    proc = subprocess.run(
        ["quarto", "render", "--to", "html"],
        cwd=BOOK_DIR,
        check=False,
    )
    if proc.returncode != 0:
        print(f"[notebooks] WARNING: quarto render exited {proc.returncode}; SVGs may be missing", file=sys.stderr)


def extract_svgs(html_path: Path) -> list[str]:
    """Return inline <svg>…</svg> blocks from a rendered Quarto HTML page, in order."""
    if not html_path.exists():
        return []
    text = html_path.read_text(encoding="utf-8")
    # Greedy-friendly: <svg ...>...</svg> non-overlapping.
    return re.findall(r"<svg\b[^>]*>.*?</svg>", text, re.S)


# ── cell splitting ───────────────────────────────────────────────────────

QMD_LINK_RE = re.compile(r"\((?P<rel>[^)\s#]+\.qmd)(?P<frag>#[^)\s]*)?\)")
HEADING_ATTR_RE = re.compile(r"\s+\{[^}]+\}\s*$")


def rewrite_links(markdown: str) -> str:
    """Rewrite [text](path/to/file.qmd) -> [text](path/to/file.ipynb)."""
    def sub(match: re.Match[str]) -> str:
        rel = match.group("rel")
        frag = match.group("frag") or ""
        return f"({Path(rel).with_suffix('.ipynb').as_posix()}{frag})"
    return QMD_LINK_RE.sub(sub, markdown)


def strip_heading_attrs(markdown: str) -> str:
    """Drop Pandoc heading attributes like `## Title {.unnumbered}`.

    JupyterLab renders headings via plain CommonMark, so the trailing `{...}`
    would otherwise show up as literal text.
    """
    return re.sub(
        r"(?m)^(#{1,6}\s+.+?)\s+\{[^}]+\}\s*$",
        r"\1",
        markdown,
    )


@dataclass
class RawBlock:
    kind: str           # 'markdown' | 'code' | 'mermaid'
    language: str       # for 'code'
    text: str           # cell source (without fences for code, full markdown for markdown)


def split_qmd(body: str) -> list[RawBlock]:
    lines = body.splitlines()
    out: list[RawBlock] = []
    buf: list[str] = []
    i = 0

    def flush_markdown() -> None:
        if not buf:
            return
        text = "\n".join(buf).strip("\n")
        if text.strip():
            out.append(RawBlock("markdown", "", text))
        buf.clear()

    while i < len(lines):
        line = lines[i]
        fence = re.match(r"^```(.*)$", line.rstrip())
        if fence:
            spec = fence.group(1).strip()
            language = spec
            is_mermaid = False
            if spec.startswith("{") and spec.endswith("}"):
                inner = spec[1:-1].strip()
                # `{mermaid}` or `{.csharp}` etc — strip leading dot
                inner = inner.lstrip(".")
                language = inner
                is_mermaid = inner.lower() == "mermaid"
            elif spec.lower() == "mermaid":
                language = "mermaid"
                is_mermaid = True

            code_lines: list[str] = []
            i += 1
            while i < len(lines) and not re.match(r"^```\s*$", lines[i].rstrip()):
                code_lines.append(lines[i])
                i += 1
            i += 1  # consume closing fence
            flush_markdown()
            if is_mermaid:
                out.append(RawBlock("mermaid", "mermaid", "\n".join(code_lines)))
            else:
                out.append(RawBlock("code", language or "text", "\n".join(code_lines)))
            continue

        buf.append(line)
        i += 1

    flush_markdown()
    return out


# ── notebook assembly ────────────────────────────────────────────────────

_CELL_COUNTER = {"n": 0}


def _next_cell_id() -> str:
    _CELL_COUNTER["n"] += 1
    return f"cell-{_CELL_COUNTER['n']:04d}"


def markdown_cell(source: str) -> dict:
    return {
        "cell_type": "markdown",
        "id": _next_cell_id(),
        "metadata": {},
        "source": _split_lines(source),
    }


def code_cell(source: str, language: str) -> dict:
    return {
        "cell_type": "code",
        "id": _next_cell_id(),
        "execution_count": None,
        "metadata": {
            "vscode": {"languageId": language},
            "language": language,
        },
        "outputs": [],
        "source": _split_lines(source),
    }


def _split_lines(text: str) -> list[str]:
    """nbformat wants each line with its trailing newline, except the last."""
    if not text:
        return []
    parts = text.split("\n")
    return [p + "\n" for p in parts[:-1]] + [parts[-1]]


_SVG_TAG_RE = re.compile(r"<svg\b([^>]*)>", re.I)
_WIDTH_HEIGHT_ATTR_RE = re.compile(r"\s+(?:width|height)\s*=\s*\"[^\"]*\"", re.I)


def _normalize_svg(svg: str) -> str:
    """Drop Quarto's hard-coded width/height (e.g. 672x480) so the viewBox controls layout.

    Quarto pins every mermaid SVG to a fixed pixel box that ignores the diagram's true
    aspect ratio, which is what makes wide flowcharts shrink into illegible thumbnails
    inside Jupyter's narrow cell column. Removing those attributes and adding
    `width:100%` lets the browser use the viewBox so the diagram fills the cell width
    at the correct aspect ratio.
    """
    def fix(match: re.Match[str]) -> str:
        attrs = _WIDTH_HEIGHT_ATTR_RE.sub("", match.group(1))
        return f'<svg{attrs} style="width:100%;height:auto;max-width:100%;display:block">'
    return _SVG_TAG_RE.sub(fix, svg, count=1)


def mermaid_svg_markdown(svg: str, source: str) -> str:
    """Markdown cell body: just the SVG, sized to fill the cell."""
    return f'<div class="mermaid-diagram" style="width:100%">\n{_normalize_svg(svg)}\n</div>'


def mermaid_fallback_markdown(source: str) -> str:
    """If we couldn't pre-render an SVG, keep the source as a mermaid fence (JupyterLab >=4 renders this)."""
    return f"```mermaid\n{source}\n```"


def build_notebook(chapter: Chapter, chapters: list[Chapter]) -> dict:
    raw = chapter.source.read_text(encoding="utf-8")
    _front, body = strip_front_matter(raw)
    blocks = split_qmd(body)

    svgs = extract_svgs(chapter.html_path)
    svg_iter = iter(svgs)

    cells: list[dict] = []
    cells.append(markdown_cell(f"# {chapter.title}"))

    for block in blocks:
        if block.kind == "markdown":
            text = strip_heading_attrs(rewrite_links(block.text))
            cells.append(markdown_cell(text))
        elif block.kind == "mermaid":
            svg = next(svg_iter, None)
            if svg:
                cells.append(markdown_cell(mermaid_svg_markdown(svg, block.text)))
            else:
                cells.append(markdown_cell(mermaid_fallback_markdown(block.text)))
        else:  # code
            cells.append(code_cell(block.text, block.language or "text"))

    return {
        "cells": cells,
        "metadata": {
            "kernelspec": {
                "display_name": "No kernel (read-only)",
                "language": "text",
                "name": "no-kernel",
            },
            "language_info": {"name": "text"},
            "book_chapter": {
                "title": chapter.title,
                "source": str(chapter.rel),
            },
        },
        "nbformat": 4,
        "nbformat_minor": 5,
    }


def build_index(chapters: list[Chapter]) -> dict:
    """A landing notebook with the table of contents."""
    lines = [
        "# Blub & Fuel: Five Engines, One Domain",
        "",
        "Open any chapter to read it as a notebook. Code blocks appear as cells you can",
        "edit and copy, but no kernel is wired up — this is a read/study experience, not",
        "an execution environment. Mermaid diagrams are pre-rendered to inline SVG.",
        "",
        "## Chapters",
        "",
    ]
    for chapter in chapters:
        href = chapter.notebook_path.relative_to(OUT_DIR).as_posix()
        lines.append(f"1. [{chapter.title}]({href})")
    return {
        "cells": [markdown_cell("\n".join(lines))],
        "metadata": {
            "kernelspec": {
                "display_name": "No kernel (read-only)",
                "language": "text",
                "name": "no-kernel",
            },
            "language_info": {"name": "text"},
        },
        "nbformat": 4,
        "nbformat_minor": 5,
    }


# ── main ─────────────────────────────────────────────────────────────────

def main() -> int:
    paths = chapter_paths()
    if not paths:
        print("No chapters found in book/_quarto.yml", file=sys.stderr)
        return 1

    chapters = [
        Chapter(source=p, rel=p.relative_to(BOOK_DIR), title=chapter_title(p))
        for p in paths
    ]

    ensure_quarto_html(chapters)

    if OUT_DIR.exists():
        for child in OUT_DIR.rglob("*.ipynb"):
            child.unlink()
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    rendered_svg = 0
    missing_svg = 0
    for chapter in chapters:
        _CELL_COUNTER["n"] = 0
        nb = build_notebook(chapter, chapters)
        chapter.notebook_path.parent.mkdir(parents=True, exist_ok=True)
        chapter.notebook_path.write_text(
            json.dumps(nb, indent=1, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )
        svgs = extract_svgs(chapter.html_path)
        # Count mermaid blocks in source to compare
        body = strip_front_matter(chapter.source.read_text(encoding="utf-8"))[1]
        merm_count = sum(1 for b in split_qmd(body) if b.kind == "mermaid")
        rendered_svg += min(len(svgs), merm_count)
        missing_svg += max(merm_count - len(svgs), 0)

    index_path = OUT_DIR / "index.ipynb"
    index_path.write_text(
        json.dumps(build_index(chapters), indent=1, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )

    print(f"[notebooks] wrote {len(chapters)} chapter notebooks + index.ipynb to {OUT_DIR.relative_to(ROOT)}")
    print(f"[notebooks] mermaid diagrams: {rendered_svg} embedded as SVG, {missing_svg} fell back to source")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
