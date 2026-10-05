using Dalamud.Plugin.Ipc;
using ECommons.Reflection;

namespace RelicTracker.IPC;

/// <summary>Knightshopper currency IDs (IPC API v2).</summary>
internal enum KnightshopperCurrency
{
    BicolorGemstone = 0,
    CompanySeal = 1,
    Gil = 2,
    Hunt = 3,
    MGP = 4,
    PVP = 5,
    Scrip = 6,
    Tomestone = 7,
    Firmament = 8,
    Cosmocredits = 9,
    OccultCrescent = 10,
}

internal enum KnightshopperStartResult
{
    Started = 0,
    Busy = 1,
    InvalidCurrency = 2,
    EmptyList = 3,
    NotReady = 4,
    NotLoggedIn = 5,
    NotAllowed = 6,
    ItemUnavailable = 7,
    InvalidQuantity = 8,
}

internal enum KnightshopperPurchaseState
{
    Unknown = 0,
    Running = 1,
    CancellationRequested = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}

internal readonly record struct KnightshopperStatus(
    KnightshopperPurchaseState State,
    int CurrentIndex,
    int TotalItems,
    uint CurrentItemId,
    string Message)
{
    public bool IsFinished =>
        State is KnightshopperPurchaseState.Succeeded or KnightshopperPurchaseState.Failed or KnightshopperPurchaseState.Cancelled;
}

/// <summary>
///     Client for Knightshopper's <c>Purchase.StartItems</c> IPC (API v2). See
///     <c>installedPlugins/Knightshopper/&lt;version&gt;/docs/IPC.md</c>.
/// </summary>
internal static class KnightshopperIpc
{
    private const int RequiredApiVersion = 2;
    private const long BindRetryMs = 60_000;

    private static ICallGateSubscriber<int>? _apiVersion;
    private static ICallGateSubscriber<bool>? _isBusy;
    private static ICallGateSubscriber<int, (uint ItemId, uint Quantity)[], (int, Guid, string)>? _startItems;
    private static ICallGateSubscriber<Guid, (int, int, int, uint, string)>? _getStatus;
    private static ICallGateSubscriber<Guid, bool>? _cancel;
    private static bool _ipcBound;
    private static bool _loggedBindFailure;
    private static long _lastBindAttemptTick;

    /// <summary>The operation RelicTracker started most recently, if any.</summary>
    public static Guid? ActiveOperation { get; private set; }

    public static KnightshopperCurrency? ActiveCurrency { get; private set; }

    public static bool IsInstalled =>
        DalamudReflector.TryGetDalamudPlugin("Knightshopper", out _, false, true)
        || Svc.PluginInterface.InstalledPlugins.Any(plugin => plugin.InternalName == "Knightshopper");

    public static bool IsEnabled =>
        DalamudReflector.TryGetDalamudPlugin("Knightshopper", out _, false, true);

    /// <summary>True when Knightshopper is loaded and supports buying a supplied item list.</summary>
    public static bool IsReady
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
        _apiVersion = null;
        _isBusy = null;
        _startItems = null;
        _getStatus = null;
        _cancel = null;
        _ipcBound = false;
        _loggedBindFailure = false;
        _lastBindAttemptTick = 0;
        ActiveOperation = null;
        ActiveCurrency = null;
    }

    public static bool IsBusy()
    {
        if (!IsReady || _isBusy == null)
        {
            return false;
        }

        try
        {
            return _isBusy.InvokeFunc();
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] Knightshopper.IsBusy failed.");
            return false;
        }
    }

    /// <summary>
    ///     Starts a purchase. Quantities are target inventory totals, not amounts to buy.
    /// </summary>
    public static bool TryStartItems(
        KnightshopperCurrency currency,
        (uint ItemId, uint Quantity)[] items,
        out string message)
    {
        if (!IsReady || _startItems == null)
        {
            message = "Knightshopper isn't loaded, or is too old (needs IPC API v2).";
            return false;
        }

        try
        {
            (int result, Guid operationId, string text) = _startItems.InvokeFunc((int)currency, items);
            var startResult = (KnightshopperStartResult)result;
            if (startResult != KnightshopperStartResult.Started)
            {
                message = DescribeStartFailure(startResult, text);
                return false;
            }

            ActiveOperation = operationId;
            ActiveCurrency = currency;
            message = text;
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[RelicTracker] Knightshopper.Purchase.StartItems failed.");
            message = ex.Message;
            return false;
        }
    }

    public static KnightshopperStatus? GetActiveStatus()
    {
        if (ActiveOperation is not { } operationId || !IsReady || _getStatus == null)
        {
            return null;
        }

        try
        {
            (int state, int index, int total, uint itemId, string text) = _getStatus.InvokeFunc(operationId);
            return new((KnightshopperPurchaseState)state, index, total, itemId, text);
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] Knightshopper.Purchase.GetStatus failed.");
            return null;
        }
    }

    public static bool CancelActive()
    {
        if (ActiveOperation is not { } operationId || !IsReady || _cancel == null)
        {
            return false;
        }

        try
        {
            return _cancel.InvokeFunc(operationId);
        }
        catch (Exception ex)
        {
            Svc.Log.Debug(ex, "[RelicTracker] Knightshopper.Purchase.Cancel failed.");
            return false;
        }
    }

    public static void ClearActive()
    {
        ActiveOperation = null;
        ActiveCurrency = null;
    }

    /// <summary>Maps a material's <c>obtainVia</c> currency (bundled data) to a Knightshopper currency.</summary>
    public static bool TryParseCurrency(string? obtainVia, out KnightshopperCurrency currency)
    {
        KnightshopperCurrency? parsed = obtainVia switch
        {
            "Gil" => KnightshopperCurrency.Gil,
            "Tomestones" => KnightshopperCurrency.Tomestone,
            "Scrips" => KnightshopperCurrency.Scrip,
            "GC seals" => KnightshopperCurrency.CompanySeal,
            "Bicolor Gemstones" => KnightshopperCurrency.BicolorGemstone,
            "Skybuilders' Scrips" => KnightshopperCurrency.Firmament,
            "Hunt seals" => KnightshopperCurrency.Hunt,
            "Cosmocredits" => KnightshopperCurrency.Cosmocredits,
            "MGP" => KnightshopperCurrency.MGP,
            "PvP" => KnightshopperCurrency.PVP,
            _ => null,
        };

        currency = parsed ?? default;
        return parsed.HasValue;
    }

    public static string CurrencyLabel(KnightshopperCurrency currency) => currency switch
    {
        KnightshopperCurrency.BicolorGemstone => "Bicolor Gemstones",
        KnightshopperCurrency.CompanySeal => "Grand Company seals",
        KnightshopperCurrency.Hunt => "Hunt seals",
        KnightshopperCurrency.PVP => "PvP currency",
        KnightshopperCurrency.Scrip => "Scrips",
        KnightshopperCurrency.Tomestone => "Tomestones",
        KnightshopperCurrency.Firmament => "Skybuilders' Scrips",
        KnightshopperCurrency.OccultCrescent => "Occult Crescent",
        _ => currency.ToString(),
    };

    private static string DescribeStartFailure(KnightshopperStartResult result, string text)
    {
        var reason = result switch
        {
            KnightshopperStartResult.Busy => "Knightshopper is already busy.",
            KnightshopperStartResult.NotAllowed =>
                "Knightshopper blocked the request. Enable its IPC permissions and add RelicTracker to its allowed plugins.",
            KnightshopperStartResult.ItemUnavailable => "Knightshopper can't buy one of these items with this currency.",
            KnightshopperStartResult.NotLoggedIn => "Not logged in.",
            KnightshopperStartResult.EmptyList => "Nothing to buy.",
            _ => $"Knightshopper did not start ({result}).",
        };

        return string.IsNullOrWhiteSpace(text) ? reason : $"{reason}\n{text}";
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
            _apiVersion ??= Svc.PluginInterface.GetIpcSubscriber<int>("Knightshopper.ApiVersion");
            _isBusy ??= Svc.PluginInterface.GetIpcSubscriber<bool>("Knightshopper.IsBusy");
            _startItems ??= Svc.PluginInterface.GetIpcSubscriber<int, (uint ItemId, uint Quantity)[], (int, Guid, string)>(
                "Knightshopper.Purchase.StartItems");
            _getStatus ??= Svc.PluginInterface.GetIpcSubscriber<Guid, (int, int, int, uint, string)>("Knightshopper.Purchase.GetStatus");
            _cancel ??= Svc.PluginInterface.GetIpcSubscriber<Guid, bool>("Knightshopper.Purchase.Cancel");

            if (!_apiVersion.HasFunction || _apiVersion.InvokeFunc() < RequiredApiVersion)
            {
                return;
            }

            _ipcBound = true;
            _loggedBindFailure = false;
            Svc.Log.Information("[RelicTracker] Knightshopper IPC ready.");
        }
        catch (Exception ex)
        {
            if (!_loggedBindFailure)
            {
                Svc.Log.Debug(ex, "[RelicTracker] Knightshopper IPC not available yet.");
                _loggedBindFailure = true;
            }
        }
    }
}
