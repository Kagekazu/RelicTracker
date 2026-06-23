using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using RelicTracker.Framework;
using RelicTracker.IPC;
using System.Diagnostics;
using System.Numerics;

namespace RelicTracker;

public sealed class RelicTrackerPlugin : IDalamudPlugin
{
    private readonly Configuration configuration;
    private readonly ItemResolver itemResolver = new();
    private readonly PluginUI pluginUi;
    private readonly RelicDataService relicData = new();
    private readonly WindowSystem windowSystem = new("RelicTracker");

    public RelicTrackerPlugin(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this);

        configuration = Svc.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Initialize(Svc.PluginInterface);

        AllaganToolsIpc.Initialize(Svc.PluginInterface);

        relicData.Load();
        itemResolver.Build();

        pluginUi = new PluginUI(configuration, relicData, itemResolver);
        windowSystem.AddWindow(pluginUi);

        Svc.Commands.AddHandler(RelicTrackerConstants.CommandName, new(OnCommand)
        {
            HelpMessage = "Open RelicTracker",
        });

        Svc.PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += ToggleUi;
        Svc.PluginInterface.UiBuilder.OpenMainUi += ToggleUi;

        Svc.Log.Information("Loaded {Name} (data {Version}).", Svc.PluginInterface.Manifest.Name, relicData.Manifest.SheetVersion);
    }

    public Configuration Configuration => configuration;

    public void Dispose()
    {
        configuration.PersistIfDirty();
        Svc.Commands.RemoveHandler(RelicTrackerConstants.CommandName);
        Svc.PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= ToggleUi;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= ToggleUi;
        windowSystem.RemoveAllWindows();
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        ToggleUi();
    }

    private void ToggleUi()
    {
        pluginUi.IsOpen = !pluginUi.IsOpen;
    }
}
