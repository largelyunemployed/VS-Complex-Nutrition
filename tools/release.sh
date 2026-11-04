#!/usr/bin/env bash
# Builds a release folder: the mod zip, the compat mod template zip, and release notes taken from CHANGELOG.md.
#
#   tools/release.sh [output folder]      (default: release/v<version>)
#
# Needs the .NET SDK and VINTAGE_STORY pointing at a Vintage Story install or server (the folder holding
# VintagestoryAPI.dll). Stops if the build or the tests fail. The version comes from ComplexNutrition/modinfo.json.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
: "${VINTAGE_STORY:?Set VINTAGE_STORY to the folder holding VintagestoryAPI.dll}"

version="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' ComplexNutrition/modinfo.json)"
out="${1:-release/v$version}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"

echo "== Complex Nutrition $version -> $out"
dotnet build -c Release ComplexNutrition/ComplexNutrition.csproj
dotnet run -c Release --project tests/ComplexNutrition.Tests

cp "release/complexnutrition_$version.zip" "$out/"

# The compat mod template that the handbook's Foods from Other Mods page points modders to.
rm -f "$out/compat-mod-template.zip"
(cd templates && python3 -c 'import shutil,sys; shutil.make_archive(sys.argv[1], "zip", ".", "compat-mod")' "$out/compat-mod-template")

# Release notes: this version's CHANGELOG section, as a starting point to edit.
python3 - "$version" "$out/release-notes.md" <<'PY'
import re, sys
version, path = sys.argv[1], sys.argv[2]
text = open("CHANGELOG.md", encoding="utf-8").read()
m = re.search(r"^## v?" + re.escape(version) + r"\b.*?(?=^## |\Z)", text, re.S | re.M)
if not m:
    sys.exit(f"CHANGELOG.md has no section for {version}")
open(path, "w", encoding="utf-8").write(f"# Complex Nutrition v{version}\n\n" + m.group(0).split("\n", 1)[1].strip() + "\n")
PY

echo "== Done:"
ls -la "$out"
