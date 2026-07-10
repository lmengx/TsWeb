using System;
using Rests;
using TShockAPI;

namespace TShockData
{
    /// <summary>
    /// GET /data/online/log/command?cmd=say hello&token=xxx
    /// 以 superadmin 身份执行服务器命令，返回输出结果
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

                // 获取执行者名称（从 Web 面板传来的），默认为 SSE-Console
                var executor = args.Parameters["executor"];
                if (string.IsNullOrWhiteSpace(executor))
                    executor = "SSE-Console";

                // 以 superadmin 身份执行
                var group = TShock.Groups.GetGroupByName("superadmin");
                var tr = new TSRestPlayer(executor, group);
                Commands.HandleCommand(tr, cmd);

                return new RestObject
                {
                    { "response", tr.GetCommandOutput() }
                };
            }
            catch (Exception ex)
            {
                return new RestObject("500") { { "error", ex.Message } };
            }
        }
    }
}
