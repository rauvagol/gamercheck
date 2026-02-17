using System;
using System.Globalization;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using GamerCheck.FFLogs;
using Lumina.Excel.Sheets;

namespace GamerCheck.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin _plugin;
    private readonly object _parseLock = new();
    private Dictionary<string, ParseCacheEntry> _parseCache = new();
    private bool _refreshRequested;

    private string _lookupName = "";
    private int _lookupRegionIndex;
    private int _lookupWorldIndex;
    private string? _lookupCacheKey;
    private bool _lookupRequested;
    private int _lastPartyCount = -1;

    public MainWindow(Plugin plugin)
        : base("GamerCheck - Party FFLogs##GamerCheck", ImGuiWindowFlags.NoScrollbar)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        _plugin = plugin;
        TitleBarButtons =
        [
            new Window.TitleBarButton
            {
                Icon = FontAwesomeIcon.Cog,
                Click = _ => _plugin.ToggleConfigUi(),
                ShowTooltip = () => ImGui.SetTooltip("Open settings")
            }
        ];
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.IsWindowAppearing())
        {
            _refreshRequested = true;
        }
        var partyCount = Plugin.PartyList.Length;
        if (partyCount > _lastPartyCount && _lastPartyCount >= 0)
            _refreshRequested = true;
        _lastPartyCount = partyCount;

        if (ImGui.BeginTabBar("##GamerCheckTabs", ImGuiTabBarFlags.None))
        {
            var partyAuditFlags = ImGui.IsWindowAppearing() ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem("Party Audit", partyAuditFlags))
            {
                DrawPartyAuditTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Lookup"))
            {
                DrawLookupTab();
                ImGui.EndTabItem();
            }
            foreach (var (name, health, bossId, durationSeconds) in ThresholdBosses)
            {
                if (ImGui.BeginTabItem(name))
                {
                    DrawBossThresholdTab(name, health, bossId, durationSeconds);
                    ImGui.EndTabItem();
                }
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawPartyAuditTab()
    {
        var partyList = Plugin.PartyList;
        var dataManager = Plugin.DataManager;
        var worldSheet = dataManager.GetExcelSheet<World>();
        if (worldSheet == null)
        {
            ImGui.Text("Could not load world data.");
            return;
        }

        var entries = new List<(string Name, uint WorldRowId, string CurrentClass)>();
        if (partyList.Length > 0)
        {
            for (var i = 0; i < partyList.Length; i++)
            {
                var member = partyList[i];
                if (member == null) continue;
                var name = member.Name.ToString();
                if (string.IsNullOrEmpty(name)) name = $"Party member {i + 1}";
                var currentClass = GetCurrentClassDisplayFromRef(member.ClassJob);
                entries.Add((name, (uint)member.World.RowId, currentClass));
            }
        }
        else
        {
            var crossWorld = CrossWorldParty.GetCrossWorldPartyMembers();
            if (crossWorld != null && crossWorld.Count > 0)
            {
                foreach (var (name, worldRowId, currentClass) in crossWorld)
                    entries.Add((name, worldRowId, currentClass));
            }
            else
            {
                var localPlayer = Plugin.ObjectTable.LocalPlayer;
                if (localPlayer == null)
                {
                    ImGui.Text("You are not logged in.");
                    return;
                }
                var myName = localPlayer.Name.ToString();
                if (string.IsNullOrEmpty(myName)) myName = "You";
                var currentClass = GetCurrentClassDisplayFromRef(localPlayer.ClassJob);
                entries.Add((myName, (uint)localPlayer.HomeWorld.RowId, currentClass));
            }
        }

        var cfg = _plugin.Configuration;
        var hasApi = !string.IsNullOrWhiteSpace(cfg.FflogsClientId) && !string.IsNullOrWhiteSpace(cfg.FflogsClientSecret);
        if (hasApi && _refreshRequested)
        {
            _refreshRequested = false;
            var refreshList = new List<(string Name, string WorldSlug, string RegionApi)>();
            for (var j = 0; j < entries.Count; j++)
            {
                var (ename, worldRowId, _) = entries[j];
                string wname;
                if (worldSheet.TryGetRow(worldRowId, out var wrow))
                    wname = wrow.Name.ToString() ?? "Unknown";
                else
                    wname = "Unknown";
                refreshList.Add((ename, wname.ToLowerInvariant().Replace(" ", ""), FFLogsApiService.ToApiRegion(GetFflogsRegion(wname))));
            }
            RefreshParsesAsync(refreshList);
        }

        using (var child = ImRaii.Child("PartyAudit", new Vector2(-1, -1), true, ImGuiWindowFlags.None))
        {
            if (!child.Success) return;

            for (var i = 0; i < entries.Count; i++)
            {
                var (name, worldRowId, currentClass) = entries[i];

                string worldName;
                try
                {
                    if (worldSheet.TryGetRow(worldRowId, out var worldRow))
                        worldName = worldRow.Name.ToString() ?? "Unknown";
                    else
                        worldName = "Unknown";
                }
                catch
                {
                    worldName = "Unknown";
                }

                var region = GetFflogsRegion(worldName);
                var regionApi = FFLogsApiService.ToApiRegion(region);
                var encodedName = Uri.EscapeDataString(name);
                var worldSlug = worldName.ToLowerInvariant().Replace(" ", "");
                var url = $"https://www.fflogs.com/character/{region}/{worldSlug}/{encodedName}";
                var cacheKey = $"{name}|{worldSlug}";

                ImGui.PushID(i);
                ImGui.Text($"{name} ({worldName}):");
                ImGui.SameLine(0, 4);
                if (ImGui.Button("Copy"))
                {
                    ImGui.SetClipboardText(url);
                }
                ImGui.SameLine(0, 4);
                if (ImGui.Button("Open"))
                {
                    OpenUrl(url);
                }
                ImGui.PopID();

                // Parses (if API configured)
                ParseCacheEntry? entry;
                lock (_parseLock)
                {
                    _parseCache.TryGetValue(cacheKey, out entry);
                }
                if (hasApi)
                    DrawParseTableContent(entry, i, currentClass);
                else
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Add Client ID + Secret in settings to show parses");

                ImGui.Spacing();
            }

            if (hasApi)
            {
                var btnSize = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());
                var contentMin = ImGui.GetWindowContentRegionMin();
                var contentMax = ImGui.GetWindowContentRegionMax();
                ImGui.SetCursorPos(new Vector2(contentMax.X - btnSize.X, contentMin.Y));
                if (ImGui.Button("##RefreshParses", btnSize))
                    _refreshRequested = true;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Refresh parses");
                var rectMin = ImGui.GetItemRectMin();
                var rectSize = ImGui.GetItemRectSize();
                var iconStr = FontAwesomeIcon.Sync.ToIconString();
                using (_plugin.PushIconFont())
                {
                    var iconSize = ImGui.CalcTextSize(iconStr);
                    var offset = new Vector2((rectSize.X - iconSize.X) * 0.5f, (rectSize.Y - iconSize.Y) * 0.5f);
                    ImGui.SetCursorScreenPos(rectMin + offset);
                    ImGui.Text(iconStr);
                }
            }
        }
    }

    private void DrawParseTableContent(ParseCacheEntry? entry, int tableId, string? currentClass = null)
    {
        if (entry == null) return;
        if (entry.Loading)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Loading parses...");
            return;
        }
        if (entry.Error != null)
        {
            ImGui.TextColored(new Vector4(0.9f, 0.3f, 0.3f, 1f), entry.Error);
            return;
        }
        if (entry.Parses == null || entry.Parses.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "  No parses for current tier");
            return;
        }
        var isHidden = entry.Parses.Count == 1 && entry.Parses[0].EncounterName == "(hidden)";
        if (isHidden)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "  Logs hidden");
            return;
        }
        if (!ImGui.BeginTable($"##parses_{tableId}", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg)) return;
        ImGui.TableSetupColumn("Boss", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Current class", ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableSetupColumn("Logged class", ImGuiTableColumnFlags.WidthFixed, 90f);
        ImGui.TableSetupColumn("rDPS", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("Delta (true min)", ImGuiTableColumnFlags.WidthFixed, 120f);
        ImGui.TableSetupColumn("True min", ImGuiTableColumnFlags.WidthFixed, 75f);
        ImGui.TableSetupColumn("Delta", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGui.TableSetupColumn("Expected rDPS", ImGuiTableColumnFlags.WidthFixed, 95f);
        ImGui.TableHeadersRow();
        var currentClassDisplay = currentClass ?? "—";
        foreach (var p in entry.Parses)
        {
            if (p.EncounterName == "(hidden)") continue;
            var hasLog = p.Rdps > 0;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text(NormalizeEncounterName(p.EncounterName));

            if (!hasLog)
            {
                ImGui.TableSetColumnIndex(1);
                ImGui.Text(currentClassDisplay);
                ImGui.TableSetColumnIndex(2);
                ImGui.Text("—");
                for (var c = 3; c < 8; c++) { ImGui.TableSetColumnIndex(c); ImGui.Text("—"); }
                continue;
            }

            var bossKey = FindBossByEncounterName(p.EncounterName);
            var className = SpecToClassName(p.Spec);
            long? expectedDps = null;
            if (bossKey.HasValue && className != null)
                expectedDps = GetExpectedDpsForBossAndClass(bossKey.Value.BossId, className);
            // True min = realistic role min (worst or second-worst by role) scaled with realistic worst comp baseline
            double? trueMinDps = null;
            if (bossKey.HasValue && className != null && ThresholdDataByBoss.TryGetValue(bossKey.Value.BossId, out var thresholdData) && thresholdData.BaselineTotalDps > 0 && bossKey.Value.Duration > 0)
            {
                var roleMin = GetRealisticRoleMinDps(bossKey.Value.BossId, className);
                if (roleMin.HasValue)
                {
                    var dpsCheck = (double)bossKey.Value.Health / bossKey.Value.Duration;
                    var scale = dpsCheck / thresholdData.BaselineTotalDps;
                    trueMinDps = Math.Round(roleMin.Value * scale, 0);
                }
            }
            var loggedClassDisplay = SpecToDisplayName(p.Spec);
            var classMismatch = currentClassDisplay != "—" && !string.Equals(currentClassDisplay, loggedClassDisplay, StringComparison.OrdinalIgnoreCase);
            var warningYellow = new Vector4(0.85f, 0.85f, 0.25f, 1f);

            ImGui.TableSetColumnIndex(1);
            if (classMismatch)
                ImGui.TextColored(warningYellow, currentClassDisplay);
            else
                ImGui.Text(currentClassDisplay);
            ImGui.TableSetColumnIndex(2);
            if (classMismatch)
                ImGui.TextColored(warningYellow, loggedClassDisplay);
            else
                ImGui.Text(loggedClassDisplay);
            ImGui.TableSetColumnIndex(3);
            if (trueMinDps.HasValue)
            {
                var minVal = trueMinDps.Value;
                var withinTolerance = minVal > 0 && p.Rdps >= minVal * 0.985 && p.Rdps < minVal;
                Vector4 color = p.Rdps >= minVal ? new Vector4(0.35f, 0.75f, 0.35f, 1f)
                    : withinTolerance ? warningYellow
                    : new Vector4(0.9f, 0.35f, 0.35f, 1f);
                ImGui.TextColored(color, $"{p.Rdps:N0}");
            }
            else
                ImGui.Text($"{p.Rdps:N0}");

            ImGui.TableSetColumnIndex(4);
            if (trueMinDps.HasValue)
            {
                var deltaTrue = p.Rdps - trueMinDps.Value;
                var pctTrue = trueMinDps.Value != 0 ? (deltaTrue * 100.0 / trueMinDps.Value) : 0.0;
                var withinToleranceTrue = deltaTrue < 0 && pctTrue >= -1.5;
                Vector4 colorTrue = deltaTrue >= 0 ? new Vector4(0.35f, 0.75f, 0.35f, 1f)
                    : withinToleranceTrue ? warningYellow
                    : new Vector4(0.9f, 0.35f, 0.35f, 1f);
                ImGui.TextColored(colorTrue, $"{(deltaTrue >= 0 ? "+" : "")}{deltaTrue:N0} ({pctTrue:+0.0;-0.0}%)");
            }
            else
                ImGui.Text("—");

            ImGui.TableSetColumnIndex(5);
            ImGui.Text(trueMinDps.HasValue ? $"{trueMinDps.Value:N0}" : "—");

            ImGui.TableSetColumnIndex(6);
            if (expectedDps.HasValue)
            {
                var delta = p.Rdps - expectedDps.Value;
                var pct = expectedDps.Value != 0 ? (delta * 100.0 / expectedDps.Value) : 0.0;
                var withinTolerance = delta < 0 && pct >= -1.5;
                Vector4 color = delta >= 0 ? new Vector4(0.35f, 0.75f, 0.35f, 1f)
                    : withinTolerance ? warningYellow
                    : new Vector4(0.9f, 0.35f, 0.35f, 1f);
                ImGui.TextColored(color, $"{(delta >= 0 ? "+" : "")}{delta:N0} ({pct:+0.0;-0.0}%)");
            }
            else
                ImGui.Text("—");
            ImGui.TableSetColumnIndex(7);
            ImGui.Text(expectedDps.HasValue ? $"{expectedDps.Value:N0}" : "—");
        }
        ImGui.EndTable();
    }

    private void DrawLookupTab()
    {
        var cfg = _plugin.Configuration;
        var hasApi = !string.IsNullOrWhiteSpace(cfg.FflogsClientId) && !string.IsNullOrWhiteSpace(cfg.FflogsClientSecret);

        if (!hasApi)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Add Client ID + Secret in settings to look up parses.");
            return;
        }

        var regionNames = new[] { "North America", "Europe", "Japan", "Oceania" };
        var regionApis = new[] { "na", "eu", "jp", "oc" };

        ImGui.SetNextItemWidth(180f);
        ImGui.InputTextWithHint("##lookup_name", "Character name", ref _lookupName, 32);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(140f);
        if (ImGui.Combo("##lookup_region", ref _lookupRegionIndex, regionNames, regionNames.Length))
        {
            var worlds = RegionWorlds.GetWorldSlugsForRegion(_lookupRegionIndex);
            if (_lookupWorldIndex >= worlds.Length) _lookupWorldIndex = 0;
        }

        var worldSlugs = RegionWorlds.GetWorldSlugsForRegion(_lookupRegionIndex);
        if (_lookupWorldIndex >= worldSlugs.Length) _lookupWorldIndex = 0;
        var worldDisplayNames = new string[worldSlugs.Length];
        var ti = CultureInfo.InvariantCulture.TextInfo;
        for (var i = 0; i < worldSlugs.Length; i++)
            worldDisplayNames[i] = ti.ToTitleCase(worldSlugs[i]);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(140f);
        ImGui.Combo("##lookup_world", ref _lookupWorldIndex, worldDisplayNames, worldDisplayNames.Length);

        ImGui.SameLine();
        if (ImGui.Button("Look up"))
        {
            var name = _lookupName.Trim();
            if (string.IsNullOrEmpty(name)) return;
            var worldSlug = worldSlugs[_lookupWorldIndex];
            var regionApi = regionApis[_lookupRegionIndex];
            _lookupCacheKey = $"{name}|{worldSlug}";
            _lookupRequested = true;
        }

        if (_lookupRequested)
        {
            _lookupRequested = false;
            var name = _lookupName.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                var worldSlug = RegionWorlds.GetWorldSlugsForRegion(_lookupRegionIndex)[_lookupWorldIndex];
                var regionApi = regionApis[_lookupRegionIndex];
                RefreshParsesAsync(new List<(string, string, string)> { (name, worldSlug, regionApi) });
            }
        }

        ImGui.Spacing();

        if (_lookupCacheKey != null)
        {
            ParseCacheEntry? entry;
            lock (_parseLock)
            {
                _parseCache.TryGetValue(_lookupCacheKey, out entry);
            }
            DrawParseTableContent(entry, 0, null);
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Enter a character name, select world and region, then click Look up.");
        }
    }

    private static readonly (string Name, long Health, int FflogsBossId, int DurationSeconds)[] ThresholdBosses =
    {
        ("Vamp Fatale", 128_140_000, 101, 605),           // 10 min 5 sec
        ("Red Hot and Deep Blue", 127_620_000, 102, 600), // 10 min
        ("The Tyrant", 154_290_000, 103, 662),            // 11 min 2 sec
        ("Lindwurm I", 104_590_000, 104, 438),            // 7 min 18 sec
        ("Lindwurm II", 131_940_000, 105, 554),           // 9 min 14 sec
    };

    /// <summary>Per-boss: baseline total DPS (weakest 8-man comp), role shares for pull-your-weight, and 50th percentile DPS per class. Baseline = 100% for normalization.</summary>
    private static readonly Dictionary<int, BossThresholdData> ThresholdDataByBoss = new()
    {
        [101] = new BossThresholdData(
            baselineTotalDps: 225_687.16, // WAR+DRK, SGE+SCH, RPR+SAM, SMN, DNC
            tankPct: 20.531, healerPct: 17.069, meleePct: 31.681, casterPct: 15.508, physRangedPct: 15.211,
            classDps: new (string Name, double Dps)[] {
                ("Black Mage", 37_082.05), ("Monk", 36_779.88), ("Dragoon", 36_683.29), ("Ninja", 36_674.94), ("Viper", 36_412.78),
                ("Samurai", 36_089.23), ("Red Mage", 35_849.09), ("Pictomancer", 35_762.92), ("Reaper", 35_404.81), ("Summoner", 34_976.82),
                ("Machinist", 34_894.99), ("Bard", 34_652.21), ("Dancer", 34_357.18),
                ("Paladin", 23_505.38), ("Gunbreaker", 23_383.58), ("Dark Knight", 23_236.31), ("Warrior", 23_101.50),
                ("Astrologian", 19_809.18), ("White Mage", 19_669.02), ("Scholar", 19_492.91), ("Sage", 19_028.40),
            }),
        [102] = new BossThresholdData( // Red Hot and Deep Blue: WAR+DRK, SCH+SGE, RPR+Viper, SMN, MCH
            baselineTotalDps: 232_214.63,
            tankPct: 20.29, healerPct: 16.45, meleePct: 31.56, casterPct: 15.81, physRangedPct: 15.89,
            classDps: new (string Name, double Dps)[] {
                ("Pictomancer", 38_742.07), ("Dragoon", 38_221.21), ("Red Mage", 38_049.36), ("Monk", 37_927.26), ("Ninja", 37_813.74),
                ("Black Mage", 37_357.86), ("Bard", 37_194.02), ("Dancer", 37_047.00), ("Samurai", 37_043.26), ("Machinist", 36_906.68),
                ("Viper", 36_732.99), ("Summoner", 36_717.06), ("Reaper", 36_562.02),
                ("Gunbreaker", 24_149.95), ("Paladin", 24_088.45), ("Dark Knight", 23_653.80), ("Warrior", 23_458.62),
                ("White Mage", 21_124.09), ("Astrologian", 20_705.18), ("Sage", 19_129.25), ("Scholar", 19_054.21),
            }),
        [103] = new BossThresholdData( // The Tyrant: WAR+DRK, SGE+SCH, RPR+Viper, SMN, DNC
            baselineTotalDps: 235_459.58,
            tankPct: 20.33, healerPct: 16.55, meleePct: 32.14, casterPct: 15.82, physRangedPct: 15.16,
            classDps: new (string Name, double Dps)[] {
                ("Black Mage", 38_847.08), ("Monk", 38_482.15), ("Ninja", 38_357.44), ("Dragoon", 38_343.01), ("Samurai", 38_085.85),
                ("Viper", 38_006.38), ("Reaper", 37_668.86), ("Pictomancer", 37_452.88), ("Red Mage", 37_428.19), ("Summoner", 37_236.74),
                ("Bard", 36_575.25), ("Machinist", 36_044.70), ("Dancer", 35_704.44),
                ("Gunbreaker", 24_229.60), ("Paladin", 24_134.31), ("Dark Knight", 23_932.39), ("Warrior", 23_930.90),
                ("Astrologian", 20_323.84), ("White Mage", 19_966.64), ("Scholar", 19_813.61), ("Sage", 19_166.26),
            }),
        [104] = new BossThresholdData( // Lindwurm I: PLD+WAR, SGE+SCH, RPR+Viper, SMN, DNC
            baselineTotalDps: 249_566.06,
            tankPct: 20.53, healerPct: 16.55, meleePct: 32.35, casterPct: 15.59, physRangedPct: 14.98,
            classDps: new (string Name, double Dps)[] {
                ("Monk", 41_329.12), ("Ninja", 40_850.52), ("Samurai", 40_738.07), ("Dragoon", 40_707.45), ("Reaper", 40_378.10),
                ("Viper", 40_366.09), ("Black Mage", 40_003.43), ("Pictomancer", 39_764.03), ("Red Mage", 39_281.45), ("Summoner", 38_897.44),
                ("Bard", 37_938.46), ("Machinist", 37_389.04), ("Dancer", 37_379.82),
                ("Gunbreaker", 26_078.79), ("Dark Knight", 25_928.19), ("Warrior", 25_710.17), ("Paladin", 25_538.24),
                ("Astrologian", 21_715.33), ("White Mage", 21_262.87), ("Scholar", 20_905.57), ("Sage", 20_390.63),
            }),
        [105] = new BossThresholdData( // Lindwurm II: WAR+DRK, SGE+SCH, RPR+SAM, SMN, DNC
            baselineTotalDps: 244_065.63,
            tankPct: 20.31, healerPct: 16.75, meleePct: 32.22, casterPct: 15.71, physRangedPct: 15.00,
            classDps: new (string Name, double Dps)[] {
                ("Black Mage", 39_936.32), ("Viper", 39_599.28), ("Monk", 39_591.12), ("Ninja", 39_526.89), ("Dragoon", 39_378.81),
                ("Samurai", 39_317.87), ("Reaper", 39_312.26), ("Red Mage", 38_675.65), ("Pictomancer", 38_581.35), ("Summoner", 38_358.06),
                ("Machinist", 37_069.09), ("Bard", 37_013.32), ("Dancer", 36_595.77),
                ("Gunbreaker", 25_134.05), ("Paladin", 24_901.58), ("Dark Knight", 24_824.23), ("Warrior", 24_762.78),
                ("White Mage", 21_249.75), ("Astrologian", 21_223.16), ("Scholar", 20_563.86), ("Sage", 20_330.80),
            }),
    };

    private readonly struct BossThresholdData
    {
        public readonly double BaselineTotalDps;
        public readonly double TankPct, HealerPct, MeleePct, CasterPct, PhysRangedPct;
        public readonly (string Name, double Dps)[] ClassDps;

        public BossThresholdData(double baselineTotalDps, double tankPct, double healerPct, double meleePct, double casterPct, double physRangedPct, (string Name, double Dps)[] classDps)
        {
            BaselineTotalDps = baselineTotalDps;
            TankPct = tankPct;
            HealerPct = healerPct;
            MeleePct = meleePct;
            CasterPct = casterPct;
            PhysRangedPct = physRangedPct;
            ClassDps = classDps;
        }

        /// <summary>Normalized score: 100 = average of the weakest 8-man comp. So class_dps / (baseline/8) * 100.</summary>
        public double NormalizedScore(double classDps) => BaselineTotalDps > 0 ? classDps * 8.0 / BaselineTotalDps * 100.0 : 0;
    }

    /// <summary>Title-case for display (e.g. "RED MAGE" -> "Red Mage").</summary>
    private static string ToDisplayCapitalization(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Trim().ToLowerInvariant());
    }

    /// <summary>Get current class display name from game RowRef (ClassJob row). Uses reflection so we use the game's exact name.</summary>
    private static string GetCurrentClassDisplayFromRef(object? classJobRowRef)
    {
        if (classJobRowRef == null) return "—";
        try
        {
            var type = classJobRowRef.GetType();
            var valueProp = type.GetProperty("ValueNullable") ?? type.GetProperty("Value");
            if (valueProp?.GetValue(classJobRowRef) is not { } row) return "—";
            var rowType = row.GetType();
            var nameProp = rowType.GetProperty("Name");
            if (nameProp?.GetValue(row) is { } nameVal)
            {
                var s = nameVal.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return ToDisplayCapitalization(s);
            }
            var abbrProp = rowType.GetProperty("Abbreviation");
            if (abbrProp?.GetValue(row) is { } abbrVal)
            {
                var s = abbrVal.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return ToDisplayCapitalization(s);
            }
        }
        catch { /* ignore */ }
        return "—";
    }

    private static string GetRoleForClass(string className) => className switch
    {
        "Paladin" or "Gunbreaker" or "Dark Knight" or "Warrior" => "Tank",
        "Astrologian" or "White Mage" or "Scholar" or "Sage" => "Healer",
        "Monk" or "Dragoon" or "Ninja" or "Viper" or "Samurai" or "Reaper" => "Melee",
        "Black Mage" or "Red Mage" or "Pictomancer" or "Summoner" => "Caster",
        "Machinist" or "Bard" or "Dancer" => "Phys Ranged",
        _ => "?"
    };

    private static readonly string[] RoleOrder = { "Melee", "Caster", "Phys Ranged", "Tank", "Healer" };

    /// <summary>Format spec for display: e.g. "RedMage" -> "Red Mage".</summary>
    private static string SpecToDisplayName(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return "—";
        var s = spec.Trim();
        if (s.Length <= 1) return s;
        var sb = new System.Text.StringBuilder(s.Length + 2);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsUpper(c) && i > 0 && char.IsLower(s[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Normalize encounter name for display and lookup (e.g. "Lindwurm" -> "Lindwurm I").</summary>
    private static string NormalizeEncounterName(string encounterName)
    {
        if (string.IsNullOrWhiteSpace(encounterName)) return encounterName ?? "";
        var key = encounterName.Trim();
        if (string.Equals(key, "Lindwurm", StringComparison.OrdinalIgnoreCase))
            return "Lindwurm I";
        return key;
    }

    /// <summary>Match encounter name from parse to our boss (local data only). Returns (bossId, health, duration) or null.</summary>
    private static (int BossId, long Health, int Duration)? FindBossByEncounterName(string encounterName)
    {
        if (string.IsNullOrWhiteSpace(encounterName)) return null;
        var key = NormalizeEncounterName(encounterName);
        foreach (var (name, health, bossId, durationSeconds) in ThresholdBosses)
        {
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                return (bossId, health, durationSeconds);
        }
        return null;
    }

    /// <summary>Map spec string (from parse) to our class name used in threshold data. Local lookup only.</summary>
    private static string? SpecToClassName(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var s = spec.Trim();
        // Abbreviations FFLogs might return
        var abbr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PLD"] = "Paladin", ["WAR"] = "Warrior", ["DRK"] = "Dark Knight", ["GNB"] = "Gunbreaker",
            ["WHM"] = "White Mage", ["SCH"] = "Scholar", ["AST"] = "Astrologian", ["SGE"] = "Sage",
            ["MNK"] = "Monk", ["DRG"] = "Dragoon", ["NIN"] = "Ninja", ["SAM"] = "Samurai", ["RPR"] = "Reaper", ["VIP"] = "Viper",
            ["BLM"] = "Black Mage", ["RDM"] = "Red Mage", ["SMN"] = "Summoner", ["PCT"] = "Pictomancer",
            ["MCH"] = "Machinist", ["BRD"] = "Bard", ["DNC"] = "Dancer",
        };
        if (abbr.TryGetValue(s, out var full)) return full;
        // Full name or camelCase from API (e.g. "Red Mage" or "RedMage") - check against our list
        var names = new[] { "Paladin", "Gunbreaker", "Dark Knight", "Warrior", "Astrologian", "White Mage", "Scholar", "Sage",
            "Monk", "Dragoon", "Ninja", "Viper", "Samurai", "Reaper", "Black Mage", "Red Mage", "Pictomancer", "Summoner",
            "Machinist", "Bard", "Dancer" };
        foreach (var n in names)
            if (string.Equals(n, s, StringComparison.OrdinalIgnoreCase)) return n;
        // API may return camelCase (e.g. RedMage); match spaced form to our names
        var spaced = SpecToDisplayName(s);
        if (spaced != "—")
            foreach (var n in names)
                if (string.Equals(n, spaced, StringComparison.OrdinalIgnoreCase)) return n;
        return null;
    }

    /// <summary>Expected DPS for this boss + class from local threshold data only. Returns null if no data.</summary>
    private static long? GetExpectedDpsForBossAndClass(int bossId, string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return null;
        if (!ThresholdDataByBoss.TryGetValue(bossId, out var data) || data.ClassDps == null) return null;
        (long Health, int Duration) bossInfo = default;
        foreach (var t in ThresholdBosses)
            if (t.FflogsBossId == bossId) { bossInfo = (t.Health, t.DurationSeconds); break; }
        if (bossInfo.Duration <= 0) return null;
        double? classDps = null;
        foreach (var (name, dps) in data.ClassDps)
            if (string.Equals(name, className.Trim(), StringComparison.OrdinalIgnoreCase)) { classDps = dps; break; }
        if (classDps == null) return null;
        double avgPerSlot = (double)bossInfo.Health / bossInfo.Duration / 8.0;
        double score = data.NormalizedScore(classDps.Value);
        return (long)Math.Round(avgPerSlot * (score / 100.0));
    }

    /// <summary>Role floor DPS for this boss: the lowest 50th % DPS among jobs in the same role as className.</summary>
    private static double? GetRoleFloorDps(int bossId, string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return null;
        if (!ThresholdDataByBoss.TryGetValue(bossId, out var data) || data.ClassDps == null) return null;
        var role = GetRoleForClass(className.Trim());
        if (role == "?") return null;
        double min = double.MaxValue;
        foreach (var (name, dps) in data.ClassDps)
            if (GetRoleForClass(name) == role) min = Math.Min(min, dps);
        return min == double.MaxValue ? null : (double?)min;
    }

    /// <summary>Unscaled "min bar" DPS for true min column: uses realistic worst comp logic. Caster/Phys Ranged = worst in role. Tank/Healer/Melee = worst if this class is worst, else second-worst.</summary>
    private static double? GetRealisticRoleMinDps(int bossId, string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return null;
        if (!ThresholdDataByBoss.TryGetValue(bossId, out var data) || data.ClassDps == null) return null;
        var role = GetRoleForClass(className.Trim());
        if (role == "?") return null;
        var inRole = new List<(string Name, double Dps)>();
        foreach (var (name, dps) in data.ClassDps)
            if (GetRoleForClass(name) == role) inRole.Add((name, dps));
        if (inRole.Count == 0) return null;
        inRole.Sort((a, b) => a.Dps.CompareTo(b.Dps));
        var worst = inRole[0];
        if (role == "Caster" || role == "Phys Ranged")
            return worst.Dps;
        if (string.Equals(className.Trim(), worst.Name, StringComparison.OrdinalIgnoreCase))
            return worst.Dps;
        return inRole.Count >= 2 ? inRole[1].Dps : (double?)worst.Dps;
    }

    private void DrawBossThresholdTab(string name, long health, int bossId, int durationSeconds)
    {
        using (var child = ImRaii.Child($"##{name}", new Vector2(-1, -1), true, ImGuiWindowFlags.None))
        {
            if (!child.Success) return;

            double totalDps = durationSeconds > 0 ? (double)health / durationSeconds : 0;
            int min = durationSeconds / 60, sec = durationSeconds % 60;

            // Boss info header: health, duration, total DPS
            ImGui.Text($"Health: {health:N0}");
            ImGui.SameLine(0, 12);
            ImGui.Text($"Duration: {min}m {sec}s");
            ImGui.SameLine(0, 12);
            ImGui.Text($"Total DPS needed: {totalDps:N0}");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Relative strength table grouped by role
            bool hasData = ThresholdDataByBoss.TryGetValue(bossId, out var data);
            ImGui.Text("Relative strength (normalized by class)");
            ImGui.TextWrapped("The \"worst party\" defines the baseline (100%). Each class is shown as % of that average. Expected DPS = class-specific minimum for this fight.");
            if (hasData && data.ClassDps != null && data.ClassDps.Length > 0)
            {
                double avgDpsPerSlot = durationSeconds > 0 ? (double)health / durationSeconds / 8.0 : 0;
                if (ImGui.BeginTable($"##Rel_{bossId}", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn("Class", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("% of average", ImGuiTableColumnFlags.WidthFixed, 100f);
                    ImGui.TableSetupColumn("Expected DPS", ImGuiTableColumnFlags.WidthFixed, 100f);
                    ImGui.TableHeadersRow();
                    foreach (var role in RoleOrder)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.9f, 1f), role);
                        ImGui.TableSetColumnIndex(1);
                        ImGui.Text("");
                        ImGui.TableSetColumnIndex(2);
                        ImGui.Text("");
                        foreach (var (className, dps) in data.ClassDps)
                        {
                            if (GetRoleForClass(className) != role) continue;
                            double score = data.NormalizedScore(dps);
                            double expectedDps = avgDpsPerSlot * (score / 100.0);
                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.Text($"  {className}");
                            ImGui.TableSetColumnIndex(1);
                            ImGui.Text($"{score:F1}%");
                            ImGui.TableSetColumnIndex(2);
                            ImGui.Text($"{expectedDps:N0}");
                        }
                    }
                    ImGui.EndTable();
                }
            }
            else
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Provide 50th percentile class DPS for this boss to show normalized scores.");
        }
    }

    private void RefreshParsesAsync(List<(string Name, string WorldSlug, string RegionApi)> entries)
    {
        var cfg = _plugin.Configuration;
        var api = new FFLogsApiService(cfg.FflogsClientId ?? "", cfg.FflogsClientSecret ?? "");
        if (!api.HasCredentials) return;

        foreach (var (name, worldSlug, regionApi) in entries)
        {
            var cacheKey = $"{name}|{worldSlug}";

            lock (_parseLock)
            {
                _parseCache[cacheKey] = new ParseCacheEntry { Loading = true };
            }

            _ = Task.Run(async () =>
            {
                var result = await api.GetCharacterRankingsAsync(regionApi, worldSlug, name).ConfigureAwait(false);
                lock (_parseLock)
                {
                    _parseCache[cacheKey] = result.IsSuccess
                        ? new ParseCacheEntry { Parses = result.Parses }
                        : new ParseCacheEntry { Error = result.Error ?? "Unknown error" };
                }
            });
        }
    }

    private sealed class ParseCacheEntry
    {
        public bool Loading;
        public string? Error;
        public List<FFLogsRankingEntry>? Parses;
    }

    private static string GetFflogsRegion(string worldName)
    {
        // FFLogs regions: na, eu, jp, oc
        var name = worldName.ToLowerInvariant();
        if (RegionWorlds.Na.Contains(name)) return "na";
        if (RegionWorlds.Eu.Contains(name)) return "eu";
        if (RegionWorlds.Jp.Contains(name)) return "jp";
        if (RegionWorlds.Oc.Contains(name)) return "oc";
        return "na"; // default
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { /* ignore */ }
    }

    private static class RegionWorlds
    {
        private static readonly string[] NaWorlds =
        {
            "adamantoise", "cactuar", "faerie", "gilgamesh", "jenova", "midgardsormr", "sargatanas", "siren",
            "balmung", "brynhildr", "coeurl", "diabolos", "goblin", "malboro", "mateus", "zalera",
            "halicarnassus", "maduin", "marilith", "seraph"
        };
        private static readonly string[] EuWorlds =
        {
            "cerberus", "louisoix", "moogle", "omega", "phantom", "ragnarok", "sagittarius",
            "alpha", "lich", "odin", "phoenix", "raiden", "shiva", "twintania", "zodiark"
        };
        private static readonly string[] JpWorlds =
        {
            "aegis", "atomos", "carbuncle", "garuda", "gungnir", "kujata", "ramuh", "tonberry", "typhon", "unicorn",
            "alexander", "bahamut", "durandal", "fenrir", "ifrit", "ridill", "tiamat", "ultima", "valefor", "yojimbo", "zeromus",
            "anima", "asura", "chocobo", "hades", "ixion", "masamune", "pandaemonium", "titan", "ultima",
            "belias", "mandragora", "ramuh", "shinryu", "titan", "ultima"
        };
        private static readonly string[] OcWorlds =
        {
            "bismarck", "ravana", "sephirot", "sophia", "zukun"
        };

        internal static readonly HashSet<string> Na = new(NaWorlds);
        internal static readonly HashSet<string> Eu = new(EuWorlds);
        internal static readonly HashSet<string> Jp = new(JpWorlds);
        internal static readonly HashSet<string> Oc = new(OcWorlds);

        internal static string[] GetWorldSlugsForRegion(int regionIndex)
        {
            return regionIndex switch
            {
                0 => NaWorlds,
                1 => EuWorlds,
                2 => JpWorlds,
                3 => OcWorlds,
                _ => NaWorlds
            };
        }
    }
}
