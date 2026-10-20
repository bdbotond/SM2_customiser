using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using SM2Customizer.Core;
using SM2Customizer.Models;
using SM2Customizer.Services;

namespace VerifyCustomizer
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "probe")
            {
                RunProbe();
                return 0;
            }
            if (args.Length > 0 && args[0] == "tpl_probe")
            {
                RunTplProbe();
                return 0;
            }
            if (args.Length > 0 && args[0] == "ogm_test")
            {
                RunOgmTest();
                return 0;
            }

            Console.WriteLine("==================================================");
            Console.WriteLine(" SM2Customizer Automated Verification Test        ");
            Console.WriteLine("==================================================");

            string gamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Space Marine 2";
            Console.WriteLine($"Target Game Path: {gamePath}");

            var workspace = new GameWorkspace(gamePath);
            if (!workspace.IsValid)
            {
                if (args.Contains("--require-game"))
                {
                    Console.WriteLine($"FAIL: Workspace invalid! Path: {workspace.DefaultOtherPakPath}");
                    return 1;
                }
                else
                {
                    Console.WriteLine($"[SKIP] Game installation not found at '{gamePath}'. Running offline test suite...");
                    return RunOfflineChecks();
                }
            }
            Console.WriteLine($"[PASS] Workspace validated. Default Other Pak: {workspace.DefaultOtherPakPath}");

            var scanner = new GameScannerService();
            if (!scanner.Scan(workspace, out string scanError))
            {
                Console.WriteLine($"FAIL: Scanner error: {scanError}");
                return 2;
            }

            Console.WriteLine($"[PASS] Scan completed successfully!");
            Console.WriteLine($"       Body Sections: {scanner.BodySections.Count}");
            Console.WriteLine($"       Presets:       {scanner.Presets.Count}");
            Console.WriteLine($"       Armor Pieces:  {scanner.AllArmorPieces.Count}");
            Console.WriteLine($"       Colors:        {scanner.Colors.Count}");
            Console.WriteLine($"       Decals:        {scanner.Decals.Count}");
            Console.WriteLine($"       Patterns:      {scanner.Patterns.Count}");

            var gms = new GameModelService();
            Console.WriteLine("\n--- Testing Chaos Pieces with Updated GameModelService ---");
            gms.ClearCache();
            var chaosPieces = scanner.AllArmorPieces.Where(p => p.Uid.Contains("ch_") || p.Uid.Contains("chaos")).ToList();
            int oversizeCount = 0;
            foreach (var cp in chaosPieces)
            {
                bool ok = gms.TryGetSlotGameMesh(workspace, cp.Section, cp.Uid, cp.Meshes, "PVE_PVP_SOLDIER", out var objData, out var srcKind, out var srcDetail, out var diag);
                if (diag != null)
                {
                    float h = diag.Max.Y - diag.Min.Y;
                    float w = diag.Max.X - diag.Min.X;
                    float d = diag.Max.Z - diag.Min.Z;
                    if (w > 2.5f || h > 3.0f || d > 2.5f || cp.Uid.Contains("sphere"))
                    {
                        oversizeCount++;
                        Console.WriteLine($"  [FAIL OVERSIZED] [{cp.Section}] {cp.Uid} -> Size=({w:F2}x{h:F2}x{d:F2})");
                    }
                }
            }
            if (oversizeCount > 0)
            {
                Console.WriteLine($"FAIL: Found {oversizeCount} oversized / ball meshes in Chaos pieces!");
                return 15;
            }
            Console.WriteLine("[PASS] ZERO oversized meshes or spheres in any Chaos pieces!");

            if (scanner.Presets.Count == 0 || scanner.Colors.Count == 0 || scanner.AllArmorPieces.Count == 0)
            {
                Console.WriteLine("FAIL: Scanned data counts are zero!");
                return 3;
            }

            // Test mod deployment into a temporary mods folder
            string tempDir = Path.Combine(Path.GetTempPath(), "SM2Customizer_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create dummy game structure in tempDir pointing to real pak
                string dummyDefaultDir = Path.Combine(tempDir, "client_pc", "root", "paks", "client", "default");
                Directory.CreateDirectory(dummyDefaultDir);

                // Copy default_other.pak to dummy path
                string dummyPak = Path.Combine(dummyDefaultDir, "default_other.pak");
                File.Copy(workspace.DefaultOtherPakPath, dummyPak, true);

                var testWorkspace = new GameWorkspace(tempDir);
                if (!testWorkspace.IsValid)
                {
                    Console.WriteLine($"FAIL: testWorkspace is not valid: {testWorkspace.DefaultOtherPakPath}");
                    return 4;
                }

                var deployer = new ModDeployService();
                var options = new ModDeployOptions
                {
                    UnlockAllColorsAndDecals = true,
                    UnlockCrossClassSlots = true,
                    UnlockAllPatterns = true
                };

                if (!deployer.Deploy(testWorkspace, scanner, scanner.Presets, scanner.Colors, scanner.Decals, options, out string deployMsg))
                {
                    Console.WriteLine($"FAIL: Deployment failed: {deployMsg}");
                    return 4;
                }

                Console.WriteLine($"[PASS] Mod deployed successfully!");

                if (!File.Exists(testWorkspace.ModPakPath))
                {
                    Console.WriteLine($"FAIL: Mod pak not found at {testWorkspace.ModPakPath}");
                    return 5;
                }

                // Verify pak archive contents
                using (var zip = ZipFile.OpenRead(testWorkspace.ModPakPath))
                {
                    Console.WriteLine($"[PASS] Mod pak verified as valid ZIP archive with {zip.Entries.Count} patched files:");
                    foreach (var entry in zip.Entries)
                    {
                        Console.WriteLine($"       - {entry.FullName} ({entry.Length} bytes)");
                    }
                }

                // Verify restore vanilla
                if (!deployer.RestoreVanilla(testWorkspace, out string restoreMsg))
                {
                    Console.WriteLine($"FAIL: Restore vanilla failed: {restoreMsg}");
                    return 6;
                }

                if (File.Exists(testWorkspace.ModPakPath))
                {
                    Console.WriteLine($"FAIL: Mod pak still exists after restore!");
                    return 7;
                }

                Console.WriteLine($"[PASS] Restore vanilla verified: mod pak cleanly deleted.");

                // Verify 3D Model Extraction & Metric Scaling
                Console.WriteLine("\n--- Verifying 3D Model Metric Bounds Extraction ---");
                string[] testSlots = new[] { "HELMET", "CHEST", "BACKPACK", "LEFT_PAULDRON", "RIGHT_PAULDRON", "LEFT_GREAVE", "RIGHT_GREAVE", "LEFT_GAUNTLET", "RIGHT_GAUNTLET" };
                foreach (var slot in testSlots)
                {
                    if (!gms.TryGetSlotGameMesh(workspace, slot, out var objData) || string.IsNullOrEmpty(objData))
                    {
                        Console.WriteLine($"FAIL: Failed to extract slot game mesh for {slot}");
                        return 8;
                    }

                    // Validate coordinates
                    float minX = float.MaxValue, maxX = float.MinValue;
                    float minY = float.MaxValue, maxY = float.MinValue;
                    float minZ = float.MaxValue, maxZ = float.MinValue;
                    int vCount = 0;

                    using var reader = new StringReader(objData);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("v "))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4 &&
                                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                                float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                                float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                            {
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                                if (z < minZ) minZ = z;
                                if (z > maxZ) maxZ = z;
                                vCount++;
                            }
                        }
                    }

                    float sizeX = maxX - minX;
                    float sizeY = maxY - minY;
                    float sizeZ = maxZ - minZ;

                    if (vCount < 50 || sizeX > 3.0f || sizeY > 3.0f || sizeZ > 3.0f || sizeX < 0.05f)
                    {
                        Console.WriteLine($"FAIL: Mesh for {slot} has invalid metric bounds: Size=({sizeX:F2}m, {sizeY:F2}m, {sizeZ:F2}m), Verts={vCount}");
                        return 9;
                    }

                    Console.WriteLine($"[PASS] Slot [{slot}]: {vCount} verts, Size: ({sizeX:F2}m x {sizeY:F2}m x {sizeZ:F2}m), X=[{minX:F2}..{maxX:F2}], Y=[{minY:F2}..{maxY:F2}], Z=[{minZ:F2}..{maxZ:F2}] -> Metric space verified.");
                }

                Console.WriteLine("==================================================");
                Console.WriteLine(" ALL TESTS PASSED SUCCESSFULLY!                   ");
                Console.WriteLine("==================================================");
                return 0;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static string CombineObjStrings(params string[] objs)
        {
            var sb = new System.Text.StringBuilder();
            int vOffset = 0;
            int vnOffset = 0;

            foreach (var obj in objs)
            {
                if (string.IsNullOrWhiteSpace(obj)) continue;
                using var reader = new StringReader(obj);
                string? line;
                int currentObjVerts = 0;
                int currentObjNorms = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("v "))
                    {
                        currentObjVerts++;
                        sb.AppendLine(line);
                    }
                    else if (line.StartsWith("vn "))
                    {
                        currentObjNorms++;
                        sb.AppendLine(line);
                    }
                    else if (line.StartsWith("f "))
                    {
                        if (vOffset == 0 && vnOffset == 0)
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
                                if (p.Contains("//"))
                                {
                                    var sub = p.Split("//");
                                    int vi = int.Parse(sub[0]) + vOffset;
                                    int vni = int.Parse(sub[1]) + vnOffset;
                                    sb.Append($" {vi}//{vni}");
                                }
                                else if (p.Contains("/"))
                                {
                                    var sub = p.Split('/');
                                    int vi = int.Parse(sub[0]) + vOffset;
                                    sb.Append($" {vi}");
                                    // if texture coordinates exist, handle if needed
                                }
                                else
                                {
                                    int vi = int.Parse(p) + vOffset;
                                    sb.Append($" {vi}");
                                }
                            }
                            sb.AppendLine();
                        }
                    }
                    else
                    {
                        sb.AppendLine(line);
                    }
                }

                vOffset += currentObjVerts;
                vnOffset += currentObjNorms;
            }

            return sb.ToString();
        }

        static bool IsLegitArmorPiece(ArmorPieceItem piece)
        {
            if (string.IsNullOrWhiteSpace(piece.Uid)) return false;
            string uid = piece.Uid.ToLowerInvariant();

            // Filter out empty sentinels
            if (uid.Contains("_empty") || uid.Contains("_none") || uid == "none" || uid.EndsWith("_empty") || uid.EndsWith("_none"))
                return false;

            // Filter out developer / hidden / placeholder / mini / reference entries
            if (uid.Contains("_hidden") || uid.Contains("hidden_") || uid.Contains("refrt_") ||
                uid.Contains("test_") || uid.Contains("dummy") || uid.Contains("placeholder") ||
                uid.Contains("lasfusil_tubes"))
                return false;

            // Filter out internal unfinished sections
            if (piece.Section.Equals("VANITY_2", StringComparison.OrdinalIgnoreCase) ||
                piece.Section.Equals("VANITY_3", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        static void AuditAllPieces(GameWorkspace workspace, GameScannerService scanner, GameModelService gms)
        {
            Console.WriteLine("--- Testing Updated GameModelService Anatomy & Resolution ---");
            gms.ClearCache();

            var testPieces = new[]
            {
                ("CHEST", "chest_support_common_01", "PVE_PVP_SUPPORT"),
                ("CHEST", "chest_custom_champion_raven_guard_soldier", "PVE_PVP_SOLDIER"),
                ("LEFT_GREAVE", "left_greave_custom_champion_space_wolves", "PVE_PVP_SOLDIER"),
                ("RIGHT_GREAVE", "right_greave_custom_champion_white_scar", "PVE_PVP_SOLDIER"),
                ("HELMET", "helmet_soldier_mastercrafted_01", "PVE_PVP_SOLDIER"),
                ("BACKPACK", "backpack_custom_soldier_champion_blood_angel", "PVE_PVP_SOLDIER")
            };

            foreach (var (slot, uid, cls) in testPieces)
            {
                var piece = scanner.AllArmorPieces.FirstOrDefault(p => p.Uid == uid);
                var meshes = piece?.Meshes;
                bool ok = gms.TryGetSlotGameMesh(workspace, slot, uid, meshes, cls, out var objData, out var srcKind, out var srcDetail, out var diag);
                if (diag != null)
                {
                    float h = diag.Max.Y - diag.Min.Y;
                    float w = diag.Max.X - diag.Min.X;
                    float d = diag.Max.Z - diag.Min.Z;
                    Console.WriteLine($"[{slot}] {uid} ({cls}) -> ok={ok}, Verts={diag.Verts}, Tris={diag.Tris}, Size=({w:F2}x{h:F2}x{d:F2}), Y=[{diag.Min.Y:F2}..{diag.Max.Y:F2}]");
                }
                else
                {
                    Console.WriteLine($"[{slot}] {uid} ({cls}) -> ok={ok}, diag=null");
                }
            }
        }

        static void RunOgmTest()
        {
            string defaultDir = @"C:\Program Files (x86)\Steam\steamapps\common\Space Marine 2\client_pc\root\paks\client\default";
            string pakPath = Path.Combine(defaultDir, "default_tpl_3.pak");
            string prefix = "tpl/cc_sm_constructor_soldier.tpl/cc_sm_constructor_soldier";

            using var fs = File.OpenRead(pakPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry($"{prefix}.tpl")!;
            byte[] tplBytes;
            using (var s = entry.Open())
            using (var ms = new MemoryStream()) { s.CopyTo(ms); tplBytes = ms.ToArray(); }

            var dbgEntry = zip.GetEntry($"{prefix}.geom_dbg")!;
            byte[] dbgBytes;
            using (var s = dbgEntry.Open())
            using (var ms = new MemoryStream()) { s.CopyTo(ms); dbgBytes = ms.ToArray(); }

            int dbgNodeCount = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(dbgBytes.AsSpan(0x45));
            int p = 0x4A;
            var nodeNames = new string[dbgNodeCount];
            for (int i = 0; i < dbgNodeCount; i++)
            {
                int len = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(dbgBytes.AsSpan(p)); p += 4;
                string name = System.Text.Encoding.ASCII.GetString(dbgBytes, p, len); p += len;
                nodeNames[i] = name;
            }

            int ogmPos = -1;
            for (int i = 0; i < tplBytes.Length - 4; i++)
            {
                if (tplBytes[i] == 0x4F && tplBytes[i+1] == 0x47 && tplBytes[i+2] == 0x4D && tplBytes[i+3] == 0x31) { ogmPos = i; break; }
            }
            int rPos = ogmPos + 4;
            uint unk_00 = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(tplBytes.AsSpan(rPos)); rPos += 4;
            uint varsCount = unk_00 & 0xFFFF;
            ushort version = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(tplBytes.AsSpan(rPos)); rPos += 2;
            int pFlags = (varsCount <= 8) ? tplBytes[rPos++] : System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(tplBytes.AsSpan(rPos));
            if (varsCount > 8) rPos += 2;
            short objCount = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(tplBytes.AsSpan(rPos)); rPos += 2;
            ushort unk_01 = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(tplBytes.AsSpan(rPos)); rPos += 2;
            ushort propCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(tplBytes.AsSpan(rPos)); rPos += 2;
            ushort unk_03 = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(tplBytes.AsSpan(rPos)); rPos += 2;

            if (propCount > 0 && tplBytes[rPos++] != 0) rPos += objCount * 2;
            if (propCount > 1 && tplBytes[rPos++] != 0) { for (int i = 0; i < objCount; i++) { int l = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(tplBytes.AsSpan(rPos)); rPos += 4 + l; } }
            if (propCount > 2 && tplBytes[rPos++] != 0) rPos += objCount * 8;
            if (propCount > 3 && tplBytes[rPos++] != 0) rPos += objCount * 2;
            if (propCount > 4 && tplBytes[rPos++] != 0) rPos += objCount * 2;
            if (propCount > 5 && tplBytes[rPos++] != 0) rPos += objCount * 2;
            if (propCount > 6 && tplBytes[rPos++] != 0) rPos += objCount * 2;
            if (propCount > 7 && tplBytes[rPos++] != 0) rPos += objCount * 2;
            if (propCount > 8 && tplBytes[rPos++] != 0) { for (int i = 0; i < objCount; i++) { int l = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(tplBytes.AsSpan(rPos)); rPos += 4 + l; } }
            if (propCount > 9 && tplBytes[rPos++] != 0) rPos += objCount * 64;
            if (propCount > 10 && tplBytes[rPos++] != 0) rPos += objCount * 64;

            // Property 11: GeomData
            if (propCount > 11 && tplBytes[rPos++] != 0)
            {
                int bitByteLen = (int)Math.Ceiling(objCount / 8.0);
                byte[] bitBuf = new byte[bitByteLen];
                Buffer.BlockCopy(tplBytes, rPos, bitBuf, 0, bitByteLen);
                rPos += bitByteLen;
                var bitArray = new System.Collections.BitArray(bitBuf);

                var queryTargets = new[] { "soldier_helmet_01", "soldier_torso_base", "soldier_shoulder_L_01", "soldier_shoulder_R_01", "soldier_knee_L_01", "soldier_boot_L_01", "soldier_arm_L_01", "soldier_hand_L_01" };

                for (int i = 0; i < objCount; i++)
                {
                    if (!bitArray[i]) continue;
                    int propC = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(tplBytes.AsSpan(rPos)); rPos += 4;
                    byte flags = tplBytes[rPos++];
                    if ((flags & (1 << 0)) != 0) rPos += 4;
                    if ((flags & (1 << 1)) != 0) rPos += 4;
                    if ((flags & (1 << 2)) != 0)
                    {
                        float minX = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(tplBytes.AsSpan(rPos)); rPos += 4;
                        float minY = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(tplBytes.AsSpan(rPos)); rPos += 4;
                        float minZ = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(tplBytes.AsSpan(rPos)); rPos += 4;
                        float maxX = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(tplBytes.AsSpan(rPos)); rPos += 4;
                        float maxY = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(tplBytes.AsSpan(rPos)); rPos += 4;
                        float maxZ = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(tplBytes.AsSpan(rPos)); rPos += 4;

                        string nName = i < nodeNames.Length ? nodeNames[i] : $"node_{i}";
                        foreach (var q in queryTargets)
                        {
                            if (nName.EndsWith(":" + q, StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine($"[{q}] Node {i}:");
                                Console.WriteLine($"  BBox Min: [{minX:F4}, {minY:F4}, {minZ:F4}]");
                                Console.WriteLine($"  BBox Max: [{maxX:F4}, {maxY:F4}, {maxZ:F4}]");
                                Console.WriteLine($"  Size:     [{maxX-minX:F4}, {maxY-minY:F4}, {maxZ-minZ:F4}]");
                            }
                        }
                    }
                    if ((flags & (1 << 3)) != 0) rPos += 60;
                }
            }
        }

        static void RunProbe()
        {
            Console.WriteLine("--- SM2 Binary & Model Probe ---");

            // 1. Scan strings in Retail.exe
            string exePath = @"C:\Program Files (x86)\Steam\steamapps\common\Space Marine 2\client_pc\root\bin\pc\Warhammer 40000 Space Marine 2 - Retail.exe";
            if (File.Exists(exePath))
            {
                Console.WriteLine($"Scanning strings in {Path.GetFileName(exePath)}...");
                byte[] exeBytes = File.ReadAllBytes(exePath);
                var found = new HashSet<string>();
                var cur = new System.Text.StringBuilder();

                for (int i = 0; i < exeBytes.Length; i++)
                {
                    byte b = exeBytes[i];
                    if (b >= 32 && b <= 126)
                    {
                        cur.Append((char)b);
                    }
                    else
                    {
                        if (cur.Length >= 6)
                        {
                            string s = cur.ToString();
                            if (s.Contains("tpl_data") || s.Contains("lods_base") || s.Contains("geom_dbg") ||
                                s.Contains("1SER") || s.Contains(".tpl") || s.Contains(".cdt") ||
                                s.Contains("MeshHeader") || s.Contains("SubMesh") || s.Contains("VertexBuffer") ||
                                s.Contains("IndexBuffer") || s.Contains("VertexDeclaration") || s.Contains("SwarmMesh"))
                            {
                                if (found.Add(s) && found.Count <= 50)
                                {
                                    Console.WriteLine($"[STR] {s}");
                                }
                            }
                        }
                        cur.Clear();
                    }
                }
                Console.WriteLine($"Total matching strings found: {found.Count}");
            }

            // 2. Inspect helmet files in default_tpl_0.pak
            string pakPath = @"C:\Program Files (x86)\Steam\steamapps\common\Space Marine 2\client_pc\root\paks\client\default\default_tpl_0.pak";
            if (File.Exists(pakPath))
            {
                using var zip = ZipFile.OpenRead(pakPath);
                string prefix = "tpl/refrt_asn_spacemarine_armor_helmet_01.tpl/refrt_asn_spacemarine_armor_helmet_01";
                foreach (var ext in new[] { ".lods_base", ".geom_dbg", ".tpl", ".cdt", ".tpl_data" })
                {
                    var entry = zip.GetEntry(prefix + ext);
                    if (entry == null) continue;
                    Console.WriteLine($"\n--- Entry: {entry.Name} ({entry.Length} bytes) ---");
                    using var stream = entry.Open();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    byte[] data = ms.ToArray();

                    if (ext == ".lods_base")
                    {
                        Console.WriteLine(System.Text.Encoding.UTF8.GetString(data));
                    }
                    else
                    {
                        Console.WriteLine("First 64 bytes (hex):");
                        Console.WriteLine(BitConverter.ToString(data, 0, Math.Min(64, data.Length)).Replace("-", " "));
                        
                        // Extract ASCII strings inside
                        var strBuilder = new System.Text.StringBuilder();
                        var strs = new List<string>();
                        for (int i = 0; i < data.Length; i++)
                        {
                            if (data[i] >= 32 && data[i] <= 126) strBuilder.Append((char)data[i]);
                            else
                            {
                                if (strBuilder.Length >= 4) strs.Add(strBuilder.ToString());
                                strBuilder.Clear();
                            }
                        }
                        Console.WriteLine($"Embedded strings ({strs.Count}): " + string.Join(", ", strs.Take(20)));
                    }
                }
            }

            // 3. Inspect .tpl structure in detail
            string tplPak = @"C:\Program Files (x86)\Steam\steamapps\common\Space Marine 2\client_pc\root\paks\client\default\default_tpl_0.pak";
            if (File.Exists(tplPak))
            {
                using var zip = ZipFile.OpenRead(tplPak);
                var tplEntry = zip.GetEntry("tpl/refrt_asn_spacemarine_armor_helmet_01.tpl/refrt_asn_spacemarine_armor_helmet_01.tpl");
                if (tplEntry != null)
                {
                    using var stream = tplEntry.Open();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    byte[] data = ms.ToArray();
                    Console.WriteLine($"\n=== .tpl Detailed Analysis (Length: {data.Length}) ===");

                    using var br = new BinaryReader(new MemoryStream(data));
                    string magic = System.Text.Encoding.ASCII.GetString(br.ReadBytes(8));
                    Console.WriteLine($"Magic: {magic}");
                    br.BaseStream.Seek(36, SeekOrigin.Begin);
                    string resHeader = System.Text.Encoding.ASCII.GetString(br.ReadBytes(16));
                    Console.WriteLine($"ResHeader: {resHeader}");

                    br.BaseStream.Seek(56, SeekOrigin.Begin);
                    int ver = br.ReadInt32();
                    int hSize = br.ReadInt32();
                    Console.WriteLine($"Version: {ver}, HeaderSize: {hSize}");
                }
            }

            // 6. Inspect geom_dbg completely
            if (File.Exists(tplPak))
            {
                using var zipG = ZipFile.OpenRead(tplPak);
                var gEntry = zipG.GetEntry("tpl/refrt_asn_spacemarine_armor_helmet_01.tpl/refrt_asn_spacemarine_armor_helmet_01.geom_dbg");
                if (gEntry != null)
                {
                    using var streamG = gEntry.Open();
                    using var msG = new MemoryStream();
                    streamG.CopyTo(msG);
                    byte[] gData = msG.ToArray();
                    Console.WriteLine($"\n=== Full geom_dbg Dump ({gData.Length} bytes) ===");
                    Console.WriteLine(BitConverter.ToString(gData).Replace("-", " "));

                    using var brG = new BinaryReader(new MemoryStream(gData));
                    for (int off = 56; off < gData.Length - 4; off += 4)
                    {
                        brG.BaseStream.Seek(off, SeekOrigin.Begin);
                        int val = brG.ReadInt32();
                        Console.WriteLine($"gdbg 0x{off:X3} ({off,3}): {val,10} (0x{val:X8})");
                    }
                }
            }

            // 5. Inspect tpl_data structure
            if (File.Exists(tplPak))
            {
                using var zipData = ZipFile.OpenRead(tplPak);
                var dataEntry = zipData.GetEntry("tpl/refrt_asn_spacemarine_armor_helmet_01.tpl/refrt_asn_spacemarine_armor_helmet_01.tpl_data");
                if (dataEntry != null)
                {
                    using var streamData = dataEntry.Open();
                    using var msData = new MemoryStream();
                    streamData.CopyTo(msData);
                    byte[] raw = msData.ToArray();
                    Console.WriteLine($"\n=== tpl_data Analysis (Length: {raw.Length}) ===");

                    // Scan chunks of 32KB and inspect ranges
                    for (int chunk = 0; chunk < raw.Length; chunk += 32768)
                    {
                        int len = Math.Min(32768, raw.Length - chunk);
                        short minVal = short.MaxValue, maxVal = short.MinValue;
                        for (int i = 0; i < len; i += 2)
                        {
                            short v = BitConverter.ToInt16(raw, chunk + i);
                            if (v < minVal) minVal = v;
                            if (v > maxVal) maxVal = v;
                        }
                        Console.WriteLine($"Chunk 0x{chunk:X6}-0x{chunk + len:X6}: range [{minVal,6}, {maxVal,6}] First 16 bytes: " +
                            BitConverter.ToString(raw, chunk, Math.Min(16, len)).Replace("-", " "));
                    }

                    // Find where the index buffer starts
                    // In index buffers, values are small indices [0..vertexCount] forming triangles
                    // Let's find the boundary between vertices and indices
                    // Notice Chunk 0x028000 still has vertices, Chunk 0x030000 has indices!
                    int ibStart = -1;
                    for (int off = 0x020000; off < 0x038000; off += 2)
                    {
                        // Check if from 'off' onward, the values look like a sequence of triangle indices < 15000
                        bool isIndexSequence = true;
                        for (int k = 0; k < 60; k += 2)
                        {
                            ushort idx = BitConverter.ToUInt16(raw, off + k);
                            if (idx > 15000) { isIndexSequence = false; break; }
                        }
                        if (isIndexSequence)
                        {
                            ibStart = off;
                            break;
                        }
                    }
                    Console.WriteLine($"Detected Index Buffer Start at: 0x{ibStart:X6} ({ibStart})");

                    int vertexCount = ibStart / 8;
                    Console.WriteLine($"Estimated Vertex Count (8-byte stride): {vertexCount}");

                    // 7. Universal Mesh Extractor Test
                    Console.WriteLine("\n=== Testing Universal TplMeshExtractor ===");
                    TestExtractMesh(tplPak, "tpl/refrt_asn_spacemarine_armor_helmet_01.tpl/refrt_asn_spacemarine_armor_helmet_01.tpl_data", @"C:\Users\FALCO\SM2Customizer\tests\helmet_extracted.obj");
                    TestExtractMesh(tplPak, "tpl/cine_titus_helmet.tpl/cine_titus_helmet.tpl_data", @"C:\Users\FALCO\SM2Customizer\tests\titus_helmet_extracted.obj");

                    string pak5 = @"C:\Program Files (x86)\Steam\steamapps\common\Space Marine 2\client_pc\root\paks\client\default\default_tpl_5.pak";
                    if (File.Exists(pak5))
                    {
                        TestExtractMesh(pak5, "tpl/refrt_asn_spacemarine_armor_shoulder_01.tpl/refrt_asn_spacemarine_armor_shoulder_01.tpl_data", @"C:\Users\FALCO\SM2Customizer\tests\shoulder01_extracted.obj");
                        TestExtractMesh(pak5, "tpl/refrt_asn_spacemarine_armor_shoulder_02.tpl/refrt_asn_spacemarine_armor_shoulder_02.tpl_data", @"C:\Users\FALCO\SM2Customizer\tests\shoulder02_extracted.obj");
                    }

                    string pak1 = @"C:\Program Files (x86)\Steam\steamapps\common\Space Marine 2\client_pc\root\paks\client\default\default_tpl_1.pak";
                    if (File.Exists(pak1))
                    {
                        TestExtractMesh(pak1, "tpl/marine_shield.tpl/marine_shield.tpl_data", @"C:\Users\FALCO\SM2Customizer\tests\shield_extracted.obj");
                    }
                }
            }
        }

        static void RunTplProbe()
        {
            string gamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Space Marine 2";
            var ws = new GameWorkspace(gamePath);
            var gms = new GameModelService();

            string[] slots = new[] { "HELMET", "CHEST", "BACKPACK", "LEFT_PAULDRON", "RIGHT_PAULDRON", "LEFT_GAUNTLET", "RIGHT_GAUNTLET", "LEFT_GREAVE", "RIGHT_GREAVE" };
            foreach (var slot in slots)
            {
                if (gms.TryGetSlotGameMesh(ws, slot, out var objData))
                {
                    string outPath = $@"C:\Users\FALCO\SM2Customizer\tests\slot_{slot.ToLowerInvariant()}.obj";
                    File.WriteAllText(outPath, objData);
                    Console.WriteLine($"[PASS] Extracted slot {slot} -> {Path.GetFileName(outPath)} ({objData.Length} chars)");
                }
                else
                {
                    Console.WriteLine($"[FAIL] Failed to extract slot {slot}");
                }
            }
        }

        static void TestExtractFull(string pakPath, string prefix, string outObj)
        {
            var res = TplMeshReader.ExtractObjFromPak(pakPath, prefix);
            if (res != null)
            {
                File.WriteAllText(outObj, res);
                Console.WriteLine($"[PASS] Extracted {Path.GetFileName(outObj)} ({res.Length} chars)");
                // Check raw vx range
                using var fs2 = File.OpenRead(pakPath);
                using var zip2 = new ZipArchive(fs2, ZipArchiveMode.Read);
                var dEntry = zip2.GetEntry(prefix + ".tpl_data");
                if (dEntry != null)
                {
                    using var s = dEntry.Open();
                    using var ms = new MemoryStream(); s.CopyTo(ms);
                    byte[] data = ms.ToArray();
                    short minVx = short.MaxValue, maxVx = short.MinValue;
                    for (int i = 0; i < Math.Min(data.Length, 100000); i += 8)
                    {
                        short v = BitConverter.ToInt16(data, i);
                        if (v < minVx) minVx = v;
                        if (v > maxVx) maxVx = v;
                    }
                    Console.WriteLine($"Raw vx range in {Path.GetFileName(prefix)}: [{minVx} .. {maxVx}]");
                }
            }
            else
            {
                Console.WriteLine($"[FAIL] Failed to extract {prefix}");
                // Let's debug why
                using var fs = File.OpenRead(pakPath);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                var entry = zip.GetEntry(prefix + ".tpl");
                Console.WriteLine($"Entry exists? {entry != null}");

                byte[] tplBytes;
                using (var s = entry!.Open())
                using (var ms = new MemoryStream()) { s.CopyTo(ms); tplBytes = ms.ToArray(); }
                var dbgEntry = zip.GetEntry(prefix + ".geom_dbg");
                byte[]? dbgBytes = null;
                if (dbgEntry != null)
                {
                    using var s = dbgEntry.Open();
                    using var ms = new MemoryStream(); { s.CopyTo(ms); dbgBytes = ms.ToArray(); }
                }
                // Call ParseTemplate via reflection or check where it fails
                var method = typeof(TplMeshReader).GetMethod("ParseTemplate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var tmpl = method?.Invoke(null, new object?[] { tplBytes, dbgBytes });
                Console.WriteLine($"ParseTemplate returned: {tmpl != null}");
                if (tmpl != null)
                {
                    var nodePathsProp = tmpl.GetType().GetField("NodePaths");
                    var subCountProp = tmpl.GetType().GetField("SubmeshCount");
                    var nodePaths = (string[])nodePathsProp!.GetValue(tmpl)!;
                    var subCount = (ushort[])subCountProp!.GetValue(tmpl)!;
                    for (int i = 0; i < nodePaths.Length; i++)
                    {
                        Console.WriteLine($"Node {i}: SubCount={subCount[i]}, Path='{nodePaths[i]}'");
                    }
                }
            }
        }

        static bool TestExtractMesh(string pakPath, string entryPath, string outObjPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(pakPath);
                var entry = zip.GetEntry(entryPath);
                if (entry == null) { Console.WriteLine($"Not found: {entryPath}"); return false; }

                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                byte[] raw = ms.ToArray();

                // 1. Scan for index buffers
                int bestIbStart = -1, bestIbEnd = -1, bestCount = 0, bestMaxIdx = 0;
                int p = 0;
                while (p < raw.Length - 64)
                {
                    int seqLen = 0;
                    int maxIdx = 0;
                    int start = p;
                    while (p < raw.Length - 2)
                    {
                        ushort val = BitConverter.ToUInt16(raw, p);
                        if (val > 35000) break;
                        if (val > maxIdx) maxIdx = val;
                        seqLen++;
                        p += 2;
                    }

                    if (seqLen >= 150 && seqLen > bestCount)
                    {
                        bestCount = seqLen;
                        bestIbStart = start;
                        bestIbEnd = p;
                        bestMaxIdx = maxIdx;
                    }
                    p += 16;
                }

                if (bestIbStart < 0 || bestMaxIdx == 0)
                {
                    Console.WriteLine($"[FAIL] No valid index buffer in {entry.Name}");
                    return false;
                }

                int vCount = bestMaxIdx + 1;
                int triCount = bestCount / 3;

                // Scale factor: int16 to meters (~0.0001m per unit)
                float scale = 0.0001f;

                using var sw = new StreamWriter(outObjPath);
                sw.WriteLine($"# Extracted from {entry.Name}");
                sw.WriteLine($"# Vertices: {vCount}, Triangles: {triCount}");

                for (int i = 0; i < vCount; i++)
                {
                    int o = i * 8;
                    if (o + 6 > raw.Length) break;
                    short vx = BitConverter.ToInt16(raw, o);
                    short vy = BitConverter.ToInt16(raw, o + 2);
                    short vz = BitConverter.ToInt16(raw, o + 4);

                    float x = vx * scale;
                    float y = vz * scale; // Game Z is Up
                    float z = -vy * scale; // Game Y is Forward -> -Z
                    sw.WriteLine(FormattableString.Invariant($"v {x:F5} {y:F5} {z:F5}"));
                }

                for (int i = bestIbStart; i < bestIbEnd - 5; i += 6)
                {
                    ushort i0 = (ushort)(BitConverter.ToUInt16(raw, i) + 1);
                    ushort i1 = (ushort)(BitConverter.ToUInt16(raw, i + 2) + 1);
                    ushort i2 = (ushort)(BitConverter.ToUInt16(raw, i + 4) + 1);
                    if (i0 <= vCount && i1 <= vCount && i2 <= vCount)
                    {
                        sw.WriteLine($"f {i0} {i1} {i2}");
                    }
                }

                Console.WriteLine($"[PASS] {Path.GetFileName(entryPath)} -> {Path.GetFileName(outObjPath)}: {vCount} verts, {triCount} tris ({new FileInfo(outObjPath).Length / 1024} KB)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {entryPath}: {ex.Message}");
                return false;
            }
        }

        static int RunOfflineChecks()
        {
            Console.WriteLine("--- Running Offline Test Suite ---");

            var importer = new CustomModelImportService();
            if (!CustomModelImportService.ModelFileFilter.Contains("*.stl") || !CustomModelImportService.ModelFileFilter.Contains("*.glb"))
            {
                Console.WriteLine("FAIL: CustomModelImportService filter missing modern 3D formats!");
                return 10;
            }
            Console.WriteLine("[PASS] CustomModelImportService multi-format filters verified.");

            var cfg = new UserConfig { LastGamePath = @"C:\Games\SM2", AutoLoadGameOnStartup = true };
            string json = System.Text.Json.JsonSerializer.Serialize(cfg);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<UserConfig>(json);
            if (deserialized == null || !deserialized.AutoLoadGameOnStartup || deserialized.LastGamePath != @"C:\Games\SM2")
            {
                Console.WriteLine("FAIL: UserConfig serialization roundtrip failed!");
                return 11;
            }
            Console.WriteLine("[PASS] UserConfig serialization roundtrip verified.");

            var deployer = new ModDeployService();
            Console.WriteLine("[PASS] ModDeployService initialized.");

            Console.WriteLine("==================================================");
            Console.WriteLine(" All Offline Checks Passed Successfully!          ");
            Console.WriteLine("==================================================");
            return 0;
        }
    }
}
