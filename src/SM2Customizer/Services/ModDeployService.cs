using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using SM2Customizer.Core;
using SM2Customizer.Models;

namespace SM2Customizer.Services
{
    public class ModDeployOptions
    {
        public bool UnlockAllColorsAndDecals { get; set; } = true;
        public bool UnlockCrossClassSlots { get; set; } = true;
        public bool UnlockAllPatterns { get; set; } = true;
    }

    public class ModDeployService
    {
        public bool Deploy(
            GameWorkspace workspace,
            GameScannerService scanner,
            List<ClassPresetModel> userPresets,
            List<PaintColorModel> userColors,
            List<DecalModel> userDecals,
            ModDeployOptions options,
            out string message)
        {
            message = string.Empty;

            if (!workspace.IsValid)
            {
                message = "Invalid Space Marine 2 game directory.";
                return false;
            }

            try
            {
                workspace.EnsureModsDirectory();

                // If existing pak exists, delete it first
                if (File.Exists(workspace.ModPakPath))
                {
                    File.Delete(workspace.ModPakPath);
                }

                // Prepare modified SSO files
                var filesToPack = new Dictionary<string, string>();

                // 1. Patch character_customization_library.sso
                if (scanner.CharacterCustomizationLibraryAst != null)
                {
                    PatchCharacterCustomizationLibrary(scanner.CharacterCustomizationLibraryAst, userPresets);
                    filesToPack["ssl/body/customization/character_customization_library.sso"] =
                        scanner.CharacterCustomizationLibraryAst.Serialize();
                }

                // 2. Patch armor_color_library.sso
                if (scanner.ArmorColorLibraryAst != null)
                {
                    PatchArmorColorLibrary(scanner.ArmorColorLibraryAst, userColors);
                    filesToPack["ssl/body/customization/armor_color_library.sso"] =
                        scanner.ArmorColorLibraryAst.Serialize();
                }

                // 3. Patch customization_decal_library.sso
                if (scanner.CustomizationDecalLibraryAst != null)
                {
                    PatchCustomizationDecalLibrary(scanner.CustomizationDecalLibraryAst, userDecals);
                    filesToPack["ssl/body/customization/customization_decal_library.sso"] =
                        scanner.CustomizationDecalLibraryAst.Serialize();
                }

                // 4. Cross-class body section unlocks
                if (options.UnlockCrossClassSlots && scanner.BodySectionAvailabilityRulesAst != null)
                {
                    PatchCrossClassAvailability(scanner.BodySectionAvailabilityRulesAst);
                    filesToPack["ssl/body/customization/character_body_section_availability_ps/customization_body_section_availability_rules_by_mastery.sso"] =
                        scanner.BodySectionAvailabilityRulesAst.Serialize();
                }

                // 5. Unlock all colors and decals by default
                if (options.UnlockAllColorsAndDecals && scanner.DefaultHeraldryItemsLibraryAst != null)
                {
                    PatchDefaultHeraldryUnlocks(scanner.DefaultHeraldryItemsLibraryAst, userColors, userDecals);
                    filesToPack["ssl/body/customization/heraldry_progression/default_heraldry_items_library.sso"] =
                        scanner.DefaultHeraldryItemsLibraryAst.Serialize();
                }

                // 6. Unlock all patterns for all classes
                if (options.UnlockAllPatterns && scanner.ArmorPatternLibraryAst != null)
                {
                    PatchArmorPatterns(scanner.ArmorPatternLibraryAst);
                    filesToPack["ssl/body/customization/armor_pattern_library.sso"] =
                        scanner.ArmorPatternLibraryAst.Serialize();
                }

                // Pack into zip / .pak
                using (var zipStream = new FileStream(workspace.ModPakPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, false))
                {
                    foreach (var kvp in filesToPack)
                    {
                        var entry = archive.CreateEntry(kvp.Key, CompressionLevel.Optimal);
                        using var entryStream = entry.Open();
                        using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
                        writer.Write(kvp.Value);
                    }
                }

                message = $"Successfully built and installed {GameWorkspace.ModPakFileName} into:\n{workspace.ModsDirectory}\n({filesToPack.Count} customized game systems injected)";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to deploy mod: {ex.Message}";
                return false;
            }
        }

        public bool RestoreVanilla(GameWorkspace workspace, out string message)
        {
            try
            {
                if (File.Exists(workspace.ModPakPath))
                {
                    File.Delete(workspace.ModPakPath);
                    message = "Successfully removed SM2Customizer.pak! Vanilla customization restored.";
                    return true;
                }
                else
                {
                    message = "No custom mod was installed. Game is already in vanilla state.";
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = $"Error removing mod: {ex.Message}";
                return false;
            }
        }

        private static void PatchCharacterCustomizationLibrary(SsoObject ast, List<ClassPresetModel> userPresets)
        {
            if (ast["appearancesByTeams"] is not SsoObject teamsObj) return;

            foreach (var preset in userPresets)
            {
                if (teamsObj[preset.Team] is not SsoObject teamObj) continue;
                if (teamObj["customizationPresets"] is not SsoObject presetsObj) continue;
                if (presetsObj[preset.ClassKey] is not SsoObject classObj) continue;
                if (classObj["armorPresets"] is not SsoObject armorPresetsObj) continue;
                if (armorPresetsObj[preset.PresetKey] is not SsoObject targetPreset) continue;

                var uidsObj = targetPreset.GetOrCreateObject("bodySectionRegionUids");
                foreach (var piece in preset.SectionPieces)
                {
                    var slotObj = uidsObj.GetOrCreateObject(piece.Key);
                    slotObj.SetString("__value", piece.Value);
                }
            }
        }

        private static void PatchArmorColorLibrary(SsoObject ast, List<PaintColorModel> userColors)
        {
            var layersObj = ast.GetOrCreateObject("primaryShaderLayers");

            foreach (var col in userColors)
            {
                var colObj = layersObj.GetOrCreateObject(col.Id);
                colObj.SetBool("isMetallic", col.IsMetallic);
                colObj.SetString("__type", "CustomizationLayerParamsProject");

                // Tint
                var tintObj = colObj.GetOrCreateObject("tint");
                tintObj.SetString("__type", "CustomizationParamColor");
                var tintVal = tintObj.GetOrCreateObject("value");
                tintVal.SetInt("a", col.A);
                tintVal.SetInt("r", col.R);
                tintVal.SetInt("g", col.G);
                tintVal.SetInt("b", col.B);
                tintVal.SetString("__type", "Color");

                // Specular
                var specObj = colObj.GetOrCreateObject("spec_metalnessTint");
                specObj.SetString("__type", "CustomizationParamColor");
                var specVal = specObj.GetOrCreateObject("value");
                specVal.SetInt("a", col.SpecA);
                specVal.SetInt("r", col.SpecR);
                specVal.SetInt("g", col.SpecG);
                specVal.SetInt("b", col.SpecB);
                specVal.SetString("__type", "Color");

                // Pattern
                var patObj = colObj.GetOrCreateObject("patternTint");
                patObj.SetString("__type", "CustomizationParamColor");
                var patVal = patObj.GetOrCreateObject("value");
                patVal.SetInt("a", col.PatternA);
                patVal.SetInt("r", col.PatternR);
                patVal.SetInt("g", col.PatternG);
                patVal.SetInt("b", col.PatternB);
                patVal.SetString("__type", "Color");
            }
        }

        private static void PatchCustomizationDecalLibrary(SsoObject ast, List<DecalModel> userDecals)
        {
            if (ast["categorizedDecals"] is not SsoObject catObj) return;

            foreach (var decal in userDecals)
            {
                if (catObj[decal.Section] is not SsoObject secObj) continue;
                if (secObj["items"] is not SsoObject itemsObj) continue;

                var dObj = itemsObj.GetOrCreateObject(decal.Id);
                dObj.SetString("material", decal.Material);
                dObj.SetInt("colorsNumber", decal.ColorsNumber);
                dObj.SetString("__type", "CustomizationDecal");

                var colorObj = dObj.GetOrCreateObject("defaultColoring");
                colorObj.SetString("primary", decal.PrimaryColor);
                colorObj.SetString("secondary", decal.SecondaryColor);
                colorObj.SetString("tertiary", decal.TertiaryColor);
                colorObj.SetString("__type", "CustomizationDecalColoringInfo");
            }
        }

        private static void PatchCrossClassAvailability(SsoObject ast)
        {
            if (ast["bodySectionAvailabilityByMastery"] is not SsoObject byMasteryObj) return;

            foreach (var masteryProp in byMasteryObj.Properties)
            {
                if (masteryProp.Value is SsoObject masteryObj &&
                    masteryObj["availabilityRules"] is SsoObject rulesObj)
                {
                    foreach (var ruleProp in rulesObj.Properties)
                    {
                        if (ruleProp.Value is SsoObject ruleObj)
                        {
                            ruleObj.SetBool("isAvailable", true);
                        }
                    }
                }
            }
        }

        private static void PatchDefaultHeraldryUnlocks(SsoObject ast, List<PaintColorModel> colors, List<DecalModel> decals)
        {
            var primaryColors = ast.GetOrCreateArray("primaryColors");
            var secondaryColors = ast.GetOrCreateArray("secondaryColors");
            var tertiaryColors = ast.GetOrCreateArray("tertiaryColors");
            var decalArr = ast.GetOrCreateArray("decals");

            foreach (var col in colors)
            {
                if (!primaryColors.ContainsString(col.Id)) primaryColors.AddString(col.Id);
                if (!secondaryColors.ContainsString(col.Id)) secondaryColors.AddString(col.Id);
                if (!tertiaryColors.ContainsString(col.Id)) tertiaryColors.AddString(col.Id);
            }

            foreach (var d in decals)
            {
                if (!decalArr.ContainsString(d.Id)) decalArr.AddString(d.Id);
            }
        }

        private static void PatchArmorPatterns(SsoObject ast)
        {
            if (ast["patterns"] is not SsoObject patternsObj) return;

            foreach (var patProp in patternsObj.Properties)
            {
                if (patProp.Value is SsoObject patObj &&
                    patObj["availabilityRules"] is SsoObject rulesObj)
                {
                    foreach (var ruleProp in rulesObj.Properties)
                    {
                        if (ruleProp.Value is SsoObject ruleObj &&
                            ruleObj["forMasteriesByTeam"] is SsoObject teamsObj)
                        {
                            foreach (var teamProp in teamsObj.Properties)
                            {
                                if (teamProp.Value is SsoObject teamObj &&
                                    teamObj["masteryAvailability"] is SsoObject availObj)
                                {
                                    foreach (var availProp in availObj.Properties)
                                    {
                                        if (availProp.Value is SsoObject vObj)
                                        {
                                            vObj.SetBool("__value", true);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
