using RelicTracker.IPC;
using static ECommons.GenericHelpers;
namespace RelicTracker;

public sealed partial class PluginUI
{
    private string collectCharacterIdInput = string.Empty;
    private bool collectInputInitialized;

    private void DrawSettingsTab()
    {
        ImGui.TextColored(MutedColor, "Relic = per-job steps and notes. Tracker = farm totals. Progress is saved per character.");
        ImGui.Spacing();

        DrawIntegrationsSection();

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

            using (ImRaii.PushColor(ImGuiCol.Text, MutedColor))
            {
                ImGui.TextWrapped(
                    "Only needed if you finished relics but no longer have the items in inventory (sold, desynthed, etc.). "
                    + "Allagan Tools already covers relics and replicas you still own.");
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

    private readonly record struct Integration(
        string Name,
        string UsedFor,
        string? Setup,
        bool Optional,
        bool Installed,
        bool Enabled,
        bool Ready,
        string? BusyLabel);

    private void DrawIntegrationsSection()
    {
        if (!BeginPanel("settings_integrations"))
        {
            return;
        }

        ImGui.TextColored(HeaderColor, "Integrations");
        ImGui.Spacing();

        Integration[] integrations =
        [
            new("Allagan Tools", "Recommended. Owned counts (bags, retainers, dresser, armoire) and relic detection - without it, only your bags are counted.", null, false,
                AllaganToolsIpc.IsInstalled, AllaganToolsIpc.IsEnabled, AllaganToolsIpc.IsReady, null),
            new("Artisan", "Crafts relic-tool turn-ins from the Relic tab.", null, true,
                ArtisanIpc.IsInstalled, ArtisanIpc.IsEnabled, ArtisanIpc.SupportsRelicToolLists,
                ArtisanIpc.SupportsRelicToolLists && ArtisanIpc.IsBusy() ? "crafting" : null),
            new("Knightshopper", "Buys missing materials from the Tracker's Buy section.",
                "In Knightshopper, enable both IPC permission switches and add RelicTracker to the allowed plugins.", true,
                KnightshopperIpc.IsInstalled, KnightshopperIpc.IsEnabled, KnightshopperIpc.IsReady,
                KnightshopperIpc.IsReady && KnightshopperIpc.IsBusy() ? "shopping" : null),
            new("Gather & Chill", "Adds missing gatherables to its gather list from the Tracker.", null, true,
                GatherChillIpc.IsInstalled, GatherChillIpc.IsEnabled, GatherChillIpc.IsReady,
                GatherChillIpc.IsReady && GatherChillIpc.IsBusy() ? "gathering" : null),
        ];

        // Gather & Chill isn't released yet: only list it for people who have a build installed.
        DrawIntegrationsTable([.. integrations.Where(integration =>
            integration.Name != "Gather & Chill" || integration.Installed)]);
        EndPanel();
    }

    private void DrawIntegrationsTable(IReadOnlyList<Integration> integrations)
    {
        using var table = ImRaii.Table("Integrations", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV, new(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 130);
        ImGui.TableSetupColumn("Used for", ImGuiTableColumnFlags.WidthStretch);

        foreach (var integration in integrations)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(integration.Name);

            ImGui.TableNextColumn();
            (string label, StatusChipKind kind, string tooltip) = (integration.Installed, integration.Enabled, integration.Ready) switch
            {
                (false, _, _) => ("Not installed", integration.Optional ? StatusChipKind.Muted : StatusChipKind.Warn,
                    $"{integration.Name} is not installed."),
                (_, false, _) => ("Disabled", StatusChipKind.Warn, $"{integration.Name} is installed but not enabled."),
                (_, _, false) => ("Needs update", StatusChipKind.Warn,
                    $"{integration.Name} is loaded, but this build doesn't have the IPC RelicTracker uses yet."),
                _ => (integration.BusyLabel is { } busy ? $"Connected · {busy}" : "Connected", StatusChipKind.Ok,
                    $"{integration.Name} connected."),
            };
            DrawStatusChip(label, kind);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(tooltip);
            }

            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, MutedColor))
            {
                ImGui.TextWrapped(integration.UsedFor);
                if (integration.Setup is { } setup)
                {
                    ImGui.TextWrapped(setup);
                }
            }
        }
    }
}
