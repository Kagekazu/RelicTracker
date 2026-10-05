using RelicTracker.IPC;
using static ECommons.GenericHelpers;
namespace RelicTracker;

public sealed partial class PluginUI
{
    private string collectCharacterIdInput = string.Empty;
    private bool collectInputInitialized;

    private void DrawSettingsTab()
    {
        if (BeginPanel("settings_intro"))
        {
            ImGui.TextColored(MutedColor, "Install Allagan Tools for owned counts (bags, retainers, dresser, armoire - including replicas).");
            ImGui.TextColored(MutedColor, "Relic = per-job steps and notes. Tracker = farm totals. Progress is saved per character.");
            EndPanel();
        }

        DrawAllaganToolsSettingsSection();
        DrawArtisanSettingsSection();
        DrawKnightshopperSettingsSection();
        DrawGatherChillSettingsSection();

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
                    + "finished on Overview, Relic, and Tracker.");
            }

            EndPanel();
        }

        if (BeginPanel("settings_collect"))
        {
            ImGui.TextColored(HeaderColor, "FFXIV Collect (optional)");
            ImGui.SameLine();
            if (config.FfxivCollectCharacterId != 0)
            {
                DrawStatusChip(ffxivCollect.IsLoading ? "Syncing…" : "Linked", ffxivCollect.IsLoading ? StatusChipKind.Warn : StatusChipKind.Ok);
            }
            else
            {
                DrawStatusChip("Off", StatusChipKind.Muted);
            }

            ImGui.TextColored(
                MutedColor,
                "Only needed if you finished relics but no longer have the items in inventory (sold, desynthed, etc.). "
                + "Allagan Tools already covers relics and replicas you still own.");
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

        ImGui.TextColored(MutedColor, "Read-only profile sync - use when relics are no longer in your inventory.");
        ImGui.Spacing();

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
        using (ImRaii.Disabled(config.FfxivCollectCharacterId == 0 && !AllaganToolsIpc.IsReady))
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
                "Find your character ID in the URL on ffxivcollect.com when viewing your profile, e.g. ffxivcollect.com/characters/123456");
        }
    }

    private void DrawAllaganToolsSettingsSection()
    {
        if (!BeginPanel("settings_at"))
        {
            return;
        }

        ImGui.TextColored(HeaderColor, "Allagan Tools");
        ImGui.TextColored(MutedColor, "Used for inventory counts, owned relic detection, and material tracking.");
        ImGui.Spacing();
        DrawPluginConnectionStatus(
            "Allagan Tools",
            AllaganToolsIpc.IsInstalled,
            AllaganToolsIpc.IsEnabled,
            AllaganToolsIpc.IsReady);
        EndPanel();
    }

    private void DrawArtisanSettingsSection()
    {
        if (!BeginPanel("settings_artisan"))
        {
            return;
        }

        ImGui.TextColored(HeaderColor, "Artisan (optional)");
        ImGui.TextColored(
            MutedColor,
            "Start premade crafting lists for DoH relic-tool steps (precrafts + collectables). Buy scrip materials first.");
        ImGui.Spacing();
        DrawPluginConnectionStatus(
            "Artisan",
            ArtisanIpc.IsInstalled,
            ArtisanIpc.IsEnabled,
            ArtisanIpc.SupportsRelicToolLists,
            optional: true,
            notReadyMessage: "Artisan found - relic craft lists need a newer Artisan build.");

        if (ArtisanIpc.SupportsRelicToolLists && ArtisanIpc.IsBusy())
        {
            ImGui.SameLine();
            ImGui.TextColored(WarningColor, "(crafting)");
        }

        EndPanel();
    }

    private void DrawKnightshopperSettingsSection()
    {
        if (!BeginPanel("settings_knightshopper"))
        {
            return;
        }

        ImGui.TextColored(HeaderColor, "Knightshopper (optional)");
        ImGui.TextColored(
            MutedColor,
            "Buy missing materials sold for gil, tomestones, scrips, seals, and other currencies from the Tracker tab.");
        ImGui.TextColored(
            MutedColor,
            "In Knightshopper, enable both IPC permission switches and add RelicTracker to the allowed plugins.");
        ImGui.Spacing();
        DrawPluginConnectionStatus(
            "Knightshopper",
            KnightshopperIpc.IsInstalled,
            KnightshopperIpc.IsEnabled,
            KnightshopperIpc.IsReady,
            optional: true,
            notReadyMessage: "Knightshopper found - buying item lists needs a newer Knightshopper build.");

        if (KnightshopperIpc.IsReady && KnightshopperIpc.IsBusy())
        {
            ImGui.SameLine();
            ImGui.TextColored(WarningColor, "(shopping)");
        }

        EndPanel();
    }

    private void DrawGatherChillSettingsSection()
    {
        if (!BeginPanel("settings_gatherchill"))
        {
            return;
        }

        ImGui.TextColored(HeaderColor, "Gather & Chill (optional)");
        ImGui.TextColored(MutedColor, "Add missing gathered and fished materials to its gather list from the Tracker tab.");
        ImGui.Spacing();
        DrawPluginConnectionStatus(
            "Gather & Chill",
            GatherChillIpc.IsInstalled,
            GatherChillIpc.IsEnabled,
            GatherChillIpc.IsReady,
            optional: true,
            notReadyMessage: "Gather & Chill found - adding to its gather list needs a newer build.");

        if (GatherChillIpc.IsReady && GatherChillIpc.IsBusy())
        {
            ImGui.SameLine();
            ImGui.TextColored(WarningColor, "(gathering)");
        }

        EndPanel();
    }
}
