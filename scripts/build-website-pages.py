"""Generate static visitor pages from the reviewed XML and shared homepage chrome."""
import argparse
import html
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
SITE = ROOT / "website/dist"


def render(page):
    home = (SITE / "index.html").read_text(encoding="utf-8")
    head = home[:re.search(r"    <body[^>]*>", home).start()]
    title, description = page.findtext("title"), page.findtext("description")
    head = re.sub(r"<title>.*?</title>", lambda _: f"<title>{html.escape(title)}</title>", head)
    for key, text in (("name=\"description\"", description), ("property=\"og:title\"", title), ("property=\"og:description\"", description)):
        head = re.sub(rf'({key}\s+content=")[^"]*', lambda m: m[1] + html.escape(text, quote=True), head)
    chrome = home[home.index('        <header class="site-header"'):home.index('        <main id="main"')]
    chrome = re.sub(r'href="#([^"]+)"', r'href="index.html#\1"', chrome)
    chrome = chrome.replace('aria-label="AsciiStudio，回到页面顶部"', 'aria-label="AsciiStudio，返回首页"')
    chrome = chrome.replace(f'href="{page.get("file")}"', f'href="{page.get("file")}" aria-current="page"')
    footer = home[home.index('        <footer class="site-footer"'):].replace('class="brand" href="#top"', 'class="brand" href="index.html"')
    content = "".join(ET.tostring(node, encoding="unicode", method="html") for node in page.find("content"))
    content = re.sub(r' (hidden|open)="\1"', r' \1', content)
    return f'''{head}    <body id="top" class="{page.get('class', '')}">
        <a class="skip-link" href="#main">跳到正文</a>
{chrome}        <main id="main" aria-label="{html.escape(page.get('label'), quote=True)}">
{content}        </main>
{footer}'''


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for page in ET.parse(ROOT / "docs/WEBSITE_PAGES.xml").getroot().findall("page"):
        target = SITE / page.get("file")
        assert target.parent == SITE and target.suffix == ".html", "Invalid page output"
        result = render(page)
        if args.check:
            assert target.read_text(encoding="utf-8") == result, f"Regenerate {target.name}"
            print(f"PASS generated {target.name}")
        else:
            target.write_text(result, encoding="utf-8", newline="\n")
            print(f"Built {target.name}")
