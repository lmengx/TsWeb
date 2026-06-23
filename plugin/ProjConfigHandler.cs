using Rests;
using System;
using System.IO;
using Newtonsoft.Json;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockData
{
    public class ProjConfigHandler
    {
        public static object GetProjConfig(RestRequestArgs args)
        {
            try
            {
                var config = AntiCheat.GetProjConfig();
                if (config != null)
                {
                    return new { status = 200, config = config };
                }
                return new { status = 500, error = "Failed to load config" };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ProjConfig] GetProjConfig error: {ex.Message}");
                return new { status = 500, error = ex.Message };
            }
        }

        public static object SaveProjConfig(RestRequestArgs args)
        {
            try
            {
                string json = args.Parameters["config"];
                if (string.IsNullOrEmpty(json))
                {
                    return new { status = 400, error = "Missing config parameter" };
                }

                var config = JsonConvert.DeserializeObject<ProjRestrictionConfig>(json);
                if (config == null)
                {
                    return new { status = 400, error = "Invalid config format" };
                }

                bool success = AntiCheat.SaveProjConfig(config);
                if (success)
                {
                    return new { status = 200, message = "Config saved successfully" };
                }
                return new { status = 500, error = "Failed to save config" };
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[ProjConfig] SaveProjConfig error: {ex.Message}");
                return new { status = 500, error = ex.Message };
            }
        }
    }
}
