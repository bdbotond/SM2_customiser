using System;
using System.IO;
using Microsoft.Win32;

namespace SM2Customizer.Services
{
    public class CustomImportResult
    {
        public string SlotName { get; set; } = "HELMET";
        public string FileName { get; set; } = string.Empty;
        public string Format { get; set; } = "obj"; // "obj", "stl", "gltf", "glb"
        public string? TextContent { get; set; }
        public string? Base64Content { get; set; }
        public bool IsBinary { get; set; }
    }

    public class CustomModelImportService
    {
        public const string ModelFileFilter = "All Supported 3D Models (*.obj;*.stl;*.gltf;*.glb)|*.obj;*.stl;*.gltf;*.glb|" +
                                              "Wavefront OBJ (*.obj)|*.obj|" +
                                              "Stereolithography STL (*.stl)|*.stl|" +
                                              "GL Transmission Format (*.gltf;*.glb)|*.gltf;*.glb|" +
                                              "All Files (*.*)|*.*";

        public bool PromptAndLoadCustomModel(string defaultSlot, out CustomImportResult? result)
        {
            result = null;

            var dialog = new OpenFileDialog
            {
                Title = "Import Custom 3D Model (OBJ, STL, GLTF, GLB)",
                Filter = ModelFileFilter
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string ext = (Path.GetExtension(dialog.FileName) ?? string.Empty).TrimStart('.').ToLowerInvariant();
                    string fileName = Path.GetFileNameWithoutExtension(dialog.FileName);
                    string slot = string.IsNullOrWhiteSpace(defaultSlot) ? "HELMET" : defaultSlot.ToUpperInvariant();

                    var import = new CustomImportResult
                    {
                        SlotName = slot,
                        FileName = fileName,
                        Format = ext
                    };

                    if (ext == "glb")
                    {
                        byte[] bytes = File.ReadAllBytes(dialog.FileName);
                        import.Base64Content = Convert.ToBase64String(bytes);
                        import.IsBinary = true;
                    }
                    else if (ext == "stl")
                    {
                        byte[] bytes = File.ReadAllBytes(dialog.FileName);
                        import.Base64Content = Convert.ToBase64String(bytes);
                        import.IsBinary = true;
                    }
                    else if (ext == "gltf")
                    {
                        import.TextContent = File.ReadAllText(dialog.FileName);
                        import.IsBinary = false;
                    }
                    else // default obj
                    {
                        import.Format = "obj";
                        import.TextContent = File.ReadAllText(dialog.FileName);
                        import.IsBinary = false;
                    }

                    result = import;
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load custom model: {ex.Message}");
                }
            }

            return false;
        }

        // Backward compatibility overload
        public bool PromptAndLoadCustomModel(out string slotName, out string fileContent, out string fileName)
        {
            slotName = "HELMET";
            fileContent = string.Empty;
            fileName = string.Empty;

            if (PromptAndLoadCustomModel("HELMET", out var res) && res != null)
            {
                slotName = res.SlotName;
                fileName = res.FileName;
                fileContent = res.TextContent ?? string.Empty;
                return true;
            }

            return false;
        }
    }
}
