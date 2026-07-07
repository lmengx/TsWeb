using Terraria;
using TShockAPI;
using TShockAPI.DB;

namespace MapManager.Commands;

public class PruneCommand : HCommand
{
    private readonly int time;

    public PruneCommand(int time, TSPlayer sender)
        : base(sender)
    {
        this.time = time;
    }

    public override void Execute()
    {
        var time = (int) (DateTime.UtcNow - MapManager.Date).TotalSeconds - this.time;
        MapManager.Database.Query("DELETE FROM History WHERE Time < @0 AND WorldID = @1", time, Main.worldID);
        MapManager.Actions.RemoveAll(a => a.time < time);
        this.sender.SendSuccessMessage("历史记录已被删除.");
    }
}