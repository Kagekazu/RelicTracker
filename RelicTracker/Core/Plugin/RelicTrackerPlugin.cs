using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using RelicTracker.IPC;
namespace RelicTracker;

public sealed class RelicTrackerPlugin : IDalamudPlugin
{
    private readonly RelicContextMenu contextMenu;
    private readonly FfxivCollectService ffxivCollect = new();
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
        ArtisanIpc.Init();

        relicData.Load();
        relicCatalog.Load();
        relicCatalog.ResolveJobs();

        pluginUi = new(Configuration, relicData, relicCatalog, ffxivCollect);
        windowSystem.AddWindow(pluginUi);
        contextMenu = new RelicContextMenu(pluginUi, new RelicItemNavigationIndex(relicData, relicCatalog));

        Svc.ClientState.Login += pluginUi.OnCharacterChanged;
        Svc.ClientState.Logout += pluginUi.OnCharacterLoggedOut;
        Svc.GameInventory.InventoryChanged += pluginUi.OnInventoryChanged;

        foreach (var commandName in RelicTrackerConstants.CommandNames)
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
        Svc.ClientState.Login -= pluginUi.OnCharacterChanged;
        Svc.ClientState.Logout -= pluginUi.OnCharacterLoggedOut;
        Svc.GameInventory.InventoryChanged -= pluginUi.OnInventoryChanged;
        contextMenu.Dispose();
        Configuration.PersistIfDirty();
        foreach (var commandName in RelicTrackerConstants.CommandNames)
        {
            Svc.Commands.RemoveHandler(commandName);
        }
        Svc.PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= ToggleUi;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= ToggleUi;
        windowSystem.RemoveAllWindows();
        AllaganToolsIpc.Dispose();
        ArtisanIpc.Dispose();
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args) => ToggleUi();

    private void ToggleUi() => pluginUi.IsOpen = !pluginUi.IsOpen;
}
