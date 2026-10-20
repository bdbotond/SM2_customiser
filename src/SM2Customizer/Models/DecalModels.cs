using System;

namespace SM2Customizer.Models
{
    public class DecalModel
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty; // LEFT_PAULDRON, RIGHT_PAULDRON, etc.
        public string Material { get; set; } = "default";
        public int ColorsNumber { get; set; } = 1;
        public string PrimaryColor { get; set; } = "White_Scar";
        public string SecondaryColor { get; set; } = "Transparent";
        public string TertiaryColor { get; set; } = "Transparent";

        public override string ToString() => DisplayName;
    }

    public class PatternModel
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string MaterialSuffix { get; set; } = "CC";
        public bool EnabledForEveryone { get; set; } = true;

        public override string ToString() => DisplayName;
    }
}
