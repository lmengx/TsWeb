using TShockAPI;

namespace MapManager.Commands;

public abstract class HCommand
{
    protected TSPlayer sender;

    public HCommand(TSPlayer sender)
    {
        this.sender = sender;
    }

    public void Error(string msg)
    {
        this.sender?.SendErrorMessage(msg);
    }

    public abstract void Execute();
}