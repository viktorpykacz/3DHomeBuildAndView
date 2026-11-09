using System.Collections.Generic;
using HomeBuilder.Data;
using UnityEngine;

namespace HomeBuilder.Geometry
{
    /// <summary>
    /// Generates procedural 3D floor & ceiling slab meshes from 2D polygon contours
    /// using Ear-Clipping triangulation and World-Space UV mapping (1 UV unit = 1 meter).
    /// Also provides automatic closed-loop room polygon detection from floor walls.
    /// </summary>
    public static class ProceduralRoomFloorBuilder
    {
        private const float Epsilon = 1e-5f;

        /// <summary>
        /// Calculates the signed area of a 2D polygon (positive for CCW, negative for CW).
        /// Absolute value is the room floor area in square meters (m^2).
        /// </summary>
        public static float ComputeSignedArea2D(IReadOnlyList<Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return 0f;
            float sum = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Count];
                sum += (a.x * b.y - b.x * a.y);
            }
            return sum * 0.5f;
        }

        /// <summary>
        /// Generates a 3D floor slab mesh at Y = elevationY with thickness extending downward to (elevationY - slabThickness).
        /// Submesh 0: Top floor surface (parquet/tiles).
        /// Submesh 1: Bottom ceiling/underside surface and perimeter edges.
        /// </summary>
        public static Mesh GenerateFloorSlabMesh(
            IReadOnlyList<Vector2> polygon2D,
            float elevationY,
            float slabThickness = 0.25f)
        {
            if (polygon2D == null || polygon2D.Count < 3)
            {
                return new Mesh();
            }

            // Ensure Counter-Clockwise (CCW) vertex order in XZ plane
            var poly = new List<Vector2>(polygon2D);
            if (ComputeSignedArea2D(poly) < 0f)
            {
                poly.Reverse();
            }

            List<int> triIndices2D = TriangulateEarClipping(poly);
            int n = poly.Count;

            var vertices = new List<Vector3>(n * 6);
            var normals = new List<Vector3>(n * 6);
            var uvs = new List<Vector2>(n * 6);
            var topTris = new List<int>(triIndices2D.Count);
            var bottomAndSideTris = new List<int>(triIndices2D.Count + n * 6);

            // 1. Top Surface (Y = elevationY, Normal = Vector3.up)
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[i];
                vertices.Add(new Vector3(p.x, elevationY, p.y));
                normals.Add(Vector3.up);
                uvs.Add(p); // World-Space UV: 1m = 1 UV unit
            }

            // In Unity's Left-Handed coordinate system (X right, Z forward, Y up),
            // a CCW loop in (X, Z) needs clockwise winding when viewed from +Y so its normal points +Y.
            for (int i = 0; i < triIndices2D.Count; i += 3)
            {
                topTris.Add(triIndices2D[i]);
                topTris.Add(triIndices2D[i + 2]);
                topTris.Add(triIndices2D[i + 1]);
            }

            // 2. Bottom Surface (Y = elevationY - slabThickness, Normal = Vector3.down)
            float bottomY = elevationY - slabThickness;
            int bottomBaseIdx = vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[i];
                vertices.Add(new Vector3(p.x, bottomY, p.y));
                normals.Add(Vector3.down);
                uvs.Add(p);
            }

            for (int i = 0; i < triIndices2D.Count; i += 3)
            {
                bottomAndSideTris.Add(bottomBaseIdx + triIndices2D[i]);
                bottomAndSideTris.Add(bottomBaseIdx + triIndices2D[i + 1]);
                bottomAndSideTris.Add(bottomBaseIdx + triIndices2D[i + 2]);
            }

            // 3. Perimeter Side Edges
            float cumulativeU = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = poly[i];
                Vector2 p1 = poly[(i + 1) % n];
                float segLen = Vector2.Distance(p0, p1);
                Vector2 edgeDir = (p1 - p0).normalized;
                Vector3 outwardNormal = new(edgeDir.y, 0f, -edgeDir.x);

                int sideBase = vertices.Count;
                vertices.Add(new Vector3(p0.x, bottomY, p0.y));
                vertices.Add(new Vector3(p1.x, bottomY, p1.y));
                vertices.Add(new Vector3(p1.x, elevationY, p1.y));
                vertices.Add(new Vector3(p0.x, elevationY, p0.y));

                for (int k = 0; k < 4; k++) normals.Add(outwardNormal);

                uvs.Add(new Vector2(cumulativeU, 0f));
                uvs.Add(new Vector2(cumulativeU + segLen, 0f));
                uvs.Add(new Vector2(cumulativeU + segLen, slabThickness));
                uvs.Add(new Vector2(cumulativeU, slabThickness));

                bottomAndSideTris.Add(sideBase);
                bottomAndSideTris.Add(sideBase + 2);
                bottomAndSideTris.Add(sideBase + 1);

                bottomAndSideTris.Add(sideBase);
                bottomAndSideTris.Add(sideBase + 3);
                bottomAndSideTris.Add(sideBase + 2);

                cumulativeU += segLen;
            }

            var mesh = new Mesh
            {
                name = "ProceduralFloorSlab",
                subMeshCount = 2
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(topTris, 0);
            mesh.SetTriangles(bottomAndSideTris, 1);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Ear-clipping triangulation for arbitrary simple 2D polygons (concave or convex) in CCW order.
        /// </summary>
        public static List<int> TriangulateEarClipping(IReadOnlyList<Vector2> ccwPolygon)
        {
            var indices = new List<int>();
            int n = ccwPolygon.Count;
            if (n < 3) return indices;

            var vertexList = new List<int>(n);
            for (int i = 0; i < n; i++) vertexList.Add(i);

            int maxIterations = n * n * 2;
            int iterations = 0;
            int iCursor = 0;

            while (vertexList.Count > 3 && iterations < maxIterations)
            {
                iterations++;
                int count = vertexList.Count;
                int prevIdx = vertexList[(iCursor - 1 + count) % count];
                int currIdx = vertexList[iCursor % count];
                int nextIdx = vertexList[(iCursor + 1) % count];

                Vector2 a = ccwPolygon[prevIdx];
                Vector2 b = ccwPolygon[currIdx];
                Vector2 c = ccwPolygon[nextIdx];

                if (IsConvexCCW(a, b, c))
                {
                    bool containsAnyOtherVertex = false;
                    for (int j = 0; j < count; j++)
                    {
                        int testIdx = vertexList[j];
                        if (testIdx == prevIdx || testIdx == currIdx || testIdx == nextIdx) continue;
                        if (PointInTriangle2D(ccwPolygon[testIdx], a, b, c))
                        {
                            containsAnyOtherVertex = true;
                            break;
                        }
                    }

                    if (!containsAnyOtherVertex)
                    {
                        indices.Add(prevIdx);
                        indices.Add(currIdx);
                        indices.Add(nextIdx);
                        vertexList.RemoveAt(iCursor % count);
                        iCursor = 0;
                        continue;
                    }
                }

                iCursor = (iCursor + 1) % count;
            }

            if (vertexList.Count == 3)
            {
                indices.Add(vertexList[0]);
                indices.Add(vertexList[1]);
                indices.Add(vertexList[2]);
            }

            return indices;
        }

        /// <summary>
        /// Extracts closed minimal cycles (rooms) from a planar graph of walls using minimal-angle face traversal.
        /// </summary>
        public static List<List<Vector2>> DetectClosedRoomPolygonsFromWalls(IReadOnlyList<WallData> walls)
        {
            var rooms = new List<List<Vector2>>();
            if (walls == null || walls.Count < 3) return rooms;

            const float snapTol = 0.02f;
            var nodes = new List<Vector2>();
            var adjacency = new List<List<int>>();

            int GetOrAddNode(Vector2 pt)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (Vector2.Distance(nodes[i], pt) <= snapTol) return i;
                }
                nodes.Add(pt);
                adjacency.Add(new List<int>());
                return nodes.Count - 1;
            }

            foreach (WallData w in walls)
            {
                Vector2 a = w.StartPoint.ToVector2();
                Vector2 b = w.EndPoint.ToVector2();
                if (Vector2.Distance(a, b) <= snapTol) continue;

                int u = GetOrAddNode(a);
                int v = GetOrAddNode(b);
                if (u != v)
                {
                    if (!adjacency[u].Contains(v)) adjacency[u].Add(v);
                    if (!adjacency[v].Contains(u)) adjacency[v].Add(u);
                }
            }

            // Sort outgoing neighbors at each node by angle
            for (int u = 0; u < nodes.Count; u++)
            {
                Vector2 origin = nodes[u];
                adjacency[u].Sort((a, b) =>
                {
                    Vector2 da = nodes[a] - origin;
                    Vector2 db = nodes[b] - origin;
                    return Mathf.Atan2(da.y, da.x).CompareTo(Mathf.Atan2(db.y, db.x));
                });
            }

            var visitedDirectedEdges = new HashSet<(int, int)>();

            for (int u = 0; u < nodes.Count; u++)
            {
                foreach (int v in adjacency[u])
                {
                    if (visitedDirectedEdges.Contains((u, v))) continue;

                    var cycleNodes = new List<int>();
                    int curr = u;
                    int next = v;
                    bool validCycle = false;

                    while (!visitedDirectedEdges.Contains((curr, next)))
                    {
                        visitedDirectedEdges.Add((curr, next));
                        cycleNodes.Add(curr);

                        List<int> neighbors = adjacency[next];
                        if (neighbors.Count < 2) break;

                        int incomingIndex = neighbors.IndexOf(curr);
                        if (incomingIndex < 0) break;

                        // Next edge in the planar face (clockwise previous in CCW-sorted adjacency list)
                        int nextOutgoingIdx = (incomingIndex - 1 + neighbors.Count) % neighbors.Count;
                        curr = next;
                        next = neighbors[nextOutgoingIdx];

                        if (curr == u && next == v)
                        {
                            validCycle = true;
                            break;
                        }
                    }

                    if (validCycle && cycleNodes.Count >= 3)
                    {
                        var poly = new List<Vector2>(cycleNodes.Count);
                        foreach (int idx in cycleNodes) poly.Add(nodes[idx]);

                        float signedArea = ComputeSignedArea2D(poly);
                        // Keep only interior CCW faces (positive area > 0.25 m^2) and ignore the unbounded outer CW face
                        if (signedArea > 0.25f)
                        {
                            rooms.Add(poly);
                        }
                    }
                }
            }

            return rooms;
        }

        private static bool IsConvexCCW(Vector2 a, Vector2 b, Vector2 c)
        {
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            return cross > Epsilon;
        }

        private static bool PointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign2D(p, a, b);
            float d2 = Sign2D(p, b, c);
            float d3 = Sign2D(p, c, a);

            bool hasNeg = (d1 < -Epsilon) || (d2 < -Epsilon) || (d3 < -Epsilon);
            bool hasPos = (d1 > Epsilon) || (d2 > Epsilon) || (d3 > Epsilon);
            return !(hasNeg && hasPos);
        }

        private static float Sign2D(Vector2 p1, Vector2 p2, Vector2 p3) =>
            (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
}
