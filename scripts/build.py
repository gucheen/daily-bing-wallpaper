import argparse
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent.parent
TIMEOUT_MS = 1_800_000
parser = argparse.ArgumentParser()
parser.add_argument("--runtime", choices=["win-x64", "win-arm64"], default="win-x64")
parser.add_argument("--test", action="store_true")
args = parser.parse_args()

def run(*command):
    subprocess.run(command, cwd=ROOT, check=True, timeout=TIMEOUT_MS / 1000)

if args.test:
    run("dotnet", "run", "--project", "tests/DailyWallpaper.Core.Tests", "-c", "Release")
    if os.name == "nt":
        run("dotnet", "run", "--project", "tests/DailyWallpaper.Windows.Tests", "-c", "Release")
    else:
        run("dotnet", "build", "tests/DailyWallpaper.Windows.Tests", "-c", "Release")
        print("Windows image tests compiled; run on Windows to execute them.")
run("dotnet", "publish", "src/DailyWallpaper.Windows", "-c", "Release", "-r", args.runtime,
    "--self-contained", "true", "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None", "-p:DebugSymbols=false", "-o", str(ROOT / "artifacts" / args.runtime))
print(f"Built: {ROOT / 'artifacts' / args.runtime / 'DailyWallpaper.exe'}")
