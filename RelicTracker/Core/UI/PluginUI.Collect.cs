using RelicTracker.IPC;
using static ECommons.GenericHelpers;
namespace RelicTracker;

public sealed partial class PluginUI
{
    private string collectCharacterIdInput = string.Empty;
    private bool collectInputInitialized;

    private void DrawCollectSection()
    {
        if (!collectInputInitialized)
        {
            collectCharacterIdInput = config.FfxivCollectCharacterId == 0
                ? string.Empty
                : config.FfxivCollectCharacterId.ToString();
            collectInputInitialized = true;
        }

        ffxivCollect.RefreshIfStale(config.FfxivCollectCharacterId, TimeSpan.FromMinutes(10));

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

    private void TriggerProgressRecheck()
    {
        var collectLinked = config.FfxivCollectCharacterId != 0;
        if (!collectLinked && !AllaganToolsIpc.IsReady)
        {
            return;
        }

        InvalidateOwnershipCache();

        if (collectLinked)
        {
            ffxivCollect.ForceRefresh(config.FfxivCollectCharacterId);
        }
    }

    private void DrawProgressRecheckButton()
    {
        if (config.FfxivCollectCharacterId == 0 && !AllaganToolsIpc.IsReady)
        {
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button("Recheck"))
        {
            TriggerProgressRecheck();
        }

        if (ImGui.IsItemHovered())
        {
            var tooltip = config.FfxivCollectCharacterId != 0 && AllaganToolsIpc.IsReady
                ? "Refresh FFXIV Collect and re-read Allagan Tools inventory counts."
                : config.FfxivCollectCharacterId != 0
                    ? "Fetch the latest relic progress from FFXIV Collect."
                    : "Re-read owned relic items (and replicas) from Allagan Tools inventory.";
            ImGui.SetTooltip(tooltip);
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

    private void DrawArtisanCraftButton(RelicLine line, string stepName, int slotIndex)
    {
        if (!string.Equals(line.Expansion, "DoHDoL", StringComparison.Ordinal) || slotIndex > 7)
        {
            return;
        }

        if (!ArtisanIpc.TryGetRelicToolListId(stepName, slotIndex, out _))
        {
            return;
        }

        using (ImRaii.Disabled(ArtisanIpc.IsBusy()))
        {
            if (ImGui.Button("Craft with Artisan"))
            {
                if (ArtisanIpc.TryStartRelicToolList(stepName, slotIndex, out string? error))
                {
                    Svc.Log.Information("[RelicTracker] Started Artisan list for {Step}.", stepName);
                }
                else if (!string.IsNullOrWhiteSpace(error))
                {
                    Svc.Log.Warning("[RelicTracker] {Error}", error);
                }
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(
                "Starts Artisan's premade list for this step (precrafts and collectables).\n"
                + "Scrip vendor mats (Select / Oddly Specific) are not included — buy those first.");
        }
    }
}
