using System;
using System.Collections.Generic;
using UnityEngine;

namespace HomeBuilder.Data
{
    /// <summary>
    /// Root container for the entire multi-story BIM house project.
    /// Designed for JSON serialization (Save/Load) and decoupled from MonoBehaviour.
    /// </summary>
    [Serializable]
    public sealed class ProjectData
    {
        public string SchemaVersion { get; set; } = "1.0.0";
        public Guid ProjectId { get; set; } = Guid.NewGuid();
        public string ProjectName { get; set; } = "Nowy Projekt Domu";
        public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
        public EnvironmentSettingsData Environment { get; set; } = new();
        public List<FloorData> Floors { get; set; } = new();
        public List<CustomAssetManifestEntry> CustomAssetLibrary { get; set; } = new();
    }

    [Serializable]
    public sealed class EnvironmentSettingsData
    {
        public float TimeOfDayHours { get; set; } = 14.5f;
        public float NorthAngleDegrees { get; set; } = 0f;
        public float Latitude { get; set; } = 52.2297f;
        public float Longitude { get; set; } = 21.0122f;
    }

    [Serializable]
    public sealed class FloorData
    {
        public Guid FloorId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Parter";
        public int LevelIndex { get; set; } = 0;
        public float ElevationY { get; set; } = 0.0f;
        public float DefaultCeilingHeight { get; set; } = 2.75f;
        public float SlabThickness { get; set; } = 0.25f;
        public bool IsAttic { get; set; } = false;
        public float KneeWallHeight { get; set; } = 1.10f;

        public BlueprintReferenceData Blueprint { get; set; } = new();
        public List<WallData> Walls { get; set; } = new();
        public List<RoomData> Rooms { get; set; } = new();
        public List<RoofPlaneData> RoofPlanes { get; set; } = new();
        public List<StairwellCutoutData> StairwellCutouts { get; set; } = new();
        public List<FurnitureData> Furnitures { get; set; } = new();
        public List<MEPConnectionLink> MEPLinks { get; set; } = new();
    }

    [Serializable]
    public sealed class StairwellCutoutData
    {
        public Guid CutoutId { get; set; } = Guid.NewGuid();
        public SerializableVector2 CenterXZ { get; set; } = new(0f, 0f);
        public SerializableVector2 SizeXZ { get; set; } = new(2.20f, 2.80f);
    }

    [Serializable]
    public sealed class BlueprintReferenceData
    {
        public string FilePath { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
        public float Opacity { get; set; } = 0.65f;
        public float MetersPerPixel { get; set; } = 0.01f;
        public SerializableVector2 WorldOffset { get; set; } = new(0f, 0f);
        public float RotationDegrees { get; set; } = 0f;
    }

    [Serializable]
    public sealed class RoomData
    {
        public Guid RoomId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Pomieszczenie";
        public List<Guid> BoundaryWallIds { get; set; } = new();
        public List<SerializableVector2> PolygonVertices2D { get; set; } = new();
        public string FloorMaterialId { get; set; } = "mat_oak_parquet";
        public string CeilingMaterialId { get; set; } = "mat_white_plaster";
        public List<MEPData> CeilingMEPItems { get; set; } = new();
    }

    [Serializable]
    public sealed class WallData
    {
        public Guid WallId { get; set; } = Guid.NewGuid();
        public SerializableVector2 StartPoint { get; set; }
        public SerializableVector2 EndPoint { get; set; }
        public float Thickness { get; set; } = 0.24f;
        public float BaseHeight { get; set; } = 2.75f;
        public bool IsLoadBearing { get; set; } = true;
        public bool ClipByRoofPlanes { get; set; } = true;

        public string FrontMaterialId { get; set; } = "mat_wall_white";
        public string BackMaterialId { get; set; } = "mat_wall_white";
        public string EdgeMaterialId { get; set; } = "mat_wall_concrete";

        public List<OpeningData> Openings { get; set; } = new();
        public List<MEPData> MountedMEPItems { get; set; } = new();
    }

    public enum OpeningType
    {
        DoorSingle,
        DoorDouble,
        WindowStandard,
        WindowRoof,
        WallOpeningPassThrough
    }

    [Serializable]
    public sealed class OpeningData
    {
        public Guid OpeningId { get; set; } = Guid.NewGuid();
        public OpeningType Type { get; set; } = OpeningType.WindowStandard;
        public float DistanceFromWallStart { get; set; } = 1.0f;
        public float Width { get; set; } = 1.20f;
        public float Height { get; set; } = 1.50f;
        public float SillHeight { get; set; } = 0.85f;
        public bool FlipHorizontal { get; set; } = false;
        public bool FlipInward { get; set; } = true;
        public string PrefabAssetId { get; set; } = "window_double_tilt";
    }

    public enum MEPCategory
    {
        ElectricalSocket230V,
        ElectricalSocket400V,
        LightSwitchSingle,
        LightSwitchDouble,
        CeilingLightPoint,
        WallLightPoint,
        WaterSupplyCold,
        WaterSupplyHot,
        SewageDrain50mm,
        SewageDrain110mm
    }

    [Serializable]
    public sealed class MEPData
    {
        public Guid MEPId { get; set; } = Guid.NewGuid();
        public MEPCategory Category { get; set; } = MEPCategory.ElectricalSocket230V;
        public Guid HostSurfaceId { get; set; }
        public int WallSide { get; set; } = 1;
        public float DistanceAlongWall { get; set; } = 0.50f;
        public float ElevationFromFloor { get; set; } = 0.30f;
        public SerializableVector3 WorldPosition { get; set; }
        public SerializableVector3 SurfaceNormal { get; set; }
        public float SafetyZoneRadius { get; set; } = 0.15f;
        public string PrefabAssetId { get; set; } = "mep_socket_single";
    }

    [Serializable]
    public sealed class MEPConnectionLink
    {
        public Guid LinkId { get; set; } = Guid.NewGuid();
        public Guid SwitchMEPId { get; set; }
        public Guid TargetLightMEPId { get; set; }
        public int SwitchButtonIndex { get; set; } = 0;
    }

    [Serializable]
    public sealed class RoofPlaneData
    {
        public Guid RoofPlaneId { get; set; } = Guid.NewGuid();
        public SerializableVector2 EaveStart { get; set; }
        public SerializableVector2 EaveEnd { get; set; }
        public float EaveElevationY { get; set; } = 1.10f;
        public float PitchAngleDegrees { get; set; } = 38.0f;
        public float Thickness { get; set; } = 0.30f;
    }

    [Serializable]
    public sealed class FurnitureData
    {
        public Guid InstanceId { get; set; } = Guid.NewGuid();
        public string CatalogOrCustomAssetId { get; set; }
        public bool IsExternalCustomModel { get; set; } = false;
        public SerializableVector3 Position { get; set; }
        public SerializableVector3 RotationEuler { get; set; }
        public SerializableVector3 Scale { get; set; } = new(1f, 1f, 1f);
        public Guid? SnappedWallId { get; set; }
        public bool HasApplianceExclusionZone { get; set; } = false;
        public Dictionary<string, string> MaterialOverrides { get; set; } = new();
    }

    [Serializable]
    public sealed class CustomAssetManifestEntry
    {
        public string AssetId { get; set; }
        public string OriginalFileName { get; set; }
        public string RelativeLocalPath { get; set; }
        public SerializableVector3 NormalizedBoundsSize { get; set; }
        public SerializableVector3 PivotOffsetApplied { get; set; }
    }

    [Serializable]
    public struct SerializableVector2
    {
        public float X;
        public float Y;

        public SerializableVector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public Vector2 ToVector2() => new(X, Y);
        public static implicit operator SerializableVector2(Vector2 v) => new(v.x, v.y);
    }

    [Serializable]
    public struct SerializableVector3
    {
        public float X;
        public float Y;
        public float Z;

        public SerializableVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vector3 ToVector3() => new(X, Y, Z);
        public static implicit operator SerializableVector3(Vector3 v) => new(v.x, v.y, v.z);
    }
}
