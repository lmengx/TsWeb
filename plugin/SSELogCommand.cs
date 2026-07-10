using System;
using Rests;
using TShockAPI;

namespace TShockData
{
    /// <summary>
    /// GET /data/online/log/command?cmd=say hello&token=xxx
    /// 以 superadmin 身份执行服务器命令，返回输出结果
    /// 命令执行信息及输出通过 Console 写入（LogInterceptor 自动捕获到 SSE）
    /// </summary>
    public static class SSELogCommand
    {
        public static object Execute(RestRequestArgs args)
        {
            try
            {
                var cmd = args.Parameters["cmd"];
                if (string.IsNullOrWhiteSpace(cmd))
                {
                    return new RestObject("400") { { "error", "Missing cmd parameter" } };
                }

                var executor = args.Parameters["executor"];
                if (string.IsNullOrWhiteSpace(executor))
                    executor = "SSE-Console";

                // ═══ 将命令执行信息写入 Console（LogInterceptor 自动捕获到 SSE） ═══
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write("[" + DateTime.Now.ToString("HH:mm:ss") + "] ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(executor);
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write(" 执行了 ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(cmd);
                Console.ResetColor();

                var group = TShock.Groups.GetGroupByName("superadmin");
                var tr = new TSRestPlayer(executor, group);
                Commands.HandleCommand(tr, cmd);

                var outputList = tr.GetCommandOutput();

                // ═══ 命令输出也写入 Console（LogInterceptor 自动捕获到 SSE） ═══
                if (outputList != null && outputList.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    foreach (var line in outputList)
                    {
                        if (!string.IsNullOrEmpty(line))
                            Console.WriteLine(line);
                    }
                    Console.ResetColor();
                }

                var output = string.Join("\n", outputList ?? new System.Collections.Generic.List<string>());

                return new RestObject
                {
                    { "response", output }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }
    }
}
