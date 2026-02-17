using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GamerCheck.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _configuration;
    private string _clientIdInput = "";
    private string _clientSecretInput = "";
    private bool _inputsInitialized;

    public ConfigWindow(Plugin plugin) : base("GamerCheck Settings###GamerCheckConfig")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;
        Size = new Vector2(420, 220);
        SizeCondition = ImGuiCond.Always;
        _configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        if (_configuration.IsConfigWindowMovable)
            Flags &= ~ImGuiWindowFlags.NoMove;
        else
            Flags |= ImGuiWindowFlags.NoMove;
    }

    public override void Draw()
    {
        if (!_inputsInitialized)
        {
            _clientIdInput = _configuration.FflogsClientId ?? "";
            _clientSecretInput = _configuration.FflogsClientSecret ?? "";
            _inputsInitialized = true;
        }

        var openOnJoin = _configuration.OpenWindowWhenPartyMemberJoins;
        if (ImGui.Checkbox("Open GamerCheck when someone joins the party", ref openOnJoin))
        {
            _configuration.OpenWindowWhenPartyMemberJoins = openOnJoin;
            _configuration.Save();
        }

        var movable = _configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable settings window", ref movable))
        {
            _configuration.IsConfigWindowMovable = movable;
            _configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("FFLogs API (for parse data)");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("Client ID", "a118cb39-...", ref _clientIdInput, 64))
        {
            _configuration.FflogsClientId = _clientIdInput?.Trim() ?? "";
            _configuration.Save();
        }
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("Client Secret (Confidential client)", "optional", ref _clientSecretInput, 128, ImGuiInputTextFlags.Password))
        {
            _configuration.FflogsClientSecret = _clientSecretInput ?? "";
            _configuration.Save();
        }
        ImGui.TextWrapped("Parse data uses the public API with Client Credentials. If your app is Public (PKCE only), create a Confidential client at FFLogs and paste its Client ID and Secret here.");
    }
}
