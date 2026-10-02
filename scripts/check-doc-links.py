"""Check local Markdown links and HTML image paths in maintained and archived docs."""
from pathlib import Path
import re
from urllib.parse import unquote

root = Path(__file__).resolve().parent.parent
files = [root / "README.md", root / "CHANGELOG.md", *sorted((root / "docs").rglob("*.md")), root / "assets/showcase/README.md"]
errors = []
for path in files:
    text = path.read_text(encoding="utf-8-sig")
    links = re.findall(r"\]\(([^)\s]+)\)", text) + re.findall(r'<img[^>]+src="([^"]+)"', text)
    for link in links:
        if "://" in link or link.startswith(("#", "mailto:")):
            continue
        target = unquote(link.split("#", 1)[0])
        if target and not (path.parent / target).exists():
            errors.append(f"{path.relative_to(root)}: {link}")
if errors:
    raise SystemExit("Broken local documentation links:\n" + "\n".join(errors))
print(f"PASS local links in {len(files)} documentation files")
