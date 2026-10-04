"""Convert the reviewed CLI tutorial snapshot into the existing visitor page XML."""
import argparse
import html
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "docs/WEBSITE_CLI_SOURCE.md"
TARGET = ROOT / "docs/WEBSITE_PAGES.xml"


def inline(text):
    """Render only code, emphasis and links; all source text is escaped."""
    pattern = r"(`[^`]+`|\*\*[^*]+\*\*|\[[^\]]+\]\([^)]+\))"
    result = []
    for part in re.split(pattern, text):
        if part.startswith("`") and part.endswith("`"):
            result.append("<code>" + html.escape(part[1:-1]) + "</code>")
        elif part.startswith("**") and part.endswith("**"):
            result.append("<strong>" + html.escape(part[2:-2]) + "</strong>")
        elif match := re.fullmatch(r"\[([^\]]+)\]\(([^)]+)\)", part):
            label, url = match.groups()
            if url == "CLI.md":
                url = "https://github.com/msmapwr/ascii-studio/blob/main/docs/CLI.md"
            if not url.startswith(("https://", "#")):
                raise ValueError(f"Unsupported tutorial link: {url}")
            extra = ' target="_blank" rel="noopener noreferrer" aria-describedby="new-tab-note"' if url.endswith("/releases") else ""
            result.append(f'<a href="{html.escape(url, quote=True)}"{extra}>{html.escape(label)}</a>')
        else:
            result.append(html.escape(part))
    return "".join(result)


def build():
    source = SOURCE.read_text(encoding="utf-8")
    headings = list(re.finditer(r"^## (\d+)\. (.+)$", source, re.M))
    assert len(headings) == 14, "Expected all fourteen CLI chapters"
    chapters, links = [], []
    code_index = 0
    for index, heading in enumerate(headings):
        number, title = heading.groups()
        anchor = f"cli-chapter-{number}"
        links.append(f'<a href="#{anchor}">{number}. {html.escape(title)}</a>')
        body = source[heading.end():headings[index + 1].start() if index + 1 < len(headings) else len(source)]
        lines = body.strip().splitlines()
        blocks, i = [], 0
        while i < len(lines):
            line = lines[i]
            if not line.strip():
                i += 1
                continue
            if line.startswith("```"):
                language = line[3:]
                code, i = [], i + 1
                while i < len(lines) and lines[i] != "```":
                    code.append(lines[i])
                    i += 1
                assert i < len(lines), "Unclosed tutorial code fence"
                code_index += 1
                code_id = f"cli-code-{code_index}"
                label = "PowerShell" if language == "powershell" else "JSON"
                blocks.append(f'<div class="copy-panel cli-code-panel"><div class="cli-code-toolbar"><span>{label}</span><button type="button" class="button button-small" data-copy-target="{code_id}" aria-label="复制第 {number} 章第 {code_index} 段代码" hidden="hidden">复制代码</button></div><pre id="{code_id}" tabindex="0" aria-label="第 {number} 章代码示例">{html.escape(chr(10).join(code))}</pre><span class="copy-feedback" role="status" aria-live="polite"></span></div>')
                i += 1
            elif line.startswith("### "):
                blocks.append("<h3>" + inline(line[4:]) + "</h3>")
                i += 1
            elif line.startswith("| "):
                rows = []
                while i < len(lines) and lines[i].startswith("|"):
                    cells = [cell.strip() for cell in lines[i].strip().strip("|").split("|")]
                    if not all(re.fullmatch(r":?-+:?", cell) for cell in cells):
                        rows.append(cells)
                    i += 1
                assert rows and all(len(row) == len(rows[0]) for row in rows), "Malformed tutorial table"
                table = '<section class="table-scroll" tabindex="0" aria-label="' + html.escape(title, quote=True) + '参数表"><table class="format-table"><caption>' + html.escape(title) + '参数参考</caption><thead><tr>'
                table += "".join('<th scope="col">' + inline(cell) + "</th>" for cell in rows[0]) + "</tr></thead><tbody>"
                for row in rows[1:]:
                    table += "<tr>" + "".join("<td>" + inline(cell) + "</td>" for cell in row) + "</tr>"
                blocks.append(table + "</tbody></table></section>")
            elif line.startswith("- "):
                items = []
                while i < len(lines) and lines[i].startswith("- "):
                    items.append("<li>" + inline(lines[i][2:]) + "</li>")
                    i += 1
                blocks.append("<ul>" + "".join(items) + "</ul>")
            else:
                paragraph = [line]
                i += 1
                while i < len(lines) and lines[i].strip() and not lines[i].startswith(("```", "### ", "|", "- ")):
                    paragraph.append(lines[i])
                    i += 1
                blocks.append("<p>" + inline(" ".join(paragraph)) + "</p>")
        chapters.append(f'<section class="guide-section" id="{anchor}" aria-labelledby="{anchor}-title"><h2 id="{anchor}-title">{number}. {html.escape(title)}</h2>{"".join(blocks)}</section>')
    return ET.fromstring(f'''<page file="cli.html" class="cli-page" label="CLI 教程">
    <title>CLI 教程 | AsciiStudio</title><description>在 PowerShell 中转换图片与文字、保存项目、导出作品，了解编辑、工作区和批处理命令的版本要求。</description><content>
    <section class="container history-hero" aria-labelledby="cli-title"><a class="back-link" href="guide.html">返回入门教程</a><h1 id="cli-title">用命令行制作字符画</h1><p>在 PowerShell 中转换、导出与批处理。先运行一个文字命令，再按需要查看图片、项目和编辑章节。</p><p class="cli-version-note">教程文档版本：1.0.0-alpha.2。下载前请确认 Releases 是否提供对应版本；较早版本可能不支持 workspace、edit、history 和 --apply。先运行 --version，再用对应命令的 --help 核对参数。</p><nav class="task-links" aria-label="CLI 教程入口"><a class="button button-primary" href="#cli-chapter-2">五分钟入门</a><a class="button" href="#cli-chapter-1">准备终端</a><a class="text-link" href="https://github.com/msmapwr/ascii-studio/releases" target="_blank" rel="noopener noreferrer" aria-describedby="new-tab-note">查看可下载版本</a></nav></section>
    <div class="container guide-layout cli-layout"><aside class="guide-index cli-index"><details open="open"><summary>本页 14 章</summary><nav aria-label="CLI 章节目录">{''.join(links)}</nav></details></aside><div class="guide-content cli-content">{''.join(chapters)}<a class="text-link" href="https://github.com/msmapwr/ascii-studio/blob/main/docs/CLI.md">查询 CLI 功能覆盖与版本范围</a></div></div>
    </content></page>''')


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    generated = build()
    tree = ET.parse(TARGET)
    root = tree.getroot()
    existing = root.find("page[@file='cli.html']")
    if args.check:
        assert existing is not None and ET.tostring(existing).strip() == ET.tostring(generated).strip(), "Regenerate CLI page XML"
        print("PASS CLI XML matches all fourteen source chapters")
    else:
        if existing is not None:
            root.remove(existing)
        root.append(generated)
        original = TARGET.read_text(encoding="utf-8")
        if existing is not None:
            original = re.sub(r'\s*<page file="cli.html".*?</page>', "", original, flags=re.S)
        TARGET.write_text(original.replace("</visitorPages>", ET.tostring(generated, encoding="unicode") + "\n</visitorPages>"), encoding="utf-8", newline="\n")
        print("Built CLI page XML from reviewed tutorial snapshot")
