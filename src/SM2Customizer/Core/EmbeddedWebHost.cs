using System;
using System.IO;
using System.Reflection;

namespace SM2Customizer.Core
{
    public static class EmbeddedWebHost
    {
        private static readonly string[] RequiredFiles = { "viewer.html", "three.min.js", "OrbitControls.js", "OBJLoader.js" };

        public static string GetWebAssetsDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localWeb = Path.Combine(baseDir, "Web");
            string nestedWeb = Path.Combine(localWeb, "Web");

            // Fix nested Web directory if created by copy
            if (Directory.Exists(nestedWeb))
            {
                foreach (var file in RequiredFiles)
                {
                    string nestedFile = Path.Combine(nestedWeb, file);
                    string localFile = Path.Combine(localWeb, file);
                    if (File.Exists(nestedFile) && (!File.Exists(localFile) || new FileInfo(localFile).Length == 0))
                    {
                        try { File.Copy(nestedFile, localFile, true); } catch { }
                    }
                }
            }

            // Verify localWeb has all required files
            bool localComplete = Directory.Exists(localWeb);
            if (localComplete)
            {
                foreach (var file in RequiredFiles)
                {
                    string path = Path.Combine(localWeb, file);
                    if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    {
                        localComplete = false;
                        break;
                    }
                }
            }

            if (localComplete)
            {
                return localWeb;
            }

            // Fallback to LocalApplicationData cache
            string appDataWeb = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SM2Customizer", "Web");

            if (!Directory.Exists(appDataWeb))
            {
                Directory.CreateDirectory(appDataWeb);
            }

            // Copy from localWeb or nestedWeb if present
            foreach (var name in RequiredFiles)
            {
                string targetPath = Path.Combine(appDataWeb, name);
                string sourcePath = Path.Combine(localWeb, name);
                string nestedPath = Path.Combine(nestedWeb, name);

                if (File.Exists(sourcePath) && new FileInfo(sourcePath).Length > 0)
                {
                    try { File.Copy(sourcePath, targetPath, true); } catch { }
                }
                else if (File.Exists(nestedPath) && new FileInfo(nestedPath).Length > 0)
                {
                    try { File.Copy(nestedPath, targetPath, true); } catch { }
                }
                else
                {
                    // Try extract from embedded resource
                    ExtractEmbeddedResource(name, targetPath);
                }
            }

            return appDataWeb;
        }

        private static void ExtractEmbeddedResource(string fileName, string targetPath)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string resourceName = $"SM2Customizer.Web.{fileName}";
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
                    stream.CopyTo(fs);
                }
            }
            catch { }
        }
    }
}
