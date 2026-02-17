# Generates pluginmaster.json from manifests inside plugins/*/latest.zip.
# Based on https://github.com/Aireil/MyDalamudPlugins / anomek/MyDalamudPlugins

import json
import os
from os.path import getmtime
from zipfile import ZipFile

# Use default branch for download URL (e.g. when running on tag)
ref = os.environ.get("GITHUB_REF", "refs/heads/main")
if ref.startswith("refs/tags/"):
    branch = os.environ.get("DEFAULT_BRANCH", "main")
else:
    branch = ref.split("refs/heads/")[-1] if "refs/heads/" in ref else "main"

DOWNLOAD_URL = f"https://github.com/{os.environ.get('GITHUB_REPOSITORY', 'USER/gamercheck').lower()}/raw/{branch}/plugins/{{plugin_name}}/latest.zip"

DEFAULTS = {
    "IsHide": False,
    "IsTestingExclusive": False,
    "ApplicableVersion": "any",
}

DUPLICATES = {
    "DownloadLinkInstall": ["DownloadLinkTesting", "DownloadLinkUpdate"],
}

TRIMMED_KEYS = [
    "Author",
    "Name",
    "Punchline",
    "Description",
    "Changelog",
    "InternalName",
    "AssemblyVersion",
    "RepoUrl",
    "ApplicableVersion",
    "Tags",
    "DalamudApiLevel",
    "IconUrl",
    "ImageUrls",
]


def main():
    master = extract_manifests()
    master = [trim_manifest(m) for m in master]
    add_extra_fields(master)
    write_master(master)
    last_update()


def extract_manifests():
    manifests = []
    for dirpath, _dirnames, filenames in os.walk("plugins"):
        if not filenames or "latest.zip" not in filenames:
            continue
        plugin_name = os.path.basename(dirpath)
        latest_zip = os.path.join(dirpath, "latest.zip")
        with ZipFile(latest_zip) as z:
            manifest = json.loads(z.read(f"{plugin_name}.json").decode("utf-8"))
        manifests.append(manifest)
    return manifests


def add_extra_fields(manifests):
    for manifest in manifests:
        name = manifest.get("InternalName", manifest.get("Name", "unknown"))
        manifest["DownloadLinkInstall"] = DOWNLOAD_URL.format(plugin_name=name)
        for k, v in DEFAULTS.items():
            if k not in manifest:
                manifest[k] = v
        for source, keys in DUPLICATES.items():
            for k in keys:
                if k not in manifest:
                    manifest[k] = manifest[source]
        manifest["DownloadCount"] = 0


def write_master(master):
    with open("pluginmaster.json", "w") as f:
        json.dump(master, f, indent=2)


def trim_manifest(plugin):
    return {k: plugin[k] for k in TRIMMED_KEYS if k in plugin}


def last_update():
    with open("pluginmaster.json") as f:
        master = json.load(f)
    for plugin in master:
        latest = f'plugins/{plugin["InternalName"]}/latest.zip'
        if os.path.isfile(latest):
            modified = int(getmtime(latest))
            if plugin.get("LastUpdate") != modified:
                plugin["LastUpdate"] = str(modified)
    with open("pluginmaster.json", "w") as f:
        json.dump(master, f, indent=2)


if __name__ == "__main__":
    main()
