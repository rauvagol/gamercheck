using Dalamud.Configuration;
using System;

namespace GamerCheck;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool OpenWindowWhenPartyMemberJoins { get; set; } = true;

    /// <summary>FFLogs API client ID (from https://www.fflogs.com/api/clients/).</summary>
    public string FflogsClientId { get; set; } = "a118cb39-9969-4eb4-a585-8330ed756907";

    /// <summary>FFLogs API client secret. Required for fetching parses. Use a Confidential client.</summary>
    public string FflogsClientSecret { get; set; } = "";

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
