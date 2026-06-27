using Terraria;
using TShockAPI;


namespace TShockData
{
    public static class ItemRestrict
    {
        private static bool _initialized = false;

        public static void Initialize()
        {
            if (_initialized) return;
            GetDataHandlers.ItemDrop.Register(OnItemDrop);
            _initialized = true;
            TShock.Log.ConsoleInfo("[TSWeb] 物品限制功能已启用 (丢出检测)");
        }

        public static void Dispose()
        {
            if (!_initialized) return;
            GetDataHandlers.ItemDrop.UnRegister(OnItemDrop);
            _initialized = false;
        }

        private static void OnItemDrop(object? sender, GetDataHandlers.ItemDropEventArgs e)
        {
            if (e.Player == null)
                return;

            // 日志输出
            TShock.Log.ConsoleInfo($"[TSWeb] 玩家丢出物品: {e.Player.Name}, ID={e.ID}, Type={e.Type}, Stacks={e.Stacks}, Pos=({e.Position.X:F1},{e.Position.Y:F1})");

            // 对接现有物品限制审查器
            var matchedItems = ItemDetection.CheckItem(e.Player, e.Type, e.Stacks);
            if (matchedItems.Count > 0)
            {
                foreach (var matchedItem in matchedItems)
                {
                    // 阻止物品掉落
                    e.Handled = true;
                    Main.item[e.ID].TurnToAir();
                    NetMessage.SendData(21, -1, -1, null, e.ID, 0, 0);

                    TShock.Log.ConsoleError($"[TSWeb] 阻止丢出违禁物品! 玩家: {e.Player.Name}, 物品ID: {e.Type}, 数量: {e.Stacks}, 限制: {matchedItem.Stack}, 处理: {matchedItem.Method}");

                    ViolationExecutor.ExecuteViolation(e.Player, matchedItem.Method, 
                        playerName: e.Player.Name, 
                        itemId: e.Type, 
                        itemName: AntiCheat.GetItemName(e.Type));
                }
            }
        }
    }
}
