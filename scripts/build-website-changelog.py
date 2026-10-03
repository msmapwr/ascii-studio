"""Render the repository's version history as a static, script-independent page."""
from pathlib import Path
import argparse
import html
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
SITE = ROOT / "website/dist"


def inline(text):
    return re.sub(r"`([^`]+)`", r"<code>\1</code>", html.escape(text))


def render():
    copy = ET.parse(ROOT / "docs/WEBSITE_CONTENT.xml").find("changelogPage")
    labels = {node.get("key"): node.text for node in copy.findall("category")}
    source = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
    versions = list(re.finditer(r"^## \[([^\]]+)\] - (\d{4}-\d{2}-\d{2})$", source, re.M))
    if not versions:
        raise ValueError("No dated versions in CHANGELOG.md")
    articles, links = [], []
    for index, match in enumerate(versions):
        version, date = match.groups()
        anchor = "version-" + version.replace(".", "-")
        links.append(f'<a href="#{anchor}">{html.escape(version)}</a>')
        end = versions[index + 1].start() if index + 1 < len(versions) else len(source)
        section = source[match.end():end]
        groups = []
        for category in re.finditer(r"^### (\w+)\n(.*?)(?=^### |\Z)", section, re.M | re.S):
            name, body = category.groups()
            items = re.findall(r"^- (.+)$", body, re.M)
            if not items:
                continue
            groups.append(f'<section class="change-group"><h3>{html.escape(labels.get(name, name))}</h3><ul>'
                          + "".join(f"<li>{inline(item)}</li>" for item in items) + "</ul></section>")
        if not groups:
            raise ValueError(f"Empty dated version: {version}")
        badge = copy.findtext("prerelease") if "-" in version else copy.findtext("release")
        articles.append(f'''<article class="release-entry" id="{anchor}" aria-labelledby="{anchor}-title">
    <header class="release-heading"><div><h2 id="{anchor}-title">{html.escape(version)}</h2>
    <span class="release-badge">{html.escape(badge)}</span></div><time datetime="{date}">{date}</time></header>
    {''.join(groups)}
    </article>''')
    home = (SITE / "index.html").read_text(encoding="utf-8")
    head = home[:home.index("    <body>")]
    head = re.sub(r"<title>.*?</title>", f"<title>{html.escape(copy.findtext('title'))}</title>", head)
    head = re.sub(r'(name="description"\s+content=")[^"]*',
                  lambda m: m[1] + html.escape(copy.findtext("description"), quote=True), head)
    head = re.sub(r'(property="og:title"\s+content=")[^"]*',
                  lambda m: m[1] + html.escape(copy.findtext("title"), quote=True), head)
    head = re.sub(r'(property="og:description"\s+content=")[^"]*',
                  lambda m: m[1] + html.escape(copy.findtext("description"), quote=True), head)
    chrome = home[home.index('        <header class="site-header"'):home.index('        <main id="main"')]
    chrome = re.sub(r'href="#([^"]+)"', r'href="index.html#\1"', chrome)
    chrome = chrome.replace('aria-label="AsciiStudio，回到页面顶部"', 'aria-label="AsciiStudio，返回首页"')
    chrome = chrome.replace('href="changelog.html"', 'href="changelog.html" aria-current="page"')
    footer = home[home.index('        <footer class="site-footer"'):]
    footer = footer.replace('class="brand" href="#top"', 'class="brand" href="index.html"')
    return f'''{head}    <body class="changelog-page">
        <a class="skip-link" href="#main">跳到正文</a>
{chrome}        <main id="main" aria-label="软件更新日志">
            <section class="container history-hero" aria-labelledby="history-title">
                <a class="back-link" href="index.html">{html.escape(copy.findtext('back'))}</a>
                <h1 id="history-title">{html.escape(copy.findtext('heading'))}</h1>
                <p>{html.escape(copy.findtext('intro'))}</p>
                <p class="history-note">{html.escape(copy.findtext('downloadNote'))}
                    <a href="https://github.com/msmapwr/ascii-studio/releases" target="_blank" rel="noopener noreferrer" aria-describedby="new-tab-note">{html.escape(copy.findtext('downloads'))}</a>
                </p>
            </section>
            <div class="container history-layout">
                <aside class="version-index" aria-label="版本索引"><h2>{html.escape(copy.findtext('index'))}</h2>
                    <nav aria-label="跳到版本">{''.join(links)}</nav>
                    <a class="history-source" href="https://github.com/msmapwr/ascii-studio/blob/main/CHANGELOG.md">{html.escape(copy.findtext('source'))}</a>
                </aside>
                <div class="release-timeline">{''.join(articles)}</div>
            </div>
        </main>
{footer}'''


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Fail when the generated page is stale")
    args = parser.parse_args()
    generated = render()
    target = SITE / "changelog.html"
    if args.check:
        assert target.read_text(encoding="utf-8") == generated, "Regenerate changelog.html"
        print("PASS generated changelog matches repository history and shared page chrome")
    else:
        target.write_text(generated, encoding="utf-8", newline="\n")
        print("Built website/dist/changelog.html")
