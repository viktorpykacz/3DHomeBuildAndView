using System;
using System.Collections.Generic;
using HomeBuilder.Assets;
using HomeBuilder.Core;
using HomeBuilder.Core.Commands;
using HomeBuilder.Data;
using HomeBuilder.Geometry;
using HomeBuilder.Snapping;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HomeBuilder.Tools
{
    public struct MEPPlacementTelemetry
    {
        public MEPCategory Category;
        public bool IsValidSurfaceHit;
        public float DistanceToNearestCorner;
        public float ElevationFromFloor;
        public bool HasCollisionWarning;
        public string StatusMessage;
    }

    /// <summary>
    /// Interactive MEP (Electrical, Lighting, Plumbing) placement & Switch-to-Light linking tool (Sprint 4).
    /// Features:
    /// - Wall normal snapping & Y-axis elevation lock (30cm, 55cm, 110cm, 210cm)
    /// - Live 3D horizontal (to nearest corner) & vertical (to floor) dimension LineRenderers
    /// - Collision detection against Appliance Hazard Zones (e.g., behind dishwashers) & Attic Roof slopes
    /// - Interactive Switch -> Light connection linking
    /// - Quick 2D Wall Elevation View alignment (Key V)
    /// </summary>
    public sealed class MEPInstallationTool : MonoBehaviour
    {
        [SerializeField] private Camera viewportCamera;
        [SerializeField] private FloorSceneRebuilder sceneRebuilder;
        [SerializeField] private CadViewportCameraController cameraController;
        [SerializeField] private LayerMask wallLayerMask = ~0;
        [SerializeField] private LayerMask applianceHazardLayerMask = ~0;

        private readonly SnappingEngine _snappingEngine = new();

        private CommandHistoryManager _commandHistory;
        private FloorData _activeFloor;
        private bool _isPlacementActive;
        private bool _isLinkingModeActive;

        public MEPCategory ActiveCategory { get; private set; } = MEPCategory.ElectricalSocket230V;
        public bool LockElevationY { get; set; } = true;
        public float PresetElevationAboveFloor { get; set; } = 0.30f;

        private Guid? _pendingSwitchToLinkId;
        private Vector3 _pendingSwitchWorldPos;

        // Live 3D Dimension Lines & Ghost Preview
        private GameObject _previewFixtureGo;
        private MeshRenderer _previewRenderer;
        private Material _validMat;
        private Material _warningMat;
        private LineRenderer _horizontalCornerDimLine;
        private LineRenderer _verticalFloorDimLine;
        private LineRenderer _linkingRubberBandLine;

        private WallData _lastHoveredWall;
        private Vector3 _lastHoveredWallNormal = Vector3.forward;

        public event Action<MEPPlacementTelemetry> OnMEPTelemetryChanged;

        public void Initialize(
            Camera cam,
            FloorSceneRebuilder rebuilder,
            CadViewportCameraController camController,
            CommandHistoryManager commandHistory,
            FloorData initialFloor)
        {
            viewportCamera = cam;
            sceneRebuilder = rebuilder;
            cameraController = camController;
            _commandHistory = commandHistory;
            _activeFloor = initialFloor;
            CreatePreviewAndDimensionRenderers();
        }

        public void SetActiveFloor(FloorData floor)
        {
            _activeFloor = floor;
            _pendingSwitchToLinkId = null;
            HideAllOverlays();
        }

        public void ActivateMEPPlacementTool(bool active, MEPCategory? categoryPreset = null)
        {
            _isPlacementActive = active;
            _isLinkingModeActive = false;
            _pendingSwitchToLinkId = null;

            if (!active)
            {
                HideAllOverlays();
                return;
            }

            if (categoryPreset.HasValue)
            {
                SelectMEPCategory(categoryPreset.Value);
            }
        }

        public void ActivateSwitchToLightLinkingMode(bool active)
        {
            _isLinkingModeActive = active;
            _isPlacementActive = false;
            _pendingSwitchToLinkId = null;
            HideAllOverlays();

            if (active)
            {
                OnMEPTelemetryChanged?.Invoke(new MEPPlacementTelemetry
                {
                    StatusMessage = "Tryb Łączenia Włącznika z Lampą: Kliknij LPM na Włączniku Ściennym, a następnie kliknij Punkt Świetlny na suficie/ścianie"
                });
            }
        }

        public void SelectMEPCategory(MEPCategory category)
        {
            ActiveCategory = category;
            PresetElevationAboveFloor = category switch
            {
                MEPCategory.ElectricalSocket230V => 0.30f,
                MEPCategory.ElectricalSocket400V => 0.30f,
                MEPCategory.LightSwitchSingle => 1.10f,
                MEPCategory.LightSwitchDouble => 1.10f,
                MEPCategory.WallLightPoint => 2.10f,
                MEPCategory.WaterSupplyCold => 0.55f,
                MEPCategory.WaterSupplyHot => 0.55f,
                MEPCategory.SewageDrain50mm => 0.45f,
                MEPCategory.SewageDrain110mm => 0.15f,
                MEPCategory.CeilingLightPoint => _activeFloor != null ? _activeFloor.DefaultCeilingHeight - 0.02f : 2.73f,
                _ => 0.30f
            };
        }

        /// <summary>
        /// Aligns the viewport camera orthogonally to the last hovered or selected wall (2D Elevation View).
        /// </summary>
        public void EnterElevationViewForHoveredWall()
        {
            if (_activeFloor == null || cameraController == null) return;
            WallData targetWall = _lastHoveredWall;
            if (targetWall == null && _activeFloor.Walls.Count > 0)
            {
                targetWall = _activeFloor.Walls[0];
                Vector2 a = targetWall.StartPoint.ToVector2();
                Vector2 b = targetWall.EndPoint.ToVector2();
                Vector2 d = (b - a).normalized;
                _lastHoveredWallNormal = new Vector3(-d.y, 0f, d.x);
            }

            if (targetWall != null)
            {
                cameraController.EnterWallElevation2DView(
                    targetWall.StartPoint.ToVector2(),
                    targetWall.EndPoint.ToVector2(),
                    _activeFloor.ElevationY,
                    targetWall.BaseHeight,
                    _lastHoveredWallNormal);
            }
        }

        private void Update()
        {
            if (_activeFloor == null || viewportCamera == null) return;

            if (_isLinkingModeActive)
            {
                HandleSwitchToLightLinkingInput();
                return;
            }

            if (!_isPlacementActive) return;

            // Press 'V' while hovering any wall to jump into perpendicular 2D Wall Elevation View
            if (Input.GetKeyDown(KeyCode.V))
            {
                EnterElevationViewForHoveredWall();
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                HideAllOverlays();
                return;
            }

            Ray ray = viewportCamera.ScreenPointToRay(Input.mousePosition);

            // Right-Click on an existing MEP fixture deletes it via DeleteMEPItemCommand (Ctrl+Z reversible)
            if (Input.GetMouseButtonDown(1) &&
                Physics.Raycast(ray, out RaycastHit rightHit, 200f) &&
                rightHit.collider.TryGetComponentInParent(out MEPInstanceIdentifier existingMepTag))
            {
                DeleteExistingMEPById(existingMepTag.MEPId);
                return;
            }

            if (ActiveCategory == MEPCategory.CeilingLightPoint)
            {
                HandleCeilingLightPlacement(ray);
            }
            else
            {
                HandleWallMEPPlacement(ray);
            }
        }

        private void HandleWallMEPPlacement(Ray cursorRay)
        {
            MEPSnapResult snap = _snappingEngine.ComputeMEPWallSnap(
                cursorRay,
                _activeFloor.ElevationY,
                LockElevationY,
                PresetElevationAboveFloor,
                wallLayerMask,
                applianceHazardLayerMask);

            if (!snap.IsValidHit)
            {
                HideAllOverlays();
                OnMEPTelemetryChanged?.Invoke(new MEPPlacementTelemetry
                {
                    Category = ActiveCategory,
                    IsValidSurfaceHit = false,
                    StatusMessage = $"MEP [{ActiveCategory}] | Wskaż powierzchnię ściany (Wys. blokady Y = {PresetElevationAboveFloor * 100f:F0} cm | Klawisz V: Widok 2D rozwinięcia ściany | PPM: usuń)"
                });
                return;
            }

            WallData hostWall = _activeFloor.Walls.Find(w => w.WallId == snap.HostWallId);
            if (hostWall == null)
            {
                HideAllOverlays();
                return;
            }

            _lastHoveredWall = hostWall;
            _lastHoveredWallNormal = snap.SurfaceNormal;

            // Check additional collisions: Appliance exclusion zones (Dishwasher/Sink) & Attic Roof Slopes
            bool hasApplianceCollision = snap.HasSafetyCollision || CheckFurnitureApplianceCollision(snap.WorldPosition);
            bool hasRoofSlopeCollision = CheckAtticRoofSlopeCollision(snap.WorldPosition);
            bool hasAnyWarning = hasApplianceCollision || hasRoofSlopeCollision;

            string warningText = hasApplianceCollision
                ? " [KOLIZJA: STREFA ZMYWARKI / AGD!]"
                : hasRoofSlopeCollision
                    ? " [KOLIZJA: POWYŻEJ SKOSU DACHU PODDASZA!]"
                    : "";

            // Update 3D Preview Fixture & Live Dimension Lines (Corner + Floor)
            _previewFixtureGo.SetActive(true);
            _previewFixtureGo.transform.SetPositionAndRotation(
                snap.WorldPosition + snap.SurfaceNormal * 0.01f,
                snap.WorldRotation);
            _previewRenderer.sharedMaterial = hasAnyWarning ? _warningMat : _validMat;

            UpdateDimensionLines(snap.WorldPosition, snap.NearestCornerWorldPoint, snap.FloorProjectionPoint, snap.SurfaceNormal);

            OnMEPTelemetryChanged?.Invoke(new MEPPlacementTelemetry
            {
                Category = ActiveCategory,
                IsValidSurfaceHit = true,
                DistanceToNearestCorner = snap.DistanceToNearestCorner,
                ElevationFromFloor = snap.ElevationFromFloor,
                HasCollisionWarning = hasAnyWarning,
                StatusMessage =
                    $"MEP [{ActiveCategory}] | Od narożnika: {snap.DistanceToNearestCorner * 100f:F0} cm ({snap.DistanceToNearestCorner:F2} m) | " +
                    $"Od podłogi: {snap.ElevationFromFloor * 100f:F0} cm ({snap.ElevationFromFloor:F2} m){warningText} | [V: Widok 2D Ściany]"
            });

            if (Input.GetMouseButtonDown(0))
            {
                Vector2 wallStart = hostWall.StartPoint.ToVector2();
                Vector2 wallDir = (hostWall.EndPoint.ToVector2() - wallStart).normalized;
                float distAlongWall = Vector2.Dot(new Vector2(snap.WorldPosition.x, snap.WorldPosition.z) - wallStart, wallDir);

                var newMep = new MEPData
                {
                    Category = ActiveCategory,
                    HostSurfaceId = hostWall.WallId,
                    DistanceAlongWall = distAlongWall,
                    ElevationFromFloor = snap.ElevationFromFloor,
                    WorldPosition = snap.WorldPosition,
                    SurfaceNormal = snap.SurfaceNormal
                };

                var cmd = new PlaceMEPItemCommand(
                    hostWall,
                    newMep,
                    (_, _, _) => sceneRebuilder.RebuildFloorMEPInstallations(_activeFloor));

                _commandHistory?.ExecuteCommand(cmd);
            }
        }

        private void HandleCeilingLightPlacement(Ray cursorRay)
        {
            float ceilingY = _activeFloor.ElevationY + _activeFloor.DefaultCeilingHeight - 0.02f;
            var floorPlane = new Plane(Vector3.up, new Vector3(0f, _activeFloor.ElevationY, 0f));

            if (!floorPlane.Raycast(cursorRay, out float enter) || enter > 250f)
            {
                HideAllOverlays();
                return;
            }

            Vector3 floorPt = cursorRay.GetPoint(enter);
            Vector3 ceilingWorldPos = new(
                Mathf.Round(floorPt.x / 0.10f) * 0.10f,
                ceilingY,
                Mathf.Round(floorPt.z / 0.10f) * 0.10f);

            _previewFixtureGo.SetActive(true);
            _previewFixtureGo.transform.SetPositionAndRotation(ceilingWorldPos, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            _previewRenderer.sharedMaterial = _validMat;

            _horizontalCornerDimLine.enabled = false;
            _verticalFloorDimLine.enabled = true;
            _verticalFloorDimLine.SetPosition(0, ceilingWorldPos);
            _verticalFloorDimLine.SetPosition(1, new Vector3(ceilingWorldPos.x, _activeFloor.ElevationY, ceilingWorldPos.z));

            OnMEPTelemetryChanged?.Invoke(new MEPPlacementTelemetry
            {
                Category = MEPCategory.CeilingLightPoint,
                IsValidSurfaceHit = true,
                ElevationFromFloor = _activeFloor.DefaultCeilingHeight,
                StatusMessage = $"Punkt Świetlny Sufitowy | Pozycja XZ: ({ceilingWorldPos.x:F2}m, {ceilingWorldPos.z:F2}m) | Wys. sufitu: {_activeFloor.DefaultCeilingHeight:F2}m | LPM: wstaw"
            });

            if (Input.GetMouseButtonDown(0))
            {
                if (_activeFloor.Rooms.Count == 0)
                {
                    _activeFloor.Rooms.Add(new RoomData { Name = "Strefa Oświetlenia" });
                }

                RoomData targetRoom = _activeFloor.Rooms[0];
                var lightMep = new MEPData
                {
                    Category = MEPCategory.CeilingLightPoint,
                    HostSurfaceId = targetRoom.RoomId,
                    ElevationFromFloor = _activeFloor.DefaultCeilingHeight,
                    WorldPosition = ceilingWorldPos,
                    SurfaceNormal = Vector3.down
                };

                var cmd = new PlaceCeilingLightCommand(
                    _activeFloor,
                    targetRoom,
                    lightMep,
                    f => sceneRebuilder.RebuildFloorMEPInstallations(f));

                _commandHistory?.ExecuteCommand(cmd);
            }
        }

        private void HandleSwitchToLightLinkingInput()
        {
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                _pendingSwitchToLinkId = null;
                _linkingRubberBandLine.enabled = false;
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Ray ray = viewportCamera.ScreenPointToRay(Input.mousePosition);
            bool hitMep = Physics.Raycast(ray, out RaycastHit hit, 200f) &&
                          hit.collider.TryGetComponentInParent(out MEPInstanceIdentifier hitMepTag);

            if (_pendingSwitchToLinkId.HasValue)
            {
                Vector3 endPt = hitMep ? hit.point : ray.GetPoint(4.0f);
                _linkingRubberBandLine.enabled = true;
                _linkingRubberBandLine.SetPosition(0, _pendingSwitchWorldPos);
                _linkingRubberBandLine.SetPosition(1, endPt);
            }

            if (Input.GetMouseButtonDown(0) && hitMep &&
                hit.collider.TryGetComponentInParent(out MEPInstanceIdentifier clickedTag))
            {
                bool isSwitch = clickedTag.Category == MEPCategory.LightSwitchSingle ||
                                clickedTag.Category == MEPCategory.LightSwitchDouble;

                bool isLight = clickedTag.Category == MEPCategory.CeilingLightPoint ||
                               clickedTag.Category == MEPCategory.WallLightPoint;

                if (!_pendingSwitchToLinkId.HasValue && isSwitch)
                {
                    _pendingSwitchToLinkId = clickedTag.MEPId;
                    _pendingSwitchWorldPos = clickedTag.transform.position;
                    OnMEPTelemetryChanged?.Invoke(new MEPPlacementTelemetry
                    {
                        StatusMessage = $"Wybrano Włącznik [{clickedTag.MEPId.ToString()[..6]}]. Teraz kliknij LPM na Punkt Świetlny (lampę), aby utworzyć obwód!"
                    });
                }
                else if (_pendingSwitchToLinkId.HasValue && isLight)
                {
                    var link = new MEPConnectionLink
                    {
                        SwitchMEPId = _pendingSwitchToLinkId.Value,
                        TargetLightMEPId = clickedTag.MEPId
                    };

                    var cmd = new CreateMEPLinkCommand(
                        _activeFloor,
                        link,
                        f => sceneRebuilder.RebuildFloorMEPInstallations(f));

                    _commandHistory?.ExecuteCommand(cmd);

                    _pendingSwitchToLinkId = null;
                    _linkingRubberBandLine.enabled = false;
                    OnMEPTelemetryChanged?.Invoke(new MEPPlacementTelemetry
                    {
                        StatusMessage = "Połączono włącznik z punktem świetlnym! W trybie Spaceru (FPP) kliknięcie włącznika zapali/zgasi tę lampę."
                    });
                }
            }
        }

        private bool CheckFurnitureApplianceCollision(Vector3 mepWorldPos)
        {
            if (_activeFloor == null) return false;
            foreach (FurnitureData furn in _activeFloor.Furnitures)
            {
                if (!furn.HasApplianceExclusionZone) continue;
                Vector3 fPos = furn.Position.ToVector3();
                // Check if MEP point is within the rear 65cm x 85cm zone behind an appliance (e.g., dishwasher)
                float horizDist = Vector2.Distance(new Vector2(mepWorldPos.x, mepWorldPos.z), new Vector2(fPos.x, fPos.z));
                bool heightOverlap = mepWorldPos.y >= fPos.y && mepWorldPos.y <= (fPos.y + 0.86f);
                if (horizDist < 0.42f && heightOverlap &&
                    (ActiveCategory == MEPCategory.ElectricalSocket230V || ActiveCategory == MEPCategory.ElectricalSocket400V))
                {
                    return true;
                }
            }
            return false;
        }

        private bool CheckAtticRoofSlopeCollision(Vector3 mepWorldPos)
        {
            if (_activeFloor == null || !_activeFloor.IsAttic || _activeFloor.RoofPlanes.Count == 0) return false;

            List<Plane> planes = ProceduralWallMeshBuilder.BuildRoofClippingPlanes(_activeFloor.RoofPlanes, _activeFloor.ElevationY);
            foreach (Plane plane in planes)
            {
                if (plane.GetDistanceToPoint(mepWorldPos) < 0.05f)
                {
                    return true;
                }
            }
            return false;
        }

        private void DeleteExistingMEPById(Guid mepId)
        {
            foreach (WallData w in _activeFloor.Walls)
            {
                MEPData foundOnWall = w.MountedMEPItems.Find(m => m.MEPId == mepId);
                if (foundOnWall != null)
                {
                    var cmd = new DeleteMEPItemCommand(
                        _activeFloor, w, null, foundOnWall,
                        f => sceneRebuilder.RebuildFloorMEPInstallations(f));
                    _commandHistory?.ExecuteCommand(cmd);
                    return;
                }
            }

            foreach (RoomData r in _activeFloor.Rooms)
            {
                MEPData foundOnCeiling = r.CeilingMEPItems.Find(m => m.MEPId == mepId);
                if (foundOnCeiling != null)
                {
                    var cmd = new DeleteMEPItemCommand(
                        _activeFloor, null, r, foundOnCeiling,
                        f => sceneRebuilder.RebuildFloorMEPInstallations(f));
                    _commandHistory?.ExecuteCommand(cmd);
                    return;
                }
            }
        }

        private void UpdateDimensionLines(Vector3 mepPos, Vector3 nearestCornerPos, Vector3 floorProjPos, Vector3 wallNormal)
        {
            Vector3 offset = wallNormal * 0.025f;

            _horizontalCornerDimLine.enabled = true;
            _horizontalCornerDimLine.SetPosition(0, mepPos + offset);
            _horizontalCornerDimLine.SetPosition(1, nearestCornerPos + offset);

            _verticalFloorDimLine.enabled = true;
            _verticalFloorDimLine.SetPosition(0, mepPos + offset);
            _verticalFloorDimLine.SetPosition(1, floorProjPos + offset);
        }

        private void HideAllOverlays()
        {
            if (_previewFixtureGo != null) _previewFixtureGo.SetActive(false);
            if (_horizontalCornerDimLine != null) _horizontalCornerDimLine.enabled = false;
            if (_verticalFloorDimLine != null) _verticalFloorDimLine.enabled = false;
            if (_linkingRubberBandLine != null) _linkingRubberBandLine.enabled = false;
        }

        private void CreatePreviewAndDimensionRenderers()
        {
            if (_previewFixtureGo != null) return;

            _previewFixtureGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _previewFixtureGo.name = "MEPPlacementPreview";
            _previewFixtureGo.transform.SetParent(transform, false);
            _previewFixtureGo.transform.localScale = new Vector3(0.09f, 0.09f, 0.03f);
            if (_previewFixtureGo.TryGetComponent(out Collider col)) Destroy(col);
            _previewRenderer = _previewFixtureGo.GetComponent<MeshRenderer>();

            Shader litShader = Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _validMat = new Material(litShader) { name = "Mat_MEPPreviewValid" };
            if (_validMat.HasProperty("_BaseColor")) _validMat.SetColor("_BaseColor", new Color(0.15f, 0.88f, 0.45f));
            if (_validMat.HasProperty("_Color")) _validMat.SetColor("_Color", new Color(0.15f, 0.88f, 0.45f));

            _warningMat = new Material(litShader) { name = "Mat_MEPPreviewWarning" };
            if (_warningMat.HasProperty("_BaseColor")) _warningMat.SetColor("_BaseColor", new Color(0.98f, 0.20f, 0.15f));
            if (_warningMat.HasProperty("_Color")) _warningMat.SetColor("_Color", new Color(0.98f, 0.20f, 0.15f));

            _horizontalCornerDimLine = CreateLine("MEPDimLine_HorizontalToCorner", new Color(0.20f, 0.85f, 1.0f), 0.016f);
            _verticalFloorDimLine = CreateLine("MEPDimLine_VerticalToFloor", new Color(0.45f, 1.0f, 0.35f), 0.016f);
            _linkingRubberBandLine = CreateLine("MEPLinking_RubberBand", new Color(1.0f, 0.85f, 0.15f), 0.022f);

            HideAllOverlays();
        }

        private LineRenderer CreateLine(string name, Color color, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.useWorldSpace = true;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                lr.material = new Material(shader) { color = color };
            }
            lr.enabled = false;
            return lr;
        }
    }
}
