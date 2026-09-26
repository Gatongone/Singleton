#!/usr/bin/env bash
#
# The suite of the tree, which a pull request and a push to the branch the releases are cut from are held to, and which
# a release runs before it publishes anything.
#
# Both of them call this rather than naming the commands themselves. A gate which runs something other than what the
# release runs is a gate which lets through the fault the release then finds, and two copies of a command are two
# commands the day one of them is changed: what is run, and why each run is there, is written here, once.
#
# What is not here is the suite of the package, which is the one thing a reader of this file looks for. Every test the
# package carries is a test of an editor: the generator is asked what it writes for a compilation an editor makes, and
# the post processor is asked what it weaves into an assembly an editor loaded, so neither is answered by a runner which
# holds no editor, and a leg which ran nothing is a leg which passed. What stands without one is the three runs below -
# that the committed plugin is the plugin of these sources, that the generator compiles, and that the script which
# writes the version of a release writes it where it belongs - and the suite which is missing is named here rather than
# left for a reader to find out about from a release.
#
# -e is what makes the run above answer for all of them rather than for the last: a suite which failed and a suite which
# passed after it are a run which failed.
set -euo pipefail

# The plugin which the package of Unity ships is built here rather than by whoever consumes it, so what is committed is
# what a Unity project loads: a change to the generator which does not carry a copy of it leaves the two out of step,
# and nothing of the build would say so.
#
# What answers that is the commit the assembly records it was built at, read out of the assembly itself. A build of it
# is not what is asked, because a build at another path is another binary: the debug directory of the image and the
# mapping of the sources in the symbols name the absolute paths of the machine which built them, so two machines do not
# produce one binary from one source, and no assertion over the bytes of a rebuild can stand.
#
# The question is whether the generator has changed since the commit which is recorded, rather than whether that commit
# is the head: the copy is committed after the sources it was built from, so the record names the parent of the commit
# which carries it, and it is what came after the record which says the binary is old.
built=$(grep -aoE '[0-9]+\.[0-9]+\.[0-9]+\+[0-9a-f]{40}' Plugins/Singleton.Generator.dll | head -1 | cut -d+ -f2 || true)
if [ -z "$built" ]; then
  echo "the plugin of Unity records no commit, so whether it is the generator of these sources cannot be read."
  exit 1
fi

if ! git merge-base --is-ancestor "$built" HEAD; then
  # A clone of one commit holds the commit it stands at and none of the ones behind it, so the record of a plugin which
  # is not out of date is a name which such a clone has no answer for: the two read alike from here, and they are told
  # apart by the clone, because what the reader has to do about them is not one thing.
  if [ "$(git rev-parse --is-shallow-repository)" = "true" ]; then
    echo "the plugin of Unity was built at $built, which this clone does not hold: the clone is shallow, and the record is read against the history. Fetch the history rather than build anything."
    exit 1
  fi

  echo "the plugin of Unity was built at $built, which is not a commit of this history: build the copy and commit it."
  exit 1
fi

if ! git diff --quiet "$built" HEAD -- 'Generator~'; then
  echo "the plugin of Unity was built at $built, and Generator~ has changed since:"
  git --no-pager diff --stat "$built" HEAD -- 'Generator~'
  exit 1
fi

# The generator, which is a project beside the package rather than a part of it. Its folder is named with a `~` because
# Unity compiles nothing under such a folder, so this is the one run of the generator which an editor does not make:
# what an editor compiles is the plugin, and a plugin is a binary which no compiler reads again.
dotnet build 'Generator~/Singleton.Generator.sln' -c Release

# The script which writes the version of a release into the files which name one, over a copy of those files. It is
# tested here as well as before a release runs it: a placeholder which stopped matching the file it was written for is a
# commit which breaks the next release rather than the one it is in.
python .github/scripts/set-version-tests.py