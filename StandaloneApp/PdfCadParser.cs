using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HomeBuilderStandalone
{
    public class CadTextItem
    {
        public string Text { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class CadLayerInfo
    {
        public string Tag { get; set; } = "";
        public string Name { get; set; } = "";
        public int SegmentCount { get; set; }
        public int PointCount { get; set; }
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
        public List<CadSegment> Segments { get; set; } = new();
        public List<string> Texts { get; set; } = new();
        public List<CadTextItem> TextItems { get; set; } = new();
    }

    public class CadSegment
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Length => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));
    }

    public class DetectedWall
    {
        public string Id { get; set; } = "";
        public double X1 { get; set; }
        public double Z1 { get; set; }
        public double X2 { get; set; }
        public double Z2 { get; set; }
        public double Thickness { get; set; }
        public double Height { get; set; } = 2.75;
        public List<DetectedOpening> Openings { get; set; } = new();
    }

    public class DetectedColumn
    {
        public string Id { get; set; } = "";
        public double X { get; set; }
        public double Z { get; set; }
        public double Width { get; set; } = 0.30;
        public double Depth { get; set; } = 0.30;
        public double Height { get; set; } = 2.75;
        public bool Round { get; set; } = false;
    }

    public class DetectedOpening
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "WINDOW"; // "WINDOW" or "DOOR"
        public double Dist { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Sill { get; set; }
    }

    public class CadPdfResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string FileName { get; set; } = "";
        public double MediaBoxWidth { get; set; }
        public double MediaBoxHeight { get; set; }
        public double ScaleFactor { get; set; } = 1.0;
        public double UnitsPerMeter { get; set; } = 236.2205;
        public string DetectedScale { get; set; } = "1:100";
        public double DetectedScaleRatio { get; set; } = 100.0;
        public string RecommendedWallLayer { get; set; } = "";
        public string RecommendedWindowLayer { get; set; } = "";
        public string RecommendedDoorLayer { get; set; } = "";
        public List<CadLayerInfo> Layers { get; set; } = new();
        public List<DetectedWall> Walls { get; set; } = new();
        public List<DetectedColumn> Columns { get; set; } = new();
        public string BlueprintSvg { get; set; } = "";
        public double BoundsMinX { get; set; }
        public double BoundsMaxX { get; set; }
        public double BoundsMinY { get; set; }
        public double BoundsMaxY { get; set; }
        public double BuildingMinX { get; set; }
        public double BuildingMaxX { get; set; }
        public double BuildingMinY { get; set; }
        public double BuildingMaxY { get; set; }
        public double BuildingCenterX { get; set; }
        public double BuildingCenterY { get; set; }
        public double SheetCenterX { get; set; }
        public double SheetCenterY { get; set; }
    }

    public static class PdfCadParser
    {
        private static readonly Regex WallRegex = new(
            @"(scian|wall|wand|mur|obrys|arch.*str|struc|part|korp|bare|a-wall)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WindowRegex = new(
            @"(okn|window|fenst|glaz|szkl|stolarka.*okn|a-glaz|a-door-glaz)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DoorRegex = new(
            @"(drzw|door|tuer|gate|bram|stolarka.*drzw|a-door)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static CadPdfResult ParsePdf(byte[] pdfBytes, string fileName, double targetWallHeight = 2.75)
        {
            var result = new CadPdfResult { FileName = fileName };

            try
            {
                string rawPdf = Encoding.Latin1.GetString(pdfBytes);

                // 1. MediaBox
                var mboxMatch = Regex.Match(rawPdf, @"/MediaBox\s*\[\s*([-\d\.]+)\s+([-\d\.]+)\s+([-\d\.]+)\s+([-\d\.]+)\s*\]");
                if (mboxMatch.Success)
                {
                    double.TryParse(mboxMatch.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double mw);
                    double.TryParse(mboxMatch.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double mh);
                    result.MediaBoxWidth = mw;
                    result.MediaBoxHeight = mh;
                }
                else
                {
                    result.MediaBoxWidth = 1191;
                    result.MediaBoxHeight = 842;
                }

                // 2. Discover OCG Layers
                var ocgMap = new Dictionary<string, string>(); // refId -> LayerName
                var ocgTagMap = new Dictionary<string, string>(); // ocX -> LayerName
                var ocgMatches = Regex.Matches(rawPdf, @"(\d+)\s+0\s+obj\s*<<\s*/Type\s*/OCG\s+/Name\s*\((.*?)\).*?>>", RegexOptions.Singleline);
                foreach (Match m in ocgMatches)
                {
                    string refId = m.Groups[1].Value;
                    string name = UnescapePdfString(m.Groups[2].Value);
                    ocgMap[refId] = name;
                }

                var propMatches = Regex.Matches(rawPdf, @"/Properties\s*<<\s*(.*?)\s*>>", RegexOptions.Singleline);
                foreach (Match pm in propMatches)
                {
                    var ocItems = Regex.Matches(pm.Groups[1].Value, @"/([^\s]+)\s+(\d+)\s+0\s+R");
                    foreach (Match om in ocItems)
                    {
                        string tag = om.Groups[1].Value;
                        string refId = om.Groups[2].Value;
                        if (ocgMap.TryGetValue(refId, out string? name))
                        {
                            ocgTagMap[tag] = name;
                        }
                    }
                }

                // 3. Decompress all streams
                var streamMatches = Regex.Matches(rawPdf, @"<<.*?>>\s*stream\r?\n(.*?)\r?\nendstream", RegexOptions.Singleline);
                var fullContentBuilder = new StringBuilder();

                foreach (Match sm in streamMatches)
                {
                    int startIdx = sm.Groups[1].Index;
                    int length = sm.Groups[1].Length;
                    byte[] streamBytes = new byte[length];
                    Array.Copy(pdfBytes, startIdx, streamBytes, 0, length);

                    byte[]? decompressed = DecompressZlib(streamBytes);
                    if (decompressed != null && decompressed.Length > 0)
                    {
                        fullContentBuilder.Append(Encoding.Latin1.GetString(decompressed));
                        fullContentBuilder.Append('\n');
                    }
                }

                string fullContent = fullContentBuilder.ToString();

                // 4. Parse Marked Content Blocks (/OC /ocX BDC ... EMC)
                var layerDataMap = new Dictionary<string, CadLayerInfo>();
                foreach (var kvp in ocgTagMap)
                {
                    layerDataMap[kvp.Key] = new CadLayerInfo
                    {
                        Tag = kvp.Key,
                        Name = kvp.Value,
                        MinX = double.MaxValue,
                        MaxX = double.MinValue,
                        MinY = double.MaxValue,
                        MaxY = double.MinValue
                    };
                }

                var bdcMatches = Regex.Matches(fullContent, @"/OC\s+/([^\s]+)\s+BDC(.*?)EMC", RegexOptions.Singleline);
                foreach (Match bm in bdcMatches)
                {
                    string tag = bm.Groups[1].Value;
                    string body = bm.Groups[2].Value;

                    if (!layerDataMap.TryGetValue(tag, out CadLayerInfo? layerInfo))
                    {
                        layerInfo = new CadLayerInfo
                        {
                            Tag = tag,
                            Name = ocgTagMap.GetValueOrDefault(tag, tag),
                            MinX = double.MaxValue,
                            MaxX = double.MinValue,
                            MinY = double.MaxValue,
                            MaxY = double.MinValue
                        };
                        layerDataMap[tag] = layerInfo;
                    }

                    ParsePathSegments(body, layerInfo);
                    ParseTextStrings(body, layerInfo);
                }

                result.Layers = layerDataMap.Values
                    .Where(l => l.SegmentCount > 0 || l.Texts.Count > 0)
                    .OrderByDescending(l => l.SegmentCount)
                    .ToList();

                if (result.Layers.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "Nie znaleziono warstw wektorowych w pliku PDF.";
                    return result;
                }

                // 5. Overall geometry bounds
                double bMinX = double.MaxValue, bMaxX = double.MinValue;
                double bMinY = double.MaxValue, bMaxY = double.MinValue;
                foreach (var l in result.Layers)
                {
                    if (l.SegmentCount > 0)
                    {
                        bMinX = Math.Min(bMinX, l.MinX);
                        bMaxX = Math.Max(bMaxX, l.MaxX);
                        bMinY = Math.Min(bMinY, l.MinY);
                        bMaxY = Math.Max(bMaxY, l.MaxY);
                    }
                }
                result.BoundsMinX = bMinX < double.MaxValue ? bMinX : 0;
                result.BoundsMaxX = bMaxX > double.MinValue ? bMaxX : 1000;
                result.BoundsMinY = bMinY < double.MaxValue ? bMinY : 0;
                result.BoundsMaxY = bMaxY > double.MinValue ? bMaxY : 1000;

                // 6. Universal Layer Classification (Zero Hardcoding)
                ClassifyLayersUniversally(result);

                // Compute Building Bounds from Recommended Wall Layer
                var recWallLayer = result.Layers.FirstOrDefault(l => l.Tag == result.RecommendedWallLayer);
                if (recWallLayer != null && recWallLayer.Segments.Count > 0)
                {
                    result.BuildingMinX = recWallLayer.MinX;
                    result.BuildingMaxX = recWallLayer.MaxX;
                    result.BuildingMinY = recWallLayer.MinY;
                    result.BuildingMaxY = recWallLayer.MaxY;
                }
                else
                {
                    result.BuildingMinX = result.BoundsMinX;
                    result.BuildingMaxX = result.BoundsMaxX;
                    result.BuildingMinY = result.BoundsMinY;
                    result.BuildingMaxY = result.BoundsMaxY;
                }
                result.SheetCenterX = (result.BoundsMinX + result.BoundsMaxX) * 0.5;
                result.SheetCenterY = (result.BoundsMinY + result.BoundsMaxY) * 0.5;

                // 7. Calculate Metric Scale Factor (Units to Meters)
                DetermineScaleFactor(result, fullContent);

                // 8. Generate Blueprint SVG
                result.BlueprintSvg = GenerateSvg(result);

                // 9. Reconstruct Centerlines for Walls and Extract Openings
                ReconstructBimGeometry(result, targetWallHeight);

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Błąd przetwarzania wektorowego PDF: {ex.Message}";
                return result;
            }
        }

        private static void ClassifyLayersUniversally(CadPdfResult result)
        {
            // Level 1: Semantic Regex Pattern Matching
            CadLayerInfo? bestWall = null;
            CadLayerInfo? bestWindow = null;
            CadLayerInfo? bestDoor = null;

            foreach (var l in result.Layers)
            {
                if (bestWall == null && WallRegex.IsMatch(l.Name) && l.SegmentCount > 10)
                {
                    bestWall = l;
                }
                if (bestWindow == null && WindowRegex.IsMatch(l.Name) && l.SegmentCount > 5)
                {
                    bestWindow = l;
                }
                if (bestDoor == null && DoorRegex.IsMatch(l.Name) && (l.SegmentCount > 5 || l.Texts.Count > 0))
                {
                    bestDoor = l;
                }
            }

            // Level 2: Geometric heuristics fallback if semantic matching didn't find all
            if (bestWall == null)
            {
                // Walls typically have many long parallel segments
                bestWall = result.Layers
                    .OrderByDescending(l => ScoreLayerAsWall(l))
                    .FirstOrDefault();
            }

            if (bestWindow == null)
            {
                bestWindow = result.Layers
                    .Where(l => l != bestWall && l != bestDoor)
                    .OrderByDescending(l => l.SegmentCount)
                    .FirstOrDefault();
            }

            if (bestDoor == null)
            {
                bestDoor = result.Layers
                    .Where(l => l != bestWall && l != bestWindow && l.Texts.Any(t => t.StartsWith("D") || t.Contains("200")))
                    .FirstOrDefault();
            }

            result.RecommendedWallLayer = bestWall?.Tag ?? "";
            result.RecommendedWindowLayer = bestWindow?.Tag ?? "";
            result.RecommendedDoorLayer = bestDoor?.Tag ?? "";
        }

        private static double ScoreLayerAsWall(CadLayerInfo layer)
        {
            if (layer.SegmentCount < 4) return 0;
            // Check for parallel segments
            double parallelPairs = 0;
            var segs = layer.Segments.Take(100).ToList();
            for (int i = 0; i < segs.Count; i++)
            {
                for (int j = i + 1; j < segs.Count; j++)
                {
                    var s1 = segs[i];
                    var s2 = segs[j];
                    bool s1H = Math.Abs(s1.Y2 - s1.Y1) < 2.0;
                    bool s2H = Math.Abs(s2.Y2 - s2.Y1) < 2.0;
                    bool s1V = Math.Abs(s1.X2 - s1.X1) < 2.0;
                    bool s2V = Math.Abs(s2.X2 - s2.X1) < 2.0;

                    if (s1H && s2H)
                    {
                        double dist = Math.Abs(s1.Y1 - s2.Y1);
                        if (dist > 10 && dist < 400) parallelPairs++;
                    }
                    else if (s1V && s2V)
                    {
                        double dist = Math.Abs(s1.X1 - s2.X1);
                        if (dist > 10 && dist < 400) parallelPairs++;
                    }
                }
            }
            return parallelPairs * Math.Log(layer.SegmentCount + 1);
        }

        private static void DetermineScaleFactor(CadPdfResult result, string fullContent)
        {
            // 1. Search for scale specification in text across the drawing (e.g. "SKALA 1:100", "1:50", "1:75", "1:200", "1:20", "1:500")
            double scaleRatio = 100.0;
            string detectedScale = "1:100";
            var scaleTextMatch = Regex.Match(fullContent, @"(?:skala|scale)?\s*1\s*:\s*([0-9]{2,3})", RegexOptions.IgnoreCase);
            if (scaleTextMatch.Success && double.TryParse(scaleTextMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedRatio) && parsedRatio >= 10 && parsedRatio <= 1000)
            {
                scaleRatio = parsedRatio;
                detectedScale = $"1:{scaleRatio:0}";
            }

            // 2. Check for standard CAD print matrix in PDF (e.g. 0.12 0 0 0.12 cm for 1:100 scale)
            var cmMatch = Regex.Match(fullContent, @"([0-9.]+)\s+0\s+0\s+([0-9.]+)\s+([0-9.]+)\s+([0-9.]+)\s+cm");
            if (cmMatch.Success && double.TryParse(cmMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double scaleMatrix) && scaleMatrix > 0.001 && scaleMatrix < 5.0)
            {
                // Real world: 1 meter at 1:scaleRatio scale:
                // At 1:100, 1m = 10mm = 10 / 25.4 * 72 pt = 28.3464567 pt.
                // At 1:scaleRatio, 1m = (1000.0 / scaleRatio) mm = (1000.0 / scaleRatio) / 25.4 * 72.0 pt.
                double ptPerMeter = (1000.0 / scaleRatio) / 25.4 * 72.0;
                double unitsPerMeter = ptPerMeter / scaleMatrix;
                if (unitsPerMeter > 20 && unitsPerMeter < 10000)
                {
                    result.UnitsPerMeter = unitsPerMeter;
                    result.ScaleFactor = 1.0 / unitsPerMeter;
                    result.DetectedScale = detectedScale;
                    result.DetectedScaleRatio = scaleRatio;
                    return;
                }
            }

            // 3. Geometric heuristic from wall thickness in wall layer
            var wallLayer = result.Layers.FirstOrDefault(l => l.Tag == result.RecommendedWallLayer);
            if (wallLayer != null && wallLayer.Segments.Count > 0)
            {
                var distances = new List<double>();
                var segs = wallLayer.Segments;
                for (int i = 0; i < Math.Min(120, segs.Count); i++)
                {
                    for (int j = i + 1; j < Math.Min(120, segs.Count); j++)
                    {
                        var s1 = segs[i];
                        var s2 = segs[j];
                        bool s1H = Math.Abs(s1.Y2 - s1.Y1) < 2.0 && s1.Length > 40;
                        bool s2H = Math.Abs(s2.Y2 - s2.Y1) < 2.0 && s2.Length > 40;
                        bool s1V = Math.Abs(s1.X2 - s1.X1) < 2.0 && s1.Length > 40;
                        bool s2V = Math.Abs(s2.X2 - s2.X1) < 2.0 && s2.Length > 40;

                        if (s1H && s2H)
                        {
                            double d = Math.Abs(s1.Y1 - s2.Y1);
                            if (d >= 20 && d <= 160) distances.Add(d);
                        }
                        else if (s1V && s2V)
                        {
                            double d = Math.Abs(s1.X1 - s2.X1);
                            if (d >= 20 && d <= 160) distances.Add(d);
                        }
                    }
                }

                if (distances.Count > 0)
                {
                    var mode = distances.GroupBy(d => Math.Round(d / 5.0) * 5.0)
                                        .OrderByDescending(g => g.Count())
                                        .First().Key;

                    // External wall thickness in Central Europe is typically 0.44m (25cm brick + 18-20cm insulation)
                    // or 0.25m structural
                    double assumedWallMeters = mode > 80 ? 0.44 : 0.25;
                    double unitsPerMeter = mode / assumedWallMeters;
                    if (unitsPerMeter > 80 && unitsPerMeter < 1000)
                    {
                        result.UnitsPerMeter = unitsPerMeter;
                        result.ScaleFactor = 1.0 / unitsPerMeter;
                        result.DetectedScale = detectedScale;
                        result.DetectedScaleRatio = scaleRatio;
                        return;
                    }
                }
            }

            // Fallback: exact 1:100 AutoCAD PDF scale (236.2205 units/m)
            result.UnitsPerMeter = 236.2205 * (100.0 / scaleRatio);
            result.ScaleFactor = 1.0 / result.UnitsPerMeter;
            result.DetectedScale = detectedScale;
            result.DetectedScaleRatio = scaleRatio;
        }

        private static void ReconstructBimGeometry(CadPdfResult result, double wallHeight)
        {
            var wallLayer = result.Layers.FirstOrDefault(l => l.Tag == result.RecommendedWallLayer);
            if (wallLayer == null || wallLayer.Segments.Count == 0) return;

            double s = result.ScaleFactor;
            const double TOLERANCE = 4.0; // PDF CAD units

            // 1. Group segments into Horizontal and Vertical coordinate clusters
            var hClusters = new Dictionary<double, List<(double x1, double x2)>>();
            var vClusters = new Dictionary<double, List<(double y1, double y2)>>();

            foreach (var seg in wallLayer.Segments)
            {
                double dx = Math.Abs(seg.X2 - seg.X1);
                double dy = Math.Abs(seg.Y2 - seg.Y1);
                if (dy <= 2.5 && dx >= 15.0)
                {
                    double midY = (seg.Y1 + seg.Y2) * 0.5;
                    double key = midY;
                    foreach (var k in hClusters.Keys)
                    {
                        if (Math.Abs(k - midY) <= TOLERANCE) { key = k; break; }
                    }
                    if (!hClusters.ContainsKey(key)) hClusters[key] = new List<(double, double)>();
                    hClusters[key].Add((Math.Min(seg.X1, seg.X2), Math.Max(seg.X1, seg.X2)));
                }
                else if (dx <= 2.5 && dy >= 15.0)
                {
                    double midX = (seg.X1 + seg.X2) * 0.5;
                    double key = midX;
                    foreach (var k in vClusters.Keys)
                    {
                        if (Math.Abs(k - midX) <= TOLERANCE) { key = k; break; }
                    }
                    if (!vClusters.ContainsKey(key)) vClusters[key] = new List<(double, double)>();
                    vClusters[key].Add((Math.Min(seg.Y1, seg.Y2), Math.Max(seg.Y1, seg.Y2)));
                }
            }

            // Merge touching/overlapping intervals per coordinate line (keeps gaps between distinct rooms/walls!)
            List<(double a, double b)> MergeIntervals(List<(double a, double b)> list)
            {
                if (list == null || list.Count == 0) return new List<(double a, double b)>();
                var sorted = list.OrderBy(x => x.a).ToList();
                var merged = new List<(double a, double b)> { sorted[0] };
                for (int k = 1; k < sorted.Count; k++)
                {
                    var last = merged[^1];
                    var cur = sorted[k];
                    if (cur.a <= last.b + 5.0)
                    {
                        merged[^1] = (last.a, Math.Max(last.b, cur.b));
                    }
                    else
                    {
                        merged.Add(cur);
                    }
                }
                return merged;
            }

            foreach (var k in hClusters.Keys.ToList()) hClusters[k] = MergeIntervals(hClusters[k]);
            foreach (var k in vClusters.Keys.ToList()) vClusters[k] = MergeIntervals(vClusters[k]);

            // 2. Collect all raw corridor segments between parallel lines within thickness range (8cm to 75cm)
            var rawHCorridors = new List<(double y, double s, double e, double thick)>();
            var sortedHy = hClusters.Keys.OrderBy(k => k).ToList();

            for (int i = 0; i < sortedHy.Count; i++)
            {
                double y1 = sortedHy[i];
                var list1 = hClusters[y1];
                for (int j = i + 1; j < Math.Min(i + 8, sortedHy.Count); j++)
                {
                    double y2 = sortedHy[j];
                    double diff = y2 - y1;
                    double thickM = diff * s;
                    if (thickM >= 0.08 && thickM <= 0.75)
                    {
                        var list2 = hClusters[y2];
                        foreach (var int1 in list1)
                        {
                            foreach (var int2 in list2)
                            {
                                double ovStart = Math.Max(int1.x1, int2.x1);
                                double ovEnd = Math.Min(int1.x2, int2.x2);
                                if (ovEnd - ovStart >= 20.0)
                                {
                                    rawHCorridors.Add(((y1 + y2) * 0.5, ovStart, ovEnd, thickM));
                                }
                            }
                        }
                    }
                }
            }

            var rawVCorridors = new List<(double x, double s, double e, double thick)>();
            var sortedVx = vClusters.Keys.OrderBy(k => k).ToList();

            for (int i = 0; i < sortedVx.Count; i++)
            {
                double x1 = sortedVx[i];
                var list1 = vClusters[x1];
                for (int j = i + 1; j < Math.Min(i + 8, sortedVx.Count); j++)
                {
                    double x2 = sortedVx[j];
                    double diff = x2 - x1;
                    double thickM = diff * s;
                    if (thickM >= 0.08 && thickM <= 0.75)
                    {
                        var list2 = vClusters[x2];
                        foreach (var int1 in list1)
                        {
                            foreach (var int2 in list2)
                            {
                                double ovStart = Math.Max(int1.y1, int2.y1);
                                double ovEnd = Math.Min(int1.y2, int2.y2);
                                if (ovEnd - ovStart >= 20.0)
                                {
                                    rawVCorridors.Add(((x1 + x2) * 0.5, ovStart, ovEnd, thickM));
                                }
                            }
                        }
                    }
                }
            }

            // Merge collinear corridor intervals on the same axis (within 8 CAD units = ~3.5 cm)
            List<(double axis, double s, double e, double thick)> MergeCorridors(List<(double pos, double s, double e, double thick)> corridors)
            {
                var result = new List<(double axis, double s, double e, double thick)>();
                var groups = new List<(double axis, List<(double s, double e, double thick)> items)>();
                const double AXIS_TOL = 8.0;

                foreach (var c in corridors)
                {
                    int grpIdx = groups.FindIndex(g => Math.Abs(g.axis - c.pos) <= AXIS_TOL);
                    if (grpIdx < 0)
                    {
                        groups.Add((c.pos, new List<(double s, double e, double thick)> { (c.s, c.e, c.thick) }));
                    }
                    else
                    {
                        groups[grpIdx].items.Add((c.s, c.e, c.thick));
                    }
                }

                foreach (var grp in groups)
                {
                    var sorted = grp.items.OrderBy(it => it.s).ToList();
                    (double s, double e, double thick)? cur = null;
                    foreach (var item in sorted)
                    {
                        if (cur == null)
                        {
                            cur = item;
                        }
                        else
                        {
                            if (item.s <= cur.Value.e + 10.0)
                            {
                                cur = (cur.Value.s, Math.Max(cur.Value.e, item.e), Math.Max(cur.Value.thick, item.thick));
                            }
                            else
                            {
                                result.Add((grp.axis, cur.Value.s, cur.Value.e, cur.Value.thick));
                                cur = item;
                            }
                        }
                    }
                    if (cur != null)
                    {
                        result.Add((grp.axis, cur.Value.s, cur.Value.e, cur.Value.thick));
                    }
                }
                return result;
            }

            var mergedH = MergeCorridors(rawHCorridors);
            var mergedV = MergeCorridors(rawVCorridors);

            var rawColumns = new List<(double cx, double cy, double w, double d)>();
            var rawHWalls = new List<(double y, double x1, double x2, double thick)>();
            var rawVWalls = new List<(double x, double y1, double y2, double thick)>();

            foreach (var h in mergedH)
            {
                double lenM = (h.e - h.s) * s;
                if (lenM <= 0.85 && h.thick <= 0.85 && Math.Min(lenM, h.thick) / Math.Max(lenM, h.thick) >= 0.70)
                {
                    rawColumns.Add(((h.s + h.e) * 0.5, h.axis, Math.Round(lenM, 2), Math.Round(h.thick, 2)));
                }
                else
                {
                    rawHWalls.Add((h.axis, h.s, h.e, Math.Round(h.thick, 2)));
                }
            }

            foreach (var v in mergedV)
            {
                double lenM = (v.e - v.s) * s;
                if (lenM <= 0.85 && v.thick <= 0.85 && Math.Min(lenM, v.thick) / Math.Max(lenM, v.thick) >= 0.70)
                {
                    double cxCol = v.axis;
                    double cyCol = (v.s + v.e) * 0.5;
                    if (!rawColumns.Any(c => Math.Sqrt((c.cx - cxCol) * (c.cx - cxCol) + (c.cy - cyCol) * (c.cy - cyCol)) < 35.0))
                    {
                        rawColumns.Add((cxCol, cyCol, Math.Round(v.thick, 2), Math.Round(lenM, 2)));
                    }
                }
                else
                {
                    rawVWalls.Add((v.axis, v.s, v.e, Math.Round(v.thick, 2)));
                }
            }

            // 3. Center building in 3D
            var allXs = rawHWalls.Select(w => w.x1).Concat(rawHWalls.Select(w => w.x2))
                .Concat(rawVWalls.Select(w => w.x))
                .Concat(rawColumns.Select(c => c.cx)).ToList();
            var allYs = rawHWalls.Select(w => w.y)
                .Concat(rawVWalls.Select(w => w.y1)).Concat(rawVWalls.Select(w => w.y2))
                .Concat(rawColumns.Select(c => c.cy)).ToList();
            if (allXs.Count == 0 || allYs.Count == 0) return;

            double cx = (allXs.Min() + allXs.Max()) * 0.5;
            double cy = (allYs.Min() + allYs.Max()) * 0.5;
            result.BuildingCenterX = cx;
            result.BuildingCenterY = cy;

            var centerlines = new List<DetectedWall>();
            int wallCounter = 1;

            // Horizontal walls: strictly Z1 == Z2 (0% tilt guarantee!)
            foreach (var hw in rawHWalls)
            {
                centerlines.Add(new DetectedWall
                {
                    Id = $"wall_h_{wallCounter++}",
                    X1 = (hw.x1 - cx) * s,
                    Z1 = -(hw.y - cy) * s,
                    X2 = (hw.x2 - cx) * s,
                    Z2 = -(hw.y - cy) * s,
                    Thickness = hw.thick > 0.08 ? hw.thick : 0.24,
                    Height = wallHeight
                });
            }

            // Vertical walls: strictly X1 == X2 (0% tilt guarantee!)
            foreach (var vw in rawVWalls)
            {
                centerlines.Add(new DetectedWall
                {
                    Id = $"wall_v_{wallCounter++}",
                    X1 = (vw.x - cx) * s,
                    Z1 = -(vw.y1 - cy) * s,
                    X2 = (vw.x - cx) * s,
                    Z2 = -(vw.y2 - cy) * s,
                    Thickness = vw.thick > 0.08 ? vw.thick : 0.24,
                    Height = wallHeight
                });
            }

            // 4. 1D Axis-Invariant Corner Snapping
            const double SNAP_M = 0.38;

            // Snap H endpoints along X to meeting V walls
            foreach (var hw in centerlines)
            {
                if (Math.Abs(hw.Z2 - hw.Z1) > 1e-5) continue; // Only H walls
                foreach (var vw in centerlines)
                {
                    if (Math.Abs(vw.X2 - vw.X1) > 1e-5) continue; // Only V walls
                    double minVz = Math.Min(vw.Z1, vw.Z2);
                    double maxVz = Math.Max(vw.Z1, vw.Z2);
                    if (hw.Z1 >= minVz - SNAP_M && hw.Z1 <= maxVz + SNAP_M)
                    {
                        if (Math.Abs(hw.X1 - vw.X1) <= SNAP_M) hw.X1 = vw.X1;
                        if (Math.Abs(hw.X2 - vw.X1) <= SNAP_M) hw.X2 = vw.X1;
                    }
                }
            }

            // Snap V endpoints along Z to meeting H walls
            foreach (var vw in centerlines)
            {
                if (Math.Abs(vw.X2 - vw.X1) > 1e-5) continue; // Only V walls
                foreach (var hw in centerlines)
                {
                    if (Math.Abs(hw.Z2 - hw.Z1) > 1e-5) continue; // Only H walls
                    double minHx = Math.Min(hw.X1, hw.X2);
                    double maxHx = Math.Max(hw.X1, hw.X2);
                    if (vw.X1 >= minHx - SNAP_M && vw.X1 <= maxHx + SNAP_M)
                    {
                        if (Math.Abs(vw.Z1 - hw.Z1) <= SNAP_M) vw.Z1 = hw.Z1;
                        if (Math.Abs(vw.Z2 - hw.Z1) <= SNAP_M) vw.Z2 = hw.Z1;
                    }
                }
            }

            // 5. Extract Openings (Doors & Windows) and place them into walls
            ExtractOpeningsIntoWalls(result, centerlines, cx, cy, s);

            result.Walls = centerlines;

            // 6. Create Detected Columns
            var columns = new List<DetectedColumn>();
            int colCounter = 1;
            foreach (var rc in rawColumns)
            {
                columns.Add(new DetectedColumn
                {
                    Id = $"col_{colCounter++}",
                    X = (rc.cx - cx) * s,
                    Z = -(rc.cy - cy) * s,
                    Width = rc.w > 0.05 ? rc.w : 0.30,
                    Depth = rc.d > 0.05 ? rc.d : 0.30,
                    Height = wallHeight,
                    Round = false
                });
            }
            result.Columns = columns;
        }

        private static double Dist(double x1, double z1, double x2, double z2) =>
            Math.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));

        private static void ExtractOpeningsIntoWalls(CadPdfResult result, List<DetectedWall> walls, double cx, double cy, double s)
        {
            var doorLayer = result.Layers.FirstOrDefault(l => l.Tag == result.RecommendedDoorLayer);
            var winLayer = result.Layers.FirstOrDefault(l => l.Tag == result.RecommendedWindowLayer);
            int opCounter = 1;

            // Extract Doors: match door text items or texts to walls
            if (doorLayer != null)
            {
                var doorSpecs = new List<(string label, double w, double h, double? x, double? y)>();
                for (int i = 0; i < doorLayer.Texts.Count; i++)
                {
                    string t = doorLayer.Texts[i];
                    if (t.StartsWith("D") && i + 2 < doorLayer.Texts.Count)
                    {
                        if (double.TryParse(doorLayer.Texts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double dw) &&
                            double.TryParse(doorLayer.Texts[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double dh))
                        {
                            doorSpecs.Add((t, dw / 100.0, dh / 100.0, null, null));
                            i += 2;
                        }
                    }
                }

                // Associate door specs with text coordinates if available
                for (int i = 0; i < doorSpecs.Count; i++)
                {
                    var item = doorLayer.TextItems.FirstOrDefault(ti => ti.Text == doorSpecs[i].label);
                    if (item != null)
                    {
                        doorSpecs[i] = (doorSpecs[i].label, doorSpecs[i].w, doorSpecs[i].h, item.X, item.Y);
                    }
                }

                int specIdx = 0;
                foreach (var spec in doorSpecs)
                {
                    double wM = spec.w > 0.6 ? spec.w : 0.90;
                    double hM = spec.h > 1.5 ? spec.h : 2.05;

                    DetectedWall? targetWall = null;
                    double bestDistAlongWall = 0;

                    if (spec.x.HasValue && spec.y.HasValue)
                    {
                        double dxWorld = (spec.x.Value - cx) * s;
                        double dzWorld = -(spec.y.Value - cy) * s;

                        double minPerpDist = double.MaxValue;
                        foreach (var w in walls)
                        {
                            double wLen = Dist(w.X1, w.Z1, w.X2, w.Z2);
                            if (wLen < wM + 0.2) continue;
                            double dirX = (w.X2 - w.X1) / wLen;
                            double dirZ = (w.Z2 - w.Z1) / wLen;
                            double proj = (dxWorld - w.X1) * dirX + (dzWorld - w.Z1) * dirZ;
                            if (proj >= 0.1 && proj <= wLen - 0.1)
                            {
                                double perpDist = Math.Abs((dxWorld - w.X1) * dirZ - (dzWorld - w.Z1) * dirX);
                                if (perpDist < minPerpDist && perpDist < 1.2)
                                {
                                    minPerpDist = perpDist;
                                    targetWall = w;
                                    bestDistAlongWall = Math.Max(0.1, Math.Min(wLen - wM - 0.1, proj - wM * 0.5));
                                }
                            }
                        }
                    }

                    // Fallback if no coordinates matched
                    if (targetWall == null)
                    {
                        targetWall = walls.Where(w => Dist(w.X1, w.Z1, w.X2, w.Z2) >= 1.6 && w.Openings.Count(o => o.Type == "DOOR") == 0)
                                          .Skip(specIdx % Math.Max(1, walls.Count))
                                          .FirstOrDefault();
                        if (targetWall != null)
                        {
                            double wLen = Dist(targetWall.X1, targetWall.Z1, targetWall.X2, targetWall.Z2);
                            bestDistAlongWall = Math.Max(0.2, (wLen - wM) * 0.35);
                        }
                    }

                    if (targetWall != null)
                    {
                        bool isGarage = spec.label.Contains("GARAZ") || spec.label.Contains("BRAMA") || wM >= 2.20;
                        targetWall.Openings.Add(new DetectedOpening
                        {
                            Id = $"op_door_{opCounter++}",
                            Type = isGarage ? "GARAGE_DOOR" : "DOOR",
                            Dist = Math.Round(bestDistAlongWall, 2),
                            Width = Math.Round(wM, 2),
                            Height = isGarage ? 2.15 : Math.Round(hM, 2),
                            Sill = 0.0
                        });
                        specIdx++;
                    }
                }
            }

            // Extract Windows from window layer segments
            if (winLayer != null && winLayer.Segments.Count > 0)
            {
                var winCandidates = new List<(double x, double y, double len)>();
                foreach (var seg in winLayer.Segments)
                {
                    double len = seg.Length * s;
                    if (len >= 0.75 && len <= 5.5)
                    {
                        winCandidates.Add(((seg.X1 + seg.X2) * 0.5, (seg.Y1 + seg.Y2) * 0.5, len));
                    }
                }

                var winClusters = new List<(double x, double z, double width)>();
                foreach (var c in winCandidates)
                {
                    double wx = (c.x - cx) * s;
                    double wz = -(c.y - cy) * s;
                    bool merged = false;
                    for (int k = 0; k < winClusters.Count; k++)
                    {
                        if (Dist(winClusters[k].x, winClusters[k].z, wx, wz) < 0.6)
                        {
                            winClusters[k] = ((winClusters[k].x + wx) * 0.5, (winClusters[k].z + wz) * 0.5, Math.Max(winClusters[k].width, c.len));
                            merged = true;
                            break;
                        }
                    }
                    if (!merged)
                    {
                        winClusters.Add((wx, wz, c.len));
                    }
                }

                foreach (var wc in winClusters)
                {
                    DetectedWall? bestWall = null;
                    double minPerp = double.MaxValue;
                    double bestDist = 0;

                    foreach (var w in walls)
                    {
                        double wLen = Dist(w.X1, w.Z1, w.X2, w.Z2);
                        if (wLen < wc.width + 0.3) continue;
                        double dirX = (w.X2 - w.X1) / wLen;
                        double dirZ = (w.Z2 - w.Z1) / wLen;
                        double proj = (wc.x - w.X1) * dirX + (wc.z - w.Z1) * dirZ;
                        if (proj >= 0.2 && proj <= wLen - 0.2)
                        {
                            double perp = Math.Abs((wc.x - w.X1) * dirZ - (wc.z - w.Z1) * dirX);
                            if (perp < minPerp && perp < 0.6)
                            {
                                minPerp = perp;
                                bestWall = w;
                                bestDist = Math.Max(0.15, Math.Min(wLen - wc.width - 0.15, proj - wc.width * 0.5));
                            }
                        }
                    }

                    if (bestWall != null && !bestWall.Openings.Any(o => Math.Abs(o.Dist - bestDist) < 0.8))
                    {
                        bool isGarageDoor = wc.width >= 2.20;
                        bestWall.Openings.Add(new DetectedOpening
                        {
                            Id = $"op_win_{opCounter++}",
                            Type = isGarageDoor ? "GARAGE_DOOR" : "WINDOW",
                            Dist = Math.Round(bestDist, 2),
                            Width = Math.Round(wc.width, 2),
                            Height = isGarageDoor ? 2.15 : 1.40,
                            Sill = isGarageDoor ? 0.0 : 0.85
                        });
                    }
                }
            }
        }

        private static string GenerateSvg(CadPdfResult result)
        {
            var sb = new StringBuilder();
            double w = Math.Max(100, result.BoundsMaxX - result.BoundsMinX);
            double h = Math.Max(100, result.BoundsMaxY - result.BoundsMinY);
            double pad = Math.Max(w, h) * 0.05;

            double vx = result.BoundsMinX - pad;
            double vy = result.BoundsMinY - pad;
            double vw = w + pad * 2;
            double vh = h + pad * 2;
            double maxYPad = result.BoundsMaxY + pad;

            sb.AppendFormat(CultureInfo.InvariantCulture,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {0:F1} {1:F1}\" width=\"{0:F1}\" height=\"{1:F1}\">\n",
                vw, vh);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "  <rect x=\"0\" y=\"0\" width=\"{0:F1}\" height=\"{1:F1}\" fill=\"#ffffff\" />\n",
                vw, vh);

            // Draw layers
            foreach (var l in result.Layers)
            {
                string color = "#cccccc";
                double strokeW = 1.0;

                if (l.Tag == result.RecommendedWallLayer) { color = "#000000"; strokeW = 3.5; }
                else if (l.Tag == result.RecommendedWindowLayer) { color = "#1971c2"; strokeW = 1.8; }
                else if (l.Tag == result.RecommendedDoorLayer) { color = "#d9480f"; strokeW = 1.8; }
                else if (l.Name.Contains("wymiar") || l.Name.Contains("dim")) { color = "#868e96"; strokeW = 0.8; }

                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "  <g id=\"{0}\" stroke=\"{1}\" stroke-width=\"{2:F1}\" fill=\"none\">\n",
                    l.Tag, color, strokeW);

                foreach (var seg in l.Segments)
                {
                    double sx1 = seg.X1 - vx;
                    double sy1 = maxYPad - seg.Y1;
                    double sx2 = seg.X2 - vx;
                    double sy2 = maxYPad - seg.Y2;

                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "    <line x1=\"{0:F1}\" y1=\"{1:F1}\" x2=\"{2:F1}\" y2=\"{3:F1}\" />\n",
                        sx1, sy1, sx2, sy2);
                }
                sb.Append("  </g>\n");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        private static void ParsePathSegments(string content, CadLayerInfo layer)
        {
            var tokens = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            double curX = 0, curY = 0;
            double startX = 0, startY = 0;
            bool hasCur = false;

            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (tok == "m" && i >= 2)
                {
                    if (double.TryParse(tokens[i - 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                        double.TryParse(tokens[i - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                    {
                        curX = x; curY = y;
                        startX = x; startY = y;
                        hasCur = true;
                        UpdateBounds(layer, x, y);
                    }
                }
                else if (tok == "l" && i >= 2 && hasCur)
                {
                    if (double.TryParse(tokens[i - 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                        double.TryParse(tokens[i - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                    {
                        layer.Segments.Add(new CadSegment { X1 = curX, Y1 = curY, X2 = x, Y2 = y });
                        layer.SegmentCount++;
                        UpdateBounds(layer, x, y);
                        curX = x; curY = y;
                    }
                }
                else if (tok == "h" && hasCur)
                {
                    if (Math.Abs(curX - startX) > 1e-4 || Math.Abs(curY - startY) > 1e-4)
                    {
                        layer.Segments.Add(new CadSegment { X1 = curX, Y1 = curY, X2 = startX, Y2 = startY });
                        layer.SegmentCount++;
                        curX = startX; curY = startY;
                    }
                }
                else if (tok == "re" && i >= 4)
                {
                    if (double.TryParse(tokens[i - 4], NumberStyles.Float, CultureInfo.InvariantCulture, out double rx) &&
                        double.TryParse(tokens[i - 3], NumberStyles.Float, CultureInfo.InvariantCulture, out double ry) &&
                        double.TryParse(tokens[i - 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double rw) &&
                        double.TryParse(tokens[i - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double rh))
                    {
                        layer.Segments.Add(new CadSegment { X1 = rx, Y1 = ry, X2 = rx + rw, Y2 = ry });
                        layer.Segments.Add(new CadSegment { X1 = rx + rw, Y1 = ry, X2 = rx + rw, Y2 = ry + rh });
                        layer.Segments.Add(new CadSegment { X1 = rx + rw, Y1 = ry + rh, X2 = rx, Y2 = ry + rh });
                        layer.Segments.Add(new CadSegment { X1 = rx, Y1 = ry + rh, X2 = rx, Y2 = ry });
                        layer.SegmentCount += 4;
                        UpdateBounds(layer, rx, ry);
                        UpdateBounds(layer, rx + rw, ry + rh);
                    }
                }
            }
        }

        private static void ParseTextStrings(string content, CadLayerInfo layer)
        {
            var tokens = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            double curTx = 0, curTy = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (tok == "Td" && i >= 2)
                {
                    if (double.TryParse(tokens[i - 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double tx) &&
                        double.TryParse(tokens[i - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ty))
                    {
                        curTx = tx; curTy = ty;
                    }
                }
                else if (tok == "Tm" && i >= 6)
                {
                    if (double.TryParse(tokens[i - 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double tx) &&
                        double.TryParse(tokens[i - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ty))
                    {
                        curTx = tx; curTy = ty;
                    }
                }
                else if (tok == "Tj" && i >= 1)
                {
                    string prev = tokens[i - 1];
                    if (prev.StartsWith("(") && prev.EndsWith(")"))
                    {
                        string str = UnescapePdfString(prev.Substring(1, prev.Length - 2)).Trim();
                        if (!string.IsNullOrEmpty(str))
                        {
                            layer.Texts.Add(str);
                            layer.TextItems.Add(new CadTextItem { Text = str, X = curTx, Y = curTy });
                        }
                    }
                }
            }
        }

        private static void UpdateBounds(CadLayerInfo layer, double x, double y)
        {
            layer.PointCount++;
            layer.MinX = Math.Min(layer.MinX, x);
            layer.MaxX = Math.Max(layer.MaxX, x);
            layer.MinY = Math.Min(layer.MinY, y);
            layer.MaxY = Math.Max(layer.MaxY, y);
        }

        private static string UnescapePdfString(string s)
        {
            return s.Replace(@"\ ", " ")
                    .Replace(@"\(", "(")
                    .Replace(@"\)", ")")
                    .Replace(@"\\", @"\")
                    .Replace(@"\/", "/");
        }

        private static byte[]? DecompressZlib(byte[] data)
        {
            try
            {
                using var inMs = new MemoryStream(data);
                using var zlib = new ZLibStream(inMs, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                zlib.CopyTo(outMs);
                return outMs.ToArray();
            }
            catch
            {
                // Fallback: try raw DeflateStream if zlib header is absent
                try
                {
                    using var inMs = new MemoryStream(data);
                    using var def = new DeflateStream(inMs, CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    def.CopyTo(outMs);
                    return outMs.ToArray();
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
