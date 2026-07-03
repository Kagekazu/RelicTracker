using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private void DrawArtisanSettingsSection()
    {
        ImGui.TextColored(HeaderColor, "Artisan (optional)");
        ImGui.TextColored(
            MutedColor,
            "Start premade crafting lists for DoH relic-tool steps (precrafts + collectables). Buy scrip materials first.");
        ImGui.Spacing();

        if (!ArtisanIpc.IsInstalled)
        {
            ImGui.TextColored(MutedColor, "Artisan is not installed.");
            return;
        }

        if (!ArtisanIpc.IsEnabled)
        {
            ImGui.TextColored(WarningColor, "Artisan is installed but not enabled.");
            return;
        }

        if (!ArtisanIpc.IsReady)
        {
            ImGui.TextColored(WarningColor, "Artisan is loading…");
            return;
        }

        ImGui.TextColored(GoodColor, "Artisan connected");
        if (ArtisanIpc.IsBusy())
        {
            ImGui.SameLine();
            ImGui.TextColored(WarningColor, "(crafting)");
        }
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
