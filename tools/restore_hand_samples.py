from pathlib import Path
import json,hashlib,shutil,sys
root=Path(__file__).resolve().parents[1]
package=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else None
if package is None or not package.is_dir(): raise SystemExit("Usage: python tools/restore_hand_samples.py PATH_TO_INSTALLED_XR_HANDS_PACKAGE")
manifest=json.loads((root/'tools/hand_samples.json').read_text())
missing=[]
for item in manifest:
 target=root/item['path']
 if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest()==item['sha256']: continue
 candidates=package.rglob(target.name)
 found=next((p for p in candidates if p.is_file() and hashlib.sha256(p.read_bytes()).hexdigest()==item['sha256']),None)
 if found:
  target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(found,target);print('Restored',item['path'])
 else: missing.append(item['path'])
if missing:
 print('Exact matching samples not found; use the matching XR Hands sample release. No substitute assets have been written.')
 print('\n'.join(missing));raise SystemExit(1)
print('All sample files restored. Open Unity and check the scene before running.')
