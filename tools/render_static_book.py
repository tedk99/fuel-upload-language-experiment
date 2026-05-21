#!/usr/bin/env python3
"""Render the Quarto book sources to dependency-free static HTML.

This is intentionally a small reader renderer, not a Quarto replacement. It
keeps the checked-in book readable for people who do not have Quarto, Pandoc,
Jupyter, language kernels, or headless Chrome installed.
"""

from __future__ import annotations

import html
import os
import re
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BOOK_DIR = ROOT / "book"
OUT_DIR = ROOT / "docs" / "book"


@dataclass(frozen=True)
class Chapter:
    source: Path
    output: Path
    title: str

    @property
    def source_rel(self) -> Path:
        return self.source.relative_to(BOOK_DIR)

    @property
    def output_rel(self) -> Path:
        return self.output.relative_to(OUT_DIR)


def strip_front_matter(text: str) -> tuple[dict[str, str], str]:
    if not text.startswith("---\n"):
        return {}, text

    end = text.find("\n---", 4)
    if end == -1:
        return {}, text

    raw = text[4:end].strip()
    body = text[end + len("\n---") :].lstrip("\n")
    data: dict[str, str] = {}
    for line in raw.splitlines():
        if ":" not in line:
            continue
        key, value = line.split(":", 1)
        value = value.strip().strip('"').strip("'")
        data[key.strip()] = value
    return data, body


def chapter_paths() -> list[Path]:
    quarto = BOOK_DIR / "_quarto.yml"
    paths: list[Path] = []
    in_chapters = False
    for line in quarto.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if stripped == "chapters:":
            in_chapters = True
            continue
        if in_chapters and stripped and not line.startswith(" "):
            break
        if in_chapters:
            match = re.match(r"-\s+(.+\.qmd)\s*$", stripped)
            if match:
                paths.append(BOOK_DIR / match.group(1))
    return paths


def chapter_title(path: Path) -> str:
    metadata, body = strip_front_matter(path.read_text(encoding="utf-8"))
    if metadata.get("title"):
        return metadata["title"]
    for line in body.splitlines():
        match = re.match(r"^#{1,6}\s+(.+?)(?:\s+\{[^}]+\})?\s*$", line)
        if match:
            return plain_text(match.group(1))
    return path.stem


def plain_text(markdown: str) -> str:
    markdown = re.sub(r"`([^`]+)`", r"\1", markdown)
    markdown = re.sub(r"\*\*([^*]+)\*\*", r"\1", markdown)
    markdown = re.sub(r"(?<!\*)\*([^*]+)\*(?!\*)", r"\1", markdown)
    markdown = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", markdown)
    return re.sub(r"\s+", " ", markdown).strip()


def output_for(path: Path) -> Path:
    return OUT_DIR / path.relative_to(BOOK_DIR).with_suffix(".html")


def relative_href(from_output: Path, to_output: Path) -> str:
    return os.path.relpath(to_output, from_output.parent).replace("\\", "/")


def rewrite_qmd_href(href: str) -> str:
    if href.startswith(("http://", "https://", "mailto:", "#")):
        return href
    if ".qmd" not in href:
        return href
    base, sep, fragment = href.partition("#")
    return str(Path(base).with_suffix(".html")).replace("\\", "/") + (
        sep + fragment if sep else ""
    )


def inline(markdown: str) -> str:
    code_tokens: list[str] = []

    def save_code(match: re.Match[str]) -> str:
        token = f"\x00CODE{len(code_tokens)}\x00"
        code_tokens.append(f"<code>{html.escape(match.group(1))}</code>")
        return token

    text = re.sub(r"`([^`]+)`", save_code, markdown)
    text = html.escape(text)

    def link(match: re.Match[str]) -> str:
        label = match.group(1)
        href = rewrite_qmd_href(html.unescape(match.group(2)))
        return f'<a href="{html.escape(href, quote=True)}">{label}</a>'

    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", link, text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"(?<!\*)\*([^*]+)\*(?!\*)", r"<em>\1</em>", text)

    for index, value in enumerate(code_tokens):
        text = text.replace(f"\x00CODE{index}\x00", value)
    return text


def is_table_start(lines: list[str], index: int) -> bool:
    if index + 1 >= len(lines):
        return False
    current = lines[index].strip()
    separator = lines[index + 1].strip()
    return (
        current.startswith("|")
        and current.endswith("|")
        and separator.startswith("|")
        and bool(re.match(r"^\|[\s:\-|]+\|$", separator))
    )


def split_table_row(line: str) -> list[str]:
    return [cell.strip() for cell in line.strip().strip("|").split("|")]


def slugify(text: str) -> str:
    text = plain_text(text).lower()
    text = re.sub(r"[^a-z0-9]+", "-", text)
    return text.strip("-") or "section"


def markdown_to_html(markdown: str) -> str:
    _, body = strip_front_matter(markdown)
    lines = body.splitlines()
    chunks: list[str] = []
    slugs: dict[str, int] = {}
    i = 0

    def unique_slug(text: str) -> str:
        base = slugify(text)
        count = slugs.get(base, 0)
        slugs[base] = count + 1
        return base if count == 0 else f"{base}-{count + 1}"

    def starts_block(index: int) -> bool:
        candidate = lines[index].strip()
        return (
            not candidate
            or candidate.startswith("```")
            or candidate == "---"
            or re.match(r"^#{1,6}\s+", candidate) is not None
            or is_table_start(lines, index)
            or candidate.startswith(">")
        )

    while i < len(lines):
        line = lines[i]
        stripped = line.strip()

        if not stripped:
            i += 1
            continue

        if stripped.startswith("```"):
            spec = stripped[3:].strip()
            language = spec.strip("{}") if spec.startswith("{") else spec
            language = language or "text"
            code_lines: list[str] = []
            i += 1
            while i < len(lines) and not lines[i].strip().startswith("```"):
                code_lines.append(lines[i])
                i += 1
            i += 1
            code = html.escape("\n".join(code_lines))
            if language == "mermaid":
                chunks.append(
                    '<figure class="diagram">'
                    "<figcaption>Mermaid diagram source</figcaption>"
                    f'<pre><code class="language-mermaid">{code}</code></pre>'
                    "</figure>"
                )
            else:
                chunks.append(
                    f'<pre><code class="language-{html.escape(language)}">{code}</code></pre>'
                )
            continue

        if stripped == "---":
            chunks.append("<hr>")
            i += 1
            continue

        heading = re.match(r"^(#{1,6})\s+(.+?)(?:\s+\{[^}]+\})?\s*$", stripped)
        if heading:
            level = len(heading.group(1))
            text = heading.group(2)
            chunks.append(
                f'<h{level} id="{unique_slug(text)}">{inline(text)}</h{level}>'
            )
            i += 1
            continue

        if is_table_start(lines, i):
            headers = split_table_row(lines[i])
            i += 2
            rows: list[list[str]] = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                rows.append(split_table_row(lines[i]))
                i += 1
            head_html = "".join(f"<th>{inline(cell)}</th>" for cell in headers)
            row_html = []
            for row in rows:
                row_html.append(
                    "<tr>"
                    + "".join(f"<td>{inline(cell)}</td>" for cell in row)
                    + "</tr>"
                )
            chunks.append(
                '<div class="table-wrap"><table><thead><tr>'
                + head_html
                + "</tr></thead><tbody>"
                + "".join(row_html)
                + "</tbody></table></div>"
            )
            continue

        if stripped.startswith(">"):
            quote_lines: list[str] = []
            while i < len(lines) and lines[i].strip().startswith(">"):
                quote_lines.append(lines[i].strip()[1:].strip())
                i += 1
            chunks.append(f"<blockquote><p>{inline(' '.join(quote_lines))}</p></blockquote>")
            continue

        if re.match(r"^[-*]\s+", stripped):
            items: list[str] = []
            while i < len(lines):
                candidate = lines[i].strip()
                bullet = re.match(r"^[-*]\s+(.+)", candidate)
                if not bullet:
                    break
                item = [bullet.group(1)]
                i += 1
                while i < len(lines) and not starts_block(i):
                    next_candidate = lines[i].strip()
                    if re.match(r"^[-*]\s+", next_candidate) or re.match(
                        r"^\d+\.\s+", next_candidate
                    ):
                        break
                    item.append(next_candidate)
                    i += 1
                items.append(" ".join(item))
                if i < len(lines) and not lines[i].strip():
                    i += 1
                    break
            chunks.append("<ul>" + "".join(f"<li>{inline(item)}</li>" for item in items) + "</ul>")
            continue

        if re.match(r"^\d+\.\s+", stripped):
            items = []
            while i < len(lines):
                candidate = lines[i].strip()
                bullet = re.match(r"^\d+\.\s+(.+)", candidate)
                if not bullet:
                    break
                item = [bullet.group(1)]
                i += 1
                while i < len(lines) and not starts_block(i):
                    next_candidate = lines[i].strip()
                    if re.match(r"^[-*]\s+", next_candidate) or re.match(
                        r"^\d+\.\s+", next_candidate
                    ):
                        break
                    item.append(next_candidate)
                    i += 1
                items.append(" ".join(item))
                if i < len(lines) and not lines[i].strip():
                    i += 1
                    break
            chunks.append("<ol>" + "".join(f"<li>{inline(item)}</li>" for item in items) + "</ol>")
            continue

        paragraph = [stripped]
        i += 1
        while i < len(lines):
            candidate = lines[i].strip()
            if (
                not candidate
                or candidate.startswith("```")
                or candidate == "---"
                or re.match(r"^#{1,6}\s+", candidate)
                or is_table_start(lines, i)
                or candidate.startswith(">")
                or re.match(r"^[-*]\s+", candidate)
                or re.match(r"^\d+\.\s+", candidate)
            ):
                break
            paragraph.append(candidate)
            i += 1
        chunks.append(f"<p>{inline(' '.join(paragraph))}</p>")

    return "\n".join(chunks)


def sidebar(chapters: list[Chapter], current: Chapter) -> str:
    items = []
    for chapter in chapters:
        href = relative_href(current.output, chapter.output)
        active = ' aria-current="page" class="active"' if chapter == current else ""
        items.append(
            f'<li><a href="{html.escape(href, quote=True)}"{active}>'
            f"{html.escape(chapter.title)}</a></li>"
        )
    return "<nav><ol>" + "".join(items) + "</ol></nav>"


def page_html(chapters: list[Chapter], current: Chapter) -> str:
    source_text = current.source.read_text(encoding="utf-8")
    body = markdown_to_html(source_text)
    css_href = relative_href(current.output, OUT_DIR / "book.css")
    source_href = relative_href(current.output, current.source)
    index = chapters.index(current)
    previous_link = ""
    next_link = ""
    if index > 0:
        previous = chapters[index - 1]
        previous_link = (
            f'<a class="pager-prev" href="{html.escape(relative_href(current.output, previous.output), quote=True)}">'
            f"Previous: {html.escape(previous.title)}</a>"
        )
    if index + 1 < len(chapters):
        nxt = chapters[index + 1]
        next_link = (
            f'<a class="pager-next" href="{html.escape(relative_href(current.output, nxt.output), quote=True)}">'
            f"Next: {html.escape(nxt.title)}</a>"
        )

    return f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{html.escape(current.title)} - Blub &amp; Fuel</title>
  <link rel="stylesheet" href="{html.escape(css_href, quote=True)}">
</head>
<body>
  <aside class="sidebar">
    <a class="book-title" href="{html.escape(relative_href(current.output, OUT_DIR / "index.html"), quote=True)}">Blub &amp; Fuel</a>
    {sidebar(chapters, current)}
  </aside>
  <main>
    <header class="page-header">
      <p class="kicker">Static reader</p>
      <h1>{html.escape(current.title)}</h1>
      <p class="source-link">Generated from <a href="{html.escape(source_href, quote=True)}">{html.escape(str(current.source_rel))}</a>.</p>
    </header>
    <article>
      {body}
    </article>
    <footer class="pager">
      {previous_link}
      {next_link}
    </footer>
  </main>
</body>
</html>
"""


CSS = """
:root {
  --bg: #faf9f7;
  --panel: #f0eee9;
  --text: #24211d;
  --muted: #686159;
  --line: #d8d2c8;
  --accent: #1f6f78;
  --code-bg: #171b1f;
  --code-text: #f3f5f6;
}

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  background: var(--bg);
  color: var(--text);
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  line-height: 1.58;
}

a {
  color: var(--accent);
}

.sidebar {
  background: var(--panel);
  border-right: 1px solid var(--line);
  bottom: 0;
  left: 0;
  overflow-y: auto;
  padding: 24px 18px;
  position: fixed;
  top: 0;
  width: 290px;
}

.book-title {
  color: var(--text);
  display: block;
  font-size: 1.1rem;
  font-weight: 700;
  margin-bottom: 18px;
  text-decoration: none;
}

.sidebar ol {
  list-style: none;
  margin: 0;
  padding: 0;
}

.sidebar li {
  margin: 0 0 4px;
}

.sidebar a {
  border-radius: 6px;
  color: var(--text);
  display: block;
  font-size: 0.92rem;
  padding: 7px 9px;
  text-decoration: none;
}

.sidebar a.active,
.sidebar a:hover {
  background: #ffffff;
}

main {
  margin-left: 290px;
  max-width: 980px;
  padding: 44px 56px 72px;
}

.page-header {
  border-bottom: 1px solid var(--line);
  margin-bottom: 32px;
  padding-bottom: 24px;
}

.kicker,
.source-link {
  color: var(--muted);
  font-size: 0.9rem;
  margin: 0;
}

.page-header h1 {
  font-size: clamp(2rem, 4vw, 3.4rem);
  letter-spacing: 0;
  line-height: 1.08;
  margin: 8px 0 12px;
}

article h1,
article h2,
article h3 {
  line-height: 1.18;
  margin: 2rem 0 0.8rem;
}

article h1 {
  font-size: 2rem;
}

article h2 {
  font-size: 1.55rem;
}

article h3 {
  font-size: 1.2rem;
}

p {
  margin: 0 0 1rem;
}

code {
  background: #ebe7df;
  border-radius: 4px;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 0.92em;
  padding: 0.12em 0.28em;
}

pre {
  background: var(--code-bg);
  border-radius: 8px;
  color: var(--code-text);
  overflow-x: auto;
  padding: 18px;
}

pre code {
  background: transparent;
  color: inherit;
  padding: 0;
}

blockquote {
  border-left: 4px solid var(--accent);
  color: #3c3731;
  margin: 1.4rem 0;
  padding-left: 1rem;
}

.table-wrap {
  overflow-x: auto;
}

table {
  border-collapse: collapse;
  font-size: 0.94rem;
  margin: 1.2rem 0 1.6rem;
  width: 100%;
}

th,
td {
  border: 1px solid var(--line);
  padding: 8px 10px;
  text-align: left;
  vertical-align: top;
}

th {
  background: var(--panel);
}

.diagram figcaption {
  color: var(--muted);
  font-size: 0.85rem;
  margin-bottom: 6px;
}

.pager {
  border-top: 1px solid var(--line);
  display: flex;
  gap: 16px;
  justify-content: space-between;
  margin-top: 48px;
  padding-top: 24px;
}

.pager a {
  font-weight: 650;
}

@media (max-width: 860px) {
  .sidebar {
    border-bottom: 1px solid var(--line);
    border-right: 0;
    max-height: 42vh;
    position: relative;
    width: 100%;
  }

  main {
    margin-left: 0;
    padding: 28px 20px 48px;
  }

  .pager {
    display: block;
  }

  .pager a {
    display: block;
    margin-bottom: 12px;
  }
}
""".strip()


def main() -> None:
    paths = chapter_paths()
    if not paths:
        raise SystemExit("No chapters found in book/_quarto.yml")

    chapters = [
        Chapter(source=path, output=output_for(path), title=chapter_title(path))
        for path in paths
    ]

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for child in OUT_DIR.rglob("._*"):
        try:
            child.unlink()
        except FileNotFoundError:
            pass
    for child in OUT_DIR.rglob("*.html"):
        try:
            child.unlink()
        except FileNotFoundError:
            pass

    (OUT_DIR / "book.css").write_text(CSS + "\n", encoding="utf-8")
    for chapter in chapters:
        chapter.output.parent.mkdir(parents=True, exist_ok=True)
        chapter.output.write_text(page_html(chapters, chapter), encoding="utf-8")

    for child in OUT_DIR.rglob("._*"):
        try:
            child.unlink()
        except FileNotFoundError:
            pass

    print(f"Rendered {len(chapters)} chapters to {OUT_DIR.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
