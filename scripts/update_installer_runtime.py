"""Pin official .NET Desktop Runtime downloads; payloads are never bundled."""
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
METADATA = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json"


def main():
    with urllib.request.urlopen(METADATA, timeout=60) as response:
        metadata = json.load(response)
    version = metadata["latest-runtime"]
    desktop = next(release["windowsdesktop"] for release in metadata["releases"]
                   if release.get("windowsdesktop", {}).get("version") == version)
    downloads = {}
    for rid in ("win-x64", "win-arm64"):
        item = next(file for file in desktop["files"]
                    if file["rid"] == rid and file["url"].endswith(".exe"))
        print(f"Verifying {item['url']}", flush=True)
        sha512, sha256 = hashlib.sha512(), hashlib.sha256()
        with urllib.request.urlopen(item["url"], timeout=120) as response:
            while chunk := response.read(1024 * 1024):
                sha512.update(chunk)
                sha256.update(chunk)
        if sha512.hexdigest().lower() != item["hash"].lower():
            raise ValueError(f"Microsoft SHA-512 mismatch: {rid}")
        downloads[rid] = {"url": item["url"], "sha256": sha256.hexdigest()}
    destination = ROOT / "installer" / "runtime.json"
    destination.write_text(json.dumps({"version": version, "downloads": downloads}, indent=2) + "\n",
                           encoding="utf-8")
    print(f"Updated {destination}")


if __name__ == "__main__":
    main()
