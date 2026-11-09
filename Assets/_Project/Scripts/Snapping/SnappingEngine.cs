using System;
using System.Collections.Generic;
using UnityEngine;

namespace HomeBuilder.Snapping
{
    public struct CabinetSnapResult
    {
        public Vector3 WorldPosition;
        public Quaternion WorldRotation;
        public Guid? SnappedWallId;
        public bool IsEdgeSnapped;
    }

    public struct MEPSnapResult
    {
        public bool IsValidHit;
        public Vector3 WorldPosition;
        public Vector3 SurfaceNormal;
        public Quaternion WorldRotation;
        public Guid HostWallId;
        public float DistanceToNearestCorner;
        public Vector3 NearestCornerWorldPoint;
        public float ElevationFromFloor;
        public Vector3 FloorProjectionPoint;
        public bool HasSafetyCollision;
        public string CollisionWarningMessage;
    }

    /// <summary>
    /// Provides precision CAD/BIM snapping for:
    /// 1. Kitchen Cabinets & Furniture (Back-to-Wall + Edge-to-Edge Snapping).
    /// 2. MEP Installations (Wall Normal alignment, Y-axis elevation lock, live corner/floor dimensions, and collision warnings).
    /// </summary>
    public sealed class SnappingEngine
    {
        private const float EdgeSnapThresholdMeters = 0.12f;

        public CabinetSnapResult ComputeCabinetSnap(
            Ray cursorRay,
            Bounds normalizedCabinetBounds,
            float floorElevationY,
            float mountElevationAboveFloor,
            LayerMask wallLayerMask,
            IReadOnlyList<(Transform transform, Bounds localBounds, Guid? wallId)> placedCabinets)
        {
            float cabinetWidth = normalizedCabinetBounds.size.x;
            float targetY = floorElevationY + mountElevationAboveFloor;

            if (Physics.Raycast(cursorRay, out RaycastHit wallHit, 100f, wallLayerMask))
            {
                Vector3 wallNormal = new Vector3(wallHit.normal.x, 0f, wallHit.normal.z).normalized;
                Vector3 wallTangent = Vector3.Cross(Vector3.up, wallNormal).normalized;
                Quaternion targetRot = Quaternion.LookRotation(wallNormal, Vector3.up);

                Vector3 candidatePos = new(wallHit.point.x, targetY, wallHit.point.z);
                Guid? hitWallId = wallHit.collider.TryGetComponent(out WallSurfaceIdentifier wallTag)
                    ? wallTag.WallId
                    : null;

                bool edgeSnapped = false;
                float bestDelta = EdgeSnapThresholdMeters;

                if (placedCabinets != null)
                {
                    foreach (var neighbor in placedCabinets)
                    {
                        if (Vector3.Dot(neighbor.transform.forward, wallNormal) < 0.98f) continue;

                        Vector3 toCandidate = candidatePos - neighbor.transform.position;
                        float alongTangent = Vector3.Dot(toCandidate, wallTangent);
                        float neighborHalfWidth = neighbor.localBounds.size.x * 0.5f;
                        float currentHalfWidth = cabinetWidth * 0.5f;

                        float snapRightOffset = neighborHalfWidth + currentHalfWidth;
                        float diffRight = Mathf.Abs(alongTangent - snapRightOffset);
                        if (diffRight < bestDelta)
                        {
                            bestDelta = diffRight;
                            candidatePos = neighbor.transform.position + wallTangent * snapRightOffset;
                            candidatePos.y = targetY;
                            edgeSnapped = true;
                        }

                        float snapLeftOffset = -(neighborHalfWidth + currentHalfWidth);
                        float diffLeft = Mathf.Abs(alongTangent - snapLeftOffset);
                        if (diffLeft < bestDelta)
                        {
                            bestDelta = diffLeft;
                            candidatePos = neighbor.transform.position + wallTangent * snapLeftOffset;
                            candidatePos.y = targetY;
                            edgeSnapped = true;
                        }
                    }
                }

                return new CabinetSnapResult
                {
                    WorldPosition = candidatePos,
                    WorldRotation = targetRot,
                    SnappedWallId = hitWallId,
                    IsEdgeSnapped = edgeSnapped
                };
            }

            var floorPlane = new Plane(Vector3.up, new Vector3(0f, targetY, 0f));
            Vector3 fallbackPos = floorPlane.Raycast(cursorRay, out float enter)
                ? cursorRay.GetPoint(enter)
                : Vector3.zero;

            return new CabinetSnapResult
            {
                WorldPosition = fallbackPos,
                WorldRotation = Quaternion.identity,
                SnappedWallId = null,
                IsEdgeSnapped = false
            };
        }

        public MEPSnapResult ComputeMEPWallSnap(
            Ray cursorRay,
            float floorElevationY,
            bool lockElevationY,
            float presetElevationAboveFloor,
            LayerMask wallLayerMask,
            LayerMask applianceHazardZoneMask)
        {
            if (!Physics.Raycast(cursorRay, out RaycastHit hit, 100f, wallLayerMask))
            {
                return new MEPSnapResult { IsValidHit = false };
            }

            if (!hit.collider.TryGetComponent(out WallSurfaceIdentifier wallSurface))
            {
                return new MEPSnapResult { IsValidHit = false };
            }

            Vector3 normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            float worldY = lockElevationY
                ? floorElevationY + presetElevationAboveFloor
                : Mathf.Max(floorElevationY + 0.05f, hit.point.y);

            Vector3 snappedPos = new(hit.point.x, worldY, hit.point.z);
            Quaternion rot = Quaternion.LookRotation(normal, Vector3.up);

            Vector2 hitXZ = new(snappedPos.x, snappedPos.z);
            Vector2 cornerA = wallSurface.GetFaceStartCornerXZ(normal);
            Vector2 cornerB = wallSurface.GetFaceEndCornerXZ(normal);

            float distA = Vector2.Distance(hitXZ, cornerA);
            float distB = Vector2.Distance(hitXZ, cornerB);
            Vector2 nearestCornerXZ = distA <= distB ? cornerA : cornerB;
            float nearestDist = Mathf.Min(distA, distB);

            Vector3 nearestCornerWorld = new(nearestCornerXZ.x, worldY, nearestCornerXZ.y);
            Vector3 floorProjection = new(snappedPos.x, floorElevationY, snappedPos.z);

            bool hasCollision = Physics.CheckSphere(snappedPos, 0.12f, applianceHazardZoneMask);
            string warning = hasCollision
                ? "UWAGA: Kolizja punktu instalacyjnego ze strefą urządzenia AGD (np. zmywarka) lub skosem dachu!"
                : string.Empty;

            return new MEPSnapResult
            {
                IsValidHit = true,
                WorldPosition = snappedPos,
                SurfaceNormal = normal,
                WorldRotation = rot,
                HostWallId = wallSurface.WallId,
                DistanceToNearestCorner = nearestDist,
                NearestCornerWorldPoint = nearestCornerWorld,
                ElevationFromFloor = worldY - floorElevationY,
                FloorProjectionPoint = floorProjection,
                HasSafetyCollision = hasCollision,
                CollisionWarningMessage = warning
            };
        }
    }

    public sealed class WallSurfaceIdentifier : MonoBehaviour
    {
        public Guid WallId;
        public Vector2 FrontStartCornerXZ;
        public Vector2 FrontEndCornerXZ;
        public Vector2 BackStartCornerXZ;
        public Vector2 BackEndCornerXZ;
        public Vector3 FrontNormal;

        public Vector2 GetFaceStartCornerXZ(Vector3 hitNormal) =>
            Vector3.Dot(hitNormal, FrontNormal) >= 0f ? FrontStartCornerXZ : BackStartCornerXZ;

        public Vector2 GetFaceEndCornerXZ(Vector3 hitNormal) =>
            Vector3.Dot(hitNormal, FrontNormal) >= 0f ? FrontEndCornerXZ : BackEndCornerXZ;
    }
}
