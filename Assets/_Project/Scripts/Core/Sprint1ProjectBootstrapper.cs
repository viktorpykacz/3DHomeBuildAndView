using System.IO;
using HomeBuilder.Assets;
using HomeBuilder.Core.Commands;
using HomeBuilder.Data;
using HomeBuilder.Floorplan;
using HomeBuilder.Geometry;
using HomeBuilder.Tools;
using HomeBuilder.UI;
using HomeBuilder.Walkthrough;
using UnityEngine;
using UnityEngine.UIElements;

namespace HomeBuilder.Core
{
    /// <summary>
    /// Complete Production Bootstrapper for Sprints 1–5 (Full Product):
    /// 1. Floorplan 2D Calibration & Multi-Story BIM (Parter + Poddasze Użytkowe)
    /// 2. Procedural 3D Walls with Miter Joins, Window/Door Cutouts & Attic Roof Plane Slicing
    /// 3. Built-in Kitchen/Furniture Catalog, Runtime .glb/.gltf Importer & Back-to-Wall / Edge-to-Edge Snapping
    /// 4. MEP Installations (Sockets, Switches, Ceiling Lights, Plumbing), Live Dimensions, Collision Warnings & 2D Wall Elevation View
    /// 5. Real-Time HDRP Environment (Physical Camera, Sun Time-of-Day, Reflection Probe) & Interactive First-Person Walkthrough (FPP)
    /// </summary>
    public sealed class Sprint1ProjectBootstrapper : MonoBehaviour
    {
        [Header("Startup Options")]
        [SerializeField] private bool createStarterDemoLayoutOnAwake = true;
        [SerializeField] private string defaultSaveFileName = "HomeBuilderProject_Full_v5.json";

        [Header("Optional Scene References (Auto-created if null)")]
        [SerializeField] private Camera mainViewportCamera;
        [SerializeField] private FloorSceneRebuilder sceneRebuilder;
        [SerializeField] private BlueprintCalibrationController blueprintController;
        [SerializeField] private BlueprintCalibrationTool calibrationTool;
        [SerializeField] private WallDrawingTool wallDrawingTool;
        [SerializeField] private WallOpeningPlacementTool openingPlacementTool;
        [SerializeField] private FurniturePlacementTool furniturePlacementTool;
        [SerializeField] private MEPInstallationTool mepInstallationTool;
        [SerializeField] private CadViewportCameraController cameraController;
        [SerializeField] private HDRPRealtimeEnvironmentController environmentController;
        [SerializeField] private WalkthroughModeController walkthroughController;
        [SerializeField] private Sprint1WorkspaceUIController workspaceUI;

        public ProjectData CurrentProject { get; private set; }
        public FloorData ActiveFloor { get; private set; }
        public CommandHistoryManager CommandHistory { get; private set; }
        public ProjectPersistenceService PersistenceService { get; private set; }

        private string DefaultSaveFilePath => Path.Combine(Application.persistentDataPath, defaultSaveFileName);

        private void Awake()
        {
            CommandHistory = new CommandHistoryManager(maxHistorySize: 250);
            PersistenceService = new ProjectPersistenceService();

            InitializeProjectModel();
            EnsureSceneComponents();

            if (createStarterDemoLayoutOnAwake && CurrentProject.Floors[0].Walls.Count == 0)
            {
                PopulateStarterGroundFloor(CurrentProject.Floors[0]);
                PopulateStarterAtticFloor(CurrentProject.Floors[1]);
            }

            sceneRebuilder.RebuildAllFloors(CurrentProject);
            environmentController.RefreshInteriorReflectionProbe();
            SelectActiveFloorByIndex(0);

            CommandHistory.OnHistoryChanged += () => environmentController.RefreshInteriorReflectionProbe();
        }

        private void InitializeProjectModel()
        {
            CurrentProject = new ProjectData
            {
                ProjectName = "3DHomeBuildAndView - Kompletny Projekt Domu (Sprints 1-5)"
            };

            var groundFloor = new FloorData
            {
                Name = "Parter",
                LevelIndex = 0,
                ElevationY = 0.0f,
                DefaultCeilingHeight = 2.75f,
                SlabThickness = 0.25f,
                IsAttic = false
            };

            var atticFloor = new FloorData
            {
                Name = "Poddasze Użytkowe",
                LevelIndex = 1,
                ElevationY = 3.00f,
                DefaultCeilingHeight = 3.45f,
                SlabThickness = 0.25f,
                IsAttic = true,
                KneeWallHeight = 1.10f
            };

            CurrentProject.Floors.Add(groundFloor);
            CurrentProject.Floors.Add(atticFloor);
            ActiveFloor = groundFloor;
        }

        public void SelectActiveFloorByIndex(int floorIndex)
        {
            if (CurrentProject == null || floorIndex < 0 || floorIndex >= CurrentProject.Floors.Count) return;

            ActiveFloor = CurrentProject.Floors[floorIndex];
            wallDrawingTool?.SetActiveFloor(ActiveFloor);
            openingPlacementTool?.SetActiveFloor(ActiveFloor);
            furniturePlacementTool?.SetActiveFloor(ActiveFloor);
            mepInstallationTool?.SetActiveFloor(ActiveFloor);
            calibrationTool?.SetActiveFloor(ActiveFloor);
            cameraController?.FocusOnElevation(ActiveFloor.ElevationY);

            bool showUpper = floorIndex > 0;
            sceneRebuilder?.SetUpperFloorsVisibleAbove(ActiveFloor.LevelIndex, CurrentProject, showUpper);
        }

        private void EnsureSceneComponents()
        {
            if (mainViewportCamera == null)
            {
                mainViewportCamera = Camera.main;
                if (mainViewportCamera == null)
                {
                    var camGo = new GameObject("MainViewportCamera");
                    mainViewportCamera = camGo.AddComponent<Camera>();
                    camGo.tag = "MainCamera";
                }
            }

            if (cameraController == null)
            {
                cameraController = mainViewportCamera.GetComponent<CadViewportCameraController>();
                if (cameraController == null)
                {
                    cameraController = mainViewportCamera.gameObject.AddComponent<CadViewportCameraController>();
                }
            }

            if (environmentController == null)
            {
                var envGo = new GameObject("HDRPRealtimeEnvironmentController");
                envGo.transform.SetParent(transform, false);
                environmentController = envGo.AddComponent<HDRPRealtimeEnvironmentController>();
            }
            environmentController.Initialize(mainViewportCamera, CurrentProject.Environment);

            if (walkthroughController == null)
            {
                var wtGo = new GameObject("WalkthroughModeController");
                wtGo.transform.SetParent(transform, false);
                walkthroughController = wtGo.AddComponent<WalkthroughModeController>();
            }
            walkthroughController.Initialize(mainViewportCamera, cameraController, environmentController);

            if (sceneRebuilder == null)
            {
                var rebuilderGo = new GameObject("FloorSceneRebuilder");
                rebuilderGo.transform.SetParent(transform, false);
                sceneRebuilder = rebuilderGo.AddComponent<FloorSceneRebuilder>();
            }

            if (blueprintController == null)
            {
                var bpGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                bpGo.name = "BlueprintReferenceQuad";
                bpGo.transform.SetParent(transform, false);
                bpGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                bpGo.transform.localScale = new Vector3(12f, 10f, 1f);
                Destroy(bpGo.GetComponent<Collider>());

                var mr = bpGo.GetComponent<MeshRenderer>();
                mr.enabled = false;
                blueprintController = bpGo.AddComponent<BlueprintCalibrationController>();
            }

            if (wallDrawingTool == null)
            {
                var toolGo = new GameObject("WallDrawingTool");
                toolGo.transform.SetParent(transform, false);
                wallDrawingTool = toolGo.AddComponent<WallDrawingTool>();
            }
            wallDrawingTool.Initialize(mainViewportCamera, sceneRebuilder, CommandHistory, ActiveFloor);

            if (openingPlacementTool == null)
            {
                var opGo = new GameObject("WallOpeningPlacementTool");
                opGo.transform.SetParent(transform, false);
                openingPlacementTool = opGo.AddComponent<WallOpeningPlacementTool>();
            }
            openingPlacementTool.Initialize(mainViewportCamera, sceneRebuilder, CommandHistory, ActiveFloor);

            if (furniturePlacementTool == null)
            {
                var furnGo = new GameObject("FurniturePlacementTool");
                furnGo.transform.SetParent(transform, false);
                furniturePlacementTool = furnGo.AddComponent<FurniturePlacementTool>();
            }
            furniturePlacementTool.Initialize(mainViewportCamera, sceneRebuilder, CommandHistory, ActiveFloor);

            if (mepInstallationTool == null)
            {
                var mepGo = new GameObject("MEPInstallationTool");
                mepGo.transform.SetParent(transform, false);
                mepInstallationTool = mepGo.AddComponent<MEPInstallationTool>();
            }
            mepInstallationTool.Initialize(mainViewportCamera, sceneRebuilder, cameraController, CommandHistory, ActiveFloor);

            if (calibrationTool == null)
            {
                var calGo = new GameObject("BlueprintCalibrationTool");
                calGo.transform.SetParent(transform, false);
                calibrationTool = calGo.AddComponent<BlueprintCalibrationTool>();
            }
            calibrationTool.Initialize(mainViewportCamera, blueprintController, CommandHistory, ActiveFloor);

            if (workspaceUI == null)
            {
                var uiGo = new GameObject("SprintWorkspaceUI");
                uiGo.transform.SetParent(transform, false);
                var uiDoc = uiGo.AddComponent<UIDocument>();
                if (uiDoc.panelSettings == null)
                {
                    uiDoc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                }
                workspaceUI = uiGo.AddComponent<Sprint1WorkspaceUIController>();
            }

            workspaceUI.Initialize(
                CommandHistory,
                wallDrawingTool,
                openingPlacementTool,
                furniturePlacementTool,
                mepInstallationTool,
                calibrationTool,
                blueprintController,
                cameraController,
                sceneRebuilder,
                walkthroughController,
                () => CurrentProject,
                () => ActiveFloor,
                SelectActiveFloorByIndex,
                SaveCurrentProjectAsync,
                LoadCurrentProjectAsync);
        }

        private static void PopulateStarterGroundFloor(FloorData floor)
        {
            Vector2 p0 = new(-3.0f, -2.0f);
            Vector2 p1 = new(0.5f, -2.0f);
            Vector2 p2 = new(3.0f, -2.0f);
            Vector2 p3 = new(3.0f, 2.0f);
            Vector2 p4 = new(0.5f, 2.0f);
            Vector2 p5 = new(-3.0f, 2.0f);

            var southWestWall = new WallData { StartPoint = p0, EndPoint = p1, Thickness = 0.24f, BaseHeight = 2.75f, IsLoadBearing = true };
            southWestWall.Openings.Add(new OpeningData
            {
                Type = OpeningType.WindowStandard,
                DistanceFromWallStart = 1.75f,
                Width = 1.40f,
                Height = 1.50f,
                SillHeight = 0.85f
            });

            var southEastWall = new WallData { StartPoint = p1, EndPoint = p2, Thickness = 0.24f, BaseHeight = 2.75f, IsLoadBearing = true };
            southEastWall.Openings.Add(new OpeningData
            {
                Type = OpeningType.DoorSingle,
                DistanceFromWallStart = 1.25f,
                Width = 0.95f,
                Height = 2.05f,
                SillHeight = 0.0f
            });

            var eastWall = new WallData { StartPoint = p2, EndPoint = p3, Thickness = 0.24f, BaseHeight = 2.75f, IsLoadBearing = true };
            var northEastWall = new WallData { StartPoint = p3, EndPoint = p4, Thickness = 0.24f, BaseHeight = 2.75f, IsLoadBearing = true };
            var northWestWall = new WallData { StartPoint = p4, EndPoint = p5, Thickness = 0.24f, BaseHeight = 2.75f, IsLoadBearing = true };
            northWestWall.Openings.Add(new OpeningData
            {
                Type = OpeningType.WindowStandard,
                DistanceFromWallStart = 1.75f,
                Width = 1.40f,
                Height = 1.50f,
                SillHeight = 0.85f
            });

            var westWall = new WallData { StartPoint = p5, EndPoint = p0, Thickness = 0.24f, BaseHeight = 2.75f, IsLoadBearing = true };

            var partitionWall = new WallData { StartPoint = p1, EndPoint = p4, Thickness = 0.12f, BaseHeight = 2.75f, IsLoadBearing = false };
            partitionWall.Openings.Add(new OpeningData
            {
                Type = OpeningType.DoorSingle,
                DistanceFromWallStart = 2.0f,
                Width = 0.90f,
                Height = 2.05f,
                SillHeight = 0.0f
            });

            floor.Walls.Add(southWestWall);
            floor.Walls.Add(southEastWall);
            floor.Walls.Add(eastWall);
            floor.Walls.Add(northEastWall);
            floor.Walls.Add(northWestWall);
            floor.Walls.Add(westWall);
            floor.Walls.Add(partitionWall);

            // Starter Kitchen Cabinets along EastWall (X = 2.88m, facing -X: Yaw = 270 deg)
            float innerEastX = 3.0f - eastWall.Thickness * 0.5f;
            Vector3 facingWestEuler = new(0f, 270f, 0f);

            floor.Furnitures.Add(new FurnitureData
            {
                CatalogOrCustomAssetId = BuiltInFurnitureCatalog.IdApplianceFridge60,
                Position = new Vector3(innerEastX, 0f, -1.20f),
                RotationEuler = facingWestEuler,
                SnappedWallId = eastWall.WallId
            });

            floor.Furnitures.Add(new FurnitureData
            {
                CatalogOrCustomAssetId = BuiltInFurnitureCatalog.IdCabinetBase60,
                Position = new Vector3(innerEastX, 0f, -0.60f),
                RotationEuler = facingWestEuler,
                SnappedWallId = eastWall.WallId
            });

            floor.Furnitures.Add(new FurnitureData
            {
                CatalogOrCustomAssetId = BuiltInFurnitureCatalog.IdApplianceDishwasher60,
                Position = new Vector3(innerEastX, 0f, 0.00f),
                RotationEuler = facingWestEuler,
                SnappedWallId = eastWall.WallId,
                HasApplianceExclusionZone = true
            });

            floor.Furnitures.Add(new FurnitureData
            {
                CatalogOrCustomAssetId = BuiltInFurnitureCatalog.IdCabinetSink80,
                Position = new Vector3(innerEastX, 0f, 0.70f),
                RotationEuler = facingWestEuler,
                SnappedWallId = eastWall.WallId,
                HasApplianceExclusionZone = true
            });

            floor.Furnitures.Add(new FurnitureData
            {
                CatalogOrCustomAssetId = BuiltInFurnitureCatalog.IdCabinetWall60,
                Position = new Vector3(innerEastX, 1.45f, -0.60f),
                RotationEuler = facingWestEuler,
                SnappedWallId = eastWall.WallId
            });
            floor.Furnitures.Add(new FurnitureData
            {
                CatalogOrCustomAssetId = BuiltInFurnitureCatalog.IdCabinetWall60,
                Position = new Vector3(innerEastX, 1.45f, 0.00f),
                RotationEuler = facingWestEuler,
                SnappedWallId = eastWall.WallId
            });

            float innerWestX = -3.0f + westWall.Thickness * 0.5f;
            floor.Furnitures.Add(new FurnitureData
            {
                CatalogOrCustomAssetId = BuiltInFurnitureCatalog.IdFurnitureSofa220,
                Position = new Vector3(innerWestX, 0f, 0.0f),
                RotationEuler = new Vector3(0f, 90f, 0f),
                SnappedWallId = westWall.WallId
            });

            floor.Furnitures.Add(new FurnitureData
            {
                CatalogOrCustomAssetId = BuiltInFurnitureCatalog.IdFurnitureTable160,
                Position = new Vector3(-0.95f, 0f, -0.45f),
                RotationEuler = Vector3.zero,
                SnappedWallId = null
            });

            // Starter MEP Installations
            var entranceSwitch = new MEPData
            {
                Category = MEPCategory.LightSwitchSingle,
                HostSurfaceId = southEastWall.WallId,
                DistanceAlongWall = 0.55f,
                ElevationFromFloor = 1.10f,
                WorldPosition = new Vector3(1.05f, 1.10f, -1.88f),
                SurfaceNormal = Vector3.forward
            };
            southEastWall.MountedMEPItems.Add(entranceSwitch);

            eastWall.MountedMEPItems.Add(new MEPData
            {
                Category = MEPCategory.ElectricalSocket230V,
                HostSurfaceId = eastWall.WallId,
                DistanceAlongWall = 1.40f,
                ElevationFromFloor = 1.10f,
                WorldPosition = new Vector3(innerEastX, 1.10f, -0.60f),
                SurfaceNormal = Vector3.left
            });

            eastWall.MountedMEPItems.Add(new MEPData
            {
                Category = MEPCategory.WaterSupplyCold,
                HostSurfaceId = eastWall.WallId,
                DistanceAlongWall = 2.80f,
                ElevationFromFloor = 0.55f,
                WorldPosition = new Vector3(innerEastX, 0.55f, 0.80f),
                SurfaceNormal = Vector3.left
            });
            eastWall.MountedMEPItems.Add(new MEPData
            {
                Category = MEPCategory.WaterSupplyHot,
                HostSurfaceId = eastWall.WallId,
                DistanceAlongWall = 2.60f,
                ElevationFromFloor = 0.55f,
                WorldPosition = new Vector3(innerEastX, 0.55f, 0.60f),
                SurfaceNormal = Vector3.left
            });
            eastWall.MountedMEPItems.Add(new MEPData
            {
                Category = MEPCategory.SewageDrain50mm,
                HostSurfaceId = eastWall.WallId,
                DistanceAlongWall = 2.70f,
                ElevationFromFloor = 0.42f,
                WorldPosition = new Vector3(innerEastX, 0.42f, 0.70f),
                SurfaceNormal = Vector3.left
            });

            westWall.MountedMEPItems.Add(new MEPData
            {
                Category = MEPCategory.ElectricalSocket230V,
                HostSurfaceId = westWall.WallId,
                DistanceAlongWall = 0.65f,
                ElevationFromFloor = 0.30f,
                WorldPosition = new Vector3(innerWestX, 0.30f, 1.35f),
                SurfaceNormal = Vector3.right
            });

            var starterRoom = new RoomData { Name = "Strefa Dzienna" };
            var kitchenCeilingLight = new MEPData
            {
                Category = MEPCategory.CeilingLightPoint,
                HostSurfaceId = starterRoom.RoomId,
                ElevationFromFloor = 2.73f,
                WorldPosition = new Vector3(1.75f, 2.73f, 0.0f),
                SurfaceNormal = Vector3.down
            };
            var livingCeilingLight = new MEPData
            {
                Category = MEPCategory.CeilingLightPoint,
                HostSurfaceId = starterRoom.RoomId,
                ElevationFromFloor = 2.73f,
                WorldPosition = new Vector3(-1.25f, 2.73f, 0.0f),
                SurfaceNormal = Vector3.down
            };
            starterRoom.CeilingMEPItems.Add(kitchenCeilingLight);
            starterRoom.CeilingMEPItems.Add(livingCeilingLight);
            floor.Rooms.Add(starterRoom);

            floor.MEPLinks.Add(new MEPConnectionLink
            {
                SwitchMEPId = entranceSwitch.MEPId,
                TargetLightMEPId = kitchenCeilingLight.MEPId
            });
            floor.MEPLinks.Add(new MEPConnectionLink
            {
                SwitchMEPId = entranceSwitch.MEPId,
                TargetLightMEPId = livingCeilingLight.MEPId
            });
        }

        private static void PopulateStarterAtticFloor(FloorData attic)
        {
            Vector2 p0 = new(-3.0f, -2.0f);
            Vector2 p1 = new(0.5f, -2.0f);
            Vector2 p2 = new(3.0f, -2.0f);
            Vector2 p3 = new(3.0f, 2.0f);
            Vector2 p4 = new(0.5f, 2.0f);
            Vector2 p5 = new(-3.0f, 2.0f);

            const float maxGableHeight = 3.45f;

            attic.Walls.Add(new WallData { StartPoint = p0, EndPoint = p1, Thickness = 0.24f, BaseHeight = maxGableHeight, IsLoadBearing = true, ClipByRoofPlanes = true });
            attic.Walls.Add(new WallData { StartPoint = p1, EndPoint = p2, Thickness = 0.24f, BaseHeight = maxGableHeight, IsLoadBearing = true, ClipByRoofPlanes = true });

            var eastGableWall = new WallData { StartPoint = p2, EndPoint = p3, Thickness = 0.24f, BaseHeight = maxGableHeight, IsLoadBearing = true, ClipByRoofPlanes = true };
            eastGableWall.Openings.Add(new OpeningData
            {
                Type = OpeningType.WindowStandard,
                DistanceFromWallStart = 2.0f,
                Width = 1.10f,
                Height = 1.30f,
                SillHeight = 0.85f
            });
            attic.Walls.Add(eastGableWall);

            attic.Walls.Add(new WallData { StartPoint = p3, EndPoint = p4, Thickness = 0.24f, BaseHeight = maxGableHeight, IsLoadBearing = true, ClipByRoofPlanes = true });
            attic.Walls.Add(new WallData { StartPoint = p4, EndPoint = p5, Thickness = 0.24f, BaseHeight = maxGableHeight, IsLoadBearing = true, ClipByRoofPlanes = true });

            var westGableWall = new WallData { StartPoint = p5, EndPoint = p0, Thickness = 0.24f, BaseHeight = maxGableHeight, IsLoadBearing = true, ClipByRoofPlanes = true };
            westGableWall.Openings.Add(new OpeningData
            {
                Type = OpeningType.WindowStandard,
                DistanceFromWallStart = 2.0f,
                Width = 1.10f,
                Height = 1.30f,
                SillHeight = 0.85f
            });
            attic.Walls.Add(westGableWall);

            var atticPartition = new WallData { StartPoint = p1, EndPoint = p4, Thickness = 0.12f, BaseHeight = maxGableHeight, IsLoadBearing = false, ClipByRoofPlanes = true };
            atticPartition.Openings.Add(new OpeningData
            {
                Type = OpeningType.DoorSingle,
                DistanceFromWallStart = 2.0f,
                Width = 0.85f,
                Height = 2.00f,
                SillHeight = 0.0f
            });
            attic.Walls.Add(atticPartition);

            attic.RoofPlanes = ProceduralRoofMeshBuilder.CreateGableRoofForWalls(
                attic.Walls,
                kneeWallHeight: attic.KneeWallHeight,
                pitchAngleDegrees: 38.0f,
                ridgeAlongXAxis: true);
        }

        public async void SaveCurrentProjectAsync()
        {
            await PersistenceService.SaveProjectAsync(CurrentProject, DefaultSaveFilePath);
        }

        public async void LoadCurrentProjectAsync()
        {
            if (!File.Exists(DefaultSaveFilePath))
            {
                Debug.LogWarning($"[Sprint1ProjectBootstrapper] Brak zapisanego pliku projektu pod: {DefaultSaveFilePath}");
                return;
            }

            CurrentProject = await PersistenceService.LoadProjectAsync(DefaultSaveFilePath);
            if (CurrentProject.Floors.Count == 0)
            {
                CurrentProject.Floors.Add(new FloorData { Name = "Parter" });
            }

            CommandHistory.Clear();
            sceneRebuilder.RebuildAllFloors(CurrentProject);
            environmentController.RefreshInteriorReflectionProbe();
            SelectActiveFloorByIndex(0);
        }
    }
}
