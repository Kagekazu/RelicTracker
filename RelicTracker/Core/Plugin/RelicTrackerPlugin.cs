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
    private readonly FfxivCollectService ffxivCollect = new();
    private readonly PluginUI pluginUi;
    private readonly RelicDataService relicData = new();
    private readonly WindowSystem windowSystem = new("RelicTracker");

    public RelicTrackerPlugin(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this);

        configuration = Svc.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Initialize(Svc.PluginInterface);

        AllaganToolsIpc.Init();

        relicData.Load();
        itemResolver.Build();

        pluginUi = new PluginUI(configuration, relicData, itemResolver, ffxivCollect);
        windowSystem.AddWindow(pluginUi);

        foreach (var commandName in RelicTrackerConstants.CommandNames)
        {
            Svc.Commands.AddHandler(commandName, new(OnCommand)
            {
                HelpMessage = "Open RelicTracker",
            });
        }

        Svc.PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += ToggleUi;
        Svc.PluginInterface.UiBuilder.OpenMainUi += ToggleUi;

        Svc.Log.Information("Loaded {Name} (data {Version}).", Svc.PluginInterface.Manifest.Name, relicData.Manifest.SheetVersion);
    }

    public Configuration Configuration => configuration;

    public void Dispose()
    {
        configuration.PersistIfDirty();
        foreach (var commandName in RelicTrackerConstants.CommandNames)
        {
            Svc.Commands.RemoveHandler(commandName);
        }
        Svc.PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= ToggleUi;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= ToggleUi;
        windowSystem.RemoveAllWindows();
        AllaganToolsIpc.Dispose();
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
