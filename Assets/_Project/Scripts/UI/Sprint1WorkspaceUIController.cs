using System;
using System.Collections.Generic;
using HomeBuilder.Assets;
using HomeBuilder.Core;
using HomeBuilder.Core.Commands;
using HomeBuilder.Data;
using HomeBuilder.Floorplan;
using HomeBuilder.Geometry;
using HomeBuilder.Tools;
using HomeBuilder.Walkthrough;
using UnityEngine;
using UnityEngine.UIElements;

namespace HomeBuilder.UI
{
    /// <summary>
    /// Complete UI Toolkit Controller (MVVM Binder) for Sprints 1–5.
    /// Binds Top Toolbar, Floor Level Switcher, Wall & Opening Tools,
    /// Kitchen/Furniture Catalog, MEP Installations & Switch-to-Light Linking,
    /// 2D Wall Elevation View, Attic Roof Configurator, and HDRP Real-Time Walkthrough FPP Mode.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class Sprint1WorkspaceUIController : MonoBehaviour
    {
        private UIDocument _uiDocument;
        private CommandHistoryManager _commandHistory;
        private WallDrawingTool _wallTool;
        private WallOpeningPlacementTool _openingTool;
        private FurniturePlacementTool _furnitureTool;
        private MEPInstallationTool _mepTool;
        private BlueprintCalibrationTool _calibrationTool;
        private BlueprintCalibrationController _blueprintController;
        private CadViewportCameraController _cameraController;
        private FloorSceneRebuilder _sceneRebuilder;
        private WalkthroughModeController _walkthroughController;

        private Func<ProjectData> _getProject;
        private Func<FloorData> _getActiveFloor;
        private Action<int> _onSelectFloorIndex;
        private Action _onSaveRequested;
        private Action _onLoadRequested;

        private bool _roofSlabsVisible = true;

        // UI Elements
        private VisualElement _leftInspectorPanel;
        private Button _btnUndo;
        private Button _btnRedo;
        private Button _btnToolDrawWall;
        private Button _btnToolPlaceWindow;
        private Button _btnToolPlaceDoor;
        private Button _btnToolPlaceFurniture;
        private Button _btnToolPlaceMEP;
        private Button _btnToolLinkMEP;
        private Button _btnToolSelect;
        private Button _btnFloorGround;
        private Button _btnFloorAttic;
        private Button _btnToggleRoofMesh;
        private Button _btnToggle2D3D;
        private Button _btnToggleWalkthrough;

        private Slider _sldTimeOfDay;
        private FloatField _fltMEPElevation;
        private TextField _txtCustomGltfPath;
        private TextField _txtBlueprintPath;
        private FloatField _fltCalibrationMeters;
        private FloatField _fltWallThickness;
        private FloatField _fltWallHeight;
        private FloatField _fltOpeningWidth;
        private FloatField _fltOpeningHeight;
        private FloatField _fltOpeningSill;
        private FloatField _fltKneeWallHeight;
        private FloatField _fltRoofPitch;
        private Button _btnDeleteSelectedWall;
        private Label _lblRoomsSummary;
        private Label _lblLiveTelemetry;
        private Label _lblHistoryStatus;
        private Label _lblWalkthroughCrosshair;
        private Label _lblWalkthroughPrompt;

        public void Initialize(
            CommandHistoryManager commandHistory,
            WallDrawingTool wallTool,
            WallOpeningPlacementTool openingTool,
            FurniturePlacementTool furnitureTool,
            MEPInstallationTool mepTool,
            BlueprintCalibrationTool calibrationTool,
            BlueprintCalibrationController blueprintController,
            CadViewportCameraController cameraController,
            FloorSceneRebuilder sceneRebuilder,
            WalkthroughModeController walkthroughController,
            Func<ProjectData> getProject,
            Func<FloorData> getActiveFloor,
            Action<int> onSelectFloorIndex,
            Action onSaveRequested,
            Action onLoadRequested)
        {
            _commandHistory = commandHistory;
            _wallTool = wallTool;
            _openingTool = openingTool;
            _furnitureTool = furnitureTool;
            _mepTool = mepTool;
            _calibrationTool = calibrationTool;
            _blueprintController = blueprintController;
            _cameraController = cameraController;
            _sceneRebuilder = sceneRebuilder;
            _walkthroughController = walkthroughController;
            _getProject = getProject;
            _getActiveFloor = getActiveFloor;
            _onSelectFloorIndex = onSelectFloorIndex;
            _onSaveRequested = onSaveRequested;
            _onLoadRequested = onLoadRequested;

            _uiDocument = GetComponent<UIDocument>();
            BuildOrBindVisualTree();
            SubscribeDomainEvents();
            RefreshUndoRedoButtons();
        }

        private void BuildOrBindVisualTree()
        {
            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null) return;

            if (root.Q<VisualElement>("RootContainer") == null)
            {
                BuildFallbackRuntimeUI(root);
            }

            _leftInspectorPanel = root.Q<VisualElement>("TopDropdownContainer") ?? root.Q<VisualElement>("LeftInspectorPanel");
            _lblWalkthroughCrosshair = root.Q<Label>("LblWalkthroughCrosshair");
            _lblWalkthroughPrompt = root.Q<Label>("LblWalkthroughPrompt");

            BindTopNavDropdown(root, "BtnNavProject", "PanelNavProject");
            BindTopNavDropdown(root, "BtnNavWalls", "PanelNavWalls");
            BindTopNavDropdown(root, "BtnNavOpenings", "PanelNavOpenings");
            BindTopNavDropdown(root, "BtnNavFurniture", "PanelNavFurniture");
            BindTopNavDropdown(root, "BtnNavMEP", "PanelNavMEP");
            BindTopNavDropdown(root, "BtnNavFloors", "PanelNavFloors");
            BindTopNavDropdown(root, "BtnNavEnvironment", "PanelNavEnvironment");

            root.Q<Button>("BtnSaveProject")?.RegisterCallback<ClickEvent>(_ => _onSaveRequested?.Invoke());
            root.Q<Button>("BtnLoadProject")?.RegisterCallback<ClickEvent>(_ => _onLoadRequested?.Invoke());

            _btnUndo = root.Q<Button>("BtnUndo");
            _btnRedo = root.Q<Button>("BtnRedo");
            _btnUndo?.RegisterCallback<ClickEvent>(_ => _commandHistory?.Undo());
            _btnRedo?.RegisterCallback<ClickEvent>(_ => _commandHistory?.Redo());

            _btnToolDrawWall = root.Q<Button>("BtnToolDrawWall");
            _btnToolPlaceWindow = root.Q<Button>("BtnToolPlaceWindow");
            _btnToolPlaceDoor = root.Q<Button>("BtnToolPlaceDoor");
            _btnToolPlaceFurniture = root.Q<Button>("BtnToolPlaceFurniture");
            _btnToolPlaceMEP = root.Q<Button>("BtnToolPlaceMEP");
            _btnToolLinkMEP = root.Q<Button>("BtnToolLinkMEP");
            _btnToolSelect = root.Q<Button>("BtnToolSelect");

            _btnToolDrawWall?.RegisterCallback<ClickEvent>(_ => SwitchTool(ActiveDesignTool.DrawWalls));
            _btnToolPlaceWindow?.RegisterCallback<ClickEvent>(_ => SwitchTool(ActiveDesignTool.PlaceWindow));
            _btnToolPlaceDoor?.RegisterCallback<ClickEvent>(_ => SwitchTool(ActiveDesignTool.PlaceDoor));
            _btnToolPlaceFurniture?.RegisterCallback<ClickEvent>(_ => SwitchTool(ActiveDesignTool.PlaceFurniture));
            _btnToolPlaceMEP?.RegisterCallback<ClickEvent>(_ => SwitchTool(ActiveDesignTool.PlaceMEPInstallation));
            _btnToolLinkMEP?.RegisterCallback<ClickEvent>(_ => SwitchTool(ActiveDesignTool.LinkMEPSwitchToLight));
            _btnToolSelect?.RegisterCallback<ClickEvent>(_ => SwitchTool(ActiveDesignTool.SelectAndInspect));

            _btnFloorGround = root.Q<Button>("BtnFloorGround");
            _btnFloorAttic = root.Q<Button>("BtnFloorAttic");
            _btnFloorGround?.RegisterCallback<ClickEvent>(_ => _onSelectFloorIndex?.Invoke(0));
            _btnFloorAttic?.RegisterCallback<ClickEvent>(_ => _onSelectFloorIndex?.Invoke(1));

            _btnToggleRoofMesh = root.Q<Button>("BtnToggleRoofMesh");
            _btnToggleRoofMesh?.RegisterCallback<ClickEvent>(_ =>
            {
                _roofSlabsVisible = !_roofSlabsVisible;
                _sceneRebuilder?.SetRoofSlabsVisible(_roofSlabsVisible);
                _btnToggleRoofMesh.text = _roofSlabsVisible ? "Dach 3D: Wł." : "Dach 3D: Przekrój";
            });

            _btnToggle2D3D = root.Q<Button>("BtnToggle2D3D");
            _btnToggle2D3D?.RegisterCallback<ClickEvent>(_ =>
            {
                if (_cameraController == null) return;
                bool next2D = !_cameraController.Is2DOrthographicMode;
                _cameraController.Set2DOrthographicMode(next2D);
                _btnToggle2D3D.text = next2D ? "Widok: 2D Rzut (Ortho)" : "Widok: 3D Orbit";
            });

            // Sprint 5: Walkthrough FPP & Time-of-Day Controls
            _btnToggleWalkthrough = root.Q<Button>("BtnToggleWalkthrough");
            _btnToggleWalkthrough?.RegisterCallback<ClickEvent>(_ => TriggerWalkthroughToggle());
            root.Q<Button>("BtnStartWalkthroughSide")?.RegisterCallback<ClickEvent>(_ => TriggerWalkthroughToggle());

            _sldTimeOfDay = root.Q<Slider>("SldTimeOfDay");
            _sldTimeOfDay?.RegisterValueChangedCallback(evt =>
            {
                _walkthroughController?.SetTimeOfDay(evt.newValue);
                if (_lblLiveTelemetry != null)
                {
                    int hours = Mathf.FloorToInt(evt.newValue);
                    int mins = Mathf.FloorToInt((evt.newValue - hours) * 60f);
                    _lblLiveTelemetry.text = $"Oświetlenie HDRP | Pora dnia: {hours:D2}:{mins:D2}";
                }
            });

            // MEP Installation Controls (Sprint 4)
            _fltMEPElevation = root.Q<FloatField>("FltMEPElevation");
            BindMEPButton(root, "BtnMEPSocket230", MEPCategory.ElectricalSocket230V, 0.30f);
            BindMEPButton(root, "BtnMEPSocketCountertop", MEPCategory.ElectricalSocket230V, 1.10f);
            BindMEPButton(root, "BtnMEPSocket400", MEPCategory.ElectricalSocket400V, 0.30f);
            BindMEPButton(root, "BtnMEPSwitchSingle", MEPCategory.LightSwitchSingle, 1.10f);
            BindMEPButton(root, "BtnMEPCeilingLight", MEPCategory.CeilingLightPoint, 2.73f);
            BindMEPButton(root, "BtnMEPWaterCold", MEPCategory.WaterSupplyCold, 0.55f);
            BindMEPButton(root, "BtnMEPWaterHot", MEPCategory.WaterSupplyHot, 0.55f);
            BindMEPButton(root, "BtnMEPSewage50", MEPCategory.SewageDrain50mm, 0.45f);

            _fltMEPElevation?.RegisterValueChangedCallback(evt =>
            {
                if (_mepTool != null)
                {
                    _mepTool.PresetElevationAboveFloor = Mathf.Clamp(evt.newValue, 0.05f, 3.40f);
                }
            });

            root.Q<Button>("BtnEnterWallElevation")?.RegisterCallback<ClickEvent>(_ =>
            {
                _mepTool?.EnterElevationViewForHoveredWall();
            });

            // Furniture & Kitchen Catalog Controls (Sprint 3)
            BindCatalogButton(root, "BtnCatalogCabinetBase60", BuiltInFurnitureCatalog.IdCabinetBase60);
            BindCatalogButton(root, "BtnCatalogSink80", BuiltInFurnitureCatalog.IdCabinetSink80);
            BindCatalogButton(root, "BtnCatalogDishwasher60", BuiltInFurnitureCatalog.IdApplianceDishwasher60);
            BindCatalogButton(root, "BtnCatalogCabinetWall60", BuiltInFurnitureCatalog.IdCabinetWall60);
            BindCatalogButton(root, "BtnCatalogFridge60", BuiltInFurnitureCatalog.IdApplianceFridge60);
            BindCatalogButton(root, "BtnCatalogSofa220", BuiltInFurnitureCatalog.IdFurnitureSofa220);
            BindCatalogButton(root, "BtnCatalogTable160", BuiltInFurnitureCatalog.IdFurnitureTable160);

            _txtCustomGltfPath = root.Q<TextField>("TxtCustomGltfPath");
            root.Q<Button>("BtnImportCustomGltf")?.RegisterCallback<ClickEvent>(_ =>
            {
                if (_furnitureTool == null || _txtCustomGltfPath == null) return;
                SwitchTool(ActiveDesignTool.PlaceFurniture);
                _furnitureTool.ImportAndSelectCustomGltfAsync(_txtCustomGltfPath.value, _getProject?.Invoke());
            });

            // Wall Controls
            _fltWallThickness = root.Q<FloatField>("FltWallThickness");
            _fltWallHeight = root.Q<FloatField>("FltWallHeight");

            root.Q<Button>("BtnPresetLoadBearing")?.RegisterCallback<ClickEvent>(_ =>
            {
                ApplyWallDimensionsFromUI(0.24f, _fltWallHeight != null ? _fltWallHeight.value : 2.75f, true);
            });

            root.Q<Button>("BtnPresetPartition")?.RegisterCallback<ClickEvent>(_ =>
            {
                ApplyWallDimensionsFromUI(0.12f, _fltWallHeight != null ? _fltWallHeight.value : 2.75f, false);
            });

            _fltWallThickness?.RegisterValueChangedCallback(evt =>
            {
                ApplyWallDimensionsFromUI(evt.newValue, _fltWallHeight != null ? _fltWallHeight.value : 2.75f, evt.newValue >= 0.20f);
            });

            _fltWallHeight?.RegisterValueChangedCallback(evt =>
            {
                float t = _fltWallThickness != null ? _fltWallThickness.value : 0.24f;
                ApplyWallDimensionsFromUI(t, evt.newValue, t >= 0.20f);
            });

            _btnDeleteSelectedWall = root.Q<Button>("BtnDeleteSelectedWall");
            _btnDeleteSelectedWall?.RegisterCallback<ClickEvent>(_ => _wallTool?.DeleteSelectedWall());

            // Opening Controls (Sprint 2)
            _fltOpeningWidth = root.Q<FloatField>("FltOpeningWidth");
            _fltOpeningHeight = root.Q<FloatField>("FltOpeningHeight");
            _fltOpeningSill = root.Q<FloatField>("FltOpeningSill");

            root.Q<Button>("BtnPresetWindow120")?.RegisterCallback<ClickEvent>(_ => SwitchTool(ActiveDesignTool.PlaceWindow));
            root.Q<Button>("BtnPresetDoor90")?.RegisterCallback<ClickEvent>(_ => SwitchTool(ActiveDesignTool.PlaceDoor));

            _fltOpeningWidth?.RegisterValueChangedCallback(evt =>
            {
                if (_openingTool != null) _openingTool.CurrentWidth = Mathf.Clamp(evt.newValue, 0.40f, 5.0f);
            });
            _fltOpeningHeight?.RegisterValueChangedCallback(evt =>
            {
                if (_openingTool != null) _openingTool.CurrentHeight = Mathf.Clamp(evt.newValue, 0.40f, 3.5f);
            });
            _fltOpeningSill?.RegisterValueChangedCallback(evt =>
            {
                if (_openingTool != null) _openingTool.CurrentSillHeight = Mathf.Clamp(evt.newValue, 0.0f, 2.5f);
            });

            // Attic & Roof Controls (Sprint 2)
            _fltKneeWallHeight = root.Q<FloatField>("FltKneeWallHeight");
            _fltRoofPitch = root.Q<FloatField>("FltRoofPitch");
            root.Q<Button>("BtnApplyGableRoof")?.RegisterCallback<ClickEvent>(_ => ApplyAtticGableRoofFromUI());

            // Blueprint Controls
            _txtBlueprintPath = root.Q<TextField>("TxtBlueprintPath");
            root.Q<Button>("BtnLoadBlueprint")?.RegisterCallback<ClickEvent>(_ =>
            {
                FloorData floor = _getActiveFloor?.Invoke();
                if (floor == null || _blueprintController == null || _txtBlueprintPath == null) return;
                _blueprintController.LoadBlueprintImageFromDisk(_txtBlueprintPath.value, floor.Blueprint, floor.ElevationY);
            });

            _fltCalibrationMeters = root.Q<FloatField>("FltCalibrationMeters");
            root.Q<Button>("BtnConfirmCalibration")?.RegisterCallback<ClickEvent>(_ =>
            {
                if (_calibrationTool != null && _fltCalibrationMeters != null)
                {
                    _calibrationTool.ConfirmRealDistanceMeters(_fltCalibrationMeters.value);
                }
            });

            _lblRoomsSummary = root.Q<Label>("LblRoomsSummary");
            _lblLiveTelemetry = root.Q<Label>("LblLiveTelemetry");
            _lblHistoryStatus = root.Q<Label>("LblHistoryStatus");
        }

        private static void BindTopNavDropdown(VisualElement root, string buttonName, string panelName)
        {
            Button btn = root.Q<Button>(buttonName);
            VisualElement panel = root.Q<VisualElement>(panelName);
            if (btn == null || panel == null) return;

            btn.RegisterCallback<ClickEvent>(_ =>
            {
                bool isCurrentlyOpen = panel.style.display == DisplayStyle.Flex;
                root.Query<VisualElement>(className: "nav-dropdown-panel").ForEach(p => p.style.display = DisplayStyle.None);
                panel.style.display = isCurrentlyOpen ? DisplayStyle.None : DisplayStyle.Flex;
            });
        }

        private void TriggerWalkthroughToggle()
        {
            if (_walkthroughController == null) return;
            FloorData floor = _getActiveFloor?.Invoke();
            float spawnY = floor != null ? floor.ElevationY + 0.08f : 0.08f;
            _walkthroughController.ToggleWalkthroughMode(new Vector3(1.1f, spawnY, -0.8f));
        }

        private void BindMEPButton(VisualElement root, string buttonName, MEPCategory category, float elevationPreset)
        {
            root.Q<Button>(buttonName)?.RegisterCallback<ClickEvent>(_ =>
            {
                if (_mepTool != null)
                {
                    _mepTool.SelectMEPCategory(category);
                    _mepTool.PresetElevationAboveFloor = elevationPreset;
                    _fltMEPElevation?.SetValueWithoutNotify(elevationPreset);
                }
                SwitchTool(ActiveDesignTool.PlaceMEPInstallation);
            });
        }

        private void BindCatalogButton(VisualElement root, string buttonName, string catalogAssetId)
        {
            root.Q<Button>(buttonName)?.RegisterCallback<ClickEvent>(_ =>
            {
                _furnitureTool?.SelectBuiltInCatalogItem(catalogAssetId);
                SwitchTool(ActiveDesignTool.PlaceFurniture);
            });
        }

        private void ApplyAtticGableRoofFromUI()
        {
            FloorData floor = _getActiveFloor?.Invoke();
            if (floor == null || _commandHistory == null || _sceneRebuilder == null) return;

            float kneeH = _fltKneeWallHeight != null ? Mathf.Clamp(_fltKneeWallHeight.value, 0.30f, 2.50f) : 1.10f;
            float pitch = _fltRoofPitch != null ? Mathf.Clamp(_fltRoofPitch.value, 10f, 65f) : 38.0f;

            List<RoofPlaneData> newRoofPlanes = ProceduralRoofMeshBuilder.CreateGableRoofForWalls(
                floor.Walls,
                kneeH,
                pitch,
                ridgeAlongXAxis: true);

            var cmd = new ConfigureAtticRoofCommand(
                floor,
                newIsAttic: true,
                newKneeHeight: kneeH,
                newRoofPlanes: newRoofPlanes,
                f => _sceneRebuilder.RebuildFloorGeometry(f));

            _commandHistory.ExecuteCommand(cmd);
        }

        private void ApplyWallDimensionsFromUI(float thickness, float height, bool isLoadBearing)
        {
            thickness = Mathf.Clamp(thickness, 0.06f, 1.0f);
            height = Mathf.Clamp(height, 0.50f, 6.0f);

            if (_fltWallThickness != null) _fltWallThickness.SetValueWithoutNotify(thickness);
            if (_fltWallHeight != null) _fltWallHeight.SetValueWithoutNotify(height);

            if (_wallTool == null) return;
            _wallTool.CurrentWallThickness = thickness;
            _wallTool.CurrentWallHeight = height;
            _wallTool.CurrentWallIsLoadBearing = isLoadBearing;

            if (_wallTool.SelectedWall != null)
            {
                _wallTool.UpdateSelectedWallDimensions(thickness, height);
            }
        }

        public void SwitchTool(ActiveDesignTool tool)
        {
            _wallTool?.SetActiveTool(tool);
            _calibrationTool?.ActivateCalibrationTool(tool == ActiveDesignTool.CalibrateBlueprint2Point);
            _furnitureTool?.ActivateFurnitureTool(tool == ActiveDesignTool.PlaceFurniture);
            _mepTool?.ActivateMEPPlacementTool(tool == ActiveDesignTool.PlaceMEPInstallation);
            if (tool == ActiveDesignTool.LinkMEPSwitchToLight)
            {
                _mepTool?.ActivateSwitchToLightLinkingMode(true);
            }

            if (tool == ActiveDesignTool.PlaceWindow)
            {
                _openingTool?.ActivateOpeningTool(true, OpeningType.WindowStandard);
                SyncOpeningFieldsFromTool();
            }
            else if (tool == ActiveDesignTool.PlaceDoor)
            {
                _openingTool?.ActivateOpeningTool(true, OpeningType.DoorSingle);
                SyncOpeningFieldsFromTool();
            }
            else
            {
                _openingTool?.ActivateOpeningTool(false);
            }

            if (_lblLiveTelemetry != null)
            {
                _lblLiveTelemetry.text = tool switch
                {
                    ActiveDesignTool.DrawWalls => "Narzędzie: Rysowanie Ścian | LPM: postaw punkt, PPM/Esc: zakończ łańcuch, Shift: blokada 45°",
                    ActiveDesignTool.PlaceWindow => "Narzędzie: Wstawianie Okna | Wskaż ścianę i kliknij LPM (PPM na oknie: usuń)",
                    ActiveDesignTool.PlaceDoor => "Narzędzie: Wstawianie Drzwi | Wskaż ścianę i kliknij LPM (R: zawiasy, PPM: usuń)",
                    ActiveDesignTool.PlaceFurniture => "Narzędzie: Meblowanie i Kuchnia | Wskaż ścianę (Back-to-Wall + Edge-to-Edge) lub podłogę (R: obróć, PPM: usuń)",
                    ActiveDesignTool.PlaceMEPInstallation => "Narzędzie: Instalacje MEP | Wskaż ścianę/sufit (Linie wymiarowe do narożnika i podłogi | V: Widok 2D Ściany | PPM: usuń)",
                    ActiveDesignTool.LinkMEPSwitchToLight => "Narzędzie: Łączenie Włącznika z Lampą | Kliknij Włącznik na ścianie, a następnie Punkt Świetlny",
                    ActiveDesignTool.SelectAndInspect => "Narzędzie: Wybierz i Edytuj | Kliknij ścianę w widoku 3D, aby zmienić parametry lub usunąć (Delete)",
                    ActiveDesignTool.CalibrateBlueprint2Point => "Narzędzie: Kalibracja Rzutu 2D | Kliknij Punkt 1 na wymiarze rzutu, a następnie Punkt 2",
                    _ => string.Empty
                };
            }
        }

        private void SyncOpeningFieldsFromTool()
        {
            if (_openingTool == null) return;
            _fltOpeningWidth?.SetValueWithoutNotify(_openingTool.CurrentWidth);
            _fltOpeningHeight?.SetValueWithoutNotify(_openingTool.CurrentHeight);
            _fltOpeningSill?.SetValueWithoutNotify(_openingTool.CurrentSillHeight);
        }

        private void SubscribeDomainEvents()
        {
            if (_commandHistory != null)
            {
                _commandHistory.OnHistoryChanged += RefreshUndoRedoButtons;
            }

            if (_wallTool != null)
            {
                _wallTool.OnPreviewTelemetryUpdated += OnWallPreviewTelemetry;
                _wallTool.OnSelectedWallChanged += OnSelectedWallChanged;
            }

            if (_openingTool != null)
            {
                _openingTool.OnOpeningTelemetryChanged += OnOpeningPreviewTelemetry;
            }

            if (_furnitureTool != null)
            {
                _furnitureTool.OnFurnitureTelemetryChanged += OnFurniturePreviewTelemetry;
            }

            if (_mepTool != null)
            {
                _mepTool.OnMEPTelemetryChanged += OnMEPPreviewTelemetry;
            }

            if (_calibrationTool != null)
            {
                _calibrationTool.OnCalibrationStateChanged += OnCalibrationStateChanged;
            }

            if (_sceneRebuilder != null)
            {
                _sceneRebuilder.OnRoomsRecalculated += OnRoomsRecalculated;
            }

            if (_walkthroughController != null)
            {
                _walkthroughController.OnViewModeChanged += OnWalkthroughModeChanged;
                _walkthroughController.OnHoverPromptChanged += OnWalkthroughHoverPromptChanged;
            }
        }

        private void OnWalkthroughModeChanged(ApplicationViewMode mode)
        {
            bool isWalkthrough = (mode == ApplicationViewMode.WalkthroughFPP);

            if (_leftInspectorPanel != null)
            {
                _leftInspectorPanel.style.display = isWalkthrough ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_lblWalkthroughCrosshair != null)
            {
                _lblWalkthroughCrosshair.style.display = isWalkthrough ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_btnToggleWalkthrough != null)
            {
                _btnToggleWalkthrough.text = isWalkthrough ? "Wyjdź ze Spaceru (Tab/Esc)" : "Spacer 3D FPP (Tab)";
            }

            if (isWalkthrough)
            {
                // Disable CAD placement tools and show all floors/roofs during Walkthrough!
                SwitchTool(ActiveDesignTool.SelectAndInspect);
                _sceneRebuilder?.SetUpperFloorsVisibleAbove(10, _getProject?.Invoke(), true);
                _sceneRebuilder?.SetRoofSlabsVisible(true);

                if (_lblLiveTelemetry != null)
                {
                    _lblLiveTelemetry.text =
                        "TRYB SPACERU FPP (HDRP Real-Time) | Ruch: WASD + Shift | Interakcja (Włączniki / Drzwi / Szafki): LPM lub E | Piętro: [1] Parter / [2] Poddasze | Wyjście: Tab / Esc";
                }
            }
            else
            {
                FloorData floor = _getActiveFloor?.Invoke();
                if (floor != null)
                {
                    _onSelectFloorIndex?.Invoke(floor.LevelIndex);
                }
                SwitchTool(ActiveDesignTool.DrawWalls);
            }
        }

        private void OnWalkthroughHoverPromptChanged(string prompt)
        {
            if (_lblWalkthroughPrompt != null)
            {
                _lblWalkthroughPrompt.text = prompt;
            }
        }

        private void RefreshUndoRedoButtons()
        {
            if (_commandHistory == null) return;
            _btnUndo?.SetEnabled(_commandHistory.CanUndo);
            _btnRedo?.SetEnabled(_commandHistory.CanRedo);
            if (_lblHistoryStatus != null)
            {
                _lblHistoryStatus.text = $"Undo: {(_commandHistory.CanUndo ? "Tak" : "Nie")} | Redo: {(_commandHistory.CanRedo ? "Tak" : "Nie")}";
            }
        }

        private void OnWallPreviewTelemetry(WallPreviewTelemetry t)
        {
            if (_lblLiveTelemetry == null || !t.IsDrawing) return;

            string snapTag = t.IsSnappedToNode ? " [PRZYCIĄGNIĘTO DO WĘZŁA]" : "";
            string angleTag = t.IsAngleLocked ? " [BLOKADA KĄTA 45°]" : "";
            _lblLiveTelemetry.text =
                $"Rysowanie ściany: Długość = {t.LengthMeters:F2} m ({t.LengthMeters * 100f:F0} cm) | Kąt = {t.AngleDegrees:F1}°{snapTag}{angleTag}";
        }

        private void OnOpeningPreviewTelemetry(OpeningPreviewTelemetry t)
        {
            if (_lblLiveTelemetry == null) return;
            _lblLiveTelemetry.text = t.StatusMessage;
        }

        private void OnFurniturePreviewTelemetry(FurnitureSnapTelemetry t)
        {
            if (_lblLiveTelemetry == null) return;
            _lblLiveTelemetry.text = t.StatusMessage;
        }

        private void OnMEPPreviewTelemetry(MEPPlacementTelemetry t)
        {
            if (_lblLiveTelemetry == null) return;
            _lblLiveTelemetry.text = t.StatusMessage;
        }

        private void OnSelectedWallChanged(WallData selected)
        {
            if (selected == null)
            {
                _btnDeleteSelectedWall?.SetEnabled(false);
                return;
            }

            _btnDeleteSelectedWall?.SetEnabled(true);
            _fltWallThickness?.SetValueWithoutNotify(selected.Thickness);
            _fltWallHeight?.SetValueWithoutNotify(selected.BaseHeight);

            float len = Vector2.Distance(selected.StartPoint.ToVector2(), selected.EndPoint.ToVector2());
            if (_lblLiveTelemetry != null)
            {
                _lblLiveTelemetry.text =
                    $"Wybrano ścianę [{selected.WallId.ToString()[..8]}] | Długość: {len:F2} m | Grubość: {selected.Thickness * 100f:F0} cm | Wysokość: {selected.BaseHeight:F2} m | MEP: {selected.MountedMEPItems.Count}";
            }
        }

        private void OnCalibrationStateChanged(CalibrationStep step, float spanMeters)
        {
            if (_lblLiveTelemetry == null) return;
            _lblLiveTelemetry.text = step switch
            {
                CalibrationStep.WaitingForFirstPoint => "Kalibracja 2-pkt: Kliknij LPM w pierwszym punkcie linii wymiarowej na rzucie",
                CalibrationStep.WaitingForSecondPoint => $"Kalibracja 2-pkt: Kliknij LPM w drugim punkcie (Aktualny rozstaw: {spanMeters:F2} jedn.)",
                CalibrationStep.ReadyToConfirmDistance => $"Kalibracja 2-pkt: Zmierzono odcinek ({spanMeters:F2} jedn.). Wpisz wymiar rzeczywisty (m) i zatwierdź",
                _ => _lblLiveTelemetry.text
            };
        }

        private void OnRoomsRecalculated(FloorData floor, int roomCount, float totalAreaM2)
        {
            FloorData active = _getActiveFloor?.Invoke();
            if (active != null && active.FloorId == floor.FloorId && _lblRoomsSummary != null)
            {
                _lblRoomsSummary.text = $"{floor.Name}: {roomCount} pom. | {totalAreaM2:F2} m²";
            }
        }

        private static void BuildFallbackRuntimeUI(VisualElement root)
        {
            var container = new VisualElement { name = "RootContainer" };
            container.style.flexGrow = 1f;
            container.style.justifyContent = Justify.SpaceBetween;
            container.pickingMode = PickingMode.Ignore;

            var topBar = new VisualElement { name = "TopToolbar" };
            topBar.style.flexDirection = FlexDirection.Row;
            topBar.style.height = 42f;
            topBar.style.backgroundColor = new Color(0.1f, 0.11f, 0.13f, 0.94f);
            topBar.style.alignItems = Align.Center;
            topBar.style.paddingLeft = 10f;

            topBar.Add(new Button { name = "BtnSaveProject", text = "Zapisz JSON" });
            topBar.Add(new Button { name = "BtnLoadProject", text = "Wczytaj JSON" });
            topBar.Add(new Button { name = "BtnUndo", text = "Cofnij (Ctrl+Z)" });
            topBar.Add(new Button { name = "BtnRedo", text = "Ponów (Ctrl+Y)" });
            topBar.Add(new Button { name = "BtnToolDrawWall", text = "Rysuj Ściany" });
            topBar.Add(new Button { name = "BtnToolPlaceWindow", text = "Okno" });
            topBar.Add(new Button { name = "BtnToolPlaceDoor", text = "Drzwi" });
            topBar.Add(new Button { name = "BtnToolPlaceFurniture", text = "Mebluj / Kuchnia" });
            topBar.Add(new Button { name = "BtnToolPlaceMEP", text = "Instalacje MEP" });
            topBar.Add(new Button { name = "BtnToolLinkMEP", text = "Połącz Włącznik->Lampa" });
            topBar.Add(new Button { name = "BtnToolSelect", text = "Edytuj" });
            topBar.Add(new Button { name = "BtnFloorGround", text = "Parter" });
            topBar.Add(new Button { name = "BtnFloorAttic", text = "Poddasze" });
            topBar.Add(new Button { name = "BtnToggleRoofMesh", text = "Dach 3D: Wł." });
            topBar.Add(new Button { name = "BtnToggle2D3D", text = "Widok: 3D Orbit" });
            topBar.Add(new Button { name = "BtnToggleWalkthrough", text = "Spacer 3D FPP (Tab)" });

            var middle = new VisualElement { name = "MiddleArea" };
            middle.style.flexGrow = 1f;
            middle.style.flexDirection = FlexDirection.Row;
            middle.pickingMode = PickingMode.Ignore;

            var leftPanel = new ScrollView { name = "LeftInspectorPanel" };
            leftPanel.style.width = 290f;
            leftPanel.style.backgroundColor = new Color(0.1f, 0.11f, 0.13f, 0.92f);
            leftPanel.style.paddingLeft = 10f;
            leftPanel.Add(new Slider("Pora dnia (0-24h):", 0f, 24f) { name = "SldTimeOfDay", value = 14.5f });
            leftPanel.Add(new Button { name = "BtnStartWalkthroughSide", text = "Wirtualny Spacer FPP (Tab)" });
            leftPanel.Add(new Button { name = "BtnMEPSocket230", text = "Gniazdko 230V (30 cm)" });
            leftPanel.Add(new Button { name = "BtnMEPSocketCountertop", text = "Gniazdko Blat (110 cm)" });
            leftPanel.Add(new Button { name = "BtnMEPSocket400", text = "Gniazdo Siłowe 400V" });
            leftPanel.Add(new Button { name = "BtnMEPSwitchSingle", text = "Włącznik Światła (110 cm)" });
            leftPanel.Add(new Button { name = "BtnMEPCeilingLight", text = "Punkt Świetlny Sufit" });
            leftPanel.Add(new Button { name = "BtnMEPWaterCold", text = "Woda Zimna (55 cm)" });
            leftPanel.Add(new Button { name = "BtnMEPWaterHot", text = "Woda Ciepła (55 cm)" });
            leftPanel.Add(new Button { name = "BtnMEPSewage50", text = "Odpływ Kan. 50mm" });
            leftPanel.Add(new FloatField("Wys. montażu MEP (m):") { name = "FltMEPElevation", value = 0.30f });
            leftPanel.Add(new Button { name = "BtnEnterWallElevation", text = "Widok 2D Rozwinięcia Ściany (V)" });

            leftPanel.Add(new Button { name = "BtnCatalogCabinetBase60", text = "Szafka Dolna 60" });
            leftPanel.Add(new Button { name = "BtnCatalogSink80", text = "Szafka Zlew 80" });
            leftPanel.Add(new Button { name = "BtnCatalogDishwasher60", text = "Zmywarka AGD 60" });
            leftPanel.Add(new Button { name = "BtnCatalogCabinetWall60", text = "Szafka Wisząca 60" });
            leftPanel.Add(new Button { name = "BtnCatalogFridge60", text = "Lodówka 60×200" });
            leftPanel.Add(new Button { name = "BtnCatalogSofa220", text = "Sofa 220 cm" });
            leftPanel.Add(new Button { name = "BtnCatalogTable160", text = "Stół 160×90 cm" });
            leftPanel.Add(new TextField("Plik .glb/.gltf:") { name = "TxtCustomGltfPath" });
            leftPanel.Add(new Button { name = "BtnImportCustomGltf", text = "Importuj model 3D (.glb/.gltf)" });

            leftPanel.Add(new Button { name = "BtnPresetLoadBearing", text = "Ściana Nośna 24 cm" });
            leftPanel.Add(new Button { name = "BtnPresetPartition", text = "Ściana Działowa 12 cm" });
            leftPanel.Add(new FloatField("Grubość ściany (m):") { name = "FltWallThickness", value = 0.24f });
            leftPanel.Add(new FloatField("Wysokość ściany (m):") { name = "FltWallHeight", value = 2.75f });
            leftPanel.Add(new Button { name = "BtnDeleteSelectedWall", text = "Usuń wybraną ścianę (Del)" });

            leftPanel.Add(new Button { name = "BtnPresetWindow120", text = "Okno 120×150" });
            leftPanel.Add(new Button { name = "BtnPresetDoor90", text = "Drzwi 90×205" });
            leftPanel.Add(new FloatField("Szerokość otworu (m):") { name = "FltOpeningWidth", value = 1.20f });
            leftPanel.Add(new FloatField("Wysokość otworu (m):") { name = "FltOpeningHeight", value = 1.50f });
            leftPanel.Add(new FloatField("Wys. parapetu (m):") { name = "FltOpeningSill", value = 0.85f });

            leftPanel.Add(new FloatField("Ścianka kolankowa (m):") { name = "FltKneeWallHeight", value = 1.10f });
            leftPanel.Add(new FloatField("Kąt połaci dachu (°):") { name = "FltRoofPitch", value = 38.0f });
            leftPanel.Add(new Button { name = "BtnApplyGableRoof", text = "Zastosuj Dach i Ścinanie Ścian" });

            leftPanel.Add(new TextField("Ścieżka PNG/JPG:") { name = "TxtBlueprintPath" });
            leftPanel.Add(new Button { name = "BtnLoadBlueprint", text = "Wgraj rzut z dysku" });
            leftPanel.Add(new FloatField("Wymiar rzeczywisty (m):") { name = "FltCalibrationMeters", value = 5.0f });
            leftPanel.Add(new Button { name = "BtnConfirmCalibration", text = "Zatwierdź skalę 2-pkt" });
            leftPanel.Add(new Label("Pomieszczenia: 0 (0.00 m²)") { name = "LblRoomsSummary" });

            middle.Add(leftPanel);

            var hudOverlay = new VisualElement { name = "WalkthroughHudOverlay" };
            hudOverlay.style.flexGrow = 1f;
            hudOverlay.style.alignItems = Align.Center;
            hudOverlay.style.justifyContent = Justify.Center;
            hudOverlay.pickingMode = PickingMode.Ignore;

            var crosshair = new Label("+") { name = "LblWalkthroughCrosshair" };
            crosshair.style.color = Color.white;
            crosshair.style.fontSize = 22f;
            crosshair.style.display = DisplayStyle.None;

            var prompt = new Label("") { name = "LblWalkthroughPrompt" };
            prompt.style.color = new Color(1f, 0.9f, 0.4f);
            prompt.style.fontSize = 15f;

            hudOverlay.Add(crosshair);
            hudOverlay.Add(prompt);
            middle.Add(hudOverlay);

            var bottomBar = new VisualElement { name = "BottomStatusBar" };
            bottomBar.style.height = 28f;
            bottomBar.style.flexDirection = FlexDirection.Row;
            bottomBar.style.justifyContent = Justify.SpaceBetween;
            bottomBar.style.alignItems = Align.Center;
            bottomBar.style.backgroundColor = new Color(0.07f, 0.08f, 0.1f, 0.96f);
            bottomBar.style.paddingLeft = 12f;
            bottomBar.style.paddingRight = 12f;

            bottomBar.Add(new Label("Gotowy") { name = "LblLiveTelemetry" });
            bottomBar.Add(new Label("Historia: 0") { name = "LblHistoryStatus" });

            container.Add(topBar);
            container.Add(middle);
            container.Add(bottomBar);
            root.Add(container);
        }
    }
}
