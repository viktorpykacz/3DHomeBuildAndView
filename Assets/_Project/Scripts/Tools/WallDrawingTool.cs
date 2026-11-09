using System;
using HomeBuilder.Core;
using HomeBuilder.Core.Commands;
using HomeBuilder.Data;
using HomeBuilder.Geometry;
using HomeBuilder.Snapping;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HomeBuilder.Tools
{
    public enum ActiveDesignTool
    {
        SelectAndInspect,
        CalibrateBlueprint2Point,
        DrawWalls,
        PlaceWindow,
        PlaceDoor,
        PlaceFurniture,
        PlaceMEPInstallation,
        LinkMEPSwitchToLight
    }

    public struct WallPreviewTelemetry
    {
        public bool IsDrawing;
        public Vector2 StartXZ;
        public Vector2 CurrentEndXZ;
        public float LengthMeters;
        public float AngleDegrees;
        public bool IsSnappedToNode;
        public bool IsAngleLocked;
    }

    /// <summary>
    /// Interactive CAD Wall Drawing & Selection tool for Sprint 1.
    /// Features:
    /// - Endpoint magnetic snapping (L/T/X junction creation)
    /// - 5cm grid snapping & Shift 45-degree ortho angle lock
    /// - Real-time 3D procedural preview mesh while dragging cursor
    /// - Continuous polyline wall drawing (LMB to place, RMB/Esc to finish chain)
    /// - Wall selection and Delete / Property modification via CommandHistoryManager
    /// </summary>
    public sealed class WallDrawingTool : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private Camera viewportCamera;
        [SerializeField] private FloorSceneRebuilder sceneRebuilder;

        [Header("Snapping Settings")]
        [SerializeField] private float nodeSnapRadiusMeters = 0.22f;
        [SerializeField] private float gridStepMeters = 0.05f;
        [SerializeField] private bool enableGridSnap = true;
        [SerializeField] private float minWallLengthMeters = 0.15f;

        [Header("Default New Wall Settings")]
        public float CurrentWallThickness { get; set; } = 0.24f;
        public float CurrentWallHeight { get; set; } = 2.75f;
        public bool CurrentWallIsLoadBearing { get; set; } = true;

        private CommandHistoryManager _commandHistory;
        private FloorData _activeFloor;
        private ActiveDesignTool _activeTool = ActiveDesignTool.DrawWalls;

        private bool _isChainDrawing;
        private Vector2 _chainStartXZ;
        private WallData _selectedWall;

        private GameObject _previewWallObject;
        private MeshFilter _previewMeshFilter;
        private MeshRenderer _previewMeshRenderer;
        private Material _previewMaterial;

        public event Action<WallPreviewTelemetry> OnPreviewTelemetryUpdated;
        public event Action<WallData> OnSelectedWallChanged;

        public WallData SelectedWall => _selectedWall;

        public void Initialize(
            Camera cam,
            FloorSceneRebuilder rebuilder,
            CommandHistoryManager commandHistory,
            FloorData initialFloor)
        {
            viewportCamera = cam;
            sceneRebuilder = rebuilder;
            _commandHistory = commandHistory;
            _activeFloor = initialFloor;
            CreatePreviewWallObject();
        }

        public void SetActiveFloor(FloorData floor)
        {
            CancelCurrentWallChain();
            SelectWall(null);
            _activeFloor = floor;
            if (_activeFloor != null)
            {
                CurrentWallHeight = _activeFloor.DefaultCeilingHeight;
            }
        }

        public void SetActiveTool(ActiveDesignTool tool)
        {
            if (_activeTool == tool) return;
            CancelCurrentWallChain();
            _activeTool = tool;
        }

        public void DeleteSelectedWall()
        {
            if (_selectedWall == null || _activeFloor == null || _commandHistory == null) return;
            WallData wallToDelete = _selectedWall;
            SelectWall(null);

            var cmd = new DeleteWallCommand(
                _activeFloor,
                wallToDelete,
                floor => sceneRebuilder.RebuildFloorGeometry(floor));
            _commandHistory.ExecuteCommand(cmd);
        }

        public void UpdateSelectedWallDimensions(float newThickness, float newHeight)
        {
            if (_selectedWall == null || _activeFloor == null || _commandHistory == null) return;
            newThickness = Mathf.Clamp(newThickness, 0.06f, 1.00f);
            newHeight = Mathf.Clamp(newHeight, 0.50f, 6.00f);

            if (Mathf.Approximately(_selectedWall.Thickness, newThickness) &&
                Mathf.Approximately(_selectedWall.BaseHeight, newHeight))
            {
                return;
            }

            var cmd = new ModifyWallPropertiesCommand(
                _activeFloor,
                _selectedWall,
                _selectedWall.Thickness,
                _selectedWall.BaseHeight,
                newThickness,
                newHeight,
                floor => sceneRebuilder.RebuildFloorGeometry(floor));

            _commandHistory.ExecuteCommand(cmd);
        }

        private void Update()
        {
            if (_activeFloor == null || viewportCamera == null) return;

            // Keyboard shortcuts for Undo / Redo / Delete
            bool ctrlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrlPressed && Input.GetKeyDown(KeyCode.Z))
            {
                CancelCurrentWallChain();
                _commandHistory?.Undo();
                return;
            }
            if (ctrlPressed && Input.GetKeyDown(KeyCode.Y))
            {
                CancelCurrentWallChain();
                _commandHistory?.Redo();
                return;
            }

            if (_activeTool == ActiveDesignTool.SelectAndInspect)
            {
                HandleSelectToolInput();
                return;
            }

            if (_activeTool == ActiveDesignTool.DrawWalls)
            {
                HandleDrawWallToolInput();
            }
        }

        private void HandleSelectToolInput()
        {
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
            {
                DeleteSelectedWall();
                return;
            }

            if (IsPointerOverUI()) return;

            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = viewportCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 200f) &&
                    hit.collider.TryGetComponent(out WallSurfaceIdentifier wallIdentifier))
                {
                    WallData found = _activeFloor.Walls.Find(w => w.WallId == wallIdentifier.WallId);
                    SelectWall(found);
                }
                else
                {
                    SelectWall(null);
                }
            }
        }

        private void HandleDrawWallToolInput()
        {
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelCurrentWallChain();
                return;
            }

            if (IsPointerOverUI()) return;

            if (!TryProjectCursorToFloorPlane(out Vector2 rawCursorXZ)) return;

            bool isAngleLocked = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Vector2 snappedXZ = ResolveSnappedPoint(
                rawCursorXZ,
                _isChainDrawing ? _chainStartXZ : null,
                isAngleLocked,
                out bool snappedToNode);

            if (_isChainDrawing)
            {
                UpdatePreviewWall(_chainStartXZ, snappedXZ, snappedToNode, isAngleLocked);
            }
            else
            {
                OnPreviewTelemetryUpdated?.Invoke(new WallPreviewTelemetry
                {
                    IsDrawing = false,
                    StartXZ = snappedXZ,
                    CurrentEndXZ = snappedXZ,
                    LengthMeters = 0f,
                    AngleDegrees = 0f,
                    IsSnappedToNode = snappedToNode,
                    IsAngleLocked = isAngleLocked
                });
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (!_isChainDrawing)
                {
                    _isChainDrawing = true;
                    _chainStartXZ = snappedXZ;
                }
                else
                {
                    float segmentLength = Vector2.Distance(_chainStartXZ, snappedXZ);
                    if (segmentLength >= minWallLengthMeters)
                    {
                        CommitWallSegment(_chainStartXZ, snappedXZ);
                        // If we snapped onto an existing wall endpoint (closing a room loop), stop the chain automatically
                        if (snappedToNode && _activeFloor.Walls.Count >= 3)
                        {
                            CancelCurrentWallChain();
                        }
                        else
                        {
                            _chainStartXZ = snappedXZ;
                        }
                    }
                }
            }
        }

        private void CommitWallSegment(Vector2 startXZ, Vector2 endXZ)
        {
            var newWall = new WallData
            {
                StartPoint = startXZ,
                EndPoint = endXZ,
                Thickness = CurrentWallThickness,
                BaseHeight = CurrentWallHeight,
                IsLoadBearing = CurrentWallIsLoadBearing,
                ClipByRoofPlanes = _activeFloor.IsAttic
            };

            var cmd = new CreateWallCommand(
                _activeFloor,
                newWall,
                floor => sceneRebuilder.RebuildFloorGeometry(floor));

            _commandHistory.ExecuteCommand(cmd);
        }

        private void UpdatePreviewWall(Vector2 startXZ, Vector2 endXZ, bool snappedToNode, bool angleLocked)
        {
            float length = Vector2.Distance(startXZ, endXZ);
            Vector2 delta = endXZ - startXZ;
            float angleDeg = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            if (angleDeg < 0f) angleDeg += 360f;

            OnPreviewTelemetryUpdated?.Invoke(new WallPreviewTelemetry
            {
                IsDrawing = true,
                StartXZ = startXZ,
                CurrentEndXZ = endXZ,
                LengthMeters = length,
                AngleDegrees = angleDeg,
                IsSnappedToNode = snappedToNode,
                IsAngleLocked = angleLocked
            });

            if (length < minWallLengthMeters)
            {
                if (_previewWallObject != null) _previewWallObject.SetActive(false);
                return;
            }

            var tempWall = new WallData
            {
                StartPoint = startXZ,
                EndPoint = endXZ,
                Thickness = CurrentWallThickness,
                BaseHeight = CurrentWallHeight,
                ClipByRoofPlanes = false
            };

            WallCornerMiter miter = ProceduralWallMeshBuilder.ComputeMiterCorners(tempWall, null, null);
            Mesh previewMesh = ProceduralWallMeshBuilder.GenerateWallMesh(tempWall, miter, null);

            if (_previewMeshFilter.sharedMesh != null) Destroy(_previewMeshFilter.sharedMesh);
            _previewMeshFilter.sharedMesh = previewMesh;

            _previewWallObject.transform.position = new Vector3(0f, _activeFloor.ElevationY, 0f);
            _previewWallObject.SetActive(true);
        }

        public void CancelCurrentWallChain()
        {
            _isChainDrawing = false;
            if (_previewWallObject != null) _previewWallObject.SetActive(false);
            OnPreviewTelemetryUpdated?.Invoke(new WallPreviewTelemetry { IsDrawing = false });
        }

        private void SelectWall(WallData wall)
        {
            _selectedWall = wall;
            OnSelectedWallChanged?.Invoke(_selectedWall);
        }

        private Vector2 ResolveSnappedPoint(
            Vector2 rawXZ,
            Vector2? anchorStartXZ,
            bool lockAngle45,
            out bool snappedToExistingNode)
        {
            snappedToExistingNode = false;

            // 1. Magnetic snap to existing wall endpoints (highest priority for closing rooms)
            if (_activeFloor != null)
            {
                float bestDist = nodeSnapRadiusMeters;
                Vector2 bestNode = rawXZ;

                foreach (WallData w in _activeFloor.Walls)
                {
                    Vector2 s = w.StartPoint.ToVector2();
                    Vector2 e = w.EndPoint.ToVector2();

                    float ds = Vector2.Distance(rawXZ, s);
                    if (ds < bestDist)
                    {
                        bestDist = ds;
                        bestNode = s;
                        snappedToExistingNode = true;
                    }

                    float de = Vector2.Distance(rawXZ, e);
                    if (de < bestDist)
                    {
                        bestDist = de;
                        bestNode = e;
                        snappedToExistingNode = true;
                    }
                }

                if (snappedToExistingNode)
                {
                    return bestNode;
                }
            }

            Vector2 result = rawXZ;

            // 2. Angle lock (0, 45, 90, 135, 180 degrees) when Shift is held or within 2.5 deg of cardinal axes
            if (anchorStartXZ.HasValue)
            {
                Vector2 delta = result - anchorStartXZ.Value;
                float dist = delta.magnitude;
                if (dist > 1e-4f)
                {
                    float angleRad = Mathf.Atan2(delta.y, delta.x);
                    float angleDeg = angleRad * Mathf.Rad2Deg;
                    float snappedDeg90 = Mathf.Round(angleDeg / 90f) * 90f;

                    if (lockAngle45)
                    {
                        float snappedDeg45 = Mathf.Round(angleDeg / 45f) * 45f;
                        float rad = snappedDeg45 * Mathf.Deg2Rad;
                        result = anchorStartXZ.Value + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * dist;
                    }
                    else if (Mathf.Abs( Mathf.DeltaAngle(angleDeg, snappedDeg90) ) <= 2.5f)
                    {
                        // Soft ortho guide near 0/90/180/270 deg
                        float rad = snappedDeg90 * Mathf.Deg2Rad;
                        result = anchorStartXZ.Value + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * dist;
                    }
                }
            }

            // 3. Grid snap (5 cm increments)
            if (enableGridSnap && gridStepMeters > 1e-4f)
            {
                result.x = Mathf.Round(result.x / gridStepMeters) * gridStepMeters;
                result.y = Mathf.Round(result.y / gridStepMeters) * gridStepMeters;
            }

            return result;
        }

        private bool TryProjectCursorToFloorPlane(out Vector2 floorXZ)
        {
            Ray ray = viewportCamera.ScreenPointToRay(Input.mousePosition);
            float y = _activeFloor != null ? _activeFloor.ElevationY : 0f;
            var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));

            if (plane.Raycast(ray, out float enter) && enter < 500f)
            {
                Vector3 hitPt = ray.GetPoint(enter);
                floorXZ = new Vector2(hitPt.x, hitPt.z);
                return true;
            }

            floorXZ = Vector2.zero;
            return false;
        }

        private void CreatePreviewWallObject()
        {
            if (_previewWallObject != null) return;

            _previewWallObject = new GameObject("WallDrawingPreview");
            _previewWallObject.transform.SetParent(transform, false);
            _previewMeshFilter = _previewWallObject.AddComponent<MeshFilter>();
            _previewMeshRenderer = _previewWallObject.AddComponent<MeshRenderer>();

            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            _previewMaterial = new Material(shader)
            {
                name = "Mat_WallPreview"
            };
            Color previewColor = new(0.20f, 0.65f, 1.0f, 0.75f);
            if (_previewMaterial.HasProperty("_BaseColor")) _previewMaterial.SetColor("_BaseColor", previewColor);
            if (_previewMaterial.HasProperty("_Color")) _previewMaterial.SetColor("_Color", previewColor);

            _previewMeshRenderer.sharedMaterials = new[] { _previewMaterial, _previewMaterial, _previewMaterial };
            _previewWallObject.SetActive(false);
        }

        private static bool IsPointerOverUI() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
