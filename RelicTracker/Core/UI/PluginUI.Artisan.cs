using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
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
