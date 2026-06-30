using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using Newtonsoft.Json;

namespace duplicateips
{
    [ApiVersion(2, 1)]
    public class DuplicateIPs : TerrariaPlugin
    {
        public override string Author => "lmx12330";
        public override string Description => "查询重复IP插件";
        public override string Name => "DuplicateIPs";
        public override Version Version => new Version(1, 0, 0, 0);
        
        public DuplicateIPs(Main game) : base(game) { }
        
        public override void Initialize()
        {
            Commands.ChatCommands.Add(new Command("tshock.admin.ban", HandleDipCommand, "dip"));
        }

        private void HandleDipCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                ShowHelp(args);
                return;
            }

            string target = args.Parameters[0];

            if (target == "*")
            {
                QueryAllDuplicateIPs(args);
            }
            else
            {
                QueryDuplicateIPs(args, target);
            }
        }

        private void QueryDuplicateIPs(CommandArgs args, string username)
        {
            try
            {
                IDbConnection db = TShock.DB;
                List<Dictionary<string, object>> allUsers = new List<Dictionary<string, object>>();
                int targetIndex = -1;

                string query = "SELECT ID, Username, UUID, KnownIPs FROM Users";
                using (QueryResult res = db.QueryReader(query))
                {
                    int index = 0;
                    while (res.Read())
                    {
                        string userUsername = res.Get<string>("Username");
                        Dictionary<string, object> user = new Dictionary<string, object>
                        {
                            { "id", res.Get<int>("ID") },
                            { "username", userUsername },
                            { "uuid", res.Get<string>("UUID") ?? "" },
                            { "knownIPs", res.Get<string>("KnownIPs") ?? "" }
                        };
                        allUsers.Add(user);
                        
                        if (userUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                        {
                            targetIndex = index;
                        }
                        index++;
                    }
                }

                if (targetIndex == -1)
                {
                    args.Player.SendErrorMessage($"玩家 {username} 不存在");
                    return;
                }

                Dictionary<string, List<int>> ipToUsers = new Dictionary<string, List<int>>();
                Dictionary<string, List<int>> uuidToUsers = new Dictionary<string, List<int>>();

                for (int i = 0; i < allUsers.Count; i++)
                {
                    var user = allUsers[i];
                    string uuid = user["uuid"].ToString();
                    string knownIPsJson = user["knownIPs"].ToString();

                    if (!string.IsNullOrEmpty(uuid))
                    {
                        if (!uuidToUsers.ContainsKey(uuid))
                            uuidToUsers[uuid] = new List<int>();
                        uuidToUsers[uuid].Add(i);
                    }

                    List<string> ips = new List<string>();
                    if (!string.IsNullOrEmpty(knownIPsJson))
                    {
                        try
                        {
                            ips = JsonConvert.DeserializeObject<List<string>>(knownIPsJson) ?? new List<string>();
                        }
                        catch { }
                    }

                    foreach (string ip in ips)
                    {
                        if (!string.IsNullOrEmpty(ip))
                        {
                            if (!ipToUsers.ContainsKey(ip))
                                ipToUsers[ip] = new List<int>();
                            ipToUsers[ip].Add(i);
                        }
                    }
                }

                int[] parent = new int[allUsers.Count];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;

                Func<int, int> find = null;
                find = (int x) => {
                    if (parent[x] != x) parent[x] = find(parent[x]);
                    return parent[x];
                };

                Action<int, int> union = (int x, int y) => {
                    int px = find(x);
                    int py = find(y);
                    if (px != py) parent[px] = py;
                };

                foreach (var kvp in ipToUsers)
                {
                    List<int> users = kvp.Value;
                    for (int i = 1; i < users.Count; i++)
                    {
                        union(users[0], users[i]);
                    }
                }

                foreach (var kvp in uuidToUsers)
                {
                    List<int> users = kvp.Value;
                    for (int i = 1; i < users.Count; i++)
                    {
                        union(users[0], users[i]);
                    }
                }

                int targetRoot = find(targetIndex);
                HashSet<int> groupMembers = new HashSet<int>();
                HashSet<string> sharedIPs = new HashSet<string>();

                for (int i = 0; i < allUsers.Count; i++)
                {
                    if (find(i) == targetRoot)
                    {
                        groupMembers.Add(i);
                        
                        var user = allUsers[i];
                        string knownIPsJson = user["knownIPs"].ToString();
                        List<string> ips = new List<string>();
                        if (!string.IsNullOrEmpty(knownIPsJson))
                        {
                            try
                            {
                                ips = JsonConvert.DeserializeObject<List<string>>(knownIPsJson) ?? new List<string>();
                            }
                            catch { }
                        }
                        foreach (string ip in ips)
                        {
                            if (!string.IsNullOrEmpty(ip))
                                sharedIPs.Add(ip);
                        }
                    }
                }

                string color = RainbowColors[0];
                args.Player.SendInfoMessage($"[c/{color}:=== 玩家 {username} 的关联分析 ===");
                args.Player.SendInfoMessage($"[c/{color}:关联账号 ({groupMembers.Count} 个):]");
                
                foreach (int idx in groupMembers)
                {
                    var user = allUsers[idx];
                    string userUsername = user["username"].ToString();
                    string mark = idx == targetIndex ? " (目标)" : "";
                    args.Player.SendInfoMessage($"  - {userUsername}{mark}");
                }
                
                args.Player.SendInfoMessage($"[c/{color}:共享IP ({sharedIPs.Count} 个):] {string.Join(", ", sharedIPs)}");

                if (groupMembers.Count == 1)
                {
                    args.Player.SendSuccessMessage("未发现关联账号");
                }
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"查询失败: {ex.Message}");
            }
        }

        private static readonly string[] RainbowColors = { "FF6B6B", "FFA07A", "FFD700", "98FB98", "87CEEB", "9370DB", "DDA0DD" };

        private void QueryAllDuplicateIPs(CommandArgs args)
        {
            try
            {
                IDbConnection db = TShock.DB;
                List<Dictionary<string, object>> allUsers = new List<Dictionary<string, object>>();

                string query = "SELECT ID, Username, UUID, KnownIPs FROM Users";
                using (QueryResult res = db.QueryReader(query))
                {
                    while (res.Read())
                    {
                        Dictionary<string, object> user = new Dictionary<string, object>
                        {
                            { "id", res.Get<int>("ID") },
                            { "username", res.Get<string>("Username") },
                            { "uuid", res.Get<string>("UUID") ?? "" },
                            { "knownIPs", res.Get<string>("KnownIPs") ?? "" }
                        };
                        allUsers.Add(user);
                    }
                }

                Dictionary<string, List<int>> ipToUsers = new Dictionary<string, List<int>>();
                Dictionary<string, List<int>> uuidToUsers = new Dictionary<string, List<int>>();

                for (int i = 0; i < allUsers.Count; i++)
                {
                    var user = allUsers[i];
                    int userId = (int)user["id"];
                    string uuid = user["uuid"].ToString();
                    string knownIPsJson = user["knownIPs"].ToString();

                    if (!string.IsNullOrEmpty(uuid))
                    {
                        if (!uuidToUsers.ContainsKey(uuid))
                            uuidToUsers[uuid] = new List<int>();
                        uuidToUsers[uuid].Add(i);
                    }

                    List<string> ips = new List<string>();
                    if (!string.IsNullOrEmpty(knownIPsJson))
                    {
                        try
                        {
                            ips = JsonConvert.DeserializeObject<List<string>>(knownIPsJson) ?? new List<string>();
                        }
                        catch { }
                    }

                    foreach (string ip in ips)
                    {
                        if (!string.IsNullOrEmpty(ip))
                        {
                            if (!ipToUsers.ContainsKey(ip))
                                ipToUsers[ip] = new List<int>();
                            ipToUsers[ip].Add(i);
                        }
                    }
                }

                int[] parent = new int[allUsers.Count];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;

                Func<int, int> find = null;
                find = (int x) => {
                    if (parent[x] != x) parent[x] = find(parent[x]);
                    return parent[x];
                };

                Action<int, int> union = (int x, int y) => {
                    int px = find(x);
                    int py = find(y);
                    if (px != py) parent[px] = py;
                };

                foreach (var kvp in ipToUsers)
                {
                    List<int> users = kvp.Value;
                    for (int i = 1; i < users.Count; i++)
                    {
                        union(users[0], users[i]);
                    }
                }

                foreach (var kvp in uuidToUsers)
                {
                    List<int> users = kvp.Value;
                    for (int i = 1; i < users.Count; i++)
                    {
                        union(users[0], users[i]);
                    }
                }

                Dictionary<int, HashSet<int>> groups = new Dictionary<int, HashSet<int>>();
                for (int i = 0; i < allUsers.Count; i++)
                {
                    int root = find(i);
                    if (!groups.ContainsKey(root))
                        groups[root] = new HashSet<int>();
                    groups[root].Add(i);
                }

                int foundCount = 0;
                int index = 1;
                foreach (var group in groups)
                {
                    if (group.Value.Count > 1)
                    {
                        foundCount++;
                        string color = RainbowColors[(index - 1) % RainbowColors.Length];
                        List<string> accounts = new List<string>();
                        HashSet<string> allIPs = new HashSet<string>();

                        foreach (int userIdx in group.Value)
                        {
                            var user = allUsers[userIdx];
                            string username = user["username"].ToString();
                            accounts.Add(username);

                            string knownIPsJson = user["knownIPs"].ToString();
                            List<string> ips = new List<string>();
                            if (!string.IsNullOrEmpty(knownIPsJson))
                            {
                                try
                                {
                                    ips = JsonConvert.DeserializeObject<List<string>>(knownIPsJson) ?? new List<string>();
                                }
                                catch { }
                            }
                            foreach (string ip in ips)
                            {
                                if (!string.IsNullOrEmpty(ip))
                                    allIPs.Add(ip);
                            }
                        }

                        args.Player.SendInfoMessage($"[c/{color}:=== 关联组 #{index} ===]");
                        args.Player.SendInfoMessage($"[c/{color}:账号:] {string.Join(", ", accounts)}");
                        args.Player.SendInfoMessage($"[c/{color}:共享IP:] {string.Join(", ", allIPs)}");
                        index++;
                    }
                }

                if (foundCount == 0)
                {
                    args.Player.SendSuccessMessage("未发现关联账号组");
                }
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage($"查询失败: {ex.Message}");
            }
        }

        private void ShowHelp(CommandArgs args)
        {
            args.Player.SendInfoMessage("=== 重复IP查询命令 ===");
            args.Player.SendInfoMessage("/dip <玩家名> - 查询指定玩家的重复IP账号");
            args.Player.SendInfoMessage("/dip * - 查询所有共享IP的账号组");
        }
    }
}