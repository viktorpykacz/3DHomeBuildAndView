using System;
using HomeBuilder.Data;
using HomeBuilder.Walkthrough;
using UnityEngine;

namespace HomeBuilder.Geometry
{
    public sealed class OpeningSurfaceIdentifier : MonoBehaviour
    {
        public Guid WallId;
        public Guid OpeningId;
    }

    /// <summary>
    /// Builds procedural 3D Window and Door assemblies (frames, sashes, glass panes, and interactive hinged door leaves)
    /// fitted accurately inside wall cutouts.
    /// </summary>
    public static class ProceduralOpeningFrameGenerator
    {
        public static GameObject CreateOpeningInstance(
            WallData hostWall,
            OpeningData opening,
            Transform parent,
            Material frameMaterial,
            Material glassMaterial,
            Material doorLeafMaterial)
        {
            Vector2 a = hostWall.StartPoint.ToVector2();
            Vector2 b = hostWall.EndPoint.ToVector2();
            Vector2 dir2D = (b - a).normalized;
            Vector3 wallTangent = new(dir2D.x, 0f, dir2D.y);
            Vector3 wallNormal = new(-dir2D.y, 0f, dir2D.x);

            Vector2 centerXZ = a + dir2D * opening.DistanceFromWallStart;
            Vector3 rootPosition = new(centerXZ.x, opening.SillHeight, centerXZ.y);
            Quaternion rootRotation = Quaternion.LookRotation(wallNormal, Vector3.up);

            var rootGo = new GameObject($"Opening_{opening.Type}_{opening.OpeningId.ToString()[..6]}");
            rootGo.transform.SetParent(parent, false);
            rootGo.transform.SetLocalPositionAndRotation(rootPosition, rootRotation);

            var identifier = rootGo.AddComponent<OpeningSurfaceIdentifier>();
            identifier.WallId = hostWall.WallId;
            identifier.OpeningId = opening.OpeningId;

            var selectionCollider = rootGo.AddComponent<BoxCollider>();
            selectionCollider.center = new Vector3(0f, opening.Height * 0.5f, 0f);
            selectionCollider.size = new Vector3(opening.Width, opening.Height, hostWall.Thickness + 0.04f);

            if (opening.Type == OpeningType.WallOpeningPassThrough)
            {
                return rootGo;
            }

            float frameThickness = 0.055f;
            float frameDepth = Mathf.Max(0.08f, hostWall.Thickness + 0.01f);
            float halfW = opening.Width * 0.5f;

            // Left jamb
            CreateBoxPart(
                "Frame_Left",
                rootGo.transform,
                new Vector3(-halfW + frameThickness * 0.5f, opening.Height * 0.5f, 0f),
                new Vector3(frameThickness, opening.Height, frameDepth),
                frameMaterial);

            // Right jamb
            CreateBoxPart(
                "Frame_Right",
                rootGo.transform,
                new Vector3(halfW - frameThickness * 0.5f, opening.Height * 0.5f, 0f),
                new Vector3(frameThickness, opening.Height, frameDepth),
                frameMaterial);

            // Top head jamb
            CreateBoxPart(
                "Frame_Top",
                rootGo.transform,
                new Vector3(0f, opening.Height - frameThickness * 0.5f, 0f),
                new Vector3(opening.Width - frameThickness * 2f, frameThickness, frameDepth),
                frameMaterial);

            if (opening.Type == OpeningType.WindowStandard || opening.Type == OpeningType.WindowRoof)
            {
                // Bottom window sill frame + sill board
                CreateBoxPart(
                    "Frame_Bottom",
                    rootGo.transform,
                    new Vector3(0f, frameThickness * 0.5f, 0f),
                    new Vector3(opening.Width - frameThickness * 2f, frameThickness, frameDepth),
                    frameMaterial);

                CreateBoxPart(
                    "Window_SillBoard",
                    rootGo.transform,
                    new Vector3(0f, -0.015f, 0f),
                    new Vector3(opening.Width + 0.06f, 0.03f, hostWall.Thickness + 0.08f),
                    frameMaterial);

                // Center vertical mullion for windows wider than 0.95m
                if (opening.Width >= 0.95f)
                {
                    CreateBoxPart(
                        "Window_CenterMullion",
                        rootGo.transform,
                        new Vector3(0f, opening.Height * 0.5f, 0f),
                        new Vector3(frameThickness * 0.8f, opening.Height - frameThickness * 2f, frameThickness),
                        frameMaterial);
                }

                // Translucent Glass Pane
                CreateBoxPart(
                    "Window_GlassPane",
                    rootGo.transform,
                    new Vector3(0f, opening.Height * 0.5f, 0f),
                    new Vector3(opening.Width - frameThickness * 2f, opening.Height - frameThickness * 2f, 0.018f),
                    glassMaterial);
            }
            else if (opening.Type == OpeningType.DoorSingle || opening.Type == OpeningType.DoorDouble)
            {
                float innerWidth = opening.Width - frameThickness * 2f;
                float innerHeight = opening.Height - frameThickness;
                float leafThickness = 0.042f;

                if (opening.Type == OpeningType.DoorSingle)
                {
                    float hingeSign = opening.FlipHorizontal ? 1f : -1f;
                    var hingeGo = new GameObject("DoorHingePivot");
                    hingeGo.transform.SetParent(rootGo.transform, false);
                    hingeGo.transform.localPosition = new Vector3(hingeSign * (innerWidth * 0.5f), 0f, 0f);

                    GameObject leaf = CreateBoxPart(
                        "DoorLeaf",
                        hingeGo.transform,
                        new Vector3(-hingeSign * (innerWidth * 0.5f), innerHeight * 0.5f, 0f),
                        new Vector3(innerWidth, innerHeight, leafThickness),
                        doorLeafMaterial,
                        includeCollider: true);

                    // Door handle visual
                    CreateBoxPart(
                        "DoorHandle",
                        leaf.transform,
                        new Vector3(-hingeSign * (innerWidth * 0.38f), 0.05f, 0f),
                        new Vector3(0.12f, 0.025f, 0.14f),
                        frameMaterial);

                    rootGo.AddComponent<HingedDoorInteractable>();
                }
                else
                {
                    float halfLeafW = innerWidth * 0.5f;
                    var leftHinge = new GameObject("DoorHingeLeft");
                    leftHinge.transform.SetParent(rootGo.transform, false);
                    leftHinge.transform.localPosition = new Vector3(-innerWidth * 0.5f, 0f, 0f);

                    CreateBoxPart(
                        "DoorLeaf_Left",
                        leftHinge.transform,
                        new Vector3(halfLeafW * 0.5f, innerHeight * 0.5f, 0f),
                        new Vector3(halfLeafW - 0.005f, innerHeight, leafThickness),
                        doorLeafMaterial,
                        includeCollider: true);

                    var rightHinge = new GameObject("DoorHingeRight");
                    rightHinge.transform.SetParent(rootGo.transform, false);
                    rightHinge.transform.localPosition = new Vector3(innerWidth * 0.5f, 0f, 0f);

                    CreateBoxPart(
                        "DoorLeaf_Right",
                        rightHinge.transform,
                        new Vector3(-halfLeafW * 0.5f, innerHeight * 0.5f, 0f),
                        new Vector3(halfLeafW - 0.005f, innerHeight, leafThickness),
                        doorLeafMaterial,
                        includeCollider: true);
                }
            }

            return rootGo;
        }

        private static GameObject CreateBoxPart(
            string name,
            Transform parent,
            Vector3 localPos,
            Vector3 scale,
            Material material,
            bool includeCollider = false)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPos;
            box.transform.localScale = scale;

            if (!includeCollider && box.TryGetComponent(out Collider col))
            {
                UnityEngine.Object.Destroy(col);
            }

            if (box.TryGetComponent(out MeshRenderer mr) && material != null)
            {
                mr.sharedMaterial = material;
            }

            return box;
        }
    }
}
