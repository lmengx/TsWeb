﻿using Rests;
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

                var db = TShock.DB;
                var parent = args.Parameters["parent"] ?? "";
                var commands = args.Parameters["commands"] ?? "";
                var chatColor = args.Parameters["chatColor"] ?? "";
                var prefix = args.Parameters["prefix"] ?? "";
                var suffix = args.Parameters["suffix"] ?? "";

                var existing = db.QueryReader("SELECT GroupName FROM GroupList WHERE GroupName = @0", groupName);
                if (existing.Read())
                {
                    return new RestObject("409") { { "error", "Group already exists" } };
                }

                db.Query(@"
                    INSERT INTO GroupList (GroupName, Parent, Commands, ChatColor, Prefix, Suffix)
                    VALUES (@0, @1, @2, @3, @4, @5)",
                    groupName, parent, commands, chatColor, prefix, suffix);

                return new RestObject() { { "response", "Group created successfully" } };
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

                var db = TShock.DB;
                var affected = db.Query("DELETE FROM GroupList WHERE GroupName = @0", groupName);

                if (affected > 0)
                {
                    return new RestObject() { { "response", "Group deleted successfully" } };
                }

                return new RestObject("404") { { "error", "Group not found" } };
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

                var db = TShock.DB;
                var parent = args.Parameters["parent"];
                var chatColor = args.Parameters["chatColor"];
                var prefix = args.Parameters["prefix"];
                var suffix = args.Parameters["suffix"];

                var updates = new System.Collections.Generic.List<string>();
                var parameters = new System.Collections.Generic.List<object>();

                if (parent != null)
                {
                    updates.Add("Parent = @0");
                    parameters.Add(parent);
                }
                if (chatColor != null)
                {
                    updates.Add("ChatColor = @" + updates.Count);
                    parameters.Add(chatColor);
                }
                if (prefix != null)
                {
                    updates.Add("Prefix = @" + updates.Count);
                    parameters.Add(prefix);
                }
                if (suffix != null)
                {
                    updates.Add("Suffix = @" + updates.Count);
                    parameters.Add(suffix);
                }

                if (updates.Count == 0)
                {
                    return new RestObject("400") { { "error", "No fields to update" } };
                }

                parameters.Add(groupName);
                var sql = $"UPDATE GroupList SET {string.Join(", ", updates)} WHERE GroupName = @{updates.Count}";
                var affected = db.Query(sql, parameters.ToArray());

                if (affected > 0)
                {
                    return new RestObject() { { "response", "Group updated successfully" } };
                }

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

                var db = TShock.DB;

                string currentCommands = "";
                using (var reader = db.QueryReader("SELECT Commands FROM GroupList WHERE GroupName = @0", groupName))
                {
                    if (reader.Read())
                    {
                        currentCommands = reader.Get<string>("Commands") ?? "";
                    }
                    else
                    {
                        return new RestObject("404") { { "error", "Group not found" } };
                    }
                }

                var commandsList = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(currentCommands))
                {
                    commandsList.AddRange(currentCommands.Split(','));
                }

                if (!commandsList.Contains(permission))
                {
                    commandsList.Add(permission);
                }

                var newCommands = string.Join(",", commandsList);
                db.Query("UPDATE GroupList SET Commands = @0 WHERE GroupName = @1", newCommands, groupName);

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

                var db = TShock.DB;

                string currentCommands = "";
                using (var reader = db.QueryReader("SELECT Commands FROM GroupList WHERE GroupName = @0", groupName))
                {
                    if (reader.Read())
                    {
                        currentCommands = reader.Get<string>("Commands") ?? "";
                    }
                    else
                    {
                        return new RestObject("404") { { "error", "Group not found" } };
                    }
                }

                var commandsList = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(currentCommands))
                {
                    commandsList.AddRange(currentCommands.Split(','));
                }

                commandsList.Remove(permission);

                var newCommands = string.Join(",", commandsList);
                db.Query("UPDATE GroupList SET Commands = @0 WHERE GroupName = @1", newCommands, groupName);

                return new RestObject() { { "response", "Permission removed successfully" } };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }
    }
}
