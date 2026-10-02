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
            ImGui.TextColored(MutedColor, "Install Allagan Tools for owned counts (bags, retainers, dresser, armoire — including replicas).");
            ImGui.TextColored(MutedColor, "Relic = per-job steps and notes. Tracker = farm totals. Progress is saved per character.");
            EndPanel();
        }

        DrawAllaganToolsSettingsSection();
        DrawArtisanSettingsSection();

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

        ImGui.TextColored(MutedColor, "Read-only profile sync — use when relics are no longer in your inventory.");
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
            ArtisanIpc.SupportsRelicToolLists);

        if (ArtisanIpc.SupportsRelicToolLists && ArtisanIpc.IsBusy())
        {
            ImGui.SameLine();
            ImGui.TextColored(WarningColor, "(crafting)");
        }

        EndPanel();
    }
}
