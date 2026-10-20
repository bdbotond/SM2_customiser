using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using SM2Customizer.Core;
using SM2Customizer.Models;

namespace SM2Customizer.Services
{
    public class GameScannerService
    {
        public List<string> BodySections { get; } = new List<string>();
        public List<ArmorPieceItem> AllArmorPieces { get; } = new List<ArmorPieceItem>();
        public List<ClassPresetModel> Presets { get; } = new List<ClassPresetModel>();
        public List<PaintColorModel> Colors { get; } = new List<PaintColorModel>();
        public List<DecalModel> Decals { get; } = new List<DecalModel>();
        public List<PatternModel> Patterns { get; } = new List<PatternModel>();

        // Retain original parsed ASTs for round-trip patching
        public SsoObject? CharacterCustomizationLibraryAst { get; private set; }
        public SsoObject? ArmorColorLibraryAst { get; private set; }
        public SsoObject? CustomizationDecalLibraryAst { get; private set; }
        public SsoObject? ArmorPatternLibraryAst { get; private set; }
        public SsoObject? BodySectionAvailabilityRulesAst { get; private set; }
        public SsoObject? DefaultHeraldryItemsLibraryAst { get; private set; }

        public bool Scan(GameWorkspace workspace, out string error)
        {
            error = string.Empty;
            BodySections.Clear();
            AllArmorPieces.Clear();
            Presets.Clear();
            Colors.Clear();
            Decals.Clear();
            Patterns.Clear();

            if (!workspace.IsValid)
            {
                error = $"Cannot find default_other.pak at: {workspace.DefaultOtherPakPath}";
                return false;
            }

            try
            {
                using var zip = ZipFile.OpenRead(workspace.DefaultOtherPakPath);

                // 1. Scan Body Sections
                var bodySecEntry = zip.GetEntry("ssl/body/customization/character_body_section_library.sso");
                if (bodySecEntry != null)
                {
                    using var stream = bodySecEntry.Open();
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    var ast = SsoParser.Parse(text);
                    if (ast["bodySections"] is SsoObject secObj)
                    {
                        foreach (var prop in secObj.Properties)
                        {
                            BodySections.Add(prop.Key);
                        }
                    }
                }

                // If fallback needed, add standard body sections
                if (BodySections.Count == 0)
                {
                    BodySections.AddRange(new[]
                    {
                        "HELMET", "BACKPACK", "CHEST", "LEFT_PAULDRON", "RIGHT_PAULDRON",
                        "LEFT_GAUNTLET", "RIGHT_GAUNTLET", "LEFT_GREAVE", "RIGHT_GREAVE",
                        "CLOTH", "COAT", "HALO", "ANTENNA", "VANITY", "HELMET_LENSES"
                    });
                }

                // 2. Scan Presets & Armor Pieces from character_customization_library.sso
                var customLibEntry = zip.GetEntry("ssl/body/customization/character_customization_library.sso");
                if (customLibEntry != null)
                {
                    using var stream = customLibEntry.Open();
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    CharacterCustomizationLibraryAst = SsoParser.Parse(text);

                    if (CharacterCustomizationLibraryAst["appearancesByTeams"] is SsoObject teamsObj)
                    {
                        foreach (var teamProp in teamsObj.Properties)
                        {
                            string teamName = teamProp.Key; // LOYALIST or CHAOS
                            if (teamProp.Value is SsoObject teamObj)
                            {
                                // Scan shared outfit parts first
                                if (teamObj["sharedOutfitParts"] is SsoObject sharedObj && sharedObj["outfitParts"] is SsoObject partsObj)
                                {
                                    foreach (var secProp in partsObj.Properties)
                                    {
                                        string sectionKey = secProp.Key;
                                        if (secProp.Value is SsoObject secVal && secVal["regions"] is SsoObject regionsObj)
                                        {
                                            foreach (var regProp in regionsObj.Properties)
                                            {
                                                RegisterArmorPiece(regProp.Key, sectionKey, "Shared", regProp.Value as SsoObject);
                                            }
                                        }
                                    }
                                }

                                if (teamObj["customizationPresets"] is SsoObject presetsObj)
                                {
                                    foreach (var classProp in presetsObj.Properties)
                                    {
                                        string classKey = classProp.Key; // PVE_PVP_SOLDIER, etc.
                                        string className = ClassPresetModel.GetClassDisplayName(classKey);

                                        if (classProp.Value is SsoObject classObj)
                                        {
                                            // Scan bodySections for this class to get full armor piece catalog & meshes
                                            if (classObj["bodySections"] is SsoObject bodySecsObj)
                                            {
                                                foreach (var secProp in bodySecsObj.Properties)
                                                {
                                                    string sectionKey = secProp.Key;
                                                    if (secProp.Value is SsoObject secVal && secVal["regions"] is SsoObject regionsObj)
                                                    {
                                                        foreach (var regProp in regionsObj.Properties)
                                                        {
                                                            RegisterArmorPiece(regProp.Key, sectionKey, className, regProp.Value as SsoObject);
                                                        }
                                                    }
                                                }
                                            }

                                            // Scan Presets
                                            if (classObj["armorPresets"] is SsoObject armorPresetsObj)
                                            {
                                                foreach (var presetProp in armorPresetsObj.Properties)
                                                {
                                                    string presetKey = presetProp.Key; // PREDEFINED_PRESET_1, etc.
                                                    var model = new ClassPresetModel
                                                    {
                                                        Team = teamName,
                                                        ClassKey = classKey,
                                                        DisplayName = className,
                                                        PresetKey = presetKey
                                                    };

                                                    if (presetProp.Value is SsoObject presetObj && presetObj["bodySectionRegionUids"] is SsoObject uidsObj)
                                                    {
                                                        foreach (var slotProp in uidsObj.Properties)
                                                        {
                                                            string section = slotProp.Key;
                                                            string pieceUid = string.Empty;
                                                            if (slotProp.Value is SsoObject valObj && valObj["__value"] is SsoValue v)
                                                            {
                                                                pieceUid = v.RawValue;
                                                            }
                                                            else if (slotProp.Value is SsoValue directVal)
                                                            {
                                                                pieceUid = directVal.RawValue;
                                                            }

                                                            if (!string.IsNullOrEmpty(pieceUid))
                                                            {
                                                                model.SectionPieces[section] = pieceUid;
                                                                RegisterArmorPiece(pieceUid, section, className, null);
                                                            }
                                                        }
                                                    }

                                                    Presets.Add(model);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // 3. Scan Colors from armor_color_library.sso
                var colorLibEntry = zip.GetEntry("ssl/body/customization/armor_color_library.sso");
                if (colorLibEntry != null)
                {
                    using var stream = colorLibEntry.Open();
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    ArmorColorLibraryAst = SsoParser.Parse(text);

                    if (ArmorColorLibraryAst["primaryShaderLayers"] is SsoObject layersObj)
                    {
                        foreach (var colorProp in layersObj.Properties)
                        {
                            string colorId = colorProp.Key;
                            if (colorProp.Value is SsoObject colorObj)
                            {
                                var colorModel = new PaintColorModel
                                {
                                    Id = colorId,
                                    DisplayName = FormatColorDisplayName(colorId),
                                    IsMetallic = colorObj.GetBool("isMetallic") ?? false
                                };

                                // Tint
                                if (colorObj["tint"] is SsoObject tintObj && tintObj["value"] is SsoObject tintVal)
                                {
                                    colorModel.A = (byte)(tintVal.GetInt("a") ?? 255);
                                    colorModel.R = (byte)(tintVal.GetInt("r") ?? 255);
                                    colorModel.G = (byte)(tintVal.GetInt("g") ?? 255);
                                    colorModel.B = (byte)(tintVal.GetInt("b") ?? 255);
                                }

                                // Specular / Metalness Tint
                                if (colorObj["spec_metalnessTint"] is SsoObject specObj && specObj["value"] is SsoObject specVal)
                                {
                                    colorModel.SpecA = (byte)(specVal.GetInt("a") ?? 255);
                                    colorModel.SpecR = (byte)(specVal.GetInt("r") ?? 255);
                                    colorModel.SpecG = (byte)(specVal.GetInt("g") ?? 255);
                                    colorModel.SpecB = (byte)(specVal.GetInt("b") ?? 255);
                                }

                                // Pattern Tint
                                if (colorObj["patternTint"] is SsoObject patObj && patObj["value"] is SsoObject patVal)
                                {
                                    colorModel.PatternA = (byte)(patVal.GetInt("a") ?? 255);
                                    colorModel.PatternR = (byte)(patVal.GetInt("r") ?? 255);
                                    colorModel.PatternG = (byte)(patVal.GetInt("g") ?? 255);
                                    colorModel.PatternB = (byte)(patVal.GetInt("b") ?? 255);
                                }

                                Colors.Add(colorModel);
                            }
                        }
                    }
                }

                // 4. Scan Decals from customization_decal_library.sso
                var decalEntry = zip.GetEntry("ssl/body/customization/customization_decal_library.sso");
                if (decalEntry != null)
                {
                    using var stream = decalEntry.Open();
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    CustomizationDecalLibraryAst = SsoParser.Parse(text);

                    if (CustomizationDecalLibraryAst["categorizedDecals"] is SsoObject catObj)
                    {
                        foreach (var secProp in catObj.Properties)
                        {
                            string section = secProp.Key; // LEFT_PAULDRON, etc.
                            if (secProp.Value is SsoObject secObj && secObj["items"] is SsoObject itemsObj)
                            {
                                foreach (var decalProp in itemsObj.Properties)
                                {
                                    string decalId = decalProp.Key;
                                    if (decalProp.Value is SsoObject dObj)
                                    {
                                        var decalModel = new DecalModel
                                        {
                                            Id = decalId,
                                            DisplayName = FormatDecalDisplayName(decalId),
                                            Section = section,
                                            Material = dObj.GetString("material") ?? "default",
                                            ColorsNumber = dObj.GetInt("colorsNumber") ?? 1
                                        };

                                        if (dObj["defaultColoring"] is SsoObject coloringObj)
                                        {
                                            decalModel.PrimaryColor = coloringObj.GetString("primary") ?? "White_Scar";
                                            decalModel.SecondaryColor = coloringObj.GetString("secondary") ?? "Transparent";
                                            decalModel.TertiaryColor = coloringObj.GetString("tertiary") ?? "Transparent";
                                        }

                                        Decals.Add(decalModel);
                                    }
                                }
                            }
                        }
                    }
                }

                // 5. Scan Patterns from armor_pattern_library.sso
                var patternEntry = zip.GetEntry("ssl/body/customization/armor_pattern_library.sso");
                if (patternEntry != null)
                {
                    using var stream = patternEntry.Open();
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    ArmorPatternLibraryAst = SsoParser.Parse(text);

                    if (ArmorPatternLibraryAst["patterns"] is SsoObject patternsObj)
                    {
                        foreach (var patProp in patternsObj.Properties)
                        {
                            Patterns.Add(new PatternModel
                            {
                                Id = patProp.Key,
                                DisplayName = FormatPatternDisplayName(patProp.Key),
                                EnabledForEveryone = true
                            });
                        }
                    }
                }

                // 6. Preload availability rules & default heraldry ASTs
                var rulesEntry = zip.GetEntry("ssl/body/customization/character_body_section_availability_ps/customization_body_section_availability_rules_by_mastery.sso");
                if (rulesEntry != null)
                {
                    using var stream = rulesEntry.Open();
                    using var reader = new StreamReader(stream);
                    BodySectionAvailabilityRulesAst = SsoParser.Parse(reader.ReadToEnd());
                }

                var heraldryEntry = zip.GetEntry("ssl/body/customization/heraldry_progression/default_heraldry_items_library.sso");
                if (heraldryEntry != null)
                {
                    using var stream = heraldryEntry.Open();
                    using var reader = new StreamReader(stream);
                    DefaultHeraldryItemsLibraryAst = SsoParser.Parse(reader.ReadToEnd());
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Error parsing game data from {workspace.DefaultOtherPakPath}: {ex.Message}";
                return false;
            }
        }

        private void RegisterArmorPiece(string uid, string section, string classOrigin, SsoObject? regionObj)
        {
            if (string.IsNullOrWhiteSpace(uid)) return;

            var piece = AllArmorPieces.Find(p => p.Uid.Equals(uid, StringComparison.OrdinalIgnoreCase));
            if (piece == null)
            {
                var (displayName, tierBadge, tierColor) = ParsePieceMetadata(uid, section);
                piece = new ArmorPieceItem
                {
                    Uid = uid,
                    DisplayName = displayName,
                    Section = section,
                    ClassOrigin = classOrigin,
                    TierBadge = tierBadge,
                    TierColor = tierColor
                };
                AllArmorPieces.Add(piece);
            }
            else if (string.IsNullOrEmpty(piece.ClassOrigin) || piece.ClassOrigin == "Shared")
            {
                piece.ClassOrigin = classOrigin;
            }

            if (regionObj != null && regionObj["meshes"] is SsoArray meshesArr)
            {
                foreach (var elem in meshesArr.Elements)
                {
                    if (elem is SsoValue sv && !string.IsNullOrWhiteSpace(sv.RawValue))
                    {
                        if (!piece.Meshes.Contains(sv.RawValue, StringComparer.OrdinalIgnoreCase))
                        {
                            piece.Meshes.Add(sv.RawValue);
                        }
                    }
                }
            }
        }

        public static (string DisplayName, string TierBadge, string TierColor) ParsePieceMetadata(string uid, string section)
        {
            if (string.IsNullOrWhiteSpace(uid)) return ("None", "", "#8E9AA8");

            string lower = uid.ToLowerInvariant();
            if (lower.Contains("empty") || lower.Contains("no_helmet") || lower == "none")
            {
                if (section.Equals("HELMET", StringComparison.OrdinalIgnoreCase))
                    return ("Bare Head (No Helmet)", "BASE", "#8E9AA8");
                return ("None (Unequipped)", "BASE", "#8E9AA8");
            }

            // Strip slot prefixes
            string stripped = uid;
            string[] prefixes = {
                "helmet_soldier_", "chest_soldier_", "backpack_soldier_", "left_pauldron_soldier_",
                "right_pauldron_soldier_", "left_gauntlet_soldier_", "right_gauntlet_soldier_",
                "left_greave_soldier_", "right_greave_soldier_", "vanity_soldier_", "cloth_soldier_",
                "coat_soldier_", "halo_soldier_",
                "helmet_sniper_", "chest_sniper_", "backpack_sniper_", "left_pauldron_sniper_",
                "right_pauldron_sniper_", "left_gauntlet_sniper_", "right_gauntlet_sniper_",
                "left_greave_sniper_", "right_greave_sniper_",
                "helmet_support_", "chest_support_", "backpack_support_", "left_pauldron_support_",
                "right_pauldron_support_", "left_gauntlet_support_", "right_gauntlet_support_",
                "left_greave_support_", "right_greave_support_",
                "helmet_tank_", "chest_tank_", "backpack_tank_", "left_pauldron_tank_",
                "right_pauldron_tank_", "left_gauntlet_tank_", "right_gauntlet_tank_",
                "left_greave_tank_", "right_greave_tank_",
                "helmet_raider_", "chest_raider_", "backpack_raider_", "left_pauldron_raider_",
                "right_pauldron_raider_", "left_gauntlet_raider_", "right_gauntlet_raider_",
                "left_greave_raider_", "right_greave_raider_",
                "helmet_berserk_", "chest_berserk_", "backpack_berserk_", "left_pauldron_berserk_",
                "right_pauldron_berserk_", "left_gauntlet_berserk_", "right_gauntlet_berserk_",
                "left_greave_berserk_", "right_greave_berserk_",
                "helmet_", "chest_", "backpack_", "left_pauldron_", "right_pauldron_",
                "left_gauntlet_", "right_gauntlet_", "left_greave_", "right_greave_",
                "vanity_", "cloth_", "coat_", "halo_", "antenna_",
                "soldier_", "sniper_", "support_", "tank_", "raider_", "berserk_"
            };

            foreach (var p in prefixes)
            {
                if (stripped.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                {
                    stripped = stripped.Substring(p.Length);
                    break;
                }
            }

            // Tiers
            if (stripped.StartsWith("common_", StringComparison.OrdinalIgnoreCase) || stripped.Equals("common", StringComparison.OrdinalIgnoreCase))
            {
                string num = stripped.StartsWith("common_") ? stripped.Substring(7) : "1";
                return ($"Standard Mk X (Type {FormatRoman(num)})", "STANDARD", "#A0ABC0");
            }
            if (stripped.StartsWith("mastercrafted_", StringComparison.OrdinalIgnoreCase) || stripped.Equals("mastercrafted", StringComparison.OrdinalIgnoreCase))
            {
                string num = stripped.StartsWith("mastercrafted_") ? stripped.Substring(14) : "1";
                return ($"Master-Crafted (Type {FormatRoman(num)})", "MASTER-CRAFTED", "#4EA8DE");
            }
            if (stripped.StartsWith("atrificer_", StringComparison.OrdinalIgnoreCase) || stripped.StartsWith("artificer_", StringComparison.OrdinalIgnoreCase))
            {
                string num = stripped.Substring(stripped.IndexOf('_') + 1);
                return ($"Artificer Armour (Type {FormatRoman(num)})", "ARTIFICER", "#BD53ED");
            }
            if (stripped.StartsWith("relic_", StringComparison.OrdinalIgnoreCase) || stripped.Equals("relic", StringComparison.OrdinalIgnoreCase))
            {
                string num = stripped.StartsWith("relic_") ? stripped.Substring(6) : "1";
                return ($"Relic Armour (Type {FormatRoman(num)})", "RELIC", "#FF9F1C");
            }
            if (stripped.StartsWith("prestige_", StringComparison.OrdinalIgnoreCase))
            {
                string num = stripped.Substring(9);
                return ($"Veteran Prestige (Type {FormatRoman(num)})", "PRESTIGE", "#E5B638");
            }
            if (stripped.StartsWith("champion_", StringComparison.OrdinalIgnoreCase))
            {
                string num = stripped.Substring(9);
                return ($"Chapter Champion (Type {FormatRoman(num)})", "CHAMPION", "#E5B638");
            }

            if (stripped.Contains("preorder", StringComparison.OrdinalIgnoreCase))
            {
                return ("Astartes Honours (DLC)", "EXCLUSIVE", "#2EC4B6");
            }
            if (stripped.Contains("ultramarine", StringComparison.OrdinalIgnoreCase))
            {
                return ("Ultramarines Champion", "DLC", "#4EA8DE");
            }
            if (stripped.Contains("dark_angels", StringComparison.OrdinalIgnoreCase))
            {
                return ("Dark Angels Veteran", "DLC", "#2EC4B6");
            }

            // Clean title casing fallback
            var parts = stripped.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int n))
                {
                    parts[i] = $"(Type {FormatRoman(n.ToString())})";
                }
                else if (parts[i].Equals("atrificer", StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = "Artificer";
                }
                else if (parts[i].Length > 0)
                {
                    parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
                }
            }
            string cleaned = string.Join(" ", parts);
            return (cleaned, "CUSTOM", "#A0ABC0");
        }

        private static string FormatRoman(string s)
        {
            if (int.TryParse(s, out int n))
            {
                return n switch
                {
                    1 => "I",
                    2 => "II",
                    3 => "III",
                    4 => "IV",
                    5 => "V",
                    6 => "VI",
                    7 => "VII",
                    8 => "VIII",
                    _ => n.ToString()
                };
            }
            return s.ToUpperInvariant();
        }

        public static string FormatPieceDisplayName(string uid, string section = "")
        {
            return ParsePieceMetadata(uid, section).DisplayName;
        }

        private static string FormatColorDisplayName(string id)
        {
            return id.Replace("_", " ");
        }

        private static string FormatDecalDisplayName(string id)
        {
            return id.Replace("Left_Pau_Decal_", "")
                     .Replace("Right_Pau_Decal_", "")
                     .Replace("Left_Greave_Decal_", "")
                     .Replace("Right_Greave_Decal_", "")
                     .Replace("_", " ");
        }

        private static string FormatPatternDisplayName(string id)
        {
            return id.Replace("_", " ");
        }
    }
}
