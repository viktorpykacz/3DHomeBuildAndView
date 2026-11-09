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
    public struct OpeningPreviewTelemetry
    {
        public bool IsHoveringWall;
        public bool IsValidPlacement;
        public OpeningType Type;
        public float Width;
        public float Height;
        public float SillHeight;
        public float DistanceFromLeftEnd;
        public float DistanceFromRightEnd;
        public bool IsCenteredOnWall;
        public string StatusMessage;
    }

    /// <summary>
    /// Interactive tool for placing, previewing, and deleting Windows & Doors on walls (Sprint 2).
    /// Supports automatic wall-midpoint snapping, 5cm step snapping, corner margin enforcement,
    /// overlap validation with existing openings, and R-key hinge flipping.
    /// </summary>
    public sealed class WallOpeningPlacementTool : MonoBehaviour
    {
        [SerializeField] private Camera viewportCamera;
        [SerializeField] private FloorSceneRebuilder sceneRebuilder;

        [Header("Snapping & Margins")]
        [SerializeField] private float cornerMarginMeters = 0.15f;
        [SerializeField] private float centerSnapThresholdMeters = 0.14f;
        [SerializeField] private float stepSnapMeters = 0.05f;

        [Header("Active Opening Parameters")]
        public OpeningType CurrentOpeningType { get; set; } = OpeningType.WindowStandard;
        public float CurrentWidth { get; set; } = 1.20f;
        public float CurrentHeight { get; set; } = 1.50f;
        public float CurrentSillHeight { get; set; } = 0.85f;
        public bool CurrentFlipHorizontal { get; set; } = false;

        private CommandHistoryManager _commandHistory;
        private FloorData _activeFloor;
        private bool _isToolActive;

        private GameObject _previewBoxGo;
        private MeshRenderer _previewRenderer;
        private Material _validPreviewMat;
        private Material _invalidPreviewMat;

        public event Action<OpeningPreviewTelemetry> OnOpeningTelemetryChanged;

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
            CreatePreviewBox();
        }

        public void SetActiveFloor(FloorData floor)
        {
            _activeFloor = floor;
            HidePreview();
        }

        public void ActivateOpeningTool(bool active, OpeningType presetType = OpeningType.WindowStandard)
        {
            _isToolActive = active;
            if (!_isToolActive)
            {
                HidePreview();
                return;
            }

            ApplyOpeningPreset(presetType);
        }

        public void ApplyOpeningPreset(OpeningType type)
        {
            CurrentOpeningType = type;
            switch (type)
            {
                case OpeningType.WindowStandard:
                    CurrentWidth = 1.20f;
                    CurrentHeight = 1.50f;
                    CurrentSillHeight = 0.85f;
                    break;
                case OpeningType.DoorSingle:
                    CurrentWidth = 0.90f;
                    CurrentHeight = 2.05f;
                    CurrentSillHeight = 0.00f;
                    break;
                case OpeningType.DoorDouble:
                    CurrentWidth = 1.60f;
                    CurrentHeight = 2.05f;
                    CurrentSillHeight = 0.00f;
                    break;
                case OpeningType.WallOpeningPassThrough:
                    CurrentWidth = 1.40f;
                    CurrentHeight = 2.15f;
                    CurrentSillHeight = 0.00f;
                    break;
            }
        }

        private void Update()
        {
            if (!_isToolActive || _activeFloor == null || viewportCamera == null) return;

            if (Input.GetKeyDown(KeyCode.R))
            {
                CurrentFlipHorizontal = !CurrentFlipHorizontal;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                HidePreview();
                return;
            }

            Ray ray = viewportCamera.ScreenPointToRay(Input.mousePosition);

            // Right-Click on an existing window/door deletes it (reversible with Ctrl+Z)
            if (Input.GetMouseButtonDown(1) &&
                Physics.Raycast(ray, out RaycastHit rightHit, 200f) &&
                rightHit.collider.TryGetComponentInParent(out OpeningSurfaceIdentifier existingOpeningTag))
            {
                WallData hostWall = _activeFloor.Walls.Find(w => w.WallId == existingOpeningTag.WallId);
                OpeningData opToRemove = hostWall?.Openings.Find(o => o.OpeningId == existingOpeningTag.OpeningId);
                if (hostWall != null && opToRemove != null)
                {
                    var delCmd = new DeleteOpeningCommand(
                        _activeFloor,
                        hostWall,
                        opToRemove,
                        f => sceneRebuilder.RebuildFloorGeometry(f));
                    _commandHistory?.ExecuteCommand(delCmd);
                }
                return;
            }

            if (!Physics.Raycast(ray, out RaycastHit hit, 200f) ||
                !hit.collider.TryGetComponent(out WallSurfaceIdentifier wallTag))
            {
                HidePreview();
                OnOpeningTelemetryChanged?.Invoke(new OpeningPreviewTelemetry
                {
                    IsHoveringWall = false,
                    StatusMessage = "Wskaż kursorem ścianę, aby wstawić otwór (LPM: wstaw, R: odwróć kierunek otwierania, PPM na oknie/drzwiach: usuń)"
                });
                return;
            }

            WallData wall = _activeFloor.Walls.Find(w => w.WallId == wallTag.WallId);
            if (wall == null)
            {
                HidePreview();
                return;
            }

            EvaluateWallOpeningCandidate(wall, hit.point);
        }

        private void EvaluateWallOpeningCandidate(WallData wall, Vector3 hitWorldPoint)
        {
            Vector2 a = wall.StartPoint.ToVector2();
            Vector2 b = wall.EndPoint.ToVector2();
            float wallLength = Vector2.Distance(a, b);
            Vector2 dir = (b - a).normalized;

            Vector2 hitXZ = new(hitWorldPoint.x, hitWorldPoint.z);
            float rawDistFromStart = Vector2.Dot(hitXZ - a, dir);

            float halfOpeningW = CurrentWidth * 0.5f;
            float minCenter = halfOpeningW + cornerMarginMeters;
            float maxCenter = wallLength - halfOpeningW - cornerMarginMeters;

            bool wallLongEnough = maxCenter >= minCenter;
            float snappedCenterDist = wallLength * 0.5f;
            bool isCentered = false;

            if (wallLongEnough)
            {
                snappedCenterDist = Mathf.Clamp(rawDistFromStart, minCenter, maxCenter);

                // Check midpoint snap first
                float wallMidpoint = wallLength * 0.5f;
                if (Mathf.Abs(snappedCenterDist - wallMidpoint) <= centerSnapThresholdMeters)
                {
                    snappedCenterDist = wallMidpoint;
                    isCentered = true;
                }
                else if (stepSnapMeters > 1e-4f)
                {
                    snappedCenterDist = Mathf.Round(snappedCenterDist / stepSnapMeters) * stepSnapMeters;
                    snappedCenterDist = Mathf.Clamp(snappedCenterDist, minCenter, maxCenter);
                }
            }

            bool overlapsExisting = CheckOverlapWithExistingOpenings(wall, snappedCenterDist, CurrentWidth);
            bool fitsVertically = (CurrentSillHeight + CurrentHeight) <= (wall.BaseHeight - 0.05f);
            bool isValid = wallLongEnough && !overlapsExisting && fitsVertically;

            float leftEdgeToStart = snappedCenterDist - halfOpeningW;
            float rightEdgeToEnd = wallLength - (snappedCenterDist + halfOpeningW);

            UpdatePreviewBoxTransform(wall, dir, snappedCenterDist, isValid);

            string reason = !wallLongEnough
                ? "Ściana jest zbyt krótka dla tego otworu!"
                : overlapsExisting
                    ? "Kolizja z innym oknem/drzwiami na tej ścianie!"
                    : !fitsVertically
                        ? "Otwór przekracza wysokość ściany!"
                        : isCentered
                            ? "[WYŚRODKOWANO NA ŚCIANIE]"
                            : "Gotowy do wstawienia (LPM)";

            OnOpeningTelemetryChanged?.Invoke(new OpeningPreviewTelemetry
            {
                IsHoveringWall = true,
                IsValidPlacement = isValid,
                Type = CurrentOpeningType,
                Width = CurrentWidth,
                Height = CurrentHeight,
                SillHeight = CurrentSillHeight,
                DistanceFromLeftEnd = Mathf.Max(0f, leftEdgeToStart),
                DistanceFromRightEnd = Mathf.Max(0f, rightEdgeToEnd),
                IsCenteredOnWall = isCentered,
                StatusMessage = $"{CurrentOpeningType}: Szer. {CurrentWidth:F2}m × Wys. {CurrentHeight:F2}m | Od lewego narożnika: {leftEdgeToStart:F2}m | Od prawego: {rightEdgeToEnd:F2}m | {reason}"
            });

            if (isValid && Input.GetMouseButtonDown(0))
            {
                var newOpening = new OpeningData
                {
                    Type = CurrentOpeningType,
                    DistanceFromWallStart = snappedCenterDist,
                    Width = CurrentWidth,
                    Height = CurrentHeight,
                    SillHeight = CurrentSillHeight,
                    FlipHorizontal = CurrentFlipHorizontal
                };

                var cmd = new CreateOpeningCommand(
                    _activeFloor,
                    wall,
                    newOpening,
                    f => sceneRebuilder.RebuildFloorGeometry(f));

                _commandHistory?.ExecuteCommand(cmd);
            }
        }

        private static bool CheckOverlapWithExistingOpenings(WallData wall, float candidateCenter, float candidateWidth)
        {
            const float minMullionSpacing = 0.10f;
            float candMin = candidateCenter - candidateWidth * 0.5f - minMullionSpacing;
            float candMax = candidateCenter + candidateWidth * 0.5f + minMullionSpacing;

            foreach (OpeningData existing in wall.Openings)
            {
                float exMin = existing.DistanceFromWallStart - existing.Width * 0.5f;
                float exMax = existing.DistanceFromWallStart + existing.Width * 0.5f;
                if (candMax > exMin && candMin < exMax)
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdatePreviewBoxTransform(WallData wall, Vector2 wallDir, float distFromStart, bool isValid)
        {
            if (_previewBoxGo == null) return;

            Vector2 a = wall.StartPoint.ToVector2();
            Vector2 centerXZ = a + wallDir * distFromStart;
            Vector3 normal3D = new(-wallDir.y, 0f, wallDir.x);

            float worldY = _activeFloor.ElevationY + CurrentSillHeight + CurrentHeight * 0.5f;
            _previewBoxGo.transform.position = new Vector3(centerXZ.x, worldY, centerXZ.y);
            _previewBoxGo.transform.rotation = Quaternion.LookRotation(normal3D, Vector3.up);
            _previewBoxGo.transform.localScale = new Vector3(CurrentWidth, CurrentHeight, wall.Thickness + 0.06f);

            _previewRenderer.sharedMaterial = isValid ? _validPreviewMat : _invalidPreviewMat;
            _previewBoxGo.SetActive(true);
        }

        private void HidePreview()
        {
            if (_previewBoxGo != null) _previewBoxGo.SetActive(false);
        }

        private void CreatePreviewBox()
        {
            if (_previewBoxGo != null) return;

            _previewBoxGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _previewBoxGo.name = "OpeningPlacementPreview";
            _previewBoxGo.transform.SetParent(transform, false);
            if (_previewBoxGo.TryGetComponent(out Collider col)) Destroy(col);

            _previewRenderer = _previewBoxGo.GetComponent<MeshRenderer>();

            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            _validPreviewMat = new Material(shader) { name = "Mat_OpeningValid" };
            Color green = new(0.18f, 0.85f, 0.42f, 0.75f);
            if (_validPreviewMat.HasProperty("_BaseColor")) _validPreviewMat.SetColor("_BaseColor", green);
            if (_validPreviewMat.HasProperty("_Color")) _validPreviewMat.SetColor("_Color", green);

            _invalidPreviewMat = new Material(shader) { name = "Mat_OpeningInvalid" };
            Color red = new(0.92f, 0.22f, 0.22f, 0.75f);
            if (_invalidPreviewMat.HasProperty("_BaseColor")) _invalidPreviewMat.SetColor("_BaseColor", red);
            if (_invalidPreviewMat.HasProperty("_Color")) _invalidPreviewMat.SetColor("_Color", red);

            _previewBoxGo.SetActive(false);
        }
    }
}
