# Publishing GamerCheck as a Third-Party Plugin Repo

Users add your repo in-game via **Dalamud Settings** → **Experimental** → **Custom Plugin Repositories** (paste URL, click +, save). Then they install GamerCheck from the Plugin Installer like any other plugin.

---

## 1. Publish your plugin code

- Push this repo to a **public** GitHub repo (e.g. `https://github.com/rauvagol/gamercheck`).
- Update `PackageProjectUrl` in `GamerCheck.csproj` and `RepoUrl` in `GamerCheck.json` to that URL.

---

## 2. Build a release zip

For each release:

1. Build in **Release** (so the output is suitable for distribution):
   ```bash
   dotnet build "GamerCheck.sln" -c Release
   ```
2. The plugin output is under `GamerCheck/bin/x64/Release/`. You need a zip that contains:
   - `GamerCheck.dll`
   - `GamerCheck.json` (manifest)

   So zip the **contents** of that folder (the DLL and JSON inside it), not the folder itself. Name it e.g. `GamerCheck-0.0.0.1.zip` or `latest.zip`.

If your build doesn’t copy `GamerCheck.json` into the output, copy it in by hand before zipping.

---

## 3. Create a GitHub Release and attach the zip

- On your repo: **Releases** → **Create a new release**.
- Tag version (e.g. `v0.0.0.1`).
- Attach the zip you built.
- Use a **stable** download URL for the repo JSON, e.g.:
  - `https://github.com/rauvagol/gamercheck/releases/download/v0.0.0.1/latest.zip`

---

## 4. Host the repo JSON (plugin list)

The “custom repo” users add is **a URL to a JSON file** that lists your plugin(s). That file must be reachable via a normal HTTP GET (no auth).

**Option A – JSON in this repo**

- Add a file, e.g. `pluginmaster.json`, in the repo (root or a `repo/` folder).
- Fill it with one entry per plugin (see `repo/pluginmaster.json` in this repo for a template).
- Use the **raw** URL as the custom repo URL, e.g.:
  - `https://raw.githubusercontent.com/rauvagol/gamercheck/main/pluginmaster.json`
  - (Replace `main` with your default branch if different.)

**Option B – Separate repo for the repo**

- Create a small repo that only holds `pluginmaster.json` (and optionally an icon).
- Use that repo’s raw URL as the custom repo URL.

When you release a new version, update `pluginmaster.json`: bump `AssemblyVersion` and `LastUpdate`. If you use the **latest** URL (see below), you do **not** need to change the download links each time.

**Using "latest" (recommended):** Set all download links once to:
`https://github.com/rauvagol/gamercheck/releases/latest/download/gamercheck.zip`
Then for each release: create a new GitHub Release, attach a zip named **exactly** `gamercheck.zip`. GitHub will serve that file from whatever release is "latest." You only update pluginmaster to bump `AssemblyVersion` and `LastUpdate` so the installer shows an update; the URLs stay the same.

---

## 5. Tell users how to add your repo

They need to add **one** URL: the one that returns the repo JSON.

Example instructions:

1. In-game, run `/xlsettings` (or open Dalamud Settings from the Plugin Installer).
2. Open the **Experimental** tab.
3. Under **Custom Plugin Repositories**, paste the URL (e.g. the raw `pluginmaster.json` URL above).
4. Click **+**, then save.
5. Open the Plugin Installer (`/xlplugins`); GamerCheck should appear. Install and enable.

---

## Fields you must keep in sync

In `pluginmaster.json` (or whatever you name it):

- **AssemblyVersion** – must match the version of the DLL you put in the zip (e.g. `0.0.0.1` → `0.0.0.1` or `0.0.0.1.0`).
- **DalamudApiLevel** – must match what your built plugin targets (e.g. 10 for current Dalamud; check your SDK or built manifest).
- **DownloadLinkInstall** and **DownloadLinkUpdate** – URL of the zip (e.g. the GitHub release download URL).
- **LastUpdate** – Unix timestamp (seconds since 1970) when you last updated the entry; use for “last updated” in the installer.

You can leave **DownloadLinkTesting** the same as the release link or omit it if you don’t have a separate testing build.
