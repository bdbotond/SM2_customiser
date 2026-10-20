using System;
using System.IO;
using System.Text.Json;

namespace SM2Customizer.Services
{
    public class UserConfig
    {
        public string? LastGamePath { get; set; }
        public bool AutoLoadGameOnStartup { get; set; } = true;
    }

    public static class ConfigStoreService
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SM2Customizer");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

        public static UserConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<UserConfig>(json) ?? new UserConfig();
                }
            }
            catch { }

            return new UserConfig();
        }

        public static void Save(UserConfig config)
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                }
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
