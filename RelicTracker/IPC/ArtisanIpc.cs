using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using ECommons.Reflection;

namespace RelicTracker.IPC;

internal static class ArtisanIpc
{
    private const long BindRetryMs = 60_000;

    /// <summary>
    ///     Matches Artisan <c>RelicToolPremadeLists.RelicToolStep</c> ordinals (1–22).
    ///     IPC is <c>Artisan.GetRelicToolListId(int stepOrdinal, int craftTypeSlot)</c>.
    /// </summary>
    private static readonly Dictionary<string, int> StepOrdinals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Skysteel +1"] = 1,
        ["Dragonsung"] = 2,
        ["Augmented Dragonsung"] = 3,
        ["Skysung"] = 4,
        ["Skybuilders'"] = 5,
        ["Augmented"] = 6,
        ["Crystalline"] = 7,
        ["Chora-Zoi's"] = 8,
        ["Brilliant"] = 9,
        ["Vrandtic"] = 10,
        ["Lodestar"] = 11,
        ["Supra"] = 12,
        ["Lucis"] = 13,
        ["Resplendent A"] = 14,
        ["Resplendent B"] = 15,
        ["Resplendent C"] = 16,
        ["Zodiac"] = 17,
        ["Umbrae"] = 18,
        ["Obscurum"] = 19,
        ["Eclipticum"] = 20,
        ["Anima"] = 21,
        ["Relic"] = 22,
    };

    /// <summary>
    ///     Craft slot for weapon-step lists, which mix crafter classes (Artisan's
    ///     <c>RelicToolPremadeLists.AllClassesSlot</c>). Tool lists use the class slot 0-7.
    /// </summary>
    public const int AllClassesSlot = 8;

    // Resplendent Material B / C item IDs run CRP..CUL, so add the craft slot.
    private const uint ResplendentMaterialB = 33194;
    private const uint ResplendentMaterialC = 33202;

    private static Func<int, int, int>? _getRelicToolListId;
    private static ICallGateSubscriber<int, object>? _startListById;
    private static ICallGateSubscriber<bool>? _isBusy;
    private static bool _ipcBound;
    private static bool _loggedBindFailure;
    private static long _lastBindAttemptTick;

    public static bool IsInstalled =>
        DalamudReflector.TryGetDalamudPlugin("Artisan", out _, false, true)
        || Svc.PluginInterface.InstalledPlugins.Any(plugin => plugin.InternalName == "Artisan");

    public static bool IsEnabled =>
        DalamudReflector.TryGetDalamudPlugin("Artisan", out _, false, true);

    public static bool SupportsRelicToolLists
    {
        get
        {
            EnsureBound();
            return _ipcBound;
        }
    }

    public static void Init() => EnsureBound();

    public static void Dispose()
    {
        _getRelicToolListId = null;
        _startListById = null;
        _isBusy = null;
        _ipcBound = false;
        _loggedBindFailure = false;
        _lastBindAttemptTick = 0;
    }

    /// <summary>
    ///     Artisan list step for a relic step. Resplendent is a craft -> turn-in chain, so it picks the
    ///     component tier from the materials in bags: C, then B, then A.
    /// </summary>
    public static string ArtisanStepFor(string stepName, int craftSlot, Func<uint, uint> bagCount)
    {
        if (!string.Equals(stepName, "Resplendent", StringComparison.OrdinalIgnoreCase) || craftSlot is < 0 or > 7)
        {
            return stepName;
        }

        if (bagCount(ResplendentMaterialC + (uint)craftSlot) >= 2)
        {
            return "Resplendent C";
        }

        return bagCount(ResplendentMaterialB + (uint)craftSlot) >= 2 ? "Resplendent B" : "Resplendent A";
    }

    public static bool TryGetRelicToolListId(string stepName, int craftSlot, out int listId)
    {
        listId = 0;
        if (!StepOrdinals.TryGetValue(stepName, out int stepOrdinal))
        {
            return false;
        }

        if (!SupportsRelicToolLists || _getRelicToolListId == null)
        {
            return false;
        }

        try
        {
            listId = _getRelicToolListId(stepOrdinal, craftSlot);
            return listId != 0;
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] Artisan.GetRelicToolListId failed for {Step} ({Ordinal}) slot {Slot}", stepName, stepOrdinal, craftSlot);
            return false;
        }
    }

    public static bool TryStartRelicToolList(string stepName, int craftSlot, out string? error)
    {
        error = null;
        if (!TryGetRelicToolListId(stepName, craftSlot, out int listId))
        {
            error = "No Artisan premade list for this step.";
            return false;
        }

        if (IsBusy())
        {
            error = "Artisan is already crafting.";
            return false;
        }

        if (_startListById == null)
        {
            error = "Artisan IPC is not ready.";
            return false;
        }

        try
        {
            _startListById.InvokeAction(listId);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool IsBusy()
    {
        if (!SupportsRelicToolLists || _isBusy == null)
        {
            return false;
        }

        try
        {
            return _isBusy.InvokeFunc();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] Artisan.IsBusy failed.");
            return false;
        }
    }

    private static void EnsureBound()
    {
        if (_ipcBound || !IsEnabled)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (_lastBindAttemptTick != 0 && now - _lastBindAttemptTick < BindRetryMs)
        {
            return;
        }

        _lastBindAttemptTick = now;

        try
        {
            _getRelicToolListId ??= BindRelicToolListIdLookup();
            _startListById ??= Svc.PluginInterface.GetIpcSubscriber<int, object>("Artisan.StartListById");
            _isBusy ??= Svc.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");

            if (_getRelicToolListId == null)
            {
                return;
            }

            _ipcBound = true;
            _loggedBindFailure = false;
            Svc.Log.Information("[RelicTracker] Artisan IPC ready.");
        }
        catch (Exception ex)
        {
            if (!_loggedBindFailure)
            {
                Svc.Log.Debug(ex, "[RelicTracker] Artisan IPC not available yet.");
                _loggedBindFailure = true;
            }
        }
    }

    /// <summary>
    ///     Artisan.GetRelicToolListId is (int step, uint craftSlot) -> uint since Artisan's relic list
    ///     refactor, and (int, int) -> int before it. Bind whichever this Artisan build registers.
    /// </summary>
    private static Func<int, int, int>? BindRelicToolListIdLookup()
    {
        var current = Svc.PluginInterface.GetIpcSubscriber<int, uint, uint>("Artisan.GetRelicToolListId");
        if (!current.HasFunction)
        {
            return null;
        }

        try
        {
            _ = current.InvokeFunc(0, 0u);
            return (step, slot) => (int)current.InvokeFunc(step, (uint)slot);
        }
        catch (IpcTypeMismatchError)
        {
            var legacy = Svc.PluginInterface.GetIpcSubscriber<int, int, int>("Artisan.GetRelicToolListId");
            _ = legacy.InvokeFunc(0, 0);
            return legacy.InvokeFunc;
        }
    }
}
