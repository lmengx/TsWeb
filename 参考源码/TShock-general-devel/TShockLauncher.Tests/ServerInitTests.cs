using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Threading;

namespace TShockLauncher.Tests;

public class ServerInitTests
{
	/// <summary>
	/// This test will ensure that the TSAPI binary boots up as expected
	/// </summary>
	[TestCase]
	public void EnsureBoots()
	{
		var are = new AutoResetEvent(false);
		HookEvents.HookDelegate<Terraria.Main, HookEvents.Terraria.Main.DedServEventArgs> cb = (instance, args) =>
		{
			args.ContinueExecution = false;
			are.Set();
			Debug.WriteLine("Server init process successful");
		};
		HookEvents.Terraria.Main.DedServ += cb;

		new Thread(() => TerrariaApi.Server.Program.Main([])).Start();

		var hit = are.WaitOne(TimeSpan.FromSeconds(10));

		HookEvents.Terraria.Main.DedServ -= cb;

		Assert.That(hit, Is.True);
	}
}

