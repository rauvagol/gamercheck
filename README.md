# GamerCheck

A Dalamud plugin that opens a window with FFLogs character URLs for your party. Use when someone joins or right‑click a party member and select GamerCheck.

## Installation (custom plugin repo)

This plugin is distributed via a **custom plugin repository** (not the official Dalamud repo). Users can install it as follows:

1. Open **XIVLauncher** → Plugin Settings (`/xlplugins` in-game or from the launcher).
2. Go to **Settings** → **Experimental** → **Custom Plugin Repositories**.
3. Add this URL (use `master` if that's your default branch):
   ```
   https://raw.githubusercontent.com/rauvagol/gamercheck/master/repo/pluginmaster.json
   ```
4. Save and refresh the plugin list. **GamerCheck** should appear; install and enable it.

## Releasing a new version

See **THIRD_PARTY_REPO.md** for full steps. In short: bump version in the project, build Release, zip the output as `gamercheck.zip`, create a GitHub Release and attach the zip, then update `repo/pluginmaster.json` (AssemblyVersion and LastUpdate). Download links in pluginmaster use `releases/latest/download/gamercheck.zip` so the URL never changes.

## Features

* **Slash command:** `/gamercheck` or `/gc` opens the main window.
* **Main UI:** FFLogs links and parse checks for each party member.
* **Settings:** Open window when a party member joins; FFLogs API (Client ID/Secret) for parse data.
* **Context menu:** Right‑click a party member → GamerCheck.

## Building

1. Open `GamerCheck.sln` in Visual Studio or Rider.
2. Build the solution (Debug or Release).
3. Output: `GamerCheck/bin/x64/Debug/GamerCheck.dll` (or `Release`).

## Activating in-game (dev)

1. `/xlsettings` → **Experimental** → add the path to `GamerCheck.dll` under **Dev Plugin Locations**.
2. `/xlplugins` → **Dev Tools** → **Installed Dev Plugins** → enable **GamerCheck**.
3. Use `/gamercheck` or `/gc` to open the window.
