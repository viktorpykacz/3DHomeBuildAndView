using System.Collections.Generic;
using HomeBuilder.Data;
using UnityEngine;

namespace HomeBuilder.Geometry
{
    public struct WallCornerMiter
    {
        public Vector2 StartLeft;
        public Vector2 StartRight;
        public Vector2 EndLeft;
        public Vector2 EndRight;
    }

    /// <summary>
    /// Generates procedural 3D wall meshes from 2D axial points, thickness, and height.
    /// Supports arbitrary-angle Miter Joins, Window/Door rectangular cutouts with inner reveals,
    /// Attic Roof Plane Slicing (Sutherland-Hodgman 3D), and World-Space UV mapping (1 UV unit = 1 meter).
    /// </summary>
    public static class ProceduralWallMeshBuilder
    {
        private const float MiterLimitMultiplier = 2.5f;
        private const float Epsilon = 1e-5f;

        public static WallCornerMiter ComputeMiterCorners(
            WallData wall,
            WallData prevConnectedAtStart,
            WallData nextConnectedAtEnd)
        {
            Vector2 a = wall.StartPoint.ToVector2();
            Vector2 b = wall.EndPoint.ToVector2();
            Vector2 dir = (b - a).normalized;
            Vector2 normal = new(-dir.y, dir.x);
            float halfT = wall.Thickness * 0.5f;

            var result = new WallCornerMiter
            {
                StartLeft = a + normal * halfT,
                StartRight = a - normal * halfT,
                EndLeft = b + normal * halfT,
                EndRight = b - normal * halfT
            };

            if (prevConnectedAtStart != null)
            {
                Vector2 otherDir = GetOutgoingDirectionFromNode(prevConnectedAtStart, a);
                Vector2 otherNormal = new(-otherDir.y, otherDir.x);
                float otherHalfT = prevConnectedAtStart.Thickness * 0.5f;

                if (TryIntersectLines2D(
                    result.StartLeft, dir,
                    a - otherNormal * otherHalfT, otherDir,
                    out Vector2 miterLeft) &&
                    Vector2.Distance(a, miterLeft) <= wall.Thickness * MiterLimitMultiplier)
                {
                    result.StartLeft = miterLeft;
                }

                if (TryIntersectLines2D(
                    result.StartRight, dir,
                    a + otherNormal * otherHalfT, otherDir,
                    out Vector2 miterRight) &&
                    Vector2.Distance(a, miterRight) <= wall.Thickness * MiterLimitMultiplier)
                {
                    result.StartRight = miterRight;
                }
            }

            if (nextConnectedAtEnd != null)
            {
                Vector2 otherDir = GetOutgoingDirectionFromNode(nextConnectedAtEnd, b);
                Vector2 otherNormal = new(-otherDir.y, otherDir.x);
                float otherHalfT = nextConnectedAtEnd.Thickness * 0.5f;

                if (TryIntersectLines2D(
                    result.EndLeft, -dir,
                    b + otherNormal * otherHalfT, otherDir,
                    out Vector2 miterEndLeft) &&
                    Vector2.Distance(b, miterEndLeft) <= wall.Thickness * MiterLimitMultiplier)
                {
                    result.EndLeft = miterEndLeft;
                }

                if (TryIntersectLines2D(
                    result.EndRight, -dir,
                    b - otherNormal * otherHalfT, otherDir,
                    out Vector2 miterEndRight) &&
                    Vector2.Distance(b, miterEndRight) <= wall.Thickness * MiterLimitMultiplier)
                {
                    result.EndRight = miterEndRight;
                }
            }

            return result;
        }

        public static List<Plane> BuildRoofClippingPlanes(IReadOnlyList<RoofPlaneData> roofPlanes, float floorElevationY)
        {
            var planes = new List<Plane>();
            if (roofPlanes == null) return planes;

            foreach (RoofPlaneData rp in roofPlanes)
            {
                Vector2 eaveA = rp.EaveStart.ToVector2();
                Vector2 eaveB = rp.EaveEnd.ToVector2();
                Vector2 eaveDir2D = (eaveB - eaveA).normalized;
                Vector2 inwardHorizontal2D = new(-eaveDir2D.y, eaveDir2D.x);

                Vector3 eaveOrigin3D = new(eaveA.x, floorElevationY + rp.EaveElevationY, eaveA.y);
                Vector3 eaveAxis3D = new(eaveDir2D.x, 0f, eaveDir2D.y);
                Vector3 inwardHorizontal3D = new(inwardHorizontal2D.x, 0f, inwardHorizontal2D.y);

                float pitchRad = rp.PitchAngleDegrees * Mathf.Deg2Rad;
                Vector3 slopeUpVector = (inwardHorizontal3D * Mathf.Cos(pitchRad) + Vector3.up * Mathf.Sin(pitchRad)).normalized;

                // Normal pointing downward into the usable attic volume
                Vector3 inwardNormal = Vector3.Cross(slopeUpVector, eaveAxis3D).normalized;
                if (inwardNormal.y > 0f) inwardNormal = -inwardNormal;

                planes.Add(new Plane(inwardNormal, eaveOrigin3D));
            }

            return planes;
        }

        public static Mesh GenerateWallMesh(
            WallData wall,
            WallCornerMiter miter,
            IReadOnlyList<Plane> roofClippingPlanes)
        {
            Vector2 a = wall.StartPoint.ToVector2();
            Vector2 b = wall.EndPoint.ToVector2();
            float length = Vector2.Distance(a, b);
            if (length < Epsilon) return new Mesh();

            var vertices = new List<Vector3>(128);
            var normals = new List<Vector3>(128);
            var uvs = new List<Vector2>(128);
            var frontTris = new List<int>(64);
            var backTris = new List<int>(64);
            var edgeTris = new List<int>(64);

            var sortedOpenings = new List<OpeningData>(wall.Openings);
            sortedOpenings.Sort((x, y) => x.DistanceFromWallStart.CompareTo(y.DistanceFromWallStart));

            IReadOnlyList<Plane> activePlanes = wall.ClipByRoofPlanes ? roofClippingPlanes : null;

            float cursorU = 0f;
            foreach (OpeningData op in sortedOpenings)
            {
                float opStart = Mathf.Clamp(op.DistanceFromWallStart - op.Width * 0.5f, 0f, length);
                float opEnd = Mathf.Clamp(op.DistanceFromWallStart + op.Width * 0.5f, 0f, length);

                if (opStart > cursorU + Epsilon)
                {
                    AddWallSpanSegment(
                        cursorU, opStart, 0f, wall.BaseHeight,
                        length, miter, activePlanes,
                        vertices, normals, uvs, frontTris, backTris, edgeTris,
                        closeLeft: cursorU <= Epsilon, closeRight: false, closeBottom: true, closeTop: true);
                }

                if (op.SillHeight > Epsilon)
                {
                    AddWallSpanSegment(
                        opStart, opEnd, 0f, op.SillHeight,
                        length, miter, activePlanes,
                        vertices, normals, uvs, frontTris, backTris, edgeTris,
                        closeLeft: false, closeRight: false, closeBottom: true, closeTop: true);
                }

                float lintelBottom = op.SillHeight + op.Height;
                if (lintelBottom < wall.BaseHeight - Epsilon)
                {
                    AddWallSpanSegment(
                        opStart, opEnd, lintelBottom, wall.BaseHeight,
                        length, miter, activePlanes,
                        vertices, normals, uvs, frontTris, backTris, edgeTris,
                        closeLeft: false, closeRight: false, closeBottom: true, closeTop: true);
                }

                AddOpeningSideReveals(
                    opStart, opEnd, op.SillHeight, Mathf.Min(lintelBottom, wall.BaseHeight),
                    length, miter, activePlanes,
                    vertices, normals, uvs, edgeTris);

                cursorU = opEnd;
            }

            if (cursorU < length - Epsilon)
            {
                AddWallSpanSegment(
                    cursorU, length, 0f, wall.BaseHeight,
                    length, miter, activePlanes,
                    vertices, normals, uvs, frontTris, backTris, edgeTris,
                    closeLeft: cursorU <= Epsilon, closeRight: true, closeBottom: true, closeTop: true);
            }

            var mesh = new Mesh
            {
                name = $"Wall_{wall.WallId:N}",
                subMeshCount = 3
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(frontTris, 0);
            mesh.SetTriangles(backTris, 1);
            mesh.SetTriangles(edgeTris, 2);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static List<Vector3> ClipPolygonByPlanes(List<Vector3> polygon, IReadOnlyList<Plane> planes)
        {
            if (planes == null || planes.Count == 0) return polygon;

            List<Vector3> currentPoly = polygon;
            foreach (Plane plane in planes)
            {
                if (currentPoly.Count < 3) break;
                var nextPoly = new List<Vector3>(currentPoly.Count + 2);

                for (int i = 0; i < currentPoly.Count; i++)
                {
                    Vector3 a = currentPoly[i];
                    Vector3 b = currentPoly[(i + 1) % currentPoly.Count];

                    float da = plane.GetDistanceToPoint(a);
                    float db = plane.GetDistanceToPoint(b);

                    bool aInside = da >= -Epsilon;
                    bool bInside = db >= -Epsilon;

                    if (aInside && bInside)
                    {
                        nextPoly.Add(b);
                    }
                    else if (aInside && !bInside)
                    {
                        float t = da / (da - db);
                        nextPoly.Add(Vector3.Lerp(a, b, t));
                    }
                    else if (!aInside && bInside)
                    {
                        float t = da / (da - db);
                        nextPoly.Add(Vector3.Lerp(a, b, t));
                        nextPoly.Add(b);
                    }
                }
                currentPoly = nextPoly;
            }
            return currentPoly;
        }

        private static void AddWallSpanSegment(
            float u0, float u1, float y0, float y1,
            float totalLength, WallCornerMiter miter, IReadOnlyList<Plane> roofPlanes,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs,
            List<int> frontTris, List<int> backTris, List<int> edgeTris,
            bool closeLeft, bool closeRight, bool closeBottom, bool closeTop)
        {
            float t0 = Mathf.Clamp01(u0 / totalLength);
            float t1 = Mathf.Clamp01(u1 / totalLength);

            Vector2 f0 = Vector2.Lerp(miter.StartLeft, miter.EndLeft, t0);
            Vector2 f1 = Vector2.Lerp(miter.StartLeft, miter.EndLeft, t1);
            Vector2 b0 = Vector2.Lerp(miter.StartRight, miter.EndRight, t0);
            Vector2 b1 = Vector2.Lerp(miter.StartRight, miter.EndRight, t1);

            var frontPoly = ClipPolygonByPlanes(new List<Vector3>
            {
                new(f0.x, y0, f0.y),
                new(f1.x, y0, f1.y),
                new(f1.x, y1, f1.y),
                new(f0.x, y1, f0.y)
            }, roofPlanes);
            AppendPlanarPolygon(frontPoly, u0, f0, f1, verts, norms, uvs, frontTris, invertWinding: false);

            var backPoly = ClipPolygonByPlanes(new List<Vector3>
            {
                new(b0.x, y0, b0.y),
                new(b1.x, y0, b1.y),
                new(b1.x, y1, b1.y),
                new(b0.x, y1, b0.y)
            }, roofPlanes);
            AppendPlanarPolygon(backPoly, u0, b0, b1, verts, norms, uvs, backTris, invertWinding: true);

            if (closeLeft)
            {
                var leftCapPoly = ClipPolygonByPlanes(new List<Vector3>
                {
                    new(b0.x, y0, b0.y), new(b0.x, y1, b0.y), new(f0.x, y1, f0.y), new(f0.x, y0, f0.y)
                }, roofPlanes);
                AppendWorldSpacePolygon(leftCapPoly, verts, norms, uvs, edgeTris);
            }

            if (closeRight)
            {
                var rightCapPoly = ClipPolygonByPlanes(new List<Vector3>
                {
                    new(f1.x, y0, f1.y), new(f1.x, y1, f1.y), new(b1.x, y1, b1.y), new(b1.x, y0, b1.y)
                }, roofPlanes);
                AppendWorldSpacePolygon(rightCapPoly, verts, norms, uvs, edgeTris);
            }

            if (closeBottom)
            {
                var bottomPoly = ClipPolygonByPlanes(new List<Vector3>
                {
                    new(f0.x, y0, f0.y), new(b0.x, y0, b0.y), new(b1.x, y0, b1.y), new(f1.x, y0, f1.y)
                }, roofPlanes);
                AppendWorldSpacePolygon(bottomPoly, verts, norms, uvs, edgeTris);
            }

            if (closeTop)
            {
                var topPoly = ClipPolygonByPlanes(new List<Vector3>
                {
                    new(f0.x, y1, f0.y), new(f1.x, y1, f1.y), new(b1.x, y1, b1.y), new(b0.x, y1, b0.y)
                }, roofPlanes);
                AppendWorldSpacePolygon(topPoly, verts, norms, uvs, edgeTris);
            }

            // Close any sloped cross-section cuts generated by roof planes so the attic wall mesh is 100% manifold
            if (roofPlanes != null && roofPlanes.Count > 0)
            {
                AddSlopedRoofCrossSectionCaps(frontPoly, backPoly, roofPlanes, verts, norms, uvs, edgeTris);
            }
        }

        private static void AddSlopedRoofCrossSectionCaps(
            List<Vector3> frontPoly,
            List<Vector3> backPoly,
            IReadOnlyList<Plane> roofPlanes,
            List<Vector3> verts,
            List<Vector3> norms,
            List<Vector2> uvs,
            List<int> edgeTris)
        {
            if (frontPoly.Count < 3 || backPoly.Count < 3) return;

            foreach (Plane plane in roofPlanes)
            {
                var frontCutPts = new List<Vector3>(2);
                var backCutPts = new List<Vector3>(2);

                for (int i = 0; i < frontPoly.Count; i++)
                {
                    if (Mathf.Abs(plane.GetDistanceToPoint(frontPoly[i])) <= 1e-3f)
                    {
                        frontCutPts.Add(frontPoly[i]);
                    }
                }

                for (int i = 0; i < backPoly.Count; i++)
                {
                    if (Mathf.Abs(plane.GetDistanceToPoint(backPoly[i])) <= 1e-3f)
                    {
                        backCutPts.Add(backPoly[i]);
                    }
                }

                if (frontCutPts.Count >= 2 && backCutPts.Count >= 2)
                {
                    Vector3 fA = frontCutPts[0];
                    Vector3 fB = frontCutPts[^1];
                    Vector3 bA = backCutPts[0];
                    Vector3 bB = backCutPts[^1];

                    // Match nearest endpoints across wall thickness
                    if ((fA - bA).sqrMagnitude > (fA - bB).sqrMagnitude)
                    {
                        (bA, bB) = (bB, bA);
                    }

                    var capQuad = new List<Vector3> { fA, fB, bB, bA };
                    Vector3 quadNormal = Vector3.Cross(capQuad[2] - capQuad[0], capQuad[1] - capQuad[0]);
                    // Roof plane normal points downward into attic, so cap normal should point upward (-plane.normal)
                    if (Vector3.Dot(quadNormal, -plane.normal) < 0f)
                    {
                        capQuad.Reverse();
                    }
                    AppendWorldSpacePolygon(capQuad, verts, norms, uvs, edgeTris);
                }
            }
        }

        private static void AddOpeningSideReveals(
            float u0, float u1, float y0, float y1,
            float totalLength, WallCornerMiter miter, IReadOnlyList<Plane> roofPlanes,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> edgeTris)
        {
            float t0 = Mathf.Clamp01(u0 / totalLength);
            float t1 = Mathf.Clamp01(u1 / totalLength);

            Vector2 f0 = Vector2.Lerp(miter.StartLeft, miter.EndLeft, t0);
            Vector2 b0 = Vector2.Lerp(miter.StartRight, miter.EndRight, t0);
            Vector2 f1 = Vector2.Lerp(miter.StartLeft, miter.EndLeft, t1);
            Vector2 b1 = Vector2.Lerp(miter.StartRight, miter.EndRight, t1);

            var leftReveal = ClipPolygonByPlanes(new List<Vector3>
            {
                new(f0.x, y0, f0.y), new(f0.x, y1, f0.y), new(b0.x, y1, b0.y), new(b0.x, y0, b0.y)
            }, roofPlanes);
            AppendWorldSpacePolygon(leftReveal, verts, norms, uvs, edgeTris);

            var rightReveal = ClipPolygonByPlanes(new List<Vector3>
            {
                new(b1.x, y0, b1.y), new(b1.x, y1, b1.y), new(f1.x, y1, f1.y), new(f1.x, y0, f1.y)
            }, roofPlanes);
            AppendWorldSpacePolygon(rightReveal, verts, norms, uvs, edgeTris);
        }

        private static void AppendPlanarPolygon(
            List<Vector3> poly, float uStart, Vector2 p0, Vector2 p1,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris, bool invertWinding)
        {
            if (poly.Count < 3) return;
            Vector3 edge1 = poly[1] - poly[0];
            Vector3 edge2 = poly[2] - poly[0];
            Vector3 normal = Vector3.Cross(edge1, edge2).normalized;
            if (invertWinding) normal = -normal;

            Vector3 tangentU = new Vector3(p1.x - p0.x, 0f, p1.y - p0.y).normalized;
            Vector3 origin = new(p0.x, 0f, p0.y);

            int baseIdx = verts.Count;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 v = poly[i];
                verts.Add(v);
                norms.Add(normal);
                float u = uStart + Vector3.Dot(v - origin, tangentU);
                uvs.Add(new Vector2(u, v.y));
            }

            for (int i = 1; i < poly.Count - 1; i++)
            {
                if (!invertWinding)
                {
                    tris.Add(baseIdx);
                    tris.Add(baseIdx + i + 1);
                    tris.Add(baseIdx + i);
                }
                else
                {
                    tris.Add(baseIdx);
                    tris.Add(baseIdx + i);
                    tris.Add(baseIdx + i + 1);
                }
            }
        }

        private static void AppendWorldSpacePolygon(
            List<Vector3> poly,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
        {
            if (poly.Count < 3) return;
            Vector3 normal = Vector3.Cross(poly[2] - poly[0], poly[1] - poly[0]).normalized;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < Epsilon) tangent = Vector3.right;
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            int baseIdx = verts.Count;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 v = poly[i];
                verts.Add(v);
                norms.Add(normal);
                uvs.Add(new Vector2(Vector3.Dot(v, tangent), Vector3.Dot(v, bitangent)));
            }

            for (int i = 1; i < poly.Count - 1; i++)
            {
                tris.Add(baseIdx);
                tris.Add(baseIdx + i);
                tris.Add(baseIdx + i + 1);
            }
        }

        private static Vector2 GetOutgoingDirectionFromNode(WallData wall, Vector2 sharedNode)
        {
            Vector2 s = wall.StartPoint.ToVector2();
            Vector2 e = wall.EndPoint.ToVector2();
            return (Vector2.SqrMagnitude(s - sharedNode) < Vector2.SqrMagnitude(e - sharedNode))
                ? (e - s).normalized
                : (s - e).normalized;
        }

        public static bool TryIntersectLines2D(
            Vector2 p1, Vector2 d1,
            Vector2 p2, Vector2 d2,
            out Vector2 intersection)
        {
            float cross = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(cross) < Epsilon)
            {
                intersection = p1;
                return false;
            }
            Vector2 diff = p2 - p1;
            float t = (diff.x * d2.y - diff.y * d2.x) / cross;
            intersection = p1 + d1 * t;
            return true;
        }
    }
}
