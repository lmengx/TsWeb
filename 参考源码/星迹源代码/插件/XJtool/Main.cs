using Terraria;
using TerrariaApi.Server;
using System.Diagnostics;
using System.Reflection;
using System.Collections.ObjectModel;
using Rests;
using TShockAPI;

namespace XJtool
{
	[ApiVersion(2, 1)]
	public class XJtool : TerrariaPlugin
	{
		public override string Author => "Jonesn";
		public override string Description => "星迹工具箱";
		public override string Name => "XJtool";
		public override Version Version => new Version(1, 0, 1, 1);
		public XJtool(Main game) : base(game) { }
		public override void Initialize()
		{
			Data.Init();
			TShock.RestApi.Register(new SecureRestCommand("/xjtool/uninstall", XJtoolUninstall, "XJtool"));//删除插件
			TShock.RestApi.Register(new SecureRestCommand("/xjtool/listall", XJtoolList, "XJtool"));//列出插件
			TShock.RestApi.Register(new SecureRestCommand("/xjtool/install", XJtoolInstall, "XJtool"));//添加插件
			LoadPlugins();
		}
		protected override void Dispose(bool Disposing)
		{
			if (Disposing) { }
			base.Dispose(Disposing);
		}
		private static Main game;
		private static readonly List<PluginContainer> plugins = new List<PluginContainer>();
		public static ReadOnlyCollection<PluginContainer> Plugins
		{
			get { return new ReadOnlyCollection<PluginContainer>(plugins); }
		}
		private object XJtoolUninstall(RestRequestArgs args)//删除插件
		{
			Data.DelPlugin(args.Request.Parameters["PluginName"]);
			return new RestObject();
		}
		private object XJtoolList(RestRequestArgs args)//列出插件
		{
			return new RestObject()
				{
					{
						"Plugins",
						Data.GetAllData()
					}
				};
		}
		private object XJtoolInstall(RestRequestArgs args)//添加插件
		{
			string[] plugin = new string[1] {args.Request.Parameters["PluginName"]};
			if (Data.Insert(plugin[0]))
			{
				LoadP(plugin);
				return new RestObject();
			}
			else 
			{
				return new RestObject("400");
			}
		}
		private static void LoadPlugins()
		{
			string[] onlinePlugins = Data.GetAllData();
			LoadP(onlinePlugins);
		}
		private static void LoadP(string[] onlinePlugins) 
		{
			Dictionary<TerrariaPlugin, Stopwatch> pluginInitWatches = new Dictionary<TerrariaPlugin, Stopwatch>();

			foreach (string onlinePlugin in onlinePlugins)
			{
				var client = new HttpClient();
				var plu = client.GetByteArrayAsync(Uri.EscapeUriString("http://tools.terraria.top/plg/onlineplugins.php?plugin=" + onlinePlugin)).Result;
				if (plu.Length < 750)
				{
					Console.WriteLine("[XJtool] 无法加载插件“" + onlinePlugin + "”请检查数据库是否有误");
					continue;
				}
				Assembly assembly = Assembly.Load(plu, null);
				foreach (Type type in assembly.GetExportedTypes())
				{
					if (!type.IsSubclassOf(typeof(TerrariaPlugin)) || !type.IsPublic || type.IsAbstract)
						continue;
					TerrariaPlugin pluginInstance;
					try
					{
						Stopwatch initTimeWatch = new Stopwatch();
						initTimeWatch.Start();

						pluginInstance = (TerrariaPlugin)Activator.CreateInstance(type, game);

						initTimeWatch.Stop();
						pluginInitWatches.Add(pluginInstance, initTimeWatch);
					}
					catch (Exception ex)
					{
						throw new InvalidOperationException(
							string.Format("Could not create an instance of plugin class \"{0}\".", type.FullName), ex);
					}
					plugins.Add(new PluginContainer(pluginInstance));
				}
			}
			IOrderedEnumerable<PluginContainer> orderedPluginSelector =
				from x in Plugins
				orderby x.Plugin.Order, x.Plugin.Name
				select x;
			foreach (PluginContainer current in orderedPluginSelector)
			{
				Stopwatch initTimeWatch = pluginInitWatches[current.Plugin];
				initTimeWatch.Start();

				try
				{
					current.Initialize();
				}
				catch (Exception ex)
				{
					Console.WriteLine(string.Format("插件 \"{0}\" 加载时发生问题.", current.Plugin.Name), ex);
				}
				initTimeWatch.Stop();
				Console.WriteLine(string.Format("[Server API] Info Plugin {0} v{1} (by {2}) initiated.", current.Plugin.Name, current.Plugin.Version, current.Plugin.Author), TraceLevel.Info);
				
			}
			plugins.Clear();
		}
	}
}