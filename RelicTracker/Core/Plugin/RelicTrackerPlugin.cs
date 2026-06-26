using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using RelicTracker.IPC;
namespace RelicTracker;

public sealed class RelicTrackerPlugin : IDalamudPlugin
{
    private readonly FfxivCollectService ffxivCollect = new();
    private readonly ItemResolver itemResolver = new();
    private readonly PluginUI pluginUi;
    private readonly RelicCatalog relicCatalog = new();
    private readonly RelicDataService relicData = new();
    private readonly WindowSystem windowSystem = new("RelicTracker");

    public RelicTrackerPlugin(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this);

        Configuration = Svc.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(Svc.PluginInterface);

        AllaganToolsIpc.Init();

        relicData.Load();
        relicCatalog.Load();
        itemResolver.Build();
        relicCatalog.ResolveJobs(itemResolver);

        pluginUi = new(Configuration, relicData, relicCatalog, itemResolver, ffxivCollect);
        windowSystem.AddWindow(pluginUi);

        foreach(string commandName in RelicTrackerConstants.CommandNames)
        {
            Svc.Commands.AddHandler(commandName, new(OnCommand)
            {
                HelpMessage = "Open RelicTracker"
            });
        }

        Svc.PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += ToggleUi;
        Svc.PluginInterface.UiBuilder.OpenMainUi += ToggleUi;

        Svc.Log.Information("Loaded {Name} (data {Version}).", Svc.PluginInterface.Manifest.Name, relicData.Manifest.SheetVersion);
    }

    public Configuration Configuration { get; }

    public void Dispose()
    {
        Configuration.PersistIfDirty();
        foreach(string commandName in RelicTrackerConstants.CommandNames)
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
