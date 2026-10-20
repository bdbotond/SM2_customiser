using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using SM2Customizer.Core;

namespace SM2Customizer.Services
{
    public enum ModelSourceKind
    {
        None,
        Explicit,
        HelmetFallback,
        ShieldFallback,
        SoldierStoryFallback
    }

    public class GameModelService
    {
        private readonly ConcurrentDictionary<string, string> _modelCache = new(StringComparer.OrdinalIgnoreCase);

        public bool TryGetSlotGameMesh(GameWorkspace workspace, string slotName, out string objData)
        {
            return TryGetSlotGameMesh(workspace, slotName, null, null, null, out objData, out _, out _);
        }

        public bool TryGetSlotGameMesh(GameWorkspace workspace, string slotName, string? pieceUid, out string objData)
        {
            return TryGetSlotGameMesh(workspace, slotName, pieceUid, null, null, out objData, out _, out _);
        }

        public bool TryGetSlotGameMesh(GameWorkspace workspace, string slotName, string? pieceUid, IReadOnlyCollection<string>? meshNames, string? classKey, out string objData)
        {
            return TryGetSlotGameMesh(workspace, slotName, pieceUid, meshNames, classKey, out objData, out _, out _);
        }

        public bool TryGetSlotGameMesh(GameWorkspace workspace, string slotName, string? pieceUid, IReadOnlyCollection<string>? meshNames, string? classKey, out string objData, out ModelSourceKind sourceKind, out string sourceDetail)
        {
            return TryGetSlotGameMesh(workspace, slotName, pieceUid, meshNames, classKey, out objData, out sourceKind, out sourceDetail, out _);
        }

        public bool TryGetSlotGameMesh(GameWorkspace workspace, string slotName, string? pieceUid, IReadOnlyCollection<string>? meshNames, string? classKey, out string objData, out ModelSourceKind sourceKind, out string sourceDetail, out MeshDiag? diag)
        {
            objData = string.Empty;
            sourceKind = ModelSourceKind.None;
            sourceDetail = string.Empty;
            diag = null;
            if (workspace == null || !workspace.IsValid) return false;

            string normalizedSlot = slotName.ToUpperInvariant();
            string cacheKey = $"{normalizedSlot}:{(pieceUid ?? "DEFAULT")}:{(classKey ?? "ALL")}";

            // Check in-memory cache first
            if (_modelCache.TryGetValue(cacheKey, out var cached))
            {
                objData = cached;
                sourceKind = ModelSourceKind.Explicit;
                sourceDetail = "Cache";
                return true;
            }

            string defaultDir = workspace.PaksDirectory;
            if (!Directory.Exists(defaultDir)) return false;

            var candidates = GetTemplateCandidates(defaultDir, classKey);

            // 1. If explicit mesh names provided, resolve across candidates (combining parts if scattered)
            if (meshNames != null && meshNames.Count > 0)
            {
                var remaining = new HashSet<string>(meshNames, StringComparer.OrdinalIgnoreCase);
                var matchedObjs = new List<string>();
                var matchedSources = new List<string>();

                foreach (var (pakFile, prefix) in candidates)
                {
                    if (remaining.Count == 0) break;
                    if (File.Exists(pakFile))
                    {
                        string? partObj = TplMeshReader.ExtractMeshByNodeNames(pakFile, prefix, remaining, out var partDiag);
                        if (!string.IsNullOrEmpty(partObj) && partDiag.Verts > 0)
                        {
                            matchedObjs.Add(partObj);
                            matchedSources.Add(prefix);
                            diag = (diag == null) ? partDiag : CombineDiag(diag, partDiag);

                            foreach (var m in meshNames)
                            {
                                if (!partDiag.MissingNames.Contains(m, StringComparer.OrdinalIgnoreCase))
                                {
                                    remaining.Remove(m);
                                }
                            }
                        }
                    }
                }

                if (matchedObjs.Count > 0)
                {
                    string combined = (matchedObjs.Count == 1) ? matchedObjs[0] : CombineObjStrings(matchedObjs);

                    // Ensure Full Marine Anatomy:
                    // If a chest or leg custom piece is only a decorative accessory or partial piece,
                    // augment it with base body anatomy so the marine is never missing a chestplate or half leg.
                    combined = AugmentWithBaseAnatomyIfNeeded(combined, defaultDir, normalizedSlot, ref diag);

                    _modelCache[cacheKey] = combined;
                    objData = combined;
                    sourceKind = ModelSourceKind.Explicit;
                    sourceDetail = string.Join(" + ", matchedSources);
                    return true;
                }
            }

            // 2. Fall back to standard canonical constructor meshes (cc_sm_constructor_soldier)
            var defaultMeshNames = GetDefaultMeshNamesForSlot(normalizedSlot);
            if (defaultMeshNames != null && defaultMeshNames.Count > 0)
            {
                string soldierConstPak = Path.Combine(defaultDir, "default_tpl_3.pak");
                string soldierConstPrefix = "tpl/cc_sm_constructor_soldier.tpl/cc_sm_constructor_soldier";
                if (File.Exists(soldierConstPak))
                {
                    string? extracted = TplMeshReader.ExtractMeshByNodeNames(soldierConstPak, soldierConstPrefix, defaultMeshNames, out var d);
                    if (!string.IsNullOrEmpty(extracted))
                    {
                        _modelCache[cacheKey] = extracted;
                        objData = extracted;
                        sourceKind = ModelSourceKind.SoldierStoryFallback;
                        sourceDetail = soldierConstPrefix;
                        diag = d;
                        return true;
                    }
                }

                // Fall back to story meshes in default_tpl_0.pak (cc_sm_soldier_story)
                string soldierStoryPak = Path.Combine(defaultDir, "default_tpl_0.pak");
                string soldierStoryPrefix = "tpl/cc_sm_soldier_story.tpl/cc_sm_soldier_story";
                if (File.Exists(soldierStoryPak))
                {
                    string? extracted = TplMeshReader.ExtractMeshByNodeNames(soldierStoryPak, soldierStoryPrefix, defaultMeshNames, out var d);
                    if (!string.IsNullOrEmpty(extracted))
                    {
                        _modelCache[cacheKey] = extracted;
                        objData = extracted;
                        sourceKind = ModelSourceKind.SoldierStoryFallback;
                        sourceDetail = soldierStoryPrefix;
                        diag = d;
                        return true;
                    }
                }
            }

            return false;
        }

        private static string AugmentWithBaseAnatomyIfNeeded(string currentObj, string defaultDir, string slot, ref MeshDiag? diag)
        {
            if (diag == null) return currentObj;

            // Anatomy criteria:
            // CHEST: Must cover height >= 1.2m and rangeY within [0.55..2.40m].
            // GREAVES: Must touch ground (minY <= 0.10m) and reach knee (maxY >= 0.85m).
            // GAUNTLETS: Must cover forearm and hand (height >= 0.35m).
            bool needsAugment = false;
            List<string>? baseNames = null;

            if (slot == "CHEST" && ((diag.Max.Y - diag.Min.Y) < 1.1f || diag.Min.Y > 0.9f || diag.Max.Y < 2.1f))
            {
                needsAugment = true;
                baseNames = new List<string> { "soldier_torso_base", "soldier_cuirasse_01", "soldier_neck_01" };
            }
            else if ((slot == "LEFT_GREAVE" || slot == "RIGHT_GREAVE") && (diag.Min.Y > 0.10f || diag.Max.Y < 0.85f))
            {
                needsAugment = true;
                baseNames = (slot == "LEFT_GREAVE")
                    ? new List<string> { "soldier_boot_L_01", "soldier_shin_L_01", "soldier_knee_L_01" }
                    : new List<string> { "soldier_boot_R_01", "soldier_shin_R_01", "soldier_knee_R_01" };
            }
            else if ((slot == "LEFT_GAUNTLET" || slot == "RIGHT_GAUNTLET") && ((diag.Max.Y - diag.Min.Y) < 0.35f))
            {
                needsAugment = true;
                baseNames = (slot == "LEFT_GAUNTLET")
                    ? new List<string> { "soldier_hand_L_01", "soldier_arm_L_01" }
                    : new List<string> { "soldier_hand_R_01", "soldier_arm_R_01" };
            }

            if (needsAugment && baseNames != null)
            {
                string soldierConstPak = Path.Combine(defaultDir, "default_tpl_3.pak");
                string soldierConstPrefix = "tpl/cc_sm_constructor_soldier.tpl/cc_sm_constructor_soldier";
                if (File.Exists(soldierConstPak))
                {
                    string? baseObj = TplMeshReader.ExtractMeshByNodeNames(soldierConstPak, soldierConstPrefix, baseNames, out var baseDiag);
                    if (!string.IsNullOrEmpty(baseObj) && baseDiag.Verts > 0)
                    {
                        diag = CombineDiag(diag, baseDiag);
                        return CombineObjStrings(new[] { currentObj, baseObj });
                    }
                }
            }

            return currentObj;
        }

        public static string CombineObjStrings(IEnumerable<string> objs)
        {
            var sb = new StringBuilder();
            int vOffset = 0;

            foreach (var obj in objs)
            {
                if (string.IsNullOrWhiteSpace(obj)) continue;
                using var reader = new StringReader(obj);
                string? line;
                int currentObjVerts = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("v "))
                    {
                        currentObjVerts++;
                        sb.AppendLine(line);
                    }
                    else if (line.StartsWith("f "))
                    {
                        if (vOffset == 0)
                        {
                            sb.AppendLine(line);
                        }
                        else
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            sb.Append("f");
                            for (int i = 1; i < parts.Length; i++)
                            {
                                string p = parts[i];
                                if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vi))
                                {
                                    sb.Append(' ').Append(vi + vOffset);
                                }
                                else if (p.Contains("//"))
                                {
                                    var sub = p.Split("//");
                                    int v = int.Parse(sub[0], CultureInfo.InvariantCulture) + vOffset;
                                    sb.Append($" {v}//{sub[1]}");
                                }
                                else if (p.Contains("/"))
                                {
                                    var sub = p.Split('/');
                                    int v = int.Parse(sub[0], CultureInfo.InvariantCulture) + vOffset;
                                    sb.Append($" {v}");
                                }
                                else
                                {
                                    sb.Append(' ').Append(p);
                                }
                            }
                            sb.AppendLine();
                        }
                    }
                    else if (!line.StartsWith("#"))
                    {
                        sb.AppendLine(line);
                    }
                }

                vOffset += currentObjVerts;
            }

            return sb.ToString();
        }

        private static MeshDiag CombineDiag(MeshDiag a, MeshDiag b)
        {
            var min = new Vector3(Math.Min(a.Min.X, b.Min.X), Math.Min(a.Min.Y, b.Min.Y), Math.Min(a.Min.Z, b.Min.Z));
            var max = new Vector3(Math.Max(a.Max.X, b.Max.X), Math.Max(a.Max.Y, b.Max.Y), Math.Max(a.Max.Z, b.Max.Z));
            return new MeshDiag(
                a.Prefix,
                a.NodesMatched + b.NodesMatched,
                a.MissingNames.Intersect(b.MissingNames, StringComparer.OrdinalIgnoreCase).ToArray(),
                a.Verts + b.Verts,
                a.Tris + b.Tris,
                a.IndexOutOfRange + b.IndexOutOfRange,
                a.Degenerate + b.Degenerate,
                min,
                max,
                Math.Max(a.MaxEdge, b.MaxEdge),
                (a.MedianEdge + b.MedianEdge) * 0.5f);
        }

        private static List<(string PakFile, string Prefix)> GetTemplateCandidates(string defaultDir, string? classKey)
        {
            var list = new List<(string PakFile, string Prefix)>();
            string normClass = classKey?.ToUpperInvariant() ?? "PVE_PVP_SOLDIER";

            switch (normClass)
            {
                case "PVE_PVP_SNIPER":
                    list.Add((Path.Combine(defaultDir, "default_tpl_5.pak"), "tpl/cc_sm_constructor_sniper.tpl/cc_sm_constructor_sniper"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_5.pak"), "tpl/cc_sm_champion_sniper_01.tpl/cc_sm_champion_sniper_01"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_4.pak"), "tpl/cc_sm_sniper_story.tpl/cc_sm_sniper_story"));
                    break;

                case "PVE_PVP_SUPPORT": // Bulwark
                    list.Add((Path.Combine(defaultDir, "default_tpl_2.pak"), "tpl/cc_sm_constructor_support.tpl/cc_sm_constructor_support"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_1.pak"), "tpl/cc_sm_champion_support_01.tpl/cc_sm_champion_support_01"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_2.pak"), "tpl/cc_sm_champion_support_02.tpl/cc_sm_champion_support_02"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_6.pak"), "tpl/cc_sm_support_story.tpl/cc_sm_support_story"));
                    break;

                case "PVE_PVP_TANK": // Heavy
                    list.Add((Path.Combine(defaultDir, "default_tpl_8.pak"), "tpl/cc_sm_constructor_tank.tpl/cc_sm_constructor_tank"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_0.pak"), "tpl/cc_sm_champion_tank_01.tpl/cc_sm_champion_tank_01"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_1.pak"), "tpl/cc_sm_champion_tank_02.tpl/cc_sm_champion_tank_02"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_6.pak"), "tpl/cc_sm_champion_tank_03.tpl/cc_sm_champion_tank_03"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_7.pak"), "tpl/cc_sm_tank_story.tpl/cc_sm_tank_story"));
                    break;

                case "PVE_PVP_RAIDER": // Vanguard
                    list.Add((Path.Combine(defaultDir, "default_tpl_3.pak"), "tpl/cc_sm_constructor_raider.tpl/cc_sm_constructor_raider"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_1.pak"), "tpl/cc_sm_champion_raider_01.tpl/cc_sm_champion_raider_01"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_5.pak"), "tpl/cc_sm_champion_raider_02.tpl/cc_sm_champion_raider_02"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_5.pak"), "tpl/cc_sm_raider_story.tpl/cc_sm_raider_story"));
                    break;

                case "PVE_PVP_BERSERK": // Assault
                    list.Add((Path.Combine(defaultDir, "default_tpl_4.pak"), "tpl/cc_sm_constructor_berserk.tpl/cc_sm_constructor_berserk"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_1.pak"), "tpl/cc_sm_champion_berserk_01.tpl/cc_sm_champion_berserk_01"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_3.pak"), "tpl/cc_sm_champion_berserk_02.tpl/cc_sm_champion_berserk_02"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_3.pak"), "tpl/cc_sm_berserk_story.tpl/cc_sm_berserk_story"));
                    break;

                case "PVE_PVP_SOLDIER": // Tactical
                default:
                    list.Add((Path.Combine(defaultDir, "default_tpl_3.pak"), "tpl/cc_sm_constructor_soldier.tpl/cc_sm_constructor_soldier"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_6.pak"), "tpl/cc_sm_champion_soldier_01.tpl/cc_sm_champion_soldier_01"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_8.pak"), "tpl/cc_sm_champion_soldier_02.tpl/cc_sm_champion_soldier_02"));
                    list.Add((Path.Combine(defaultDir, "default_tpl_0.pak"), "tpl/cc_sm_soldier_story.tpl/cc_sm_soldier_story"));
                    break;
            }

            // Cross-class & additional constructor candidates
            var allClasses = new (string pak, string prefix)[]
            {
                (Path.Combine(defaultDir, "default_tpl_3.pak"), "tpl/cc_sm_constructor_soldier.tpl/cc_sm_constructor_soldier"),
                (Path.Combine(defaultDir, "default_tpl_4.pak"), "tpl/cc_sm_constructor_berserk.tpl/cc_sm_constructor_berserk"),
                (Path.Combine(defaultDir, "default_tpl_2.pak"), "tpl/cc_sm_constructor_support.tpl/cc_sm_constructor_support"),
                (Path.Combine(defaultDir, "default_tpl_8.pak"), "tpl/cc_sm_constructor_tank.tpl/cc_sm_constructor_tank"),
                (Path.Combine(defaultDir, "default_tpl_3.pak"), "tpl/cc_sm_constructor_raider.tpl/cc_sm_constructor_raider"),
                (Path.Combine(defaultDir, "default_tpl_5.pak"), "tpl/cc_sm_constructor_sniper.tpl/cc_sm_constructor_sniper"),
                (Path.Combine(defaultDir, "default_tpl_0.pak"), "tpl/cc_sm_constructor_victrix_guard.tpl/cc_sm_constructor_victrix_guard")
            };

            foreach (var c in allClasses)
            {
                if (!list.Exists(x => x.Prefix == c.prefix))
                {
                    list.Add(c);
                }
            }

            return list;
        }

        private static List<string>? GetDefaultMeshNamesForSlot(string slotName)
        {
            return slotName switch
            {
                "HELMET" => new List<string> { "soldier_helmet_01_grp", "soldier_helmet_01", "soldier_helmet_01_glass", "helmet_shared_base", "helmet_shared_base_lenses", "helmet_shared_base_glass" },
                "CHEST" => new List<string> { "soldier_cuirasse_01", "soldier_cuirasse_plate_01", "soldier_torso_base", "soldier_neck_01", "soldier_belt_bag_01", "soldier_belt_bag_02", "soldier_side_plate_L_01_grp", "soldier_side_plate_R_01_grp", "soldier_belt_base_grp", "soldier_belt_badge_01" },
                "BACKPACK" => new List<string> { "soldier_backpack", "soldier_backpack_cap_01" },
                "LEFT_PAULDRON" => new List<string> { "soldier_shoulder_L_01" },
                "RIGHT_PAULDRON" => new List<string> { "soldier_shoulder_R_01" },
                "LEFT_GAUNTLET" => new List<string> { "soldier_hand_L_01", "soldier_arm_L_01" },
                "RIGHT_GAUNTLET" => new List<string> { "soldier_hand_R_01", "soldier_arm_R_01" },
                "LEFT_GREAVE" => new List<string> { "soldier_knee_L_01", "soldier_shin_L_01", "soldier_boot_L_01" },
                "RIGHT_GREAVE" => new List<string> { "soldier_knee_R_01", "soldier_shin_R_01", "soldier_boot_R_01" },
                _ => null
            };
        }

        public void ClearCache()
        {
            _modelCache.Clear();
            TplMeshReader.ClearCache();
        }
    }
}