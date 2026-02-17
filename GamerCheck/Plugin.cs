using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Game.Gui.ContextMenu;
using GamerCheck.Windows;

namespace GamerCheck;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/gamercheck";
    private const string CommandNameShort = "/gc";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("GamerCheck");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private int _lastPartyCount;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open GamerCheck window with FFLogs links for your party"
        });
        CommandManager.AddHandler(CommandNameShort, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open GamerCheck window"
        });

        // Open window when someone joins the party
        _lastPartyCount = PartyList.Length;
        Framework.Update += OnFrameworkUpdate;

        // Add "GamerCheck" to party member context menu
        ContextMenu.OnMenuOpened += OnContextMenuOpened;

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("GamerCheck loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        Framework.Update -= OnFrameworkUpdate;
        ContextMenu.OnMenuOpened -= OnContextMenuOpened;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandNameShort);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!Configuration.OpenWindowWhenPartyMemberJoins)
            return;

        var current = PartyList.Length;
        if (current > _lastPartyCount && _lastPartyCount > 0)
        {
            MainWindow.IsOpen = true;
        }
        _lastPartyCount = current;
    }

    private void OnContextMenuOpened(IMenuOpenedArgs args)
    {
        // Only add when right-clicking a party member (Default menu with target in our party)
        if (args.MenuType != ContextMenuType.Default || args.Target is not MenuTargetDefault defaultTarget)
            return;

        var contentId = defaultTarget.TargetContentId;
        var inParty = false;
        for (var i = 0; i < PartyList.Length; i++)
        {
            var member = PartyList[i];
            if (member != null && (ulong)member.ContentId == contentId)
            {
                inParty = true;
                break;
            }
        }
        if (!inParty) return;

        args.AddMenuItem(new MenuItem
        {
            Name = "GamerCheck",
            OnClicked = _ => MainWindow.IsOpen = true
        });
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();

    /// <summary>Push the icon font for drawing FontAwesome icons. Dispose the return value to pop.</summary>
    public IDisposable PushIconFont() => PluginInterface.UiBuilder.IconFontHandle.Push();
}
