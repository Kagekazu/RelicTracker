using System.Threading;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using RelicTracker.IPC;
namespace RelicTracker;

public sealed class RelicTrackerPlugin(IDalamudPluginInterface pluginInterface) : IAsyncDalamudPlugin
{
    private static readonly string[] CommandNames = ["/relictracker", "/rtracker"];

    private readonly FfxivCollectService ffxivCollect = new();
    private readonly RelicCatalog relicCatalog = new();
    private readonly RelicDataService relicData = new();
    private readonly WindowSystem windowSystem = new("RelicTracker");

    private RelicContextMenu? contextMenu;
    private bool ecommonsInitialized;
    private PluginUI? pluginUi;

    public Configuration Configuration { get; private set; } = null!;

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ECommonsMain.Init(pluginInterface, this);
        ecommonsInitialized = true;

        Configuration = Svc.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(Svc.PluginInterface);

        AllaganToolsIpc.Init();
        ArtisanIpc.Init();

        relicData.Load();
        relicCatalog.Load();
        cancellationToken.ThrowIfCancellationRequested();
        relicCatalog.ResolveJobs();
        ArmorShopResolver.Apply(relicData, relicCatalog);
        cancellationToken.ThrowIfCancellationRequested();

        pluginUi = new(Configuration, relicData, relicCatalog, ffxivCollect);
        windowSystem.AddWindow(pluginUi);
        contextMenu = new RelicContextMenu(pluginUi, new RelicItemNavigationIndex(relicData, relicCatalog));

        Svc.ClientState.Login += pluginUi.OnCharacterChanged;
        Svc.ClientState.Logout += pluginUi.OnCharacterLoggedOut;
        Svc.GameInventory.InventoryChanged += pluginUi.OnInventoryChanged;

        foreach (var commandName in CommandNames)
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
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!ecommonsInitialized)
        {
            return ValueTask.CompletedTask;
        }

        if (pluginUi is not null)
        {
            Svc.ClientState.Login -= pluginUi.OnCharacterChanged;
            Svc.ClientState.Logout -= pluginUi.OnCharacterLoggedOut;
            Svc.GameInventory.InventoryChanged -= pluginUi.OnInventoryChanged;
            Configuration.PersistIfDirty();
        }

        contextMenu?.Dispose();
        foreach (var commandName in CommandNames)
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
        return ValueTask.CompletedTask;
    }

    private void OnCommand(string command, string args) => ToggleUi();

    private void ToggleUi() => pluginUi!.IsOpen = !pluginUi.IsOpen;
}
