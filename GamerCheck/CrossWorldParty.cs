using System.Collections.Generic;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace GamerCheck;

/// <summary>
/// Reads cross-world party from game (like TomestoneViewer's TeamList) when IPartyList is empty.
/// Requires FFXIVClientStructs reference (e.g. from Dalamud dev folder).
/// </summary>
public static class CrossWorldParty
{
    /// <summary>Get party members from cross-realm party when not in a duty. Returns null if not in cross-realm party or on error.</summary>
    public static unsafe List<(string Name, uint WorldRowId, string CurrentClass)>? GetCrossWorldPartyMembers()
    {
        try
        {
            var infoModule = InfoModule.Instance();
            if (infoModule == null) return null;
            var proxy = infoModule->GetInfoProxyById(InfoProxyId.CrossRealmParty);
            if (proxy == null) return null;
            var crossRealm = (InfoProxyCrossRealm*)proxy;
            if (!crossRealm->IsInCrossRealmParty)
                return null;

            var count = InfoProxyCrossRealm.GetPartyMemberCount();
            if (count == 0)
                return null;

            var list = new List<(string Name, uint WorldRowId, string CurrentClass)>(count);
            for (uint i = 0; i < count; i++)
            {
                var m = InfoProxyCrossRealm.GetGroupMember(i, -1);
                if (m == null)
                    continue;

                var name = ReadMemberName(m);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var worldId = (ushort)(m->HomeWorld & 0xFFFF);
                var jobId = (uint)m->ClassJobId;
                var currentClass = ClassJobIdToDisplayName(jobId);
                list.Add((name.Trim(), worldId, currentClass));
            }

            return list.Count > 0 ? list : null;
        }
        catch
        {
            return null;
        }
    }

    private static unsafe string ReadMemberName(CrossRealmMember* m)
    {
        if (m == null) return "";
        // _name is at offset 0x33, FixedSizeArray32 (null-terminated string)
        var ptr = (byte*)m + 0x33;
        var end = ptr + 32;
        var len = 0;
        while (ptr + len < end && *(ptr + len) != 0)
            len++;
        return len > 0 ? Encoding.UTF8.GetString(ptr, len) : "";
    }

    /// <summary>Cross-realm struct uses game job ID (different from ClassJob sheet RowId). Same scheme as TomestoneViewer JobId.</summary>
    private static string ClassJobIdToDisplayName(uint jobId)
    {
        return jobId switch
        {
            19 => "Paladin", 20 => "Monk", 21 => "Warrior", 22 => "Dragoon", 23 => "Bard",
            24 => "White Mage", 25 => "Black Mage", 26 => "Arcanist", 27 => "Summoner", 28 => "Scholar",
            29 => "Rogue", 30 => "Ninja", 31 => "Machinist", 32 => "Dark Knight", 33 => "Astrologian",
            34 => "Samurai", 35 => "Red Mage", 36 => "Blue Mage", 37 => "Gunbreaker", 38 => "Dancer",
            39 => "Reaper", 40 => "Sage", 41 => "Viper", 42 => "Pictomancer",
            _ => "—"
        };
    }
}
