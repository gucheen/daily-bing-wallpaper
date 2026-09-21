import argparse
import os
import json
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
TIMEOUT_MS = 1_800_000
parser = argparse.ArgumentParser()
parser.add_argument("--runtime", choices=["win-x64", "win-arm64"], default="win-x64")
parser.add_argument("--test", action="store_true")
parser.add_argument("--framework-dependent", action="store_true", help="Exclude the .NET runtime")
parser.add_argument("--installer", action="store_true", help="Build a lightweight online installer (Windows / Inno Setup)")
parser.add_argument("--iscc", help="Path to the Inno Setup compiler ISCC.exe")
args = parser.parse_args()
if args.installer:
    if os.name != "nt":
        parser.error("--installer requires Windows and Inno Setup 6.7.3 or later")
    args.framework_dependent = True
    candidates = [args.iscc, os.environ.get("ISCC"), shutil.which("ISCC.exe")]
    for variable in ("ProgramFiles(x86)", "ProgramFiles", "LOCALAPPDATA"):
        if base := os.environ.get(variable):
            candidates.append(str(Path(base) / ("Programs/Inno Setup 6" if variable == "LOCALAPPDATA" else "Inno Setup 6") / "ISCC.exe"))
    compiler = next((path for path in candidates if path and Path(path).is_file()), None)
    if compiler is None:
        parser.error("Install Inno Setup 6.7.3+ or pass --iscc PATH (https://jrsoftware.org/isdl.php)")
output = ROOT / "artifacts" / (args.runtime + ("-framework-dependent" if args.framework_dependent else ""))

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
    "--self-contained", "false" if args.framework_dependent else "true",
    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None", "-p:DebugSymbols=false", "-o", str(output))
print(f"Built: {output / 'DailyWallpaper.exe'}")
if args.installer:
    runtime = json.loads((ROOT / "installer/runtime.json").read_text(encoding="utf-8"))
    dependency = runtime["downloads"][args.runtime]
    version = ET.parse(ROOT / "src/DailyWallpaper.Windows/DailyWallpaper.Windows.csproj").findtext("PropertyGroup/Version")
    architecture = args.runtime.removeprefix("win-")
    run(str(compiler), f"/DAppVersion={version}", f"/DRuntime={args.runtime}",
        f"/DAllowedArchitecture={'x64compatible' if architecture == 'x64' else 'arm64'}",
        f"/DDotNetArchitecture={architecture}", f"/DRuntimeUrl={dependency['url']}",
        f"/DRuntimeSHA256={dependency['sha256']}", str(ROOT / "installer/DailyWallpaper.iss"))
    print(f"Installer: {ROOT / 'artifacts/installer' / f'DailyWallpaper-{args.runtime}-Setup.exe'}")
