using RelicTracker.IPC;
using System.Numerics;
using System.Reflection;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private enum StatusChipKind
    {
        Ok,
        Warn,
        Muted,
    }

    private static readonly Vector4 PanelBg = RelicTheme.PanelBg;
    private static readonly Vector4 PanelBorder = RelicTheme.PanelBorder;
    private static readonly Vector4 ChipOkBg = new(0.18f, 0.38f, 0.24f, 0.95f);
    private static readonly Vector4 ChipWarnBg = new(0.42f, 0.32f, 0.10f, 0.95f);
    private static readonly Vector4 ChipMutedBg = new(0.22f, 0.22f, 0.25f, 0.95f);

    private const float PanelPadX = 12f;
    private const float PanelPadY = 10f;

    private static readonly TimeSpan CollectRefreshInterval = TimeSpan.FromMinutes(10);

    private readonly Stack<float> panelStack = new();

    private bool CollectIdLinked => config.CollectCharacterId != 0;

    private static string Sentence(string text) =>
        text.Length == 0 ? text : string.Concat(char.ToUpperInvariant(text[0]).ToString(), text[1..]);

    private bool CollectActive => CollectIdLinked && ffxivCollect.Snapshot.CharacterId == config.CollectCharacterId;

    private bool ArmorAutoTracked => CollectActive || MogVaultIpc.IsReady;

    private void DrawMogVaultBlocker()
    {
        if (!BeginPanel("mogvault_required"))
        {
            return;
        }

        ImGui.TextColored(HeaderColor, "MogVault is required");
        ImGui.Spacing();

        if (!MogVaultIpc.IsInstalled)
        {
            ImGui.TextWrapped("The Materials page uses MogVault to count items across your inventory.");
            ImGui.TextColored(WarningColor, "Install MogVault from /xlplugins.");
        }
        else if (!MogVaultIpc.IsEnabled)
        {
            ImGui.TextWrapped("MogVault is installed but disabled.");
            ImGui.TextColored(WarningColor, "Enable MogVault in /xlplugins.");
        }
        else
        {
            ImGui.TextWrapped("MogVault is installed but not ready yet.");
            ImGui.TextColored(WarningColor, "Wait for it to finish loading. If this remains, update MogVault.");
        }

        ImGui.Spacing();
        ImGui.TextColored(MutedColor, "The materials list will appear when MogVault is ready.");
        EndPanel();
    }

    private void RefreshCollectIfStale() =>
        ffxivCollect.RefreshIfStale(config.CollectCharacterId, CollectRefreshInterval);

    private bool DrawCatalogLoadError()
    {
        if (catalog.IsLoaded && catalog.Lines.Count > 0)
        {
            return false;
        }

        ImGui.TextColored(WarningColor, "Relic data failed to load. Reload RelicTracker in /xlplugins, or check Dalamud's log.");
        return true;
    }

    private void EndStickyHeader()
    {
        ImGui.Dummy(new Vector2(0, 2));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 6));
    }

    private bool BeginPanel(string id)
    {
        ImGui.PushID(id);
        float width = ImGui.GetContentRegionAvail().X;
        panelStack.Push(width);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(width, PanelPadY));
        ImGui.Indent(PanelPadX);
        return true;
    }

    private void EndPanel()
    {
        float width = panelStack.Pop();
        ImGui.Unindent(PanelPadX);
        ImGui.Dummy(new Vector2(width, PanelPadY));
        ImGui.EndGroup();

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        max.X = min.X + width;

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(PanelBg), 5f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(PanelBorder), 5f);
        drawList.ChannelsMerge();

        ImGui.PopID();
        ImGui.Dummy(new Vector2(0, 8));
    }

    private void DrawStatusChip(string label, StatusChipKind kind)
    {
        Vector4 bg = kind switch
        {
            StatusChipKind.Ok => ChipOkBg,
            StatusChipKind.Warn => ChipWarnBg,
            _ => ChipMutedBg
        };
        Vector4 fg = kind switch
        {
            StatusChipKind.Ok => GoodColor,
            StatusChipKind.Warn => WarningColor,
            _ => MutedColor
        };

        ImGui.PushStyleColor(ImGuiCol.Button, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, bg);
        ImGui.PushStyleColor(ImGuiCol.Text, fg);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 11f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 3f));
        ImGui.SmallButton(label);
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);
    }

    private void TriggerProgressRecheck()
    {
        var collectLinked = config.CollectCharacterId != 0;
        if (!collectLinked && !MogVaultIpc.IsReady)
        {
            return;
        }

        InvalidateOwnershipCache();

        if (collectLinked)
        {
            ffxivCollect.ForceRefresh(config.CollectCharacterId);
        }
    }

    private void DrawProgressRecheckButton()
    {
        if (config.CollectCharacterId == 0 && !MogVaultIpc.IsReady)
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
            var tooltip = config.CollectCharacterId != 0 && MogVaultIpc.IsReady
                ? "Refresh FFXIV Collect and re-read MogVault inventory counts."
                : config.CollectCharacterId != 0
                    ? "Fetch the latest relic progress from FFXIV Collect."
                    : "Re-read owned relic items (and replicas) from MogVault inventory.";
            ImGui.SetTooltip(tooltip);
        }
    }

    private void DrawProgressSourceChip(bool inventoryLinked, bool collectLinked)
    {
        var label = inventoryLinked && collectLinked
            ? "MogVault + Collect"
            : inventoryLinked
                ? "MogVault"
                : "FFXIV Collect";
        DrawStatusChip(label, StatusChipKind.Ok);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"Progress here is read from {(inventoryLinked && collectLinked ? "MogVault inventory and FFXIV Collect" : inventoryLinked ? "MogVault inventory" : "FFXIV Collect")}.");
        }
    }

    private void DrawQuestRewardsSection(
        string configKey,
        IReadOnlyList<ShoppingQuestRewardRow> rewards,
        string header,
        string blurb,
        ImGuiTableFlags tableFlags,
        float ownedColumnWidth,
        bool showResolvedTooltip)
    {
        if (rewards.Count == 0)
        {
            return;
        }

        var ownedCount = rewards.Count(row => row.Owned > 0);
        if (!DrawCollapsingSection(configKey, header, ownedCount > 0))
        {
            return;
        }

        ImGui.TextColored(MutedColor, blurb);
        ImGui.Spacing();

        using var table = ImRaii.Table($"QuestRewards_{configKey}", 2, tableFlags, new(0, 0));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("Reward", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, ownedColumnWidth);
        ImGui.TableHeadersRow();

        foreach (var reward in rewards)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (reward.Resolved)
            {
                ImGui.TextUnformatted(reward.DisplayMaterial);
                if (showResolvedTooltip && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Counts toward component materials for this step when still in inventory.");
                }
            }
            else
            {
                ImGui.TextColored(WarningColor, reward.DisplayMaterial);
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(
                reward.Owned > 0 ? GoodColor : MutedColor,
                reward.Resolved ? reward.Owned.ToString() : "-");
        }

        ImGui.Spacing();
    }

    private sealed record StatusMessage(string Text, bool IsError);

    private void DrawStatusMessage(ref StatusMessage? message, string id)
    {
        if (message is null)
        {
            return;
        }

        ImGui.TextColored(message.IsError ? BadColor : GoodColor, message.Text);
        ImGui.SameLine();
        if (ImGui.SmallButton($"x##{id}"))
        {
            message = null;
        }
    }

    private uint PrimaryItemId(ShoppingMaterialRow row) =>
        data.MaterialIdsByName.TryGetValue(row.Material, out var ids) && ids.Count > 0 ? ids[0] : 0;
    private static readonly Vector4 TipColor = new(0.55f, 0.8f, 0.95f, 1f);

    // Note markup, per line: "1. " and "- " lists, "Tip:" lines, a short line ending in ':' as a heading, blank = spacing.
    private void DrawNote(string note, float wrapPos = 0f)
    {
        const float listIndent = 8f;
        ImGui.PushTextWrapPos(wrapPos);
        foreach (var raw in note.Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                ImGui.Spacing();
                continue;
            }

            if (TrySplitListMarker(line, out var marker, out var rest))
            {
                ImGui.Indent(listIndent);
                ImGui.TextColored(MutedColor, marker);
                ImGui.SameLine();
                ImGui.TextUnformatted(rest);
                ImGui.Unindent(listIndent);
            }
            else if (line.StartsWith("Tip:", StringComparison.Ordinal))
            {
                ImGui.TextColored(TipColor, line);
            }
            else if (line.EndsWith(':') && line.Length <= 40)
            {
                ImGui.Spacing();
                ImGui.TextColored(HeaderColor, line);
            }
            else
            {
                ImGui.TextUnformatted(line);
            }
        }

        ImGui.PopTextWrapPos();
    }

    private static bool TrySplitListMarker(string line, out string marker, out string rest)
    {
        marker = rest = string.Empty;
        if (line.StartsWith("- ", StringComparison.Ordinal))
        {
            marker = "•";
            rest = line[2..];
            return true;
        }

        var dot = line.IndexOf(". ", StringComparison.Ordinal);
        if (dot is > 0 and <= 2 && line[..dot].All(char.IsDigit))
        {
            marker = line[..(dot + 1)];
            rest = line[(dot + 2)..];
            return true;
        }

        return false;
    }

    private void DrawTitleBarVersion(int customTitleBarButtonCount, bool showAdditionalOptionsButton)
    {
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        if (windowSize.X <= 0f || windowSize.Y <= 0f)
        {
            return;
        }

        var text = versionLabel ??= GetVersionLabel();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var textSize = ImGui.CalcTextSize(text);
        var style = ImGui.GetStyle();
        var buttonSize = ImGui.GetFontSize();
        var spacing = style.ItemInnerSpacing.X;

        var numNativeButtons = 1;
        if (style.WindowMenuButtonPosition == ImGuiDir.Right)
        {
            numNativeButtons++;
        }

        var numCustomButtons = customTitleBarButtonCount + (showAdditionalOptionsButton ? 1 : 0);
        var padRight = (numNativeButtons + numCustomButtons) * (buttonSize + spacing);

        Vector2 position = new(
            windowPos.X + windowSize.X - padRight - textSize.X,
            windowPos.Y + style.FramePadding.Y);

        var color = ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.TextDisabled]);
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(windowPos, windowPos + windowSize, false);
        drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), position, color, text);
        drawList.PopClipRect();
    }

    private static string? versionLabel;

    private static string GetVersionLabel()
    {
        var manifestVersion = Svc.PluginInterface.Manifest.AssemblyVersion;
        if (manifestVersion != null)
        {
            return "v" + FormatVersion(manifestVersion);
        }

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        return assemblyVersion != null ? "v" + FormatVersion(assemblyVersion) : "v?.?.?.?";
    }

    private static string FormatVersion(Version version) =>
        version.Revision >= 0 ? version.ToString(4) : version.ToString(3);

}
