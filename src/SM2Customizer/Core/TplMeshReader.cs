using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace SM2Customizer.Core
{
    public sealed record MeshDiag(
        string Prefix,
        int NodesMatched,
        string[] MissingNames,
        int Verts,
        int Tris,
        int IndexOutOfRange,
        int Degenerate,
        Vector3 Min,
        Vector3 Max,
        float MaxEdge,
        float MedianEdge);

    public static class TplMeshReader
    {
        // Safety constants to strictly prevent infinite / large loops
        private const int MaxNodes = 50000;
        private const int MaxBuffers = 10000;
        private const int MaxStreamGroups = 10000;
        private const int MaxSubmeshes = 50000;
        private const int MaxScanBytes = 50000000;

        private class StreamGroup
        {
            public int Vb0Idx;
            public int Vb0Off;
            public int Vb1Idx;
            public int Vb1Off;
            public int IbIdx;
            public int IbOff;
        }

        private struct SubmeshRecord
        {
            public ushort VStart;
            public ushort VCount;
            public ushort FStart;
            public ushort FCount;
        }

        private class TplTemplate
        {
            public string PakPath = string.Empty;
            public string Prefix = string.Empty;
            public int NodeCount;
            public string[] NodePaths = Array.Empty<string>();
            public ushort[] SubmeshStart = Array.Empty<ushort>();
            public ushort[] SubmeshCount = Array.Empty<ushort>();
            public int NumBuffers;
            public long[] BufferOffsets = Array.Empty<long>();
            public int[] BufferStrides = Array.Empty<int>();
            public int NumStreamGroups;
            public StreamGroup[] StreamGroups = Array.Empty<StreamGroup>();
            public int NumSubmeshes;
            public int[] SubmeshStreamGroup = Array.Empty<int>();
            public SubmeshRecord[] Submeshes = Array.Empty<SubmeshRecord>();
            public Vector3[] NodeCenter = Array.Empty<Vector3>();
            public Vector3[] NodeExtent = Array.Empty<Vector3>();
            public long DataStartInPak = -1;
            public long DataLength = 0;
            public byte[]? MemoryData = null; // Used if entry is compressed or extracted in memory
        }

        private static readonly ConcurrentDictionary<string, TplTemplate> TemplateCache = new(StringComparer.OrdinalIgnoreCase);

        public static string? ExtractMeshByNodeNames(string pakPath, string tplEntryPrefix, IReadOnlyCollection<string>? targetMeshNames)
        {
            return ExtractMeshByNodeNames(pakPath, tplEntryPrefix, targetMeshNames, out _);
        }

        public static string? ExtractMeshByNodeNames(string pakPath, string tplEntryPrefix, IReadOnlyCollection<string>? targetMeshNames, out MeshDiag diag)
        {
            diag = new MeshDiag(tplEntryPrefix, 0, targetMeshNames?.ToArray() ?? Array.Empty<string>(), 0, 0, 0, 0, Vector3.Zero, Vector3.Zero, 0, 0);
            if (!File.Exists(pakPath)) return null;

            var template = GetOrLoadTemplate(pakPath, tplEntryPrefix);
            if (template == null) return null;

            return ExtractObjFromTemplate(template, targetMeshNames, out diag);
        }

        public static string? ExtractObjFromPak(string pakPath, string entryPath)
        {
            return ExtractObjFromPak(pakPath, entryPath, out _);
        }

        public static string? ExtractObjFromPak(string pakPath, string entryPath, out MeshDiag diag)
        {
            diag = new MeshDiag(entryPath, 0, Array.Empty<string>(), 0, 0, 0, 0, Vector3.Zero, Vector3.Zero, 0, 0);
            if (!File.Exists(pakPath)) return null;

            // Normalize entryPath to prefix (e.g. tpl/xxx.tpl/xxx.tpl_data -> tpl/xxx.tpl/xxx)
            string norm = entryPath.Replace('\\', '/');
            string prefix = norm;
            if (prefix.EndsWith(".tpl_data", StringComparison.OrdinalIgnoreCase))
                prefix = prefix[..^9];
            else if (prefix.EndsWith(".tpl", StringComparison.OrdinalIgnoreCase))
                prefix = prefix[..^4];
            else if (prefix.EndsWith(".geom_dbg", StringComparison.OrdinalIgnoreCase))
                prefix = prefix[..^9];

            return ExtractMeshByNodeNames(pakPath, prefix, null, out diag);
        }

        private static TplTemplate? GetOrLoadTemplate(string pakPath, string prefix)
        {
            string cacheKey = $"{pakPath}:{prefix}";
            if (TemplateCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            try
            {
                using var fs = File.OpenRead(pakPath);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: true);

                string tplPath = $"{prefix}.tpl";
                var tplEntry = zip.GetEntry(tplPath);
                if (tplEntry == null) return null;

                byte[] tplBytes;
                using (var s = tplEntry.Open())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    tplBytes = ms.ToArray();
                }

                // Node names from .geom_dbg if present
                string dbgPath = $"{prefix}.geom_dbg";
                var dbgEntry = zip.GetEntry(dbgPath);
                byte[]? dbgBytes = null;
                if (dbgEntry != null)
                {
                    using var s = dbgEntry.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    dbgBytes = ms.ToArray();
                }

                // Check .tpl_data
                string dataPath = $"{prefix}.tpl_data";
                var dataEntry = zip.GetEntry(dataPath);
                if (dataEntry == null) return null;

                long dataStartInPak = -1;
                byte[]? memoryData = null;

                if (dataEntry.Length == dataEntry.CompressedLength)
                {
                    // Stored uncompressed: read 1 byte to get local file offset in pak
                    using var ds = dataEntry.Open();
                    ds.ReadByte();
                    dataStartInPak = fs.Position - 1;
                }
                else
                {
                    // Compressed: read to memory (with safety upper limit 60MB)
                    if (dataEntry.Length <= 60 * 1024 * 1024)
                    {
                        using var ds = dataEntry.Open();
                        using var ms = new MemoryStream();
                        ds.CopyTo(ms);
                        memoryData = ms.ToArray();
                    }
                }

                var template = ParseTemplate(tplBytes, dbgBytes);
                if (template == null) return null;

                template.PakPath = pakPath;
                template.Prefix = prefix;
                template.DataStartInPak = dataStartInPak;
                template.DataLength = dataEntry.Length;
                template.MemoryData = memoryData;

                TemplateCache[cacheKey] = template;
                return template;
            }
            catch
            {
                return null;
            }
        }

        private static TplTemplate? ParseTemplate(byte[] tpl, byte[]? dbg)
        {
            if (tpl == null || tpl.Length < 64) return null;

            // 1. Find OGM1 marker in .tpl
            int ogmPos = -1;
            int maxScan = Math.Min(tpl.Length - 4, MaxScanBytes);
            for (int i = 0; i < maxScan; i++)
            {
                if (tpl[i] == 0x4F && tpl[i + 1] == 0x47 && tpl[i + 2] == 0x4D && tpl[i + 3] == 0x31)
                {
                    ogmPos = i;
                    break;
                }
            }

            if (ogmPos < 0 || ogmPos + 16 > tpl.Length) return null;

            int nodeCount = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(ogmPos + 12));
            if (nodeCount <= 0 || nodeCount > MaxNodes) return null;

            var template = new TplTemplate
            {
                NodeCount = nodeCount,
                NodePaths = new string[nodeCount]
            };

            // 2. Parse node paths from .geom_dbg
            if (dbg != null && dbg.Length > 0x49)
            {
                int dbgNodeCount = BinaryPrimitives.ReadInt32LittleEndian(dbg.AsSpan(0x45));
                int p = 0x4A;
                int countToRead = Math.Min(nodeCount, Math.Min(dbgNodeCount, MaxNodes));
                for (int i = 0; i < countToRead; i++)
                {
                    if (p + 4 > dbg.Length) break;
                    int len = BinaryPrimitives.ReadInt32LittleEndian(dbg.AsSpan(p));
                    p += 4;
                    if (len <= 0 || p + len > dbg.Length) break;
                    template.NodePaths[i] = Encoding.ASCII.GetString(dbg, p, len);
                    p += len;
                }
            }
            else
            {
                for (int i = 0; i < nodeCount; i++)
                {
                    template.NodePaths[i] = $"node_{i}";
                }
            }

            for (int i = 0; i < nodeCount; i++)
            {
                if (string.IsNullOrEmpty(template.NodePaths[i]))
                {
                    template.NodePaths[i] = $"node_{i}";
                }
            }
            ReadOnlySpan<byte> tplSpan = tpl;
            Span<byte> markerPat = stackalloc byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(markerPat[..4], nodeCount);
            BinaryPrimitives.WriteInt32LittleEndian(markerPat.Slice(4, 4), 2);
            BinaryPrimitives.WriteInt32LittleEndian(markerPat.Slice(8, 4), 1);

            int markerPos = -1;
            int scanStart = ogmPos;
            int maxMarkerScan = Math.Min(tpl.Length - 12, MaxScanBytes);
            while (scanStart < maxMarkerScan)
            {
                int idx = tplSpan[scanStart..maxMarkerScan].IndexOf(markerPat);
                if (idx < 0) break;
                markerPos = scanStart + idx;
                break;
            }

            if (markerPos < 0) return null;

            // 4. Read SubmeshStart and SubmeshCount
            template.SubmeshStart = new ushort[nodeCount];
            template.SubmeshCount = new ushort[nodeCount];
            template.NodeCenter = new Vector3[nodeCount];
            template.NodeExtent = new Vector3[nodeCount];

            for (int i = 0; i < nodeCount; i++)
            {
                template.NodeCenter[i] = Vector3.Zero;
                template.NodeExtent[i] = new Vector3(0.5f, 0.5f, 0.5f);
            }

            // Parse exact node bounds from objGEOM_MNG / objOBJSerializer (OGM1)
            bool parsedOgmBounds = false;
            try
            {
                int rPos = ogmPos + 4;
                if (rPos + 10 <= tpl.Length)
                {
                    uint unk_00 = BinaryPrimitives.ReadUInt32LittleEndian(tpl.AsSpan(rPos)); rPos += 4;
                    uint varsCount = unk_00 & 0xFFFF;
                    ushort version = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(rPos)); rPos += 2;
                    int pFlags = (varsCount <= 8) ? tpl[rPos++] : BinaryPrimitives.ReadInt16LittleEndian(tpl.AsSpan(rPos));
                    if (varsCount > 8) rPos += 2;

                    short objCount = BinaryPrimitives.ReadInt16LittleEndian(tpl.AsSpan(rPos)); rPos += 2;
                    ushort unk_01 = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(rPos)); rPos += 2;
                    ushort propCount = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(rPos)); rPos += 2;
                    ushort unk_03 = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(rPos)); rPos += 2;

                    if (objCount == nodeCount && propCount > 11)
                    {
                        if (propCount > 0 && tpl[rPos++] != 0) rPos += objCount * 2;
                        if (propCount > 1 && tpl[rPos++] != 0) { for (int i = 0; i < objCount; i++) { int l = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(rPos)); rPos += 4 + l; } }
                        if (propCount > 2 && tpl[rPos++] != 0) rPos += objCount * 8;
                        if (propCount > 3 && tpl[rPos++] != 0) rPos += objCount * 2;
                        if (propCount > 4 && tpl[rPos++] != 0) rPos += objCount * 2;
                        if (propCount > 5 && tpl[rPos++] != 0) rPos += objCount * 2;
                        if (propCount > 6 && tpl[rPos++] != 0) rPos += objCount * 2;
                        if (propCount > 7 && tpl[rPos++] != 0) rPos += objCount * 2;
                        if (propCount > 8 && tpl[rPos++] != 0) { for (int i = 0; i < objCount; i++) { int l = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(rPos)); rPos += 4 + l; } }
                        if (propCount > 9 && tpl[rPos++] != 0) rPos += objCount * 64;
                        if (propCount > 10 && tpl[rPos++] != 0) rPos += objCount * 64;

                        // Property 11: GeomData (objGEOM_UNSHARED)
                        if (propCount > 11 && tpl[rPos++] != 0)
                        {
                            int bitByteLen = (int)Math.Ceiling(objCount / 8.0);
                            byte[] bitBuf = new byte[bitByteLen];
                            Buffer.BlockCopy(tpl, rPos, bitBuf, 0, bitByteLen);
                            rPos += bitByteLen;
                            var bitArray = new System.Collections.BitArray(bitBuf);

                            for (int i = 0; i < objCount; i++)
                            {
                                if (!bitArray[i]) continue;
                                int propC = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(rPos)); rPos += 4;
                                byte flags = tpl[rPos++];

                                if ((flags & (1 << 0)) != 0) rPos += 4; // splitIndex
                                if ((flags & (1 << 1)) != 0) rPos += 4; // numSplits

                                if ((flags & (1 << 2)) != 0) // BoundingBox
                                {
                                    float minX = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(rPos)); rPos += 4;
                                    float minY = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(rPos)); rPos += 4;
                                    float minZ = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(rPos)); rPos += 4;
                                    float maxX = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(rPos)); rPos += 4;
                                    float maxY = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(rPos)); rPos += 4;
                                    float maxZ = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(rPos)); rPos += 4;

                                    if (float.IsFinite(minX) && float.IsFinite(maxX) &&
                                        float.IsFinite(minY) && float.IsFinite(maxY) &&
                                        float.IsFinite(minZ) && float.IsFinite(maxZ))
                                    {
                                        template.NodeCenter[i] = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
                                        template.NodeExtent[i] = new Vector3((maxX - minX) * 0.5f, (maxY - minY) * 0.5f, (maxZ - minZ) * 0.5f);
                                    }
                                }
                                if ((flags & (1 << 3)) != 0) rPos += 60; // obb
                            }
                            parsedOgmBounds = true;
                        }
                    }
                }
            }
            catch
            {
                parsedOgmBounds = false;
            }

            // Fallback to byte pattern scan if OGM1 geomData property was not found
            if (!parsedOgmBounds)
            {
                int boundsStart = -1;
                int scanLimit = Math.Min(tpl.Length - 28, ogmPos + 131072);
                for (int p = ogmPos; p < scanLimit; p++)
                {
                    if (tpl[p] == 0x04 && tpl[p + 1] == 0x00 && tpl[p + 2] == 0x00 && tpl[p + 3] == 0x00 && tpl[p + 4] == 0x0C)
                    {
                        float minX = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(p + 5));
                        float minY = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(p + 9));
                        float minZ = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(p + 13));
                        float maxX = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(p + 17));
                        float maxY = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(p + 21));
                        float maxZ = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(p + 25));

                        if (minX <= maxX && minY <= maxY && minZ <= maxZ &&
                            minX >= -25.0f && maxX <= 25.0f &&
                            minY >= -25.0f && maxY <= 25.0f &&
                            minZ >= -25.0f && maxZ <= 25.0f &&
                            (maxX - minX) > 0.001f && (maxY - minY) > 0.001f && (maxZ - minZ) > 0.001f)
                        {
                            boundsStart = p + 5;
                            break;
                        }
                    }
                }

                if (boundsStart >= 0)
                {
                    Vector3 lastValidCenter = Vector3.Zero;
                    Vector3 lastValidExtent = new Vector3(0.5f, 0.5f, 0.5f);

                    for (int i = 0; i < nodeCount; i++)
                    {
                        int bOff = boundsStart + (i * 89);
                        if (bOff + 24 > tpl.Length) break;
                        if (bOff > boundsStart && tpl[bOff - 1] != 0x0C) break;

                        float minX = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(bOff));
                        float minY = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(bOff + 4));
                        float minZ = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(bOff + 8));
                        float maxX = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(bOff + 12));
                        float maxY = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(bOff + 16));
                        float maxZ = BinaryPrimitives.ReadSingleLittleEndian(tpl.AsSpan(bOff + 20));

                        if (float.IsFinite(minX) && float.IsFinite(maxX) &&
                            float.IsFinite(minY) && float.IsFinite(maxY) &&
                            float.IsFinite(minZ) && float.IsFinite(maxZ) &&
                            minX <= maxX && minY <= maxY && minZ <= maxZ &&
                            minX >= -15.0f && maxX <= 15.0f &&
                            minY >= -15.0f && maxY <= 15.0f &&
                            minZ >= -15.0f && maxZ <= 15.0f)
                        {
                            lastValidCenter = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
                            lastValidExtent = new Vector3((maxX - minX) * 0.5f, (maxY - minY) * 0.5f, (maxZ - minZ) * 0.5f);
                            template.NodeCenter[i] = lastValidCenter;
                            template.NodeExtent[i] = lastValidExtent;
                        }
                        else
                        {
                            template.NodeCenter[i] = lastValidCenter;
                            template.NodeExtent[i] = lastValidExtent;
                        }
                    }
                }
            }

            int subStartOff = markerPos + 9;
            int subCountOff = markerPos + 10 + (nodeCount * 2);

            if (subCountOff + (nodeCount * 2) > tpl.Length) return null;

            for (int i = 0; i < nodeCount; i++)
            {
                template.SubmeshStart[i] = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(subStartOff + (i * 2)));
                template.SubmeshCount[i] = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(subCountOff + (i * 2)));
            }

            // 5. Buffer & Submesh Table Header
            int tablePos = markerPos + 18 + (nodeCount * 4);
            if (tablePos + 24 > tpl.Length) return null;

            int numBufs = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(tablePos + 4));
            int numSG = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(tablePos + 8));
            int numSub = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(tablePos + 12));

            if (numBufs <= 0 || numBufs > MaxBuffers || numSG <= 0 || numSG > MaxStreamGroups || numSub <= 0 || numSub > MaxSubmeshes)
            {
                return null;
            }

            template.NumBuffers = numBufs;
            template.NumStreamGroups = numSG;
            template.NumSubmeshes = numSub;
            template.BufferOffsets = new long[numBufs];
            template.BufferStrides = new int[numBufs];
            template.StreamGroups = new StreamGroup[numSG];
            template.SubmeshStreamGroup = new int[numSub];
            template.Submeshes = new SubmeshRecord[numSub];

            // 6. Walk Chunks after tablePos
            int chunkPtr = tablePos + 24;
            int chunkEndLimit = tpl.Length - 6;

            int chunk2Pos = -1, chunk3Pos = -1, chunk4Pos = -1;

            int maxChunkWalk = 20;
            while (chunkPtr + 6 <= chunkEndLimit && maxChunkWalk-- > 0)
            {
                ushort cid = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(chunkPtr));
                int cend = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(chunkPtr + 2));
                if (cend <= chunkPtr || cend > tpl.Length) break;

                if (cid == 2) chunk2Pos = chunkPtr;
                else if (cid == 3) chunk3Pos = chunkPtr;
                else if (cid == 4) chunk4Pos = chunkPtr;

                chunkPtr = cend;
            }

            if (chunk2Pos < 0 || chunk3Pos < 0 || chunk4Pos < 0) return null;

            // Parse Chunk 2: Buffer Strides and Sizes
            int c2Sub0End = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(chunk2Pos + 8));
            if (c2Sub0End + 6 > tpl.Length) return null;
            int c2Sub4End = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(c2Sub0End + 2));
            if (c2Sub4End + 6 > tpl.Length) return null;
            int c2Sub1End = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(c2Sub4End + 2));
            if (c2Sub1End + 6 > tpl.Length) return null;

            int stridesStart = c2Sub4End + 6;
            int sizesStart = c2Sub1End + 6;

            if (stridesStart + (numBufs * 2) > tpl.Length || sizesStart + (numBufs * 4) > tpl.Length) return null;

            long cumOffset = 0;
            for (int i = 0; i < numBufs; i++)
            {
                template.BufferOffsets[i] = cumOffset;
                template.BufferStrides[i] = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(stridesStart + (i * 2)));
                uint sz = BinaryPrimitives.ReadUInt32LittleEndian(tpl.AsSpan(sizesStart + (i * 4)));
                cumOffset += sz;
            }

            // Parse Chunk 3: Stream Groups (SubChunk 2 at chunk3Pos + 6)
            int sgRecordStart = chunk3Pos + 12;
            if (sgRecordStart + (numSG * 25) > tpl.Length) return null;

            for (int i = 0; i < numSG; i++)
            {
                int off = sgRecordStart + (i * 25);
                template.StreamGroups[i] = new StreamGroup
                {
                    Vb0Idx = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(off + 1)),
                    Vb0Off = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(off + 5)),
                    Vb1Idx = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(off + 9)),
                    Vb1Off = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(off + 13)),
                    IbIdx = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(off + 17)),
                    IbOff = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(off + 21))
                };
            }

            // Parse Chunk 4: Submesh Records (SubChunk 0) and Stream Group Indices (SubChunk 1)
            int subRecStart = chunk4Pos + 12;
            int sub0End = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(chunk4Pos + 8));

            int sp = subRecStart;
            for (int i = 0; i < numSub; i++)
            {
                if (sp + 16 > tpl.Length) break;
                ushort vs = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(sp));
                ushort vc = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(sp + 2));
                ushort fs = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(sp + 4));
                ushort fc = BinaryPrimitives.ReadUInt16LittleEndian(tpl.AsSpan(sp + 6));
                uint mask = BinaryPrimitives.ReadUInt32LittleEndian(tpl.AsSpan(sp + 12));
                int pop = BitOperations.PopCount(mask);

                template.Submeshes[i] = new SubmeshRecord
                {
                    VStart = vs,
                    VCount = vc,
                    FStart = fs,
                    FCount = fc
                };

                sp += 16 + (pop * 8);
            }

            // SubChunk 1: submeshStreamGroupIndices
            int subSgStart = sub0End + 6;
            if (subSgStart + (numSub * 4) <= tpl.Length)
            {
                for (int i = 0; i < numSub; i++)
                {
                    template.SubmeshStreamGroup[i] = BinaryPrimitives.ReadInt32LittleEndian(tpl.AsSpan(subSgStart + (i * 4)));
                }
            }

            return template;
        }

        private static string? ExtractObjFromTemplate(TplTemplate tpl, IReadOnlyCollection<string>? targetMeshNames, out MeshDiag diag)
        {
            var matchedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nodeIndices = new HashSet<int>();

            bool selectAllLOD0 = (targetMeshNames == null || targetMeshNames.Count == 0);

            for (int i = 0; i < tpl.NodeCount; i++)
            {
                string path = tpl.NodePaths[i];
                if (string.IsNullOrEmpty(path)) path = $"node_{i}";

                // Skip non-LOD0 nodes
                if (path.Contains("_LOD1", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("_LOD2", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("_LOD3", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("_LOD4", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("_LOD5", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("_SIM", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (selectAllLOD0)
                {
                    if (tpl.SubmeshCount[i] > 0)
                        nodeIndices.Add(i);
                    continue;
                }

                // Check against target mesh names
                string leaf = path;
                int pipeIdx = leaf.LastIndexOf('|');
                if (pipeIdx >= 0) leaf = leaf[(pipeIdx + 1)..];
                int colonIdx = leaf.LastIndexOf(':');
                if (colonIdx >= 0) leaf = leaf[(colonIdx + 1)..];

                foreach (var target in targetMeshNames!)
                {
                    if (string.IsNullOrWhiteSpace(target)) continue;

                    if (string.Equals(leaf, target, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedNames.Add(target);
                        if (tpl.SubmeshCount[i] > 0) nodeIndices.Add(i);
                        break;
                    }

                    if (target.EndsWith("_grp", StringComparison.OrdinalIgnoreCase))
                    {
                        string baseTarget = target[..^4];
                        if (string.Equals(leaf, baseTarget, StringComparison.OrdinalIgnoreCase) ||
                            ((path.Contains($"|{target}|", StringComparison.OrdinalIgnoreCase) || path.Contains($":{target}|", StringComparison.OrdinalIgnoreCase)) && !leaf.Contains("_vanity_", StringComparison.OrdinalIgnoreCase)))
                        {
                            matchedNames.Add(target);
                            if (tpl.SubmeshCount[i] > 0) nodeIndices.Add(i);
                            break;
                        }
                    }
                }
            }

            string[] missingNames = targetMeshNames != null
                ? targetMeshNames.Where(t => !matchedNames.Contains(t)).ToArray()
                : Array.Empty<string>();

            diag = new MeshDiag(tpl.Prefix, matchedNames.Count, missingNames, 0, 0, 0, 0, Vector3.Zero, Vector3.Zero, 0, 0);

            if (nodeIndices.Count == 0) return null;

            // Collect all submesh indices
            var submeshIndices = new List<int>();
            foreach (int nodeIdx in nodeIndices)
            {
                int start = tpl.SubmeshStart[nodeIdx];
                int count = tpl.SubmeshCount[nodeIdx];
                for (int s = start; s < start + count && s < tpl.NumSubmeshes; s++)
                {
                    if (s >= 0) submeshIndices.Add(s);
                }
            }

            if (submeshIndices.Count == 0) return null;

            FileStream? fs = null;
            try
            {
                if (tpl.MemoryData == null && tpl.DataStartInPak >= 0)
                {
                    fs = File.OpenRead(tpl.PakPath);
                }

                var sbVertices = new StringBuilder(128 * 1024);
                var sbFaces = new StringBuilder(128 * 1024);

                int globalVertexOffset = 0;
                int totalVertices = 0;
                int totalTriangles = 0;
                int indexOutOfRange = 0;
                int degenerateCount = 0;
                float maxEdge = 0f;
                var edgeSamples = new List<float>(4096);

                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
                var allVertsList = new List<Vector3>(16384);

                Span<byte> vBuf = stackalloc byte[16];
                Span<byte> iBuf = stackalloc byte[6];

                var submeshToNodeName = new string[tpl.NumSubmeshes];
                var submeshToNodeIdx = new int[tpl.NumSubmeshes];
                Array.Fill(submeshToNodeIdx, -1);

                for (int n = 0; n < tpl.SubmeshStart.Length && n < tpl.NodePaths.Length; n++)
                {
                    int start = tpl.SubmeshStart[n];
                    int count = tpl.SubmeshCount[n];
                    string name = tpl.NodePaths[n];
                    int colon = name.IndexOf(':');
                    if (colon >= 0) name = name[(colon + 1)..];
                    int bar = name.LastIndexOf('|');
                    if (bar >= 0) name = name[(bar + 1)..];

                    for (int s = start; s < start + count && s < tpl.NumSubmeshes; s++)
                    {
                        if (s >= 0)
                        {
                            submeshToNodeName[s] = name;
                            submeshToNodeIdx[s] = n;
                        }
                    }
                }

                // Pre-scan vertices for each submesh to calculate accurate node-level fixed-point bounds
                var nodeRanges = new Dictionary<int, (short minX, short maxX, short minY, short maxY, short minZ, short maxZ)>();

                foreach (int subIdx in submeshIndices)
                {
                    var rec = tpl.Submeshes[subIdx];
                    if (rec.VCount == 0 || rec.FCount == 0) continue;
                    int sgIdx = tpl.SubmeshStreamGroup[subIdx];
                    if (sgIdx < 0 || sgIdx >= tpl.NumStreamGroups) continue;
                    var sg = tpl.StreamGroups[sgIdx];
                    int vb0Idx = sg.Vb0Idx;
                    if (vb0Idx < 0 || vb0Idx >= tpl.NumBuffers) continue;
                    int stride = tpl.BufferStrides[vb0Idx];
                    if (stride <= 0) stride = 16;
                    long vbBase = tpl.BufferOffsets[vb0Idx] + sg.Vb0Off;

                    int nodeIdx = submeshToNodeIdx[subIdx];
                    short nMinX = short.MaxValue, nMaxX = short.MinValue;
                    short nMinY = short.MaxValue, nMaxY = short.MinValue;
                    short nMinZ = short.MaxValue, nMaxZ = short.MinValue;

                    if (nodeRanges.TryGetValue(nodeIdx, out var existing))
                    {
                        nMinX = existing.minX; nMaxX = existing.maxX;
                        nMinY = existing.minY; nMaxY = existing.maxY;
                        nMinZ = existing.minZ; nMaxZ = existing.maxZ;
                    }

                    for (int v = 0; v < rec.VCount; v++)
                    {
                        long vOffset = vbBase + ((rec.VStart + v) * stride);
                        if (!ReadDataBytes(fs, tpl, vOffset, vBuf[..Math.Min(stride, 16)]))
                            break;

                        short vx = BinaryPrimitives.ReadInt16LittleEndian(vBuf[..2]);
                        short vy = BinaryPrimitives.ReadInt16LittleEndian(vBuf.Slice(2, 2));
                        short vz = BinaryPrimitives.ReadInt16LittleEndian(vBuf.Slice(4, 2));

                        if (vx < nMinX) nMinX = vx; if (vx > nMaxX) nMaxX = vx;
                        if (vy < nMinY) nMinY = vy; if (vy > nMaxY) nMaxY = vy;
                        if (vz < nMinZ) nMinZ = vz; if (vz > nMaxZ) nMaxZ = vz;
                    }

                    nodeRanges[nodeIdx] = (nMinX, nMaxX, nMinY, nMaxY, nMinZ, nMaxZ);
                }

                foreach (int subIdx in submeshIndices)
                {
                    var rec = tpl.Submeshes[subIdx];
                    if (rec.VCount == 0 || rec.FCount == 0) continue;

                    string subName = submeshToNodeName[subIdx] ?? $"submesh_{subIdx}";
                    sbFaces.Append("o ").Append(subName).Append('\n');
                    sbFaces.Append("g ").Append(subName).Append('\n');

                    int sgIdx = tpl.SubmeshStreamGroup[subIdx];
                    if (sgIdx < 0 || sgIdx >= tpl.NumStreamGroups) continue;
                    var sg = tpl.StreamGroups[sgIdx];

                    int vb0Idx = sg.Vb0Idx;
                    int ibIdx = sg.IbIdx;
                    if (vb0Idx < 0 || vb0Idx >= tpl.NumBuffers || ibIdx < 0 || ibIdx >= tpl.NumBuffers) continue;

                    int stride = tpl.BufferStrides[vb0Idx];
                    if (stride <= 0) stride = 16;

                    long vbBase = tpl.BufferOffsets[vb0Idx] + sg.Vb0Off;
                    long ibBase = tpl.BufferOffsets[ibIdx] + sg.IbOff;

                    int nodeIdx = submeshToNodeIdx[subIdx];
                    Vector3 center = (nodeIdx >= 0 && nodeIdx < tpl.NodeCenter.Length) ? tpl.NodeCenter[nodeIdx] : Vector3.Zero;
                    Vector3 extent = (nodeIdx >= 0 && nodeIdx < tpl.NodeExtent.Length) ? tpl.NodeExtent[nodeIdx] : Vector3.One;

                    if (!float.IsFinite(extent.X) || extent.X <= 0.001f || extent.X > 5.0f) extent.X = 0.5f;
                    if (!float.IsFinite(extent.Y) || extent.Y <= 0.001f || extent.Y > 5.0f) extent.Y = 0.5f;
                    if (!float.IsFinite(extent.Z) || extent.Z <= 0.001f || extent.Z > 5.0f) extent.Z = 0.5f;
                    if (!float.IsFinite(center.X) || Math.Abs(center.X) > 5.0f) center.X = 0;
                    if (!float.IsFinite(center.Y) || Math.Abs(center.Y) > 5.0f) center.Y = 0;
                    if (!float.IsFinite(center.Z) || Math.Abs(center.Z) > 5.0f) center.Z = 0;

                    bool hasValidNodeRange = nodeRanges.TryGetValue(nodeIdx, out var nr) &&
                                             nr.maxX > nr.minX && nr.maxY > nr.minY && nr.maxZ > nr.minZ;

                    float rangeVx = hasValidNodeRange ? (nr.maxX - nr.minX) : 65535f;
                    float rangeVy = hasValidNodeRange ? (nr.maxY - nr.minY) : 65535f;
                    float rangeVz = hasValidNodeRange ? (nr.maxZ - nr.minZ) : 65535f;

                    float minBoxX = center.X - extent.X;
                    float minBoxY = center.Y - extent.Y;
                    float minBoxZ = center.Z - extent.Z;
                    float sizeBoxX = extent.X * 2.0f;
                    float sizeBoxY = extent.Y * 2.0f;
                    float sizeBoxZ = extent.Z * 2.0f;

                    int submeshVertexStart = allVertsList.Count;

                    // Read Vertices
                    for (int v = 0; v < rec.VCount; v++)
                    {
                        long vOffset = vbBase + ((rec.VStart + v) * stride);
                        if (!ReadDataBytes(fs, tpl, vOffset, vBuf[..Math.Min(stride, 16)]))
                            break;

                        short vx = BinaryPrimitives.ReadInt16LittleEndian(vBuf[..2]);
                        short vy = BinaryPrimitives.ReadInt16LittleEndian(vBuf.Slice(2, 2));
                        short vz = BinaryPrimitives.ReadInt16LittleEndian(vBuf.Slice(4, 2));

                        float x, y, z;
                        if (hasValidNodeRange && extent != Vector3.One)
                        {
                            float fx = (vx - nr.minX) / rangeVx;
                            float fy = (vy - nr.minY) / rangeVy;
                            float fz = (vz - nr.minZ) / rangeVz;

                            x = minBoxX + (fx * sizeBoxX);
                            y = minBoxY + (fy * sizeBoxY);
                            z = minBoxZ + (fz * sizeBoxZ);
                        }
                        else
                        {
                            float normX = vx / 32767.0f;
                            float normY = vy / 32767.0f;
                            float normZ = vz / 32767.0f;

                            x = center.X + (normX * extent.X);
                            y = center.Y + (normY * extent.Y);
                            z = center.Z + (normZ * extent.Z);
                        }

                        var vertPos = new Vector3(x, y, z);
                        allVertsList.Add(vertPos);

                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                        if (z < minZ) minZ = z;
                        if (z > maxZ) maxZ = z;

                        sbVertices.Append("v ").Append(x.ToString("F5", CultureInfo.InvariantCulture)).Append(' ')
                                  .Append(y.ToString("F5", CultureInfo.InvariantCulture)).Append(' ')
                                  .Append(z.ToString("F5", CultureInfo.InvariantCulture)).Append('\n');
                        totalVertices++;
                    }

                    // Read Faces
                    for (int f = 0; f < rec.FCount; f++)
                    {
                        long fOffset = ibBase + ((rec.FStart + f) * 6);
                        if (!ReadDataBytes(fs, tpl, fOffset, iBuf))
                            break;

                        ushort i0 = BinaryPrimitives.ReadUInt16LittleEndian(iBuf[..2]);
                        ushort i1 = BinaryPrimitives.ReadUInt16LittleEndian(iBuf.Slice(2, 2));
                        ushort i2 = BinaryPrimitives.ReadUInt16LittleEndian(iBuf.Slice(4, 2));

                        // Integrity checks
                        if (i0 < rec.VStart || i0 >= rec.VStart + rec.VCount) indexOutOfRange++;
                        if (i1 < rec.VStart || i1 >= rec.VStart + rec.VCount) indexOutOfRange++;
                        if (i2 < rec.VStart || i2 >= rec.VStart + rec.VCount) indexOutOfRange++;

                        if (i0 == i1 || i1 == i2 || i2 == i0)
                        {
                            degenerateCount++;
                        }

                        int rel0 = (i0 - rec.VStart) + globalVertexOffset + 1;
                        int rel1 = (i1 - rec.VStart) + globalVertexOffset + 1;
                        int rel2 = (i2 - rec.VStart) + globalVertexOffset + 1;

                        int local0 = submeshVertexStart + (i0 - rec.VStart);
                        int local1 = submeshVertexStart + (i1 - rec.VStart);
                        int local2 = submeshVertexStart + (i2 - rec.VStart);

                        if (local0 >= 0 && local0 < allVertsList.Count &&
                            local1 >= 0 && local1 < allVertsList.Count &&
                            local2 >= 0 && local2 < allVertsList.Count)
                        {
                            float e0 = Vector3.Distance(allVertsList[local0], allVertsList[local1]);
                            float e1 = Vector3.Distance(allVertsList[local1], allVertsList[local2]);
                            float e2 = Vector3.Distance(allVertsList[local2], allVertsList[local0]);
                            float localMax = Math.Max(e0, Math.Max(e1, e2));
                            if (localMax > maxEdge) maxEdge = localMax;
                            if (edgeSamples.Count < 4000)
                            {
                                edgeSamples.Add(e0);
                                edgeSamples.Add(e1);
                                edgeSamples.Add(e2);
                            }
                        }

                        sbFaces.Append("f ").Append(rel0).Append(' ')
                               .Append(rel1).Append(' ')
                               .Append(rel2).Append('\n');
                        totalTriangles++;
                    }

                    globalVertexOffset += rec.VCount;
                }

                float medianEdge = 0f;
                if (edgeSamples.Count > 0)
                {
                    edgeSamples.Sort();
                    medianEdge = edgeSamples[edgeSamples.Count / 2];
                }

                if (totalVertices == 0 || totalTriangles == 0)
                {
                    diag = new MeshDiag(tpl.Prefix, matchedNames.Count, missingNames, totalVertices, totalTriangles, indexOutOfRange, degenerateCount, Vector3.Zero, Vector3.Zero, maxEdge, medianEdge);
                    return null;
                }

                diag = new MeshDiag(tpl.Prefix, matchedNames.Count, missingNames, totalVertices, totalTriangles, indexOutOfRange, degenerateCount, new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ), maxEdge, medianEdge);

                var finalSb = new StringBuilder(sbVertices.Length + sbFaces.Length + 128);
                finalSb.AppendLine($"# Space Marine 2 Real In-Game Extracted Model");
                finalSb.AppendLine($"# Vertices: {totalVertices}, Triangles: {totalTriangles}");
                finalSb.Append(sbVertices);
                finalSb.Append(sbFaces);

                return finalSb.ToString();
            }
            finally
            {
                fs?.Dispose();
            }
        }

        private static bool ReadDataBytes(FileStream? fs, TplTemplate tpl, long offset, Span<byte> destination)
        {
            if (tpl.MemoryData != null)
            {
                if (offset < 0 || offset + destination.Length > tpl.MemoryData.Length)
                    return false;
                tpl.MemoryData.AsSpan((int)offset, destination.Length).CopyTo(destination);
                return true;
            }

            if (fs != null && tpl.DataStartInPak >= 0)
            {
                if (offset < 0 || offset + destination.Length > tpl.DataLength)
                    return false;

                fs.Position = tpl.DataStartInPak + offset;
                int read = fs.Read(destination);
                return read == destination.Length;
            }

            return false;
        }

        public static void ClearCache()
        {
            TemplateCache.Clear();
        }
    }
}
