from __future__ import annotations
import sys
from pathlib import Path

if len(sys.argv) != 2:
    raise SystemExit('usage: apply_zenith_compile_fixes.py <prime-root>')
root=Path(sys.argv[1]).resolve()
path=root/'src/MphRead/Mods/MapGen/MapAltFormCertification.cs'
text=path.read_text(encoding='utf-8')
old='            PlayerValues values = Metadata.PlayerValues[i];\n'
if text.count(old)!=1:
    raise RuntimeError(f'expected one PlayerValues declaration, found {text.count(old)}')
path.write_text(text.replace(old,'            var values = Metadata.PlayerValues[i];\n',1),encoding='utf-8',newline='\n')
print('Zenith certification compile compatibility applied in',root)
