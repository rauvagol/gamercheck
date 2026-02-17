# GamerCheck

A Dalamud plugin that opens a window with FFLogs information for each party member, and a comparison to minimum dps for their role, and expected dps for their class.

The requirements are an average amount, so if some party members are over the minimum, that adds leeway for the others.

Note that minimums assume no use of dps limit break, which can account for over 3.5k dps, lowering the requirements by more than 800 per person.

## Installation (custom plugin repo)

This plugin is distributed via a **custom plugin repository** (not the official Dalamud repo). Users can install it as follows:

1. Open **XIVLauncher** → Plugin Settings (`/xlplugins` in-game or from the launcher).
2. Go to **Settings** → **Experimental** → **Custom Plugin Repositories**.
3. Add this URL:
   ```
   https://raw.githubusercontent.com/rauvagol/gamercheck/master/pluginmaster.json
   ```
4. Save and refresh the plugin list. **GamerCheck** should appear; install and enable it.