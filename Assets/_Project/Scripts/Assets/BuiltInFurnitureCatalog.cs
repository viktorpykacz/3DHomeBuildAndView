using System;
using System.Collections.Generic;
using HomeBuilder.Walkthrough;
using UnityEngine;

namespace HomeBuilder.Assets
{
    public sealed class FurnitureInstanceIdentifier : MonoBehaviour
    {
        public Guid InstanceId;
        public string CatalogOrCustomAssetId;
        public Guid? SnappedWallId;
        public Bounds NormalizedLocalBounds;
        public bool HasApplianceExclusionZone;
    }

    public sealed class BuiltInCatalogEntry
    {
        public string AssetId;
        public string DisplayName;
        public Vector3 DimensionsSize; // X = Width, Y = Height, Z = Depth (meters)
        public float DefaultMountElevationY; // 0.0m for floor cabinets, 1.45m for wall-hung cabinets
        public bool SnapBackToWallByDefault;
        public bool HasApplianceExclusionZone;
        public bool HasHingedDoorFront;
    }

    /// <summary>
    /// Built-in parametric catalog of kitchen cabinets, appliances (AGD), and living room furniture.
    /// All generated models are strictly normalized to Bottom-Back-Center pivot:
    /// X in [-Width/2, +Width/2], Y in [0, Height], Z in [0, Depth].
    /// </summary>
    public static class BuiltInFurnitureCatalog
    {
        public const string IdCabinetBase60 = "cabinet_base_60";
        public const string IdCabinetSink80 = "cabinet_sink_80";
        public const string IdApplianceDishwasher60 = "appliance_dishwasher_60";
        public const string IdCabinetWall60 = "cabinet_wall_60";
        public const string IdApplianceFridge60 = "appliance_fridge_60";
        public const string IdFurnitureSofa220 = "furniture_sofa_220";
        public const string IdFurnitureTable160 = "furniture_table_160";

        private static readonly Dictionary<string, BuiltInCatalogEntry> _entries = new()
        {
            [IdCabinetBase60] = new BuiltInCatalogEntry
            {
                AssetId = IdCabinetBase60,
                DisplayName = "Szafka dolna 60 cm (z blatem)",
                DimensionsSize = new Vector3(0.60f, 0.85f, 0.60f),
                DefaultMountElevationY = 0.00f,
                SnapBackToWallByDefault = true,
                HasApplianceExclusionZone = false,
                HasHingedDoorFront = true
            },
            [IdCabinetSink80] = new BuiltInCatalogEntry
            {
                AssetId = IdCabinetSink80,
                DisplayName = "Szafka ze zlewem 80 cm",
                DimensionsSize = new Vector3(0.80f, 0.85f, 0.60f),
                DefaultMountElevationY = 0.00f,
                SnapBackToWallByDefault = true,
                HasApplianceExclusionZone = true,
                HasHingedDoorFront = true
            },
            [IdApplianceDishwasher60] = new BuiltInCatalogEntry
            {
                AssetId = IdApplianceDishwasher60,
                DisplayName = "Zmywarka AGD 60 cm (Strefa Zakazu Gniazdek)",
                DimensionsSize = new Vector3(0.60f, 0.85f, 0.60f),
                DefaultMountElevationY = 0.00f,
                SnapBackToWallByDefault = true,
                HasApplianceExclusionZone = true,
                HasHingedDoorFront = true
            },
            [IdCabinetWall60] = new BuiltInCatalogEntry
            {
                AssetId = IdCabinetWall60,
                DisplayName = "Szafka wisząca 60 cm (Wys. 145 cm)",
                DimensionsSize = new Vector3(0.60f, 0.72f, 0.35f),
                DefaultMountElevationY = 1.45f,
                SnapBackToWallByDefault = true,
                HasApplianceExclusionZone = false,
                HasHingedDoorFront = true
            },
            [IdApplianceFridge60] = new BuiltInCatalogEntry
            {
                AssetId = IdApplianceFridge60,
                DisplayName = "Lodówka 60×200 cm",
                DimensionsSize = new Vector3(0.60f, 2.00f, 0.65f),
                DefaultMountElevationY = 0.00f,
                SnapBackToWallByDefault = true,
                HasApplianceExclusionZone = false,
                HasHingedDoorFront = true
            },
            [IdFurnitureSofa220] = new BuiltInCatalogEntry
            {
                AssetId = IdFurnitureSofa220,
                DisplayName = "Sofa salonowa 220 cm",
                DimensionsSize = new Vector3(2.20f, 0.82f, 0.95f),
                DefaultMountElevationY = 0.00f,
                SnapBackToWallByDefault = true,
                HasApplianceExclusionZone = false,
                HasHingedDoorFront = false
            },
            [IdFurnitureTable160] = new BuiltInCatalogEntry
            {
                AssetId = IdFurnitureTable160,
                DisplayName = "Stół jadalniany 160×90 cm",
                DimensionsSize = new Vector3(1.60f, 0.76f, 0.90f),
                DefaultMountElevationY = 0.00f,
                SnapBackToWallByDefault = false,
                HasApplianceExclusionZone = false,
                HasHingedDoorFront = false
            }
        };

        public static IReadOnlyDictionary<string, BuiltInCatalogEntry> Entries => _entries;

        public static BuiltInCatalogEntry GetEntryOrDefault(string assetId)
        {
            if (!string.IsNullOrEmpty(assetId) && _entries.TryGetValue(assetId, out BuiltInCatalogEntry entry))
            {
                return entry;
            }
            return _entries[IdCabinetBase60];
        }

        /// <summary>
        /// Instantiates a 3D procedural furniture/cabinet model with Bottom-Back-Center pivot,
        /// BoxCollider, HingedDoorInteractable on cabinet fronts, and optional Appliance Hazard Zone collider.
        /// </summary>
        public static GameObject CreateCatalogModelInstance(
            string assetId,
            Transform parent,
            Material bodyMat,
            Material frontMat,
            Material countertopMat,
            Material metalMat,
            int applianceHazardLayer = 0)
        {
            BuiltInCatalogEntry entry = GetEntryOrDefault(assetId);
            Vector3 size = entry.DimensionsSize;

            var rootGo = new GameObject($"Catalog_{entry.AssetId}");
            if (parent != null) rootGo.transform.SetParent(parent, false);

            var localBounds = new Bounds(new Vector3(0f, size.y * 0.5f, size.z * 0.5f), size);
            var mainCollider = rootGo.AddComponent<BoxCollider>();
            mainCollider.center = localBounds.center;
            mainCollider.size = localBounds.size;

            var identifier = rootGo.AddComponent<FurnitureInstanceIdentifier>();
            identifier.CatalogOrCustomAssetId = entry.AssetId;
            identifier.NormalizedLocalBounds = localBounds;
            identifier.HasApplianceExclusionZone = entry.HasApplianceExclusionZone;

            if (entry.AssetId == IdFurnitureTable160)
            {
                BuildTableGeometry(rootGo.transform, size, countertopMat, metalMat);
            }
            else if (entry.AssetId == IdFurnitureSofa220)
            {
                BuildSofaGeometry(rootGo.transform, size, frontMat, bodyMat);
            }
            else
            {
                BuildCabinetOrApplianceGeometry(rootGo, entry, size, bodyMat, frontMat, countertopMat, metalMat);
            }

            if (entry.HasApplianceExclusionZone)
            {
                var hazardGo = new GameObject("ApplianceMEPExclusionZone");
                hazardGo.layer = applianceHazardLayer;
                hazardGo.transform.SetParent(rootGo.transform, false);
                var hazardBox = hazardGo.AddComponent<BoxCollider>();
                hazardBox.isTrigger = true;
                // Covers the rear wall area directly behind the dishwasher/sink where electrical sockets are forbidden
                hazardBox.center = new Vector3(0f, size.y * 0.5f, 0.05f);
                hazardBox.size = new Vector3(size.x, size.y, 0.25f);
            }

            return rootGo;
        }

        private static void BuildCabinetOrApplianceGeometry(
            GameObject rootGo,
            BuiltInCatalogEntry entry,
            Vector3 size,
            Material bodyMat,
            Material frontMat,
            Material countertopMat,
            Material metalMat)
        {
            bool hasCountertop = entry.AssetId == IdCabinetBase60 ||
                                 entry.AssetId == IdCabinetSink80 ||
                                 entry.AssetId == IdApplianceDishwasher60;

            float countertopHeight = hasCountertop ? 0.038f : 0f;
            float carcassHeight = size.y - countertopHeight;
            float frontThickness = 0.02f;
            float carcassDepth = size.z - frontThickness;

            // Main carcass box (X centered, Y from 0 to carcassHeight, Z from 0 to carcassDepth)
            CreateBoxPart(
                "Carcass",
                rootGo.transform,
                new Vector3(0f, carcassHeight * 0.5f, carcassDepth * 0.5f),
                new Vector3(size.x - 0.004f, carcassHeight, carcassDepth),
                entry.AssetId == IdApplianceDishwasher60 || entry.AssetId == IdApplianceFridge60 ? metalMat : bodyMat);

            // Countertop slab (slightly overhanging at front by 2cm)
            if (hasCountertop)
            {
                CreateBoxPart(
                    "Countertop",
                    rootGo.transform,
                    new Vector3(0f, carcassHeight + countertopHeight * 0.5f, (size.z + 0.02f) * 0.5f),
                    new Vector3(size.x, countertopHeight, size.z + 0.02f),
                    countertopMat);
            }

            // Sink basin & faucet on sink cabinet
            if (entry.AssetId == IdCabinetSink80)
            {
                CreateBoxPart(
                    "SinkBasin",
                    rootGo.transform,
                    new Vector3(0f, size.y + 0.005f, size.z * 0.52f),
                    new Vector3(size.x * 0.72f, 0.015f, size.z * 0.65f),
                    metalMat);

                CreateBoxPart(
                    "SinkFaucetStem",
                    rootGo.transform,
                    new Vector3(0f, size.y + 0.14f, 0.12f),
                    new Vector3(0.035f, 0.28f, 0.035f),
                    metalMat);

                CreateBoxPart(
                    "SinkFaucetSpout",
                    rootGo.transform,
                    new Vector3(0f, size.y + 0.26f, 0.21f),
                    new Vector3(0.03f, 0.03f, 0.20f),
                    metalMat);
            }

            // Hinged front door (opens in Walkthrough FPP mode!)
            if (entry.HasHingedDoorFront)
            {
                var hingePivot = new GameObject("CabinetDoorHinge");
                hingePivot.transform.SetParent(rootGo.transform, false);
                hingePivot.transform.localPosition = new Vector3(-size.x * 0.5f + 0.01f, 0f, carcassDepth);

                GameObject doorFront = CreateBoxPart(
                    "CabinetFrontLeaf",
                    hingePivot.transform,
                    new Vector3(size.x * 0.5f - 0.01f, carcassHeight * 0.5f, frontThickness * 0.5f),
                    new Vector3(size.x - 0.006f, carcassHeight - 0.006f, frontThickness),
                    entry.AssetId == IdApplianceDishwasher60 || entry.AssetId == IdApplianceFridge60 ? metalMat : frontMat);

                // Handle bar
                CreateBoxPart(
                    "Handle",
                    doorFront.transform,
                    new Vector3(size.x * 0.36f, carcassHeight * 0.35f, frontThickness),
                    new Vector3(0.02f, 0.14f, 0.025f),
                    metalMat);

                rootGo.AddComponent<HingedDoorInteractable>();
            }
        }

        private static void BuildSofaGeometry(Transform root, Vector3 size, Material fabricMat, Material baseMat)
        {
            float seatH = 0.42f;
            float backD = 0.22f;
            float armW = 0.18f;

            // Seat base
            CreateBoxPart("SofaSeat", root, new Vector3(0f, seatH * 0.5f, size.z * 0.5f), new Vector3(size.x, seatH, size.z), fabricMat);
            // Backrest (at Z near 0 so back is flush with wall!)
            CreateBoxPart("SofaBackrest", root, new Vector3(0f, size.y * 0.5f, backD * 0.5f), new Vector3(size.x, size.y, backD), fabricMat);
            // Left & Right Armrests
            CreateBoxPart("SofaArmL", root, new Vector3(-size.x * 0.5f + armW * 0.5f, size.y * 0.38f, size.z * 0.5f), new Vector3(armW, size.y * 0.76f, size.z), baseMat);
            CreateBoxPart("SofaArmR", root, new Vector3(size.x * 0.5f - armW * 0.5f, size.y * 0.38f, size.z * 0.5f), new Vector3(armW, size.y * 0.76f, size.z), baseMat);
        }

        private static void BuildTableGeometry(Transform root, Vector3 size, Material topMat, Material legMat)
        {
            float topThickness = 0.04f;
            float legW = 0.06f;
            float legH = size.y - topThickness;

            CreateBoxPart("TableTop", root, new Vector3(0f, size.y - topThickness * 0.5f, size.z * 0.5f), new Vector3(size.x, topThickness, size.z), topMat);

            float xOff = size.x * 0.5f - legW;
            float zNear = legW;
            float zFar = size.z - legW;

            CreateBoxPart("LegFL", root, new Vector3(-xOff, legH * 0.5f, zFar), new Vector3(legW, legH, legW), legMat);
            CreateBoxPart("LegFR", root, new Vector3(xOff, legH * 0.5f, zFar), new Vector3(legW, legH, legW), legMat);
            CreateBoxPart("LegBL", root, new Vector3(-xOff, legH * 0.5f, zNear), new Vector3(legW, legH, legW), legMat);
            CreateBoxPart("LegBR", root, new Vector3(xOff, legH * 0.5f, zNear), new Vector3(legW, legH, legW), legMat);
        }

        private static GameObject CreateBoxPart(
            string name,
            Transform parent,
            Vector3 localPos,
            Vector3 scale,
            Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPos;
            box.transform.localScale = scale;

            if (box.TryGetComponent(out Collider col))
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
