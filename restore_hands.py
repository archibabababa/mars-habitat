from pathlib import Path
import hashlib
import shutil
import sys

ROOT = Path(__file__).resolve().parent
FILES = [('Unity/Assets/SpatialCollective/OfficialHands/Left Hand Tracking.prefab', 'cdf2d1b50d1973e87624180a0c0feb5f135732b31ee8e85d9d6157a2b4fe3f3a'), ('Unity/Assets/SpatialCollective/OfficialHands/Right Hand Tracking.prefab', '84ab9461a0c5b866b40b29303da0455a9ab426036ebf8c1114d48b5fd0e2c8ca'), ('Unity/Assets/SpatialCollective/OfficialHands/Models/LeftHand.fbx', '828996c1e01f710222a90caa3451a2099ec82f0651242a9b4847091a508ec521'), ('Unity/Assets/SpatialCollective/OfficialHands/Models/RightHand.fbx', '215bd9c9782a74c95b47e08e802bd2e37b9bd0e2205428543dbdef9da7990db3')]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    package = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "Unity/Library/PackageCache"
    if not package.is_dir():
        raise SystemExit("Open Unity to install packages, or pass the XR Hands package folder.")
    missing = []
    for relative, expected in FILES:
        target = ROOT / relative
        if target.is_file() and digest(target) == expected:
            continue
        source = next((p for p in package.rglob(target.name)
                       if p.is_file() and digest(p) == expected), None)
        if source is None:
            missing.append(relative)
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
    if missing:
        raise SystemExit("Matching XR Hands 1.8.1 files not found:\n" + "\n".join(missing))
    print("Hand assets restored.")


if __name__ == "__main__":
    main()
