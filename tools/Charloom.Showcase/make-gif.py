"""Combine actual WinApp screenshots captured by scripts/capture-showcase.ps1."""
from pathlib import Path
from PIL import Image
import argparse

parser = argparse.ArgumentParser()
parser.add_argument("--frames", type=Path, default=Path("artifacts/showcase-frames"))
parser.add_argument("--output", type=Path, default=Path("assets/showcase/text-workflow.gif"))
args = parser.parse_args()
frames = []
for path in sorted(args.frames.glob("*.png")):
    with Image.open(path) as image:
        image.thumbnail((960, 640), Image.Resampling.LANCZOS)
        frames.append(image.convert("RGB").quantize(colors=128))
if len(frames) < 2:
    raise SystemExit("Capture at least two actual application frames first")
args.output.parent.mkdir(parents=True, exist_ok=True)
frames[0].save(args.output, save_all=True, append_images=frames[1:], duration=1800, loop=0, optimize=True)
print(args.output.resolve())
