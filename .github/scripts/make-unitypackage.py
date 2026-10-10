"""Build the `.unitypackage` of the package of Unity, for attaching to a release.

A `.unitypackage` is a gzipped tar of one directory per asset, named by the GUID of that asset, holding three files:

    <guid>/asset       the bytes of the file
    <guid>/asset.meta  the `.meta` Unity wrote for it, which is where the GUID and the import settings live
    <guid>/pathname    where the file goes in the project which imports it

Unity reads the `pathname` of every entry and writes each asset there, so the archive is a way of handing a project a
set of files rather than a package which a manager resolves. What it carries is therefore what the package carries and
nothing which a manager would have done for the reader:

    python .github/scripts/make-unitypackage.py Singleton.unitypackage

The one thing a manager does which an archive cannot is resolve the dependencies of the package. This package declares
one - `com.unity.nuget.mono-cecil`, which is where `Mono.Cecil.dll` comes from - and the Editor half of the package
does not compile without it, because its assembly says so:

    "overrideReferences": true, "precompiledReferences": [ "Mono.Cecil.dll" ]

A reader who took the package from a registry is given that file by the manager. A reader who imports this archive is
not, so the archive carries the file itself: it is fetched from the registry of Unity at the version the package names,
and lands in the folder of the package so that the assembly which refers to it finds it. Mono.Cecil is MIT, and the
licence of it travels beside it.

What that costs is that a project which imports this archive *and* installs the package of the registry would hold
Mono.Cecil twice, which is an assembly clash. The two ways are alternatives rather than additions.
"""

import argparse
import gzip
import io
import json
import pathlib
import re
import sys
import tarfile
import urllib.request

REPOSITORY = pathlib.Path(__file__).resolve().parents[2]
PACKAGE = REPOSITORY / "Unity"

# Where the files land in the project which imports the archive. One folder, named after the package, so that what was
# imported is one thing which can be removed again.
ROOT = "Assets/Singleton"

# What the package does not carry, and so what the archive does not either. The generator's project is how the plugin
# which the package ships was built rather than a part of the package, and `.npmignore` is the list of that which is
# read by a manager this archive is not.
NOT_CARRIED = {".npmignore", "Generator~"}

# The dependency whose file a manager would have resolved and an archive cannot.
CECIL = "com.unity.nuget.mono-cecil"

# The GUID of the `.meta` inside that package, which is kept rather than made up: a file which is the same file should
# be the same asset to whoever has both.
CECIL_META = "Mono.Cecil.dll.meta"

GUID = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)


def carried() -> list[pathlib.Path]:
    """Every file of the package which the archive carries, in a settled order.

    A file without a `.meta` is refused rather than passed over. Unity makes one for every asset it imports, so a file
    of a package which has none is one whose GUID is decided by whoever imports it: two readers would hold assets which
    are not the same asset, and the package would be a different package on every machine.
    """
    found = []
    for path in sorted(PACKAGE.rglob("*")):
        if not path.is_file():
            continue

        relative = path.relative_to(PACKAGE)
        if any(part in NOT_CARRIED for part in relative.parts):
            continue
        if path.suffix == ".meta":
            continue

        if not path.with_name(path.name + ".meta").is_file():
            raise SystemExit(f"{relative.as_posix()} has no '.meta', so its GUID is not the package's to give. "
                             "Import it in Unity and commit the '.meta' it writes.")

        found.append(path)

    return found


def guid_of(meta: pathlib.Path) -> str:
    """The GUID which a `.meta` gives its asset."""
    match = GUID.search(meta.read_text(encoding="utf-8"))
    if not match:
        raise SystemExit(f"{meta.name} names no GUID")

    return match.group(1).lower()


def version() -> str:
    """The version of the package, which the name of the archive is built from."""
    return json.loads((PACKAGE / "package.json").read_text(encoding="utf-8"))["version"]


def fetch_cecil(destination: pathlib.Path) -> pathlib.Path:
    """Fetch Mono.Cecil from the registry of Unity, at the version the package depends on.

    What is fetched is the package the registry would have handed a manager, rather than a copy of the file kept here:
    a file kept here is one which nothing says is the version the package depends on, and which is read as being that
    version until somebody looks.
    """
    declared = json.loads((PACKAGE / "package.json").read_text(encoding="utf-8"))["dependencies"][CECIL]
    url = f"https://download.packages.unity.com/{CECIL}/-/{CECIL}-{declared}.tgz"

    print(f"fetching {CECIL}@{declared}")
    with urllib.request.urlopen(url) as answer:
        archive = answer.read()

    destination.mkdir(parents=True, exist_ok=True)
    with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as tar:
        for member in tar.getmembers():
            name = pathlib.PurePosixPath(member.name).name
            if name in ("Mono.Cecil.dll", CECIL_META, "LICENSE.md"):
                source = tar.extractfile(member)
                if source is None:
                    continue
                (destination / name).write_bytes(source.read())

    for wanted in ("Mono.Cecil.dll", CECIL_META):
        if not (destination / wanted).is_file():
            raise SystemExit(f"{CECIL}-{declared} holds no {wanted}")

    return destination


def entries(cecil: pathlib.Path) -> list[tuple[str, bytes, bytes, bytes]]:
    """Every asset of the archive: its GUID, its bytes, its `.meta`, and where it goes."""
    built = []

    for path in carried():
        meta = path.with_name(path.name + ".meta")
        built.append((guid_of(meta), path.read_bytes(), meta.read_bytes(),
                      f"{ROOT}/{path.relative_to(PACKAGE).as_posix()}"))

    # The file a manager would have resolved, and the licence of it, carried where the assembly which refers to it is
    # read from. The meta of the licence is written here, because the licence of a package is not an asset which that
    # package gave a GUID to.
    built.append((guid_of(cecil / CECIL_META), (cecil / "Mono.Cecil.dll").read_bytes(), (cecil / CECIL_META).read_bytes(),
                  f"{ROOT}/Plugins/Mono.Cecil.dll"))

    licence = cecil / "LICENSE.md"
    if licence.is_file():
        built.append((licence_guid(), licence.read_bytes(),
                      f"fileFormatVersion: 2\nguid: {licence_guid()}\n".encode("utf-8"),
                      f"{ROOT}/Plugins/Mono.Cecil.LICENSE.md"))

    seen = set()
    for guid, _, _, destination in built:
        if guid in seen:
            raise SystemExit(f"two assets of the archive are given the GUID {guid}, which is what a copied '.meta' "
                             "looks like")
        if destination in seen:
            raise SystemExit(f"two assets of the archive are written to {destination}")
        seen.add(guid)
        seen.add(destination)

    return built


def licence_guid() -> str:
    """The GUID given to the licence of Mono.Cecil, which is settled rather than random.

    It is not an asset of either package, so no `.meta` of the repository gives it one - but an archive which names a
    different GUID every time it is built is an archive which is a different download every time, and a release which
    cannot be told from another release by its bytes.
    """
    return "4d1b0a2e6b0f47c1a5c53a3e5b6f7d81"


def write(built: list[tuple[str, bytes, bytes, bytes]], output: pathlib.Path) -> None:
    """Write the archive, settled: the same files give the same bytes.

    Everything a tar records about a machine - when it was made, by whom, under which name - is written as nothing, and
    so is the time the gzip header carries, which is otherwise the moment of the build. What a reader compares a
    download against is the commit, and an archive which changes without the commit changing is one which has to be
    read to be believed.
    """
    output.parent.mkdir(parents=True, exist_ok=True)

    def add(tar: tarfile.TarFile, name: str, body: bytes) -> None:
        info = tarfile.TarInfo(name)
        info.size = len(body)
        info.mtime = 0
        info.mode = 0o644
        info.uid = info.gid = 0
        info.uname = info.gname = ""
        tar.addfile(info, io.BytesIO(body))

    # The gzip header is written through a stream of its own rather than left to `tarfile`, which takes the time it is
    # asked for as the time the archive was made. USTAR is asked for as well: the names here are short and plain, and a
    # format which can say more than it needs writes headers which say nothing and differ.
    with open(output, "wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=9) as compressed:
            with tarfile.open(fileobj=compressed, mode="w", format=tarfile.USTAR_FORMAT) as tar:
                for guid, asset, meta, destination in sorted(built):
                    add(tar, f"{guid}/asset", asset)
                    add(tar, f"{guid}/asset.meta", meta)
                    add(tar, f"{guid}/pathname", destination.encode("utf-8"))


def verify(output: pathlib.Path) -> int:
    """Read the archive back and answer how many assets it holds.

    What is asked of it is the shape Unity reads rather than that Unity read it: every directory holds the three files
    and no others, every `pathname` is one path under the root, and every `.meta` gives the GUID of its own directory.
    """
    assets = 0
    held: dict[str, set[str]] = {}

    with tarfile.open(output, "r:gz") as tar:
        for member in tar.getmembers():
            if not member.isfile():
                raise SystemExit(f"{member.name} is not a file")

            parts = pathlib.PurePosixPath(member.name).parts
            if len(parts) != 2:
                raise SystemExit(f"{member.name} is not '<guid>/<file>'")
            held.setdefault(parts[0], set()).add(parts[1])

        for guid, names in sorted(held.items()):
            if not re.fullmatch(r"[0-9a-f]{32}", guid):
                raise SystemExit(f"'{guid}' is not a GUID")
            if names != {"asset", "asset.meta", "pathname"}:
                raise SystemExit(f"{guid} holds {sorted(names)} rather than the three files of an asset")

            meta = tar.extractfile(f"{guid}/asset.meta").read().decode("utf-8")
            if guid_of_text(meta) != guid:
                raise SystemExit(f"{guid}/asset.meta names another GUID")

            destination = tar.extractfile(f"{guid}/pathname").read().decode("utf-8")
            if not destination.startswith(ROOT + "/"):
                raise SystemExit(f"{guid} is written to '{destination}', which is not under {ROOT}")

            assets += 1

    return assets


def guid_of_text(meta: str) -> str:
    """The GUID a `.meta` written as text names."""
    match = GUID.search(meta)
    return match.group(1).lower() if match else ""


def main() -> None:
    """Build the archive and read it back."""
    parser = argparse.ArgumentParser(description="Build the .unitypackage of the package of Unity.")
    parser.add_argument("output", nargs="?", help="where to write it; the version is put in the name when it is left out")
    parser.add_argument("--mono-cecil", type=pathlib.Path,
                        help="a directory holding Mono.Cecil.dll and its .meta, rather than fetching them")
    arguments = parser.parse_args()

    output = pathlib.Path(arguments.output) if arguments.output \
        else REPOSITORY / f"Singleton-{version()}.unitypackage"

    if arguments.mono_cecil:
        cecil = arguments.mono_cecil
        for wanted in ("Mono.Cecil.dll", CECIL_META):
            if not (cecil / wanted).is_file():
                raise SystemExit(f"{cecil} holds no {wanted}")
    else:
        # Fetched beside the repository rather than into it: what is downloaded is a build input of one run, and the
        # ignore rules of the repository name nothing of it.
        cecil = fetch_cecil(REPOSITORY / "obj" / "mono-cecil")

    built = entries(cecil)
    write(built, output)
    assets = verify(output)

    print(f"{output.name}: {assets} assets, {output.stat().st_size} bytes")


if __name__ == "__main__":
    main()
