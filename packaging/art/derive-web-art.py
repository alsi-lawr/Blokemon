"""Derives the illustrations a browser is served from the approved artwork.

`content/art` is the source of record. Every file in it carries the sha256 of the source it was
approved from, and nothing here writes to it, renames it or deletes it. What this produces beside
it, in `content/art-web`, is the delivered form: the same pixels at the same size, re-encoded from
PNG to WebP at quality 95, plus a placeholder small enough to travel inside the card's own markup.

The numbers are the reason. An approved illustration is a base64 PNG inside an SVG wrapper and
reaches the browser at about 250 KB; the same picture as WebP q95 is about 35 KB, and a page
showing the whole collection asks for a hundred and thirty of them. The placeholder is twenty-four
pixels wide - a couple of hundred bytes - which is small enough to inline into every card so that
no card is ever an empty rectangle while its illustration is on the way.

Eight of the files are true vector - the card back and the seven energy symbols - and are already
small. They are copied through untouched: there is nothing to re-encode and rasterising them would
lose the one thing they have.

Run it with cwebp on the path:

    nix-shell -p libwebp --run 'python3 packaging/art/derive-web-art.py'
"""

import base64
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
APPROVED = ROOT / "content" / "art"
DELIVERED = ROOT / "content" / "art-web"

# The delivered encoding. Alex asked for 95 rather than the 80 that is usually enough: the
# illustrations are the product, so the bytes are worth spending.
QUALITY = 95

# The widths a card is actually seen at. The card is drawn on a 750 x 1050 grid with the
# illustration 590 wide, and every place it appears scales that grid down: a collection tile shows
# the art about 124 css pixels across, a card held up to be read about 295. Multiply by the screen
# and the range of pixels genuinely wanted runs from about 30 to the 590 the artwork has, so three
# widths cover it without a candidate that is never the right answer.
WIDTHS = (200, 400, 590)

# The placeholder. Wide enough to carry the shape and the colours of the illustration, narrow
# enough that inlining one into every card costs less than a single request would.
PLACEHOLDER_WIDTH = 24
PLACEHOLDER_QUALITY = 60

EMBEDDED_PNG = re.compile(r"data:image/png;base64,([A-Za-z0-9+/=]+)")


def cwebp(source: pathlib.Path, target: pathlib.Path, quality: int, width: int | None) -> None:
    command = ["cwebp", "-quiet", "-q", str(quality)]
    if width is not None:
        command += ["-resize", str(width), "0"]
    command += [str(source), "-o", str(target)]
    subprocess.run(command, check=True)


def derive(approved: pathlib.Path, work: pathlib.Path) -> str:
    """Writes the delivered form of one approved illustration and says what it did."""
    markup = approved.read_text(encoding="utf-8")
    embedded = EMBEDDED_PNG.search(markup)

    if embedded is None:
        # Vector, and already small. Copied rather than rasterised.
        shutil.copyfile(approved, DELIVERED / approved.name)
        return f"{approved.name}: copied, {approved.stat().st_size:,} bytes"

    png = work / f"{approved.stem}.png"
    png.write_bytes(base64.b64decode(embedded.group(1)))

    written = []
    for width in WIDTHS:
        target = DELIVERED / f"{approved.stem}-{width}.webp"
        # The widest is the artwork's own size, so it is encoded rather than resampled.
        cwebp(png, target, QUALITY, None if width >= 590 else width)
        written.append(f"{width}w {target.stat().st_size:,}")

    placeholder = DELIVERED / f"{approved.stem}.lqip.webp"
    cwebp(png, placeholder, PLACEHOLDER_QUALITY, PLACEHOLDER_WIDTH)
    png.unlink()

    return (
        f"{approved.stem}: {approved.stat().st_size:,} bytes -> "
        + ", ".join(written)
        + f" (+{placeholder.stat().st_size:,} placeholder)"
    )


def main() -> int:
    if shutil.which("cwebp") is None:
        print("cwebp is not on the path; see the note at the top of this file.", file=sys.stderr)
        return 2

    approved = sorted(APPROVED.glob("*.svg"))
    if not approved:
        print(f"No approved illustrations under {APPROVED}.", file=sys.stderr)
        return 2

    # Rebuilt from scratch, so that an illustration withdrawn upstream does not linger here.
    if DELIVERED.exists():
        shutil.rmtree(DELIVERED)
    DELIVERED.mkdir(parents=True)

    with tempfile.TemporaryDirectory() as scratch:
        work = pathlib.Path(scratch)
        for illustration in approved:
            print(derive(illustration, work))

    before = sum(path.stat().st_size for path in approved)
    after = sum(path.stat().st_size for path in DELIVERED.iterdir())
    print(
        f"\n{len(approved)} illustrations: {before / 1048576:.1f} MB approved"
        f" -> {after / 1048576:.1f} MB delivered"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
