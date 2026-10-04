"""Ensure the CLI website preserves source chapters and exact copyable snippets."""
from html.parser import HTMLParser
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent.parent
source = (ROOT / "docs/WEBSITE_CLI_SOURCE.md").read_text(encoding="utf-8")


class Tutorial(HTMLParser):
    def __init__(self):
        super().__init__()
        self.snippets = []
        self.in_code = False
        self.chapter_ids = []
        self.copy_targets = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "section" and re.fullmatch(r"cli-chapter-\d+", attrs.get("id", "")):
            self.chapter_ids.append(attrs["id"])
        if tag == "pre" and attrs.get("id", "").startswith("cli-code-"):
            self.in_code = True
            self.snippets.append("")
            assert attrs.get("tabindex") == "0", "Code must be keyboard scrollable"
        if "data-copy-target" in attrs:
            self.copy_targets.append(attrs["data-copy-target"])

    def handle_endtag(self, tag):
        if tag == "pre":
            self.in_code = False

    def handle_data(self, data):
        if self.in_code:
            self.snippets[-1] += data


page = Tutorial()
page.feed((ROOT / "website/dist/cli.html").read_text(encoding="utf-8"))
expected = re.findall(r"^```(?:powershell|json)\n([\s\S]*?)\n```", source, re.M)
assert page.snippets == expected, "CLI snippets changed or disappeared"
assert page.chapter_ids == [f"cli-chapter-{number}" for number in range(1, 15)], "Chapter order changed"
assert page.copy_targets == [f"cli-code-{number}" for number in range(1, len(expected) + 1)], "Copy target missing"
print(f"PASS all 14 CLI chapters and {len(expected)} exact copyable snippets")
