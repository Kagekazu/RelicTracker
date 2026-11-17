using RelicTracker.IPC;

namespace RelicTracker;

public sealed partial class PluginUI
{
    private void DrawMaterialsPlan(string expansionId)
    {
        if (DrawCatalogLoadError())
        {
            return;
        }

        string? scope = string.IsNullOrEmpty(config.TrackerLineFilter) ? null : config.TrackerLineFilter;
        string query = materialFilter.Trim();
        using var pane = ImRaii.Child($"##MaterialsPlan_{expansionId}_{scope}", new(0, -1), false);
        if (!pane)
        {
            return;
        }

        var statuses = TrackerStatuses(expansionId, scope);
        Func<uint, uint> ownedLookup = CreateOwnedLookup();
        DrawLucisFirstJobPicker(expansionId, scope);
        if (statuses.Count > 0)
        {
            var title = scope ?? (statuses.Count == 1
                ? statuses[0].Line.CollectType
                : ExpansionNames.IsTools(expansionId) ? "Relic tools" : "Relic weapons");
            DrawWeaponAndToolPlan(expansionId, scope, title, statuses, ownedLookup, query);
        }

        foreach (var armor in catalog.ArmorLinesFor(expansionId).Where(line => scope is null || line.LineName == scope))
        {
            if (statuses.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            DrawArmorPlan(armor, ownedLookup, query);
        }
    }

    private void DrawWeaponAndToolPlan(
        string expansionId,
        string? scope,
        string title,
        IReadOnlyList<RelicLineStatus> statuses,
        Func<uint, uint> ownedLookup,
        string query)
    {
        ImGui.TextColored(HeaderColor, title);
        var materials = MergeByMaterial(GetTrackerShoppingMaterials(expansionId, scope, ownedLookup));
        var visible = materials.Where(row =>
            (!config.HideCompleteMaterials || !row.Resolved || row.Short > 0)
            && (Matches(row.DisplayMaterial, query) || Matches(row.Material, query)
                || Matches(row.Step, query) || row.ForSteps.Any(step => Matches(step, query)))).ToArray();

        var perWeapon = visible.Where(row => !row.OneTime).ToArray();
        var buy = perWeapon.Where(HasBuyOrTradeOption).ToArray();
        var craft = perWeapon.Where(row => row.Obtain == "craft" || row.Craftable).ToArray();
        var gather = perWeapon.Where(row => row.Obtain is "gather" or "fish").ToArray();
        var content = perWeapon.Where(row => !IsBuyOrTrade(row) && row.Obtain is not ("craft" or "gather" or "fish")).ToArray();
        var unlocks = visible.Where(row => row.OneTime).ToArray();
        var activities = GetActivities(expansionId, statuses);
        var rewards = GetPrefarmRewards(expansionId, statuses, ownedLookup)
            .Where(reward => Matches(reward.DisplayMaterial, query) || Matches(reward.Material, query)
                || Matches(reward.CatalogStep, query)).ToArray();
        bool hasShoppingStatus = KnightshopperIpc.ActiveOperation is not null
            || knightshopperSync is not null || knightshopperResult is not null;

        // Quest objectives still matter after all the items for an unlock are in the player's bags.
        var pendingUnlocks = statuses
            .SelectMany(status => Enumerable.Range(0, status.TierCount)
                .Where(tier => status.ReachedPerStep[tier] == 0)
                .Select(tier => (Series: status.Line.CollectType, Step: status.Line.StepName(tier))))
            .Where(task => unlocks.Any(row => row.ForSteps.Contains(task.Step))
                || (Matches(task.Step, query) && UnlockInstructions(task.Series, task.Step).Any()))
            .ToArray();

        if (perWeapon.Length == 0 && pendingUnlocks.Length == 0 && activities.Count == 0
            && rewards.Length == 0 && !hasShoppingStatus)
        {
            ImGui.TextUnformatted(query.Length == 0 ? "No materials or quests left to show." : "No matching materials or quests.");
            return;
        }

        if (!ImGui.BeginTabBar("##MaterialTasks"))
        {
            return;
        }

        try
        {
            if ((buy.Length > 0 || hasShoppingStatus) && ImGui.BeginTabItem("Buy & trade###Buy"))
            {
                DrawKnightshopperBar(buy);
                DrawMaterialTasks(buy, showBuySource: true);
                ImGui.EndTabItem();
            }
            if (craft.Length > 0 && ImGui.BeginTabItem("Craft"))
            {
                if (ExpansionNames.IsTools(expansionId) && ArtisanIpc.SupportsRelicToolLists)
                {
                    ImGui.TextWrapped("Choose a crafter on the tool’s upgrade page to start its Artisan list.");
                }
                else
                {
                    DrawArtisanWeaponListButtons(expansionId, craft);
                }
                DrawMaterialTasks(craft);
                ImGui.EndTabItem();
            }
            if (gather.Length > 0 && ImGui.BeginTabItem("Gather & fish"))
            {
                DrawGatherChillButton(gather);
                DrawMaterialTasks(gather);
                ImGui.EndTabItem();
            }
            if ((content.Length > 0 || activities.Count > 0 || rewards.Length > 0) && ImGui.BeginTabItem("Quests & duties"))
            {
                if (rewards.Length > 0)
                {
                    DrawQuestRewardsSubsection($"{expansionId}|PlanRewards", rewards);
                }
                DrawMaterialTasks(content);
                foreach (var activity in activities)
                {
                    using var id = ImRaii.PushId(activity.Line);
                    using var detail = ImRaii.TreeNode($"{activity.Step} · {Plural(activity.JobsLeft, "job")} left");
                    if (detail)
                    {
                        DrawMaterialActivityChecklist(activity);
                        if (!string.IsNullOrWhiteSpace(activity.Note) && ImGui.CollapsingHeader("Quest details"))
                        {
                            DrawNote(activity.Note);
                        }
                    }
                }
                ImGui.EndTabItem();
            }
            if (pendingUnlocks.Length > 0 && ImGui.BeginTabItem("One-time unlocks"))
            {
                DrawUnlockTasks(expansionId, pendingUnlocks, unlocks);
                ImGui.EndTabItem();
            }
        }
        finally
        {
            ImGui.EndTabBar();
        }
    }

    private void DrawMaterialActivityChecklist(TrackerActivity activity)
    {
        var note = catalog.StepChecklist(activity.Line, activity.Step);
        if (note is not null && note.Contains("[[", StringComparison.Ordinal))
        {
            // Materials covers every class. Let players inspect the variant they need.
            foreach (var (label, slot) in new[] { ("Crafters", 0), ("Miner & Botanist", 8), ("Fisher", 10) })
            {
                using var role = ImRaii.TreeNode(label);
                if (role)
                {
                    DrawRelicChecklist(ChecklistLines(NoteForDiscipline(note, slot)));
                }
            }
        }
        else
        {
            DrawRelicChecklist(ChecklistLines(note));
        }
    }

    private void DrawUnlockTasks(
        string expansionId,
        IReadOnlyList<(string Series, string Step)> pendingUnlocks,
        IReadOnlyList<ShoppingMaterialRow> rows)
    {
        ImGui.TextWrapped("Complete each unlock once to upgrade all jobs.");
        var purchases = rows.Where(row => row.Obtain == "buy" || row.BuyAlternative is not null).ToArray();
        if (purchases.Length > 0)
        {
            DrawKnightshopperBar(purchases, "one-time unlocks");
        }

        foreach (var (series, step) in pendingUnlocks)
        {
            using var id = ImRaii.PushId($"{series}/{step}");
            if (!ImGui.CollapsingHeader(step, ImGuiTreeNodeFlags.DefaultOpen))
            {
                continue;
            }

            foreach (var instruction in UnlockInstructions(series, step))
            {
                ImGui.TextWrapped(instruction);
            }

            var stepRows = rows.Where(row => row.ForSteps.Contains(step)).ToArray();
            var buy = stepRows.Where(HasBuyOrTradeOption).ToArray();
            if (buy.Length > 0)
            {
                ImGui.TextColored(HeaderColor, "Buy & trade");
                DrawMaterialTasks(buy, showBuySource: true);
            }

            var craft = stepRows.Where(row => row.Obtain == "craft" || row.Craftable).ToArray();
            if (craft.Length > 0)
            {
                ImGui.TextColored(HeaderColor, "Craft");
                DrawArtisanWeaponListButtons(expansionId, craft);
                DrawMaterialTasks(craft);
            }

            var gather = stepRows.Where(row => row.Obtain is "gather" or "fish").ToArray();
            if (gather.Length > 0)
            {
                ImGui.TextColored(HeaderColor, "Gather & fish");
                DrawGatherChillButton(gather);
                DrawMaterialTasks(gather);
            }

            var content = stepRows.Where(row => !IsBuyOrTrade(row) && row.Obtain is not ("craft" or "gather" or "fish")).ToArray();
            if (content.Length > 0)
            {
                ImGui.TextColored(HeaderColor, "Collect from content");
                DrawMaterialTasks(content);
            }
        }
    }

    private static bool IsBuyOrTrade(ShoppingMaterialRow row) => row.Obtain is "buy" or "trade";

    private static bool HasBuyOrTradeOption(ShoppingMaterialRow row) => IsBuyOrTrade(row) || row.BuyAlternative is not null;

    private IEnumerable<string> UnlockInstructions(string series, string step) =>
        ChecklistLines(catalog.StepChecklist(series, step))
            .Where(line => line.StartsWith("Once: ", StringComparison.Ordinal))
            .Select(line => line[6..]);

    private void DrawMaterialTasks(IEnumerable<ShoppingMaterialRow> rows, bool showBuySource = false)
    {
        foreach (var row in rows.OrderBy(row => row.Resolved && row.Short == 0))
        {
            var source = data.MaterialSources.TryGetValue(row.Material, out var location)
                ? string.IsNullOrWhiteSpace(row.ObtainVia) || location.Contains(row.ObtainVia, StringComparison.OrdinalIgnoreCase)
                    ? location
                    : $"{location} · {(row.Obtain == "trade" ? row.ObtainLabel : row.ObtainVia)}"
                : row.ObtainLabel;
            if (showBuySource && row.BuyAlternative is { } currency)
            {
                source = VendorAlternativeUnlocked(row) ? $"NPC shop · {currency}" : $"NPC shop · {currency} · Quest unlock required";
            }
            var state = row.Resolved ? row.Short == 0 ? "Ready" : $"{row.Short:N0} left" : $"{row.Need:N0} needed";
            using var id = ImRaii.PushId(row.Material);
            using var detail = DrawMaterialTask(PrimaryItemId(row), row.DisplayMaterial, state, source, row.Resolved && row.Short == 0);
            if (!detail)
            {
                continue;
            }

            ImGui.TextWrapped(row.Resolved
                ? $"Total needed {row.Need:N0} · In inventory {row.OwnedInventory:N0}"
                : "Inventory could not be matched for this item.");
            if (!showBuySource && row.Resolved && row.Obtain is "gather" or "fish" && GatherChillIpc.IsReady)
            {
                DrawGatherChillRowButton(row);
            }
            if (row.OwnedQuestCredit > 0)
            {
                ImGui.TextUnformatted($"Covered by quest rewards {row.OwnedQuestCredit:N0}");
            }
            if (row.ForSteps.Count > 0)
            {
                ImGui.TextWrapped($"Needed for {string.Join(", ", row.ForSteps)}");
            }
            if (row.Purchase is { Unit: > 0 } && PurchaseSummary(row) is { } purchase)
            {
                ImGui.TextWrapped(purchase);
            }
        }
    }

    private void DrawArmorPlan(ArmorLine armor, Func<uint, uint> ownedLookup, string query)
    {
        ImGui.TextColored(HeaderColor, armor.LineName);
        if (!data.ArmorCosts.TryGetValue(armor.Expansion, out var costs))
        {
            ImGui.TextUnformatted("No armour costs are available.");
            return;
        }

        var ownership = GetOwnership();
        using var armourScope = ImRaii.PushId(armor.LineName);
        bool canShop = armor.Expansion == "DT" && armor.LineName == "Phantom Armor";
        if (canShop)
        {
            canShop = !DrawKnightshopperStatus();
        }

        bool any = false;
        foreach (var set in armor.Sets)
        {
            bool drewSet = false;
            foreach (var tier in set.Tiers)
            {
                string tierName = $"{set.Name} {tier.Label}";
                var tierCosts = costs.Where(cost => ArmorCostCalculator.CostAppliesTo(cost, set.Name, tier, null))
                    .Select(cost =>
                    {
                        var (inventory, credit, owned) = ArmorCurrencyOwned(armor.Expansion, cost, ownedLookup);
                        uint need = (uint)Math.Max(0, cost.AllTotal);
                        return (Cost: cost, Currency: ItemDisplayNames.Label(cost.CurrencyIds, cost.Currency),
                            Inventory: inventory, Credit: credit, Remaining: need > owned ? need - owned : 0);
                    })
                    .Where(row => Matches(tierName, query) || Matches(row.Currency, query) || Matches(row.Cost.Set, query))
                    .ToArray();
                var purchases = canShop && (Matches(tierName, query) || tierCosts.Length > 0)
                    ? GetPhantomArmorPurchases(armor, set, tier, ownership, ownedLookup)
                    : [];
                var visibleCosts = tierCosts.Where(row =>
                    !config.HideCompleteMaterials || row.Cost.CurrencyIds.Count == 0 || row.Remaining > 0).ToArray();
                if (visibleCosts.Length == 0 && purchases.Count == 0)
                {
                    continue;
                }

                if (!drewSet)
                {
                    ImGui.Spacing();
                    var heading = set.Name switch
                    {
                        ArmorCostCalculator.ArcanautsSet => $"{set.Name} · South Horn",
                        ArmorCostCalculator.PhantomVisionSet => $"{set.Name} · North Horn",
                        _ => set.Name,
                    };
                    ImGui.TextColored(HeaderColor, heading);
                    drewSet = true;
                }

                any = true;
                int have = ownership.OwnedPieceCount(tier.CollectType, tier.Pieces);
                ImGui.TextUnformatted($"{tier.Label} · {have}/{tier.Pieces} pieces");
                using var tierId = ImRaii.PushId(tier.CollectType);
                if (purchases.Count > 0)
                {
                    ImGui.SameLine();
                    DrawPhantomArmorShoppingButton(set, tier, purchases);
                }
                foreach (var row in visibleCosts)
                {
                    bool resolved = row.Cost.CurrencyIds.Count > 0;
                    var state = resolved ? row.Remaining == 0 ? "Ready" : $"{row.Remaining:N0} left" : "Check inventory";
                    string source = ArmorCostPerPiece(armor.Expansion, row.Cost);
                    using var id = ImRaii.PushId($"{row.Cost.Set}/{row.Cost.Currency}");
                    using var detail = DrawMaterialTask(row.Cost.CurrencyIds.FirstOrDefault(), row.Currency, state, source,
                        resolved && row.Remaining == 0);
                    if (!detail)
                    {
                        continue;
                    }

                    ImGui.TextWrapped(resolved
                        ? $"Total cost {row.Cost.AllTotal:N0} · In inventory {row.Inventory:N0} · Covered by owned pieces {row.Credit:N0}"
                        : $"Total cost {row.Cost.AllTotal:N0} · Inventory count unavailable");
                    ImGui.TextWrapped("The same currency in your bags can be used for multiple tiers. Each tier shows its own cost.");
                    if (!string.IsNullOrWhiteSpace(row.Cost.Note))
                    {
                        ImGui.TextWrapped(row.Cost.Note);
                    }
                }
                ImGui.Spacing();
            }
        }

        if (!any)
        {
            ImGui.TextUnformatted(query.Length == 0 ? "No armour costs left to show." : "No matching armour tiers or currencies.");
        }
    }

    private string ArmorCostPerPiece(string expansionId, ArmorCostRow cost)
    {
        if (ArmorCostCalculator.TryResolveCostTarget(expansionId, cost.Set, catalog, out _, out _, out _, out var slot)
            && slot is not null)
        {
            return $"{cost.PerPiece:N0} per matching piece";
        }

        return ArmorCostCalculator.HasSplitSlotCost(cost)
            ? $"{cost.PerPiece:N0} for body or legs · {ArmorCostCalculator.OtherSlotCost(cost):N0} for other pieces"
            : $"{cost.PerPiece:N0} per piece";
    }

    private static ImRaii.TreeNodeDisposable DrawMaterialTask(uint itemId, string name, string state, string source, bool ready)
    {
        ImGui.Separator();
        DrawSeriesIcon(itemId, ImGui.GetFrameHeight());
        ImGui.SameLine();
        ImGui.TextColored(ready ? GoodColor : ShortColor, state);
        ImGui.SameLine();
        var detail = ImRaii.TreeNode($"{name}###details", ImGuiTreeNodeFlags.SpanAvailWidth);
        if (!string.IsNullOrWhiteSpace(source))
        {
            using var muted = ImRaii.PushColor(ImGuiCol.Text, MutedColor);
            ImGui.TextWrapped(source);
        }
        return detail;
    }
}
