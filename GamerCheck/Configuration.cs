using Dalamud.Configuration;
using System;

namespace GamerCheck;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool OpenWindowWhenPartyMemberJoins { get; set; } = true;

    /// <summary>FFLogs API client ID (from https://www.fflogs.com/api/clients/).</summary>
    public string FflogsClientId { get; set; } = "";

    /// <summary>FFLogs API client secret. Required for fetching parses. Use a Confidential client.</summary>
    public string FflogsClientSecret { get; set; } = "";

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
