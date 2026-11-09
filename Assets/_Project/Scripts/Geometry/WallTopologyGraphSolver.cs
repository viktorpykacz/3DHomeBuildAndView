using System;
using System.Collections.Generic;
using HomeBuilder.Data;
using UnityEngine;

namespace HomeBuilder.Geometry
{
    /// <summary>
    /// Solves 2D wall graph topology at shared nodes (L-junctions, T-junctions, X-junctions)
    /// by sorting outgoing wall half-edges radially using atan2(dy, dx).
    /// Each side of a wall (Left/Right) intersects only with its immediate angular neighbor around the node.
    /// </summary>
    public static class WallTopologyGraphSolver
    {
        private const float NodeSnapTolerance = 0.01f; // 1 cm tolerance for shared node clustering
        private const float MiterLimitMultiplier = 2.5f;

        private struct DirectedHalfEdge
        {
            public WallData Wall;
            public bool IsStartEndpoint;
            public Vector2 NodePosition;
            public Vector2 OutgoingDir;
            public Vector2 LeftNormal; // (-dir.y, dir.x) relative to OutgoingDir
            public float AngleRad;
        }

        public static Dictionary<Guid, WallCornerMiter> SolveFloorWallMiters(IReadOnlyList<WallData> walls)
        {
            var miters = new Dictionary<Guid, WallCornerMiter>(walls.Count);

            // 1. Initialize default unjoined perpendicular corners for every wall
            foreach (WallData w in walls)
            {
                Vector2 a = w.StartPoint.ToVector2();
                Vector2 b = w.EndPoint.ToVector2();
                Vector2 dir = (b - a).normalized;
                Vector2 n = new(-dir.y, dir.x);
                float halfT = w.Thickness * 0.5f;

                miters[w.WallId] = new WallCornerMiter
                {
                    StartLeft = a + n * halfT,
                    StartRight = a - n * halfT,
                    EndLeft = b + n * halfT,
                    EndRight = b - n * halfT
                };
            }

            // 2. Cluster wall endpoints into shared topological nodes
            var nodeClusters = new List<(Vector2 center, List<DirectedHalfEdge> edges)>();

            foreach (WallData w in walls)
            {
                Vector2 a = w.StartPoint.ToVector2();
                Vector2 b = w.EndPoint.ToVector2();
                if (Vector2.Distance(a, b) < 1e-4f) continue;

                AddEndpointToCluster(nodeClusters, w, isStart: true, a, (b - a).normalized);
                AddEndpointToCluster(nodeClusters, w, isStart: false, b, (a - b).normalized);
            }

            // 3. For each node with >= 2 incident walls, sort radially by angle and intersect adjacent half-edges
            foreach (var (nodePos, edges) in nodeClusters)
            {
                if (edges.Count < 2) continue;

                edges.Sort((e1, e2) => e1.AngleRad.CompareTo(e2.AngleRad));

                int count = edges.Count;
                for (int i = 0; i < count; i++)
                {
                    DirectedHalfEdge current = edges[i];
                    DirectedHalfEdge ccwNeighbor = edges[(i + 1) % count]; // Neighbor on the LeftNormal side of OutgoingDir
                    DirectedHalfEdge cwNeighbor = edges[(i - 1 + count) % count]; // Neighbor on the RightNormal side of OutgoingDir

                    float halfT = current.Wall.Thickness * 0.5f;
                    float ccwHalfT = ccwNeighbor.Wall.Thickness * 0.5f;
                    float cwHalfT = cwNeighbor.Wall.Thickness * 0.5f;

                    Vector2 currLeftLineOrigin = nodePos + current.LeftNormal * halfT;
                    Vector2 ccwRightLineOrigin = nodePos - ccwNeighbor.LeftNormal * ccwHalfT;

                    Vector2 currRightLineOrigin = nodePos - current.LeftNormal * halfT;
                    Vector2 cwLeftLineOrigin = nodePos + cwNeighbor.LeftNormal * cwHalfT;

                    float maxMiterDistCcw = Mathf.Max(current.Wall.Thickness, ccwNeighbor.Wall.Thickness) * MiterLimitMultiplier;
                    float maxMiterDistCw = Mathf.Max(current.Wall.Thickness, cwNeighbor.Wall.Thickness) * MiterLimitMultiplier;

                    Vector2 resolvedLeftOfOutgoing = currLeftLineOrigin;
                    if (ProceduralWallMeshBuilder.TryIntersectLines2D(
                        currLeftLineOrigin, current.OutgoingDir,
                        ccwRightLineOrigin, ccwNeighbor.OutgoingDir,
                        out Vector2 ixLeft) &&
                        Vector2.Distance(nodePos, ixLeft) <= maxMiterDistCcw)
                    {
                        resolvedLeftOfOutgoing = ixLeft;
                    }

                    Vector2 resolvedRightOfOutgoing = currRightLineOrigin;
                    if (ProceduralWallMeshBuilder.TryIntersectLines2D(
                        currRightLineOrigin, current.OutgoingDir,
                        cwLeftLineOrigin, cwNeighbor.OutgoingDir,
                        out Vector2 ixRight) &&
                        Vector2.Distance(nodePos, ixRight) <= maxMiterDistCw)
                    {
                        resolvedRightOfOutgoing = ixRight;
                    }

                    WallCornerMiter m = miters[current.Wall.WallId];
                    if (current.IsStartEndpoint)
                    {
                        // OutgoingDir == (End - Start), so LeftOfOutgoing == StartLeft, RightOfOutgoing == StartRight
                        m.StartLeft = resolvedLeftOfOutgoing;
                        m.StartRight = resolvedRightOfOutgoing;
                    }
                    else
                    {
                        // OutgoingDir == (Start - End), which is opposite to wall's canonical direction (End - Start)!
                        // Therefore LeftOfOutgoing corresponds to EndRight, and RightOfOutgoing corresponds to EndLeft.
                        m.EndRight = resolvedLeftOfOutgoing;
                        m.EndLeft = resolvedRightOfOutgoing;
                    }
                    miters[current.Wall.WallId] = m;
                }
            }

            return miters;
        }

        private static void AddEndpointToCluster(
            List<(Vector2 center, List<DirectedHalfEdge> edges)> clusters,
            WallData wall,
            bool isStart,
            Vector2 endpoint,
            Vector2 outgoingDir)
        {
            Vector2 leftNormal = new(-outgoingDir.y, outgoingDir.x);
            float angle = Mathf.Atan2(outgoingDir.y, outgoingDir.x);

            var halfEdge = new DirectedHalfEdge
            {
                Wall = wall,
                IsStartEndpoint = isStart,
                NodePosition = endpoint,
                OutgoingDir = outgoingDir,
                LeftNormal = leftNormal,
                AngleRad = angle
            };

            for (int i = 0; i < clusters.Count; i++)
            {
                if (Vector2.Distance(clusters[i].center, endpoint) <= NodeSnapTolerance)
                {
                    clusters[i].edges.Add(halfEdge);
                    return;
                }
            }

            clusters.Add((endpoint, new List<DirectedHalfEdge> { halfEdge }));
        }
    }
}
