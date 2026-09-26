using Terraria;
using Terraria.GameContent;

namespace InvJudge;

/// <summary>
/// 单个配方的判定信息（从 Main.recipe 构建的只读快照）。
/// </summary>
public sealed class CraftRecipeInfo
{
    /// <summary>Main.recipe 中的下标</summary>
    public int RecipeIndex;

    /// <summary>产物物品类型</summary>
    public int ResultType;

    /// <summary>单次合成产物数量（createItem.stack）</summary>
    public int ResultStack;

    /// <summary>需求清单（type, 数量）。type 可能为 -1 表示组需求（见 RequirementGroupId）</summary>
    public List<(int Type, int Stack)> Requirements = new();

    /// <summary>对应需求的组 id（-1 = 具体物品）</summary>
    public List<int> RequirementGroupId = new();

    /// <summary>该配方是否在微光中可分解（产物可被微光还原为材料）</summary>
    public bool ShimmerDecraftable;

    /// <summary>配方是否标记 notDecraftable（不可分解）</summary>
    public bool NotDecraftable;

    /// <summary>产物名称（缓存便于日志）</summary>
    public string ResultName = "";

    /// <summary>需求物品名称缓存（便于日志）</summary>
    public List<string> RequirementNames = new();

    public override string ToString()
    {
        var reqs = new List<string>();
        for (int i = 0; i < Requirements.Count; i++)
        {
            var (t, s) = Requirements[i];
            reqs.Add($"{s}x{RequirementNames[i]}");
        }
        return $"[{RecipeIndex}] {ResultName} = {string.Join(" + ", reqs)} (产物{ResultStack}){(ShimmerDecraftable ? " [微光可分解]" : "")}";
    }
}

/// <summary>
/// 配方索引：服务端启动时从 Main.recipe 构建，供合成校验快速查找。
/// 同时构建「微光可分解物品」集合（微光还原分解配方重点监控对象）。
/// </summary>
public static class RecipeIndex
{
    /// <summary>按产物物品类型 → 可产出该物品的配方列表</summary>
    private static Dictionary<int, List<CraftRecipeInfo>> _byResult = new();

    /// <summary>物品类型 → 微光分解配方（GetDecraftingRecipeIndex），-1 = 不可分解</summary>
    private static Dictionary<int, int> _decraftIndex = new();

    /// <summary>产物为微光可分解物品的配方（严格模式监控对象）</summary>
    private static HashSet<int> _shimmerDecraftableItems = new();

    /// <summary>全部配方（含不可用/禁用，用于解释性匹配）</summary>
    private static List<CraftRecipeInfo> _all = new();

    private static bool _built;

    public static IReadOnlyDictionary<int, List<CraftRecipeInfo>> ByResult => _byResult;
    public static IReadOnlyList<CraftRecipeInfo> All => _all;
    public static IReadOnlyDictionary<int, int> DecraftIndex => _decraftIndex;

    public static bool IsShimmerDecraftableItem(int itemType)
        => _shimmerDecraftableItems.Contains(itemType);

    /// <summary>
    /// 构建索引。在服务器世界加载完成（Main.recipe 填充完毕）后调用。
    /// </summary>
    public static void Build()
    {
        _byResult.Clear();
        _decraftIndex.Clear();
        _shimmerDecraftableItems.Clear();
        _all.Clear();

        var recipes = Main.recipe;
        int count = recipes?.Length ?? 0;
        for (int i = 0; i < count; i++)
        {
            Recipe r = recipes![i];
            if (r == null || r.createItem == null || r.createItem.type <= 0)
                continue;

            var info = new CraftRecipeInfo
            {
                RecipeIndex = i,
                ResultType = r.createItem.type,
                ResultStack = Math.Max(1, r.createItem.stack),
                NotDecraftable = r.notDecraftable,
                ResultName = ItemName(r.createItem.type),
            };

            // 需求清单（含组需求）
            for (int j = 0; j < r.requiredItem.Length; j++)
            {
                var reqItem = r.requiredItem[j];
                if (reqItem == null || reqItem.type <= 0 || reqItem.stack <= 0)
                    continue;

                // requiredItemQuickLookup 带组信息（Matches 处理 RecipeGroup）
                int groupId = -1;
                if (j < r.requiredItemQuickLookup.Length)
                {
                    var entry = r.requiredItemQuickLookup[j];
                    if (entry.IsRecipeGroup)
                        groupId = entry.itemIdOrRecipeGroup; // FakeItemIdOffset 段
                }

                info.Requirements.Add((reqItem.type, reqItem.stack));
                info.RequirementGroupId.Add(groupId);
                info.RequirementNames.Add(ItemName(reqItem.type));
            }

            // 微光可分解判定（原版服务端 ShimmerTransforms）
            int decraftIdx = SafeGetDecraftIndex(r.createItem.type);
            info.ShimmerDecraftable = decraftIdx >= 0;

            _all.Add(info);

            if (!_byResult.TryGetValue(info.ResultType, out var list))
            {
                list = new List<CraftRecipeInfo>();
                _byResult[info.ResultType] = list;
            }
            list.Add(info);
        }

        // 微光可分解物品集合
        foreach (var kv in _decraftIndex)
        {
            if (kv.Value >= 0)
                _shimmerDecraftableItems.Add(kv.Key);
        }

        _built = true;
        TShockAPI.TShock.Log.ConsoleInfo(
            $"[InvJudge] 配方索引构建完成: 共 {_all.Count} 条配方, {_byResult.Count} 种产物, 微光可分解物品 {_shimmerDecraftableItems.Count} 种");
    }

    public static bool IsBuilt => _built;

    private static int SafeGetDecraftIndex(int itemType)
    {
        if (_decraftIndex.TryGetValue(itemType, out int cached))
            return cached;

        int result;
        try
        {
            result = ShimmerTransforms.GetDecraftingRecipeIndex(itemType);
        }
        catch
        {
            result = -1;
        }
        _decraftIndex[itemType] = result;
        return result;
    }

    /// <summary>物品类型 → 名称（带缓存，减少 Lang 查询开销）</summary>
    private static readonly Dictionary<int, string> _nameCache = new();
    public static string ItemName(int type)
    {
        if (type <= 0) return "空";
        if (_nameCache.TryGetValue(type, out var name)) return name;
        try
        {
            name = Terraria.Lang.GetItemNameValue(type);
        }
        catch
        {
            name = type.ToString();
        }
        if (string.IsNullOrWhiteSpace(name)) name = type.ToString();
        _nameCache[type] = name;
        return name;
    }

    /// <summary>
    /// 用消耗的材料集合 + 产物，尝试匹配一个配方，返回匹配信息（含按扣材推算的合成次数与按产物推算的次数）。
    /// consumed: 物品类型 → 净消耗数量（正数）
    /// produced: 物品类型 → 净产出数量（正数）
    /// </summary>
    public static List<CraftMatch> MatchCraft(Dictionary<int, int> consumed, Dictionary<int, int> produced)
    {
        var results = new List<CraftMatch>();

        foreach (var (resultType, producedCount) in produced)
        {
            if (!_byResult.TryGetValue(resultType, out var candidates))
                continue;

            foreach (var info in candidates)
            {
                // 产物数量必须达到至少 1 次
                int craftByProduced = producedCount / info.ResultStack;
                if (craftByProduced <= 0)
                    continue;

                // 检查扣材是否满足该配方需求
                int craftByConsumed = int.MaxValue;
                bool allReqsMet = true;
                foreach (var (reqType, reqStack) in info.Requirements)
                {
                    int available = GetConsumedForRequirement(consumed, info, reqType);
                    if (available <= 0)
                    {
                        allReqsMet = false;
                        break;
                    }
                    int times = available / reqStack;
                    if (times < craftByConsumed)
                        craftByConsumed = times;
                }

                if (!allReqsMet || craftByConsumed <= 0)
                    continue;

                results.Add(new CraftMatch
                {
                    Info = info,
                    CraftByConsumed = craftByConsumed,
                    CraftByProduced = craftByProduced,
                    ProducedCount = producedCount,
                });
            }
        }

        return results;
    }

    /// <summary>
    /// 取某需求在消耗集合中的可匹配数量（处理组需求：组内任意成员物品的消耗都计入）。
    /// </summary>
    private static int GetConsumedForRequirement(Dictionary<int, int> consumed, CraftRecipeInfo info, int reqType)
    {
        // 找到该需求对应的组 id
        int idx = info.Requirements.FindIndex(r => r.Type == reqType);
        int groupId = idx >= 0 ? info.RequirementGroupId[idx] : -1;

        if (groupId < 0)
        {
            consumed.TryGetValue(reqType, out int c);
            return c;
        }

        // 组需求：累加组内所有成员物品的消耗
        int total = 0;
        try
        {
            var group = Terraria.RecipeGroup.recipeGroups[groupId - Terraria.RecipeGroup.FakeItemIdOffset];
            foreach (int memberType in group.ValidItems)
            {
                if (consumed.TryGetValue(memberType, out int c))
                    total += c;
            }
        }
        catch
        {
            consumed.TryGetValue(reqType, out total);
        }
        return total;
    }
}

/// <summary>一次配方匹配结果</summary>
public sealed class CraftMatch
{
    public CraftRecipeInfo Info = null!;

    /// <summary>按消耗材料推算的合成次数（扣材 / 单次需求）</summary>
    public int CraftByConsumed;

    /// <summary>按产物数量推算的合成次数（产量 / 单次产物）</summary>
    public int CraftByProduced;

    /// <summary>实际产物数量</summary>
    public int ProducedCount;

    /// <summary>产量 / 扣材支撑量 的倍率（>1 说明产物超出扣材应有的量）</summary>
    public double Ratio => CraftByConsumed > 0 ? (double)CraftByProduced / CraftByConsumed : double.PositiveInfinity;
}
