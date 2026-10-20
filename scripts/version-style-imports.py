"""Version each first-party CSS import by its content, so a new para.css cannot reuse an old imported theme."""
from pathlib import Path
import hashlib
import re

root = Path(__file__).resolve().parents[1] / "src/BelemRestaurantes.Web/wwwroot/css"
entry = root / "para.css"

def version(match):
    name = match[1]
    digest = hashlib.sha256((root / name).read_bytes()).hexdigest()[:12]
    return f"@import url('./{name}?v={digest}');"

original = entry.read_text(encoding="utf-8-sig")
updated = re.sub(r"@import url\('\./([^'?]+)(?:\?[^']*)?'\);", version, original)
if updated != original:
    entry.write_text(updated, encoding="utf-8")
