using System;
using System.Diagnostics;
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
        Size = new Vector2(460, 340);
        SizeCondition = ImGuiCond.Always;
        _configuration = plugin.Configuration;
    }

    public void Dispose() { }

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

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("FFLogs API (for parse data)");
        ImGui.TextWrapped("To show in-game parses, create a Confidential client at FFLogs and paste its Client ID and Secret below.");
        ImGui.Spacing();
        const string fflogsClientsUrl = "https://www.fflogs.com/api/clients/";
        ImGui.Text("1. Open ");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.6f, 1f, 1f));
        ImGui.Text("FFLogs API clients");
        if (ImGui.IsItemClicked())
            OpenUrl(fflogsClientsUrl);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.Text(" (click to open).");
        ImGui.TextWrapped("2. Create a new client and choose \"Confidential\" as the client type. For the application name and redirect/callback URL, you can enter anything (e.g. \"Application  Name\" and https://google.com).");
        ImGui.TextWrapped("3. Copy the Client ID and Client Secret into the fields below.");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("Client ID", "paste from FFLogs", ref _clientIdInput, 64))
        {
            _configuration.FflogsClientId = _clientIdInput?.Trim() ?? "";
            _configuration.Save();
        }
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("Client Secret", "paste from FFLogs", ref _clientSecretInput, 128, ImGuiInputTextFlags.Password))
        {
            _configuration.FflogsClientSecret = _clientSecretInput ?? "";
            _configuration.Save();
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }
}
