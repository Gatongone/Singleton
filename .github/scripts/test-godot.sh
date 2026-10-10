#!/usr/bin/env bash
#
# The suite of the package of Godot, which a pull request and a push to the branch the releases are cut from are held
# to, and which a release runs before it publishes anything.
#
# What is asked here is not what the suite of the package of Unity asks. There, every test is a test of an editor: the
# generator is asked what it writes for a compilation only an editor makes, and the post processor is asked what it
# weaves into an assembly only an editor loads, so a runner which holds no editor can answer neither and what stands
# without one is that the generator compiles and that the committed plugin is the generator of these sources.
#
# Here the engine is a program a runner can be given, and both of the ways this is installed are exercised rather than
# compiled. There are two of them, and neither is a variation of the other: a package, which hands the runtime over and
# names the generator and the weaver from files of its own, and an addon, which is a folder of the project and leaves
# the runtime to be read as one of its files, with the two that a build has to be told about named by the plugin. A
# reader who takes one of them is given something the other does not have, so a suite which ran only one would pass
# while half of what is published was broken.
#
# Both legs are held to the same suite of Godot, which is the point: which files were handed over is not something a
# singleton can tell, so a difference between the two is a difference between the wirings.
#
# The engine is named by GODOT rather than by this script. The workflow which runs this downloads a build of Godot and
# names it there; a machine which runs it by hand names its own. A version written here would be a second place to
# write one, and the two would be read apart.
#
# -e is what makes the run answer for all of it rather than for the last: a suite which failed and a suite which passed
# after it are a run which failed.
set -euo pipefail

if [ -z "${GODOT:-}" ] || [ ! -f "${GODOT}" ]; then
  echo "GODOT is not set to a build of Godot, so the suite of the Godot package was not run."
  echo "It is set to '${GODOT:-}', which is not a file."
  exit 1
fi

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)

# A quiet build, and one which does not reach for the compiler server: a runner is shared, and the server is a pipe
# between processes which a sandboxed runner may refuse as well as a process left behind between runs.
quiet=(-v minimal -p:UseSharedCompilation=false -nodeReuse:false)

# What the package is packed into, which is a folder of this run rather than a registry. What is asked is whether the
# package this tree produces builds and runs, and a package published before this commit does not answer that.
feed=$(mktemp -d)
log=$(mktemp)
addon=$(mktemp -d)
trap 'rm -rf "$feed" "$log" "$addon"' EXIT

# Run a project of Godot and answer whether its suite passed.
#
# What the suite answers with is what it printed, so a run which never ends is a run which never answered - and a scene
# which failed to load is exactly that, because the node which would have stopped the engine is in the scene which did
# not load. It is given a time for that reason rather than because it is slow.
suite() {
  local project=$1
  local waited=0

  "$GODOT" --headless --path "$project" > "$log" 2>&1 &
  local engine=$!

  while kill -0 "$engine" 2>/dev/null; do
    if [ "$waited" -ge 180 ]; then
      kill -9 "$engine" 2>/dev/null || true
      wait "$engine" 2>/dev/null || true
      cat "$log"
      echo "the suite of '$project' did not answer within 180 seconds, and was stopped."
      return 1
    fi

    sleep 1
    waited=$((waited + 1))
  done

  wait "$engine" 2>/dev/null || true
  cat "$log"

  if ! grep -q '== ALL PASS ==' "$log"; then
    echo "the suite of '$project' did not pass."
    return 1
  fi
}

# The import is what writes the cache of script classes, which is what the engine reads to know which type of C# a
# script is. Without it a scene cannot be started at all, and a run of it waits rather than failing.
import_project() {
  "$GODOT" --headless --path "$1" --import --quit
}

echo "building the generator"
dotnet build "$root/Godot/Generator/Singleton.Generator.csproj" -c Release "${quiet[@]}"

echo "building the weaver"
dotnet build "$root/Godot/Weaver/Singleton.Weaver.csproj" -c Release "${quiet[@]}"

# The addon is assembled by the same project which is packed, so this writes both what a registry is given and what is
# attached to a release.
echo "packing the package, and assembling the addon"
dotnet pack "$root/Godot/Singleton.Godot.csproj" -c Release "${quiet[@]}" -o "$feed"

addonzip=$(find "$root/Godot/bin/Release" -name 'Singleton.Godot.addons.*.zip' | head -1)
if [ -z "$addonzip" ]; then
  echo "no addon was assembled, so the addon way of installing this was not run."
  exit 1
fi

echo "the addon is $(basename "$addonzip")"

echo
echo "=== the package ==="
dotnet build "$root/Godot/Tests/Tests.csproj" -c Debug "${quiet[@]}" -p:RestoreAdditionalProjectSources="$feed"
import_project "$root/Godot/Tests"
suite "$root/Godot/Tests"

# The addon leg is the suite again, in a project which installs the addon and nothing else: the same scripts, the same
# scene, and a project file which names no package at all.
echo
echo "=== the addon ==="
cp "$root/Godot/Tests/Bootstrap.cs" "$root/Godot/Tests/Singletons.cs" "$root/Godot/Tests/LoadedNode.cs" \
   "$root/Godot/Tests/Main.tscn" "$root/Godot/Tests/Loaded.tscn" "$root/Godot/Tests/project.godot" "$addon/"

# A Godot project file and nothing else, which is what an addon install is: no package, no property, no import. What
# wires the generator and the weaver is written by the plugin below.
cat > "$addon/AddonTests.csproj" <<'PROJECT'
<Project Sdk="Godot.NET.Sdk/4.7.0">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
PROJECT

unzip -q "$addonzip" -d "$addon"
sed -i 's/assembly_name="Tests"/assembly_name="AddonTests"/' "$addon/project.godot"

# What the editor writes into project.godot when the plugin is switched on in Project > Project Settings > Plugins.
printf '\n[editor_plugins]\n\nenabled=PackedStringArray("res://addons/singleton/plugin.cfg")\n' >> "$addon/project.godot"

# Switching the addon on is what writes the file the build reads, and it is the editor which switches it on - so this
# run of the editor stands in for the one a reader makes. It is also the whole reason the plugin is written in
# GDScript: a plugin of C# can only be loaded from an assembly which was built, and the build here cannot succeed
# before this file exists.
echo "switching the addon on"
import_project "$addon"

if [ ! -f "$addon/Directory.Build.props" ]; then
  echo "the addon did not write Directory.Build.props, so the generator and the weaver are not wired."
  exit 1
fi

dotnet build "$addon/AddonTests.csproj" -c Debug "${quiet[@]}"
suite "$addon"

echo
echo "the suite of the Godot package passed, both as a package and as an addon."
