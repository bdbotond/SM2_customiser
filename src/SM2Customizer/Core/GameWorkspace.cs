using System;
using System.IO;
using System.Collections.Generic;

namespace SM2Customizer.Core
{
    public class GameWorkspace
    {
        public string GameRootPath { get; private set; } = string.Empty;
        public string PaksDirectory { get; private set; } = string.Empty;
        public string DefaultOtherPakPath { get; private set; } = string.Empty;
        public string ModsDirectory { get; private set; } = string.Empty;
        public string ModPakPath { get; private set; } = string.Empty;
        public bool IsValid { get; private set; }

        public static readonly string ModPakFileName = "SM2Customizer.pak";

        public GameWorkspace(string rootPath)
        {
            SetRootPath(rootPath);
        }

        public bool SetRootPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                IsValid = false;
                return false;
            }

            // Clean path
            var dir = path.TrimEnd('\\', '/');

            // Check if the user selected client_pc or client_pc\root or root directly
            if (File.Exists(Path.Combine(dir, "default_other.pak")))
            {
                // User pointed directly to default paks dir
                DefaultOtherPakPath = Path.Combine(dir, "default_other.pak");
                // climb up to find root
                var parent = Directory.GetParent(dir)?.Parent?.Parent?.Parent?.FullName;
                GameRootPath = parent ?? dir;
            }
            else
            {
                GameRootPath = dir;
                DefaultOtherPakPath = Path.Combine(GameRootPath, "client_pc", "root", "paks", "client", "default", "default_other.pak");
            }

            PaksDirectory = Path.Combine(GameRootPath, "client_pc", "root", "paks", "client", "default");
            ModsDirectory = Path.Combine(GameRootPath, "client_pc", "root", "mods");
            ModPakPath = Path.Combine(ModsDirectory, ModPakFileName);

            IsValid = File.Exists(DefaultOtherPakPath);
            return IsValid;
        }

        public static string? TryAutoDetect()
        {
            var candidates = new List<string>
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Space Marine 2",
                @"D:\SteamLibrary\steamapps\common\Space Marine 2",
                @"E:\SteamLibrary\steamapps\common\Space Marine 2",
                @"F:\SteamLibrary\steamapps\common\Space Marine 2",
                @"C:\Steam\steamapps\common\Space Marine 2",
                @"D:\Steam\steamapps\common\Space Marine 2",
                @"C:\Games\Space Marine 2"
            };

            // Check Windows Registry for Steam install path
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string steamPath)
                {
                    candidates.Add(Path.Combine(steamPath, "steamapps", "common", "Space Marine 2"));
                }
            }
            catch { }

            foreach (var path in candidates)
            {
                if (File.Exists(Path.Combine(path, "client_pc", "root", "paks", "client", "default", "default_other.pak")))
                {
                    return path;
                }
            }

            return null;
        }

        public void EnsureModsDirectory()
        {
            if (!Directory.Exists(ModsDirectory))
            {
                Directory.CreateDirectory(ModsDirectory);
            }
        }
    }
}
