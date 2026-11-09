using System.Collections.Generic;
using HomeBuilder.Data;
using UnityEngine;

namespace HomeBuilder.Geometry
{
    /// <summary>
    /// Generates RoofPlaneData configurations (e.g., Gable Roof over a building footprint)
    /// and constructs 3D sloped roof slab meshes for visualization and walkthrough mode.
    /// </summary>
    public static class ProceduralRoofMeshBuilder
    {
        /// <summary>
        /// Automatically computes two opposing RoofPlaneData entries (Gable Roof / Dach Dwuspadowy)
        /// spanning the bounding box of the floor's walls along the X or Z ridge axis.
        /// </summary>
        public static List<RoofPlaneData> CreateGableRoofForWalls(
            IReadOnlyList<WallData> walls,
            float kneeWallHeight = 1.10f,
            float pitchAngleDegrees = 38.0f,
            bool ridgeAlongXAxis = true)
        {
            var result = new List<RoofPlaneData>(2);
            if (walls == null || walls.Count == 0)
            {
                // Fallback 6m x 4m footprint
                result.Add(new RoofPlaneData
                {
                    EaveStart = new Vector2(-3f, -2f),
                    EaveEnd = new Vector2(3f, -2f),
                    EaveElevationY = kneeWallHeight,
                    PitchAngleDegrees = pitchAngleDegrees
                });
                result.Add(new RoofPlaneData
                {
                    EaveStart = new Vector2(3f, 2f),
                    EaveEnd = new Vector2(-3f, 2f),
                    EaveElevationY = kneeWallHeight,
                    PitchAngleDegrees = pitchAngleDegrees
                });
                return result;
            }

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (WallData w in walls)
            {
                Vector2 a = w.StartPoint.ToVector2();
                Vector2 b = w.EndPoint.ToVector2();
                minX = Mathf.Min(minX, a.x, b.x);
                maxX = Mathf.Max(maxX, a.x, b.x);
                minZ = Mathf.Min(minZ, a.y, b.y);
                maxZ = Mathf.Max(maxZ, a.y, b.y);
            }

            if (ridgeAlongXAxis)
            {
                // Eaves run along South (minZ: West->East) and North (maxZ: East->West)
                // Left-hand normal (-dir.y, dir.x) points inward into the building for both!
                result.Add(new RoofPlaneData
                {
                    EaveStart = new Vector2(minX, minZ),
                    EaveEnd = new Vector2(maxX, minZ),
                    EaveElevationY = kneeWallHeight,
                    PitchAngleDegrees = pitchAngleDegrees,
                    Thickness = 0.28f
                });

                result.Add(new RoofPlaneData
                {
                    EaveStart = new Vector2(maxX, maxZ),
                    EaveEnd = new Vector2(minX, maxZ),
                    EaveElevationY = kneeWallHeight,
                    PitchAngleDegrees = pitchAngleDegrees,
                    Thickness = 0.28f
                });
            }
            else
            {
                // Eaves run along East (maxX: South->North) and West (minX: North->South)
                result.Add(new RoofPlaneData
                {
                    EaveStart = new Vector2(maxX, minZ),
                    EaveEnd = new Vector2(maxX, maxZ),
                    EaveElevationY = kneeWallHeight,
                    PitchAngleDegrees = pitchAngleDegrees,
                    Thickness = 0.28f
                });

                result.Add(new RoofPlaneData
                {
                    EaveStart = new Vector2(minX, maxZ),
                    EaveEnd = new Vector2(minX, minZ),
                    EaveElevationY = kneeWallHeight,
                    PitchAngleDegrees = pitchAngleDegrees,
                    Thickness = 0.28f
                });
            }

            return result;
        }

        /// <summary>
        /// Generates a 3D sloped slab mesh for a single RoofPlaneData up to horizontalRunMeters (e.g., half-span to the ridge).
        /// </summary>
        public static Mesh GenerateRoofSlabMesh(RoofPlaneData roofPlane, float horizontalRunMeters)
        {
            Vector2 a = roofPlane.EaveStart.ToVector2();
            Vector2 b = roofPlane.EaveEnd.ToVector2();
            float eaveLen = Vector2.Distance(a, b);
            if (eaveLen < 1e-4f || horizontalRunMeters <= 0.05f) return new Mesh();

            Vector2 eaveDir2D = (b - a).normalized;
            Vector2 inward2D = new(-eaveDir2D.y, eaveDir2D.x);

            float pitchRad = roofPlane.PitchAngleDegrees * Mathf.Deg2Rad;
            float riseY = horizontalRunMeters * Mathf.Tan(pitchRad);

            Vector3 eaveA = new(a.x, roofPlane.EaveElevationY, a.y);
            Vector3 eaveB = new(b.x, roofPlane.EaveElevationY, b.y);
            Vector3 ridgeA = new(a.x + inward2D.x * horizontalRunMeters, roofPlane.EaveElevationY + riseY, a.y + inward2D.y * horizontalRunMeters);
            Vector3 ridgeB = new(b.x + inward2D.x * horizontalRunMeters, roofPlane.EaveElevationY + riseY, b.y + inward2D.y * horizontalRunMeters);

            Vector3 eaveAxis = (eaveB - eaveA).normalized;
            Vector3 slopeAxis = (ridgeA - eaveA).normalized;
            Vector3 upwardNormal = Vector3.Cross(eaveAxis, slopeAxis).normalized;
            if (upwardNormal.y < 0f) upwardNormal = -upwardNormal;

            Vector3 thicknessOffset = upwardNormal * roofPlane.Thickness;

            Vector3 topEaveA = eaveA + thicknessOffset;
            Vector3 topEaveB = eaveB + thicknessOffset;
            Vector3 topRidgeA = ridgeA + thicknessOffset;
            Vector3 topRidgeB = ridgeB + thicknessOffset;

            float slopeLength = Vector3.Distance(eaveA, ridgeA);

            var verts = new List<Vector3>(16);
            var norms = new List<Vector3>(16);
            var uvs = new List<Vector2>(16);
            var tris = new List<int>(24);

            // Bottom interior ceiling face
            int baseBot = verts.Count;
            verts.Add(eaveA);
            verts.Add(eaveB);
            verts.Add(ridgeB);
            verts.Add(ridgeA);
            for (int i = 0; i < 4; i++) norms.Add(-upwardNormal);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(eaveLen, 0f));
            uvs.Add(new Vector2(eaveLen, slopeLength));
            uvs.Add(new Vector2(0f, slopeLength));
            tris.Add(baseBot); tris.Add(baseBot + 1); tris.Add(baseBot + 2);
            tris.Add(baseBot); tris.Add(baseBot + 2); tris.Add(baseBot + 3);

            // Top exterior roof face
            int baseTop = verts.Count;
            verts.Add(topEaveA);
            verts.Add(topRidgeA);
            verts.Add(topRidgeB);
            verts.Add(topEaveB);
            for (int i = 0; i < 4; i++) norms.Add(upwardNormal);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, slopeLength));
            uvs.Add(new Vector2(eaveLen, slopeLength));
            uvs.Add(new Vector2(eaveLen, 0f));
            tris.Add(baseTop); tris.Add(baseTop + 1); tris.Add(baseTop + 2);
            tris.Add(baseTop); tris.Add(baseTop + 2); tris.Add(baseTop + 3);

            var mesh = new Mesh { name = $"RoofPlane_{roofPlane.RoofPlaneId:N}" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
