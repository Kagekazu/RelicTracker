using RelicTracker.IPC;
using static ECommons.GenericHelpers;
namespace RelicTracker;

public sealed partial class PluginUI
{
    private string collectCharacterIdInput = string.Empty;
    private bool collectInputInitialized;

    private void DrawSettingsTab()
    {
        ImGui.TextColored(HeaderColor, "Inventory");
        DrawIntegration(new("MogVault",
            "Counts relics and materials in your bags, retainers, dresser, and armoire.",
            "Wait for MogVault to finish loading, or update it in /xlplugins.",
            false, MogVaultIpc.IsInstalled, MogVaultIpc.IsEnabled, MogVaultIpc.IsReady, null));

        ImGui.Spacing();
        ImGui.TextColored(HeaderColor, "Optional progress source");
        DrawCollectSettings();

        ImGui.Spacing();
        ImGui.TextColored(HeaderColor, "Optional helpers");
        DrawOptionalHelpers();

        ImGui.Spacing();
        DrawDisplaySettings();
    }

    private void DrawDisplaySettings()
    {
        if (BeginPanel("settings_display"))
        {
            ImGui.TextColored(HeaderColor, "Display");
            ImGui.Spacing();
            var hidePhyseos = config.HidePhyseosRelics;
            if (ImGui.Checkbox("Hide Physeos (Eureka Weapons)", ref hidePhyseos))
            {
                config.HidePhyseosRelics = hidePhyseos;
                config.OnSettingChanged();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Physeos is the Baldesion Arsenal upgrade after Eureka. Same look/stats outside Eureka, "
                    + "and it does not count as a new relic for achievements. When enabled, Eureka counts as "
                    + "finished in the catalogue, upgrade path, and materials list.");
            }

            EndPanel();
        }
    }

    private void DrawCollectSettings()
    {
        if (BeginPanel("settings_collect"))
        {
            ImGui.TextColored(HeaderColor, "FFXIV Collect");
            ImGui.SameLine();
            if (config.FfxivCollectCharacterId != 0)
            {
                DrawStatusChip(ffxivCollect.IsLoading ? "Syncing…" : "Linked", ffxivCollect.IsLoading ? StatusChipKind.Warn : StatusChipKind.Ok);
            }
            else
            {
                DrawStatusChip("Off", StatusChipKind.Muted);
            }

            using (ImRaii.PushColor(ImGuiCol.Text, MutedColor))
            {
                ImGui.TextWrapped(
                    "Only needed if you finished relics but no longer have the items in inventory (sold, desynthed, etc.). "
                    + "MogVault already covers relics and replicas you still own. Linking a profile lets RelicTracker fetch its public relic lists.");
            }

            ImGui.Spacing();
            DrawCollectSection();
            EndPanel();
        }
    }

    private void DrawCollectSection()
    {
        if (!collectInputInitialized)
        {
            collectCharacterIdInput = config.FfxivCollectCharacterId == 0
                ? string.Empty
                : config.FfxivCollectCharacterId.ToString();
            collectInputInitialized = true;
        }

        RefreshCollectIfStale();

        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##collectCharacterId", "Character ID", ref collectCharacterIdInput, 32);

        ImGui.SameLine();
        if (ImGui.Button("Save ID"))
        {
            if (ulong.TryParse(collectCharacterIdInput.Trim(), out var parsed) && parsed > 0)
            {
                config.FfxivCollectCharacterId = parsed;
                config.OnSettingChanged();
                InvalidateOwnershipCache();
                ffxivCollect.Refresh(parsed);
            }
            else
            {
                config.FfxivCollectCharacterId = 0;
                config.OnSettingChanged();
                InvalidateOwnershipCache();
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(config.FfxivCollectCharacterId == 0 && !MogVaultIpc.IsReady))
        {
            if (ImGui.Button("Recheck"))
            {
                TriggerProgressRecheck();
            }
        }

        if (config.FfxivCollectCharacterId > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Open profile"))
            {
                ShellStart($"https://ffxivcollect.com/characters/{config.FfxivCollectCharacterId}");
            }
        }

        if (ffxivCollect.IsLoading)
        {
            ImGui.TextColored(MutedColor, "Loading…");
        }
        else if (!string.IsNullOrWhiteSpace(ffxivCollect.StatusMessage))
        {
            ImGui.TextColored(WarningColor, ffxivCollect.StatusMessage);
        }
        else if (ffxivCollect.LastRefreshUtc is DateTime refreshed)
        {
            ImGui.TextColored(
                GoodColor,
                $"Owned {ffxivCollect.Snapshot.Owned.Count} · Missing {ffxivCollect.Snapshot.Missing.Count} · Updated {refreshed.ToLocalTime():t}");
        }

        if (config.FfxivCollectCharacterId == 0)
        {
            ImGui.Spacing();
            ImGui.TextWrapped(
                "Find your character ID in the URL on ffxivcollect.com when viewing your public profile, e.g. ffxivcollect.com/characters/123456. "
                + "Saving it lets RelicTracker request that profile's relic lists.");
        }
    }

    private readonly record struct Integration(
        string Name,
        string UsedFor,
        string? Setup,
        bool Optional,
        bool Installed,
        bool Enabled,
        bool Ready,
        string? BusyLabel);

    private void DrawOptionalHelpers()
    {
        bool artisanReady = ArtisanIpc.SupportsRelicToolLists;
        DrawIntegration(new("Artisan", "Crafts relic tool turn-ins from the upgrade page.",
            "Update Artisan to a version that supports relic tool craft lists.", true,
            ArtisanIpc.IsInstalled, ArtisanIpc.IsEnabled, artisanReady,
            artisanReady && ArtisanIpc.IsBusy() ? "Crafting" : null));

        bool knightshopperReady = KnightshopperIpc.IsReady;
        DrawIntegration(new("Knightshopper",
            "Save a list to review in Knightshopper, or buy missing materials right away. In Knightshopper Settings, open IPC, turn on both purchase options, and add RelicTracker to the allowed plugins.",
            "Wait for Knightshopper to finish loading, or update it in /xlplugins.", true,
            KnightshopperIpc.IsInstalled, KnightshopperIpc.IsEnabled, knightshopperReady,
            knightshopperReady && KnightshopperIpc.IsBusy() ? "Shopping" : null));

        // Gather & Chill isn't released yet: only list it for people who have a build installed.
        if (GatherChillIpc.IsInstalled)
        {
            bool gatherChillReady = GatherChillIpc.IsReady;
            DrawIntegration(new("Gather & Chill", "Adds missing gatherables to its list from the Materials page.",
                "Wait for Gather & Chill to finish loading, or update it in /xlplugins.", true,
                true, GatherChillIpc.IsEnabled, gatherChillReady,
                gatherChillReady && GatherChillIpc.IsBusy() ? "Gathering" : null));
        }
    }

    private void DrawIntegration(Integration integration)
    {
        if (!BeginPanel($"settings_{integration.Name}"))
        {
            return;
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(HeaderColor, integration.Name);
        ImGui.SameLine();

        (string label, StatusChipKind kind, string? recovery) = (integration.Installed, integration.Enabled, integration.Ready) switch
        {
            (false, _, _) => ("Not installed", integration.Optional ? StatusChipKind.Muted : StatusChipKind.Warn,
                $"Install {integration.Name} in /xlplugins."),
            (_, false, _) => ("Disabled", StatusChipKind.Warn, $"Enable {integration.Name} in /xlplugins."),
            (_, _, false) => ("Not ready", StatusChipKind.Warn, integration.Setup),
            _ => (integration.BusyLabel ?? "Ready", StatusChipKind.Ok, null),
        };
        DrawStatusChip(label, kind);

        using (ImRaii.PushColor(ImGuiCol.Text, MutedColor))
        {
            ImGui.TextWrapped(integration.UsedFor);
        }

        if (recovery is not null)
        {
            ImGui.TextWrapped(recovery);
        }

        EndPanel();
    }
}
