using System;
using System.Collections.Generic;

namespace SM2Customizer.Models
{
    public class CustomizerProject
    {
        public string ProjectName { get; set; } = "My Space Marine Preset";
        public string GameVersion { get; set; } = "1.0";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool UnlockAllColorsAndDecals { get; set; } = true;
        public bool UnlockCrossClassSlots { get; set; } = true;
        public bool UnlockAllPatterns { get; set; } = true;

        public List<ClassPresetModel> Presets { get; set; } = new List<ClassPresetModel>();
        public List<PaintColorModel> CustomColors { get; set; } = new List<PaintColorModel>();
        public List<DecalModel> CustomDecals { get; set; } = new List<DecalModel>();
    }
}
