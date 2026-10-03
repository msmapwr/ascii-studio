"""Validate the static website's content, links, accessibility labels and publish boundary."""
from html.parser import HTMLParser
from pathlib import Path
import xml.etree.ElementTree as ET
import re

ROOT = Path(__file__).resolve().parent.parent
SITE = ROOT / "website/dist"


class Page(HTMLParser):
    def __init__(self):
        super().__init__()
        self.elements = []
        self.text = []

    def handle_starttag(self, tag, attrs):
        self.elements.append((tag, dict(attrs)))

    def handle_data(self, data):
        self.text.append(data)


page = Page()
page.feed((SITE / "index.html").read_text(encoding="utf-8"))
ids = [a["id"] for _, a in page.elements if "id" in a]
assert len(ids) == len(set(ids)), "Duplicate HTML identifiers"
assert sum(tag == "h1" for tag, _ in page.elements) == 1, "Expected one page heading"
assert sum(tag == "main" for tag, _ in page.elements) == 1, "Expected one main landmark"
for tag, attrs in page.elements:
    for key in ("href", "src"):
        value = attrs.get(key, "")
        if value.startswith("#"):
            assert value[1:] in ids, f"Missing anchor: {value}"
        elif value and not value.startswith(("https://", "data:")):
            asset = (SITE / value).resolve()
            assert asset.is_relative_to(SITE.resolve()), f"Asset escapes publish directory: {value}"
            assert asset.is_file(), f"Missing asset: {value}"
    if tag == "img":
        assert "alt" in attrs and "width" in attrs and "height" in attrs, "Image label/dimensions missing"
    if tag == "fluent-button":
        assert attrs.get("aria-label"), "Fluent control label missing"
    if attrs.get("href", "").endswith("/releases"):
        assert attrs.get("target") == "_blank", "Release link must open a new tab"
        assert "noopener" in attrs.get("rel", ""), "Release link must protect opener"
    for key in ("aria-controls", "aria-describedby", "aria-labelledby"):
        for reference in attrs.get(key, "").split():
            assert reference in ids, f"Missing accessible reference: {reference}"

content = ET.parse(ROOT / "docs/WEBSITE_CONTENT.xml").getroot()
visible_text = re.sub(r"\s+", "", " ".join(page.text))
for section in content.findall("main/section"):
    assert re.sub(r"\s+", "", section.findtext("heading")) in visible_text, "Missing section heading"
    for node in section.iter():
        if node.tag in ("body", "answer", "question", "requirements", "releaseNote", "formats", "detail"):
            assert re.sub(r"\s+", "", node.text or "") in visible_text, f"Missing planned copy: {node.text}"
assert not (SITE / ".git").exists(), "Repository metadata leaked into output"
assert not any(SITE.rglob("*.xml")), "Planning XML leaked into output"
print(f"PASS website content, {len(ids)} identifiers, assets, links and accessibility labels")
