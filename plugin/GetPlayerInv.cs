﻿﻿using Rests;
using System;
using System.Data;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;

namespace TShockData
{
    public class GetPlayerInv
    {
        public static object EditInv(RestRequestArgs args)//编辑玩家背包
        {
            int netID = int.Parse(args.Parameters["netID"]);
            int prefix = int.Parse(args.Parameters["prefix"]);
            int stack = int.Parse(args.Parameters["stack"]);
            int index = int.Parse(args.Parameters["index"]);
            string player = args.Parameters["player"];
            var account = TShock.UserAccounts.GetUserAccountByName(player);
            if (account == null)
            {
                throw new Exception("找不到玩家");
            }
            var player1 = TShockAPI.TSPlayer.FindByNameOrID(account.Name);
            if (player1.Count > 0)
            {
                foreach (var tSPlayer in player1)
                {
                    tSPlayer.Kick("管理员修改了您的背包",true);
                }
                return new RestObject()
                    {
                        {
                        "response",
                        "修改成功"
                        }
                    };
                Thread.Sleep(1000);//等待玩家踢出，以免被保存覆盖
            }
            try
            {
                IDbConnection db = TShock.DB;
                string strinventory = "";
                using (QueryResult res = db.QueryReader("SELECT Inventory FROM tsCharacter WHERE Account = @0", account.ID))
                {
                    if (res.Read())
                    {
                        strinventory = res.Get<string>("Inventory");
                    }
                    else
                    {
                        return false;
                    }
                }
                if (strinventory != "")
                {
                    string[] arrinventory = strinventory.Split("~");
                    string[] item = arrinventory[index].Split(",");
                    item[0] = netID.ToString();
                    item[1] = stack.ToString();
                    item[2] = prefix.ToString();
                    arrinventory[index] = string.Join(",", item);
                    string finalinv = string.Join("~", arrinventory);
                    db.Query("UPDATE tsCharacter SET Inventory = @0 WHERE Account = @1", finalinv, account.ID);
                    return new RestObject()
                        {
                            {
                            "response",
                            "修改成功"
                            }
                        };
                }
                else
                {
                    return new RestObject("400")
                        {
                            {
                            "response",
                            "数据库错误"
                            }
                        };
                }
            }
            catch (Exception ex)
            {
                return new RestObject()
                    {
                        {
                        "response",
                        ex
                        }
                    };
            }
        }
        public static object GetInv(RestRequestArgs args)//查看玩家背包
        {
            string player = args.Parameters["player"];
            var inv = GetInv(player);
            if (inv == null)
            {
                return new RestObject("401")
                {
                    {
                        "error",
                        "找不到玩家或查询数据时出错"
                    }
                };
            }
            return new RestObject()
            {
                {
                    "inventory",
                    inv
                }
            };
        }
        public static NetItem[] GetInv(string player)
        {
            int id = -1;
            var account = TShock.UserAccounts.GetUserAccountByName(player);
            if (account == null)
            {
                throw new Exception("找不到玩家");
            }
            id = account.ID;
            var data = TShock.CharacterDB.GetPlayerData(null, id);
            return data.inventory;
        }
    }
}

