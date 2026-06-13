using NUnit.Framework;
using TShockAPI;
using TShockAPI.DB;

namespace TShockLauncher.Tests;

public class GroupTests
{
	/// <summary>
	/// This tests to ensure the group commands work.
	/// </summary>
	/// <remarks>Due to the switch to Microsoft.Data.Sqlite, nulls have to be replaced with DBNull for the query to complete</remarks>
	[TestCase]
	public void TestPermissions()
	{
		var groups = TShock.Groups = new GroupManager(TShock.DB);

		if (!groups.GroupExists("test"))
			groups.AddGroup("test", null, "test", Group.defaultChatColor);

		groups.AddPermissions("test", new() { "abc" });

		var hasperm = groups.GetGroupByName("test").Permissions.Contains("abc");
		Assert.That(hasperm, Is.True);

		groups.DeletePermissions("test", new() { "abc" });

		hasperm = groups.GetGroupByName("test").Permissions.Contains("abc");
		Assert.That(hasperm, Is.False);

		groups.DeleteGroup("test");

		var g = groups.GetGroupByName("test");
		Assert.That(g, Is.Null);
	}
}

