using Rests;
using System;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    public class GroupOP
    {
        public static void Initialize()
        {
            RegisterRestCommands();
        }

        private static void RegisterRestCommands()
        {
            TShock.RestApi.Register(new SecureRestCommand("/data/groups/list", GetAllGroups, "data.groups"));
            TShock.RestApi.Register(new SecureRestCommand("/data/groups/get", GetGroup, "data.groups"));
            TShock.RestApi.Register(new SecureRestCommand("/data/groups/create", CreateGroup, "data.groups"));
            TShock.RestApi.Register(new SecureRestCommand("/data/groups/delete", DeleteGroup, "data.groups"));
            TShock.RestApi.Register(new SecureRestCommand("/data/groups/update", UpdateGroup, "data.groups"));
            TShock.RestApi.Register(new SecureRestCommand("/data/groups/permission/add", AddPermission, "data.groups"));
            TShock.RestApi.Register(new SecureRestCommand("/data/groups/permission/remove", RemovePermission, "data.groups"));
        }

        public static object GetAllGroups(RestRequestArgs args)
        {
            try
            {
                var db = TShock.DB;
                var groups = new System.Collections.Generic.List<object>();

                using (var reader = db.QueryReader("SELECT * FROM GroupList"))
                {
                    while (reader.Read())
                    {
                        groups.Add(new
                        {
                            GroupName = reader.Get<string>("GroupName"),
                            Parent = reader.Get<string>("Parent") ?? "",
                            Commands = reader.Get<string>("Commands") ?? "",
                            ChatColor = reader.Get<string>("ChatColor") ?? "",
                            Prefix = reader.Get<string>("Prefix") ?? "",
                            Suffix = reader.Get<string>("Suffix") ?? ""
                        });
                    }
                }

                return new RestObject()
                {
                    { "groups", groups }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        public static object GetGroup(RestRequestArgs args)
        {
            try
            {
                var groupName = args.Parameters["groupName"];
                if (string.IsNullOrEmpty(groupName))
                {
                    return new RestObject("400") { { "error", "groupName is required" } };
                }

                var db = TShock.DB;

                using (var reader = db.QueryReader("SELECT * FROM GroupList WHERE GroupName = @0", groupName))
                {
                    if (reader.Read())
                    {
                        return new RestObject()
                        {
                            { "groupName", reader.Get<string>("GroupName") },
                            { "parent", reader.Get<string>("Parent") ?? "" },
                            { "commands", reader.Get<string>("Commands") ?? "" },
                            { "chatColor", reader.Get<string>("ChatColor") ?? "" },
                            { "prefix", reader.Get<string>("Prefix") ?? "" },
                            { "suffix", reader.Get<string>("Suffix") ?? "" }
                        };
                    }
                }

                return new RestObject("404") { { "error", "Group not found" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        public static object CreateGroup(RestRequestArgs args)
        {
            try
            {
                var groupName = args.Parameters["groupName"];
                if (string.IsNullOrEmpty(groupName))
                {
                    return new RestObject("400") { { "error", "groupName is required" } };
                }

                var parent = args.Parameters["parent"] ?? "";
                var commands = args.Parameters["commands"] ?? "";
                var chatColor = args.Parameters["chatColor"] ?? "";
                var prefix = args.Parameters["prefix"] ?? "";
                var suffix = args.Parameters["suffix"] ?? "";

                // 经官方 API 创建：写库 + 同步内存缓存（TShock.Groups.groups），
                // 否则 /user group 等指令按内存缓存查找时找不到新建的组
                if (TShock.Groups.GroupExists(groupName))
                {
                    return new RestObject("409") { { "error", "Group already exists" } };
                }

                TShock.Groups.AddGroup(groupName, parent, commands, chatColor);

                // AddGroup 的 SQL 仅写 GroupName/Parent/Commands/ChatColor 四列，
                // Prefix/Suffix 需要再经 UpdateGroup 补齐（同样会同步内存缓存）
                if (!string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(suffix))
                {
                    // 官方签名顺序：UpdateGroup(name, parentname, permissions, chatcolor, suffix, prefix)
                    TShock.Groups.UpdateGroup(groupName, parent, commands, chatColor, suffix, prefix);
                }

                return new RestObject() { { "response", "Group created successfully" } };
            }
            catch (GroupExistsException)
            {
                return new RestObject("409") { { "error", "Group already exists" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        public static object DeleteGroup(RestRequestArgs args)
        {
            try
            {
                var groupName = args.Parameters["groupName"];
                if (string.IsNullOrEmpty(groupName))
                {
                    return new RestObject("400") { { "error", "groupName is required" } };
                }

                // 经官方 API 删除：同步移除内存缓存，避免删除后组仍可用/引用悬空
                var result = TShock.Groups.DeleteGroup(groupName);
                if (result.Contains("has been deleted successfully"))
                {
                    return new RestObject() { { "response", "Group deleted successfully" } };
                }
                if (result.Contains("doesn't exist"))
                {
                    return new RestObject("404") { { "error", "Group not found" } };
                }

                return new RestObject("500") { { "error", result } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        public static object UpdateGroup(RestRequestArgs args)
        {
            try
            {
                var groupName = args.Parameters["groupName"];
                if (string.IsNullOrEmpty(groupName))
                {
                    return new RestObject("400") { { "error", "groupName is required" } };
                }

                // 经官方 API 更新（写库 + 同步内存缓存 + 父组校验/防环）
                var group = TShock.Groups.GetGroupByName(groupName);
                if (group == null)
                {
                    return new RestObject("404") { { "error", "Group not found" } };
                }

                // 未传的字段沿用当前值；前端更新时不传 commands，故必须保留现有权限
                var parent = args.Parameters["parent"] ?? group.ParentName;
                var chatColor = args.Parameters["chatColor"] ?? group.ChatColor;
                var prefix = args.Parameters["prefix"] ?? group.Prefix;
                var suffix = args.Parameters["suffix"] ?? group.Suffix;
                var permissions = group.Permissions;

                // 官方签名顺序：UpdateGroup(name, parentname, permissions, chatcolor, suffix, prefix)
                TShock.Groups.UpdateGroup(groupName, parent, permissions, chatColor, suffix, prefix);

                return new RestObject() { { "response", "Group updated successfully" } };
            }
            catch (GroupNotExistException)
            {
                return new RestObject("404") { { "error", "Group not found" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        public static object AddPermission(RestRequestArgs args)
        {
            try
            {
                var groupName = args.Parameters["groupName"];
                var permission = args.Parameters["permission"];

                if (string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(permission))
                {
                    return new RestObject("400") { { "error", "groupName and permission are required" } };
                }

                // 经官方 API 添加：同步更新内存 Group.Permissions（含去重/否定权限处理）
                var result = TShock.Groups.AddPermissions(groupName, new System.Collections.Generic.List<string> { permission });
                if (result.Contains("doesn't exist"))
                {
                    return new RestObject("404") { { "error", "Group not found" } };
                }
                if (string.IsNullOrEmpty(result))
                {
                    return new RestObject("500") { { "error", "Failed to add permission" } };
                }

                return new RestObject() { { "response", "Permission added successfully" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }

        public static object RemovePermission(RestRequestArgs args)
        {
            try
            {
                var groupName = args.Parameters["groupName"];
                var permission = args.Parameters["permission"];

                if (string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(permission))
                {
                    return new RestObject("400") { { "error", "groupName and permission are required" } };
                }

                // 经官方 API 移除：同步更新内存 Group.Permissions
                var result = TShock.Groups.DeletePermissions(groupName, new System.Collections.Generic.List<string> { permission });
                if (result.Contains("doesn't exist"))
                {
                    return new RestObject("404") { { "error", "Group not found" } };
                }
                if (string.IsNullOrEmpty(result))
                {
                    return new RestObject("500") { { "error", "Failed to remove permission" } };
                }

                return new RestObject() { { "response", "Permission removed successfully" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }
    }
}
