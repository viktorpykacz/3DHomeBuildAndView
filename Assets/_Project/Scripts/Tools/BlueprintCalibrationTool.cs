using System;
using HomeBuilder.Core.Commands;
using HomeBuilder.Data;
using HomeBuilder.Floorplan;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HomeBuilder.Tools
{
    public enum CalibrationStep
    {
        Idle,
        WaitingForFirstPoint,
        WaitingForSecondPoint,
        ReadyToConfirmDistance
    }

    /// <summary>
    /// Interactive 2-point scale calibration tool for 2D architectural floorplan blueprints.
    /// User clicks Point 1 and Point 2 on a known dimension line on the blueprint and enters
    /// the real-world distance in meters (e.g., 5.40m). Executes a reversible CalibrateBlueprintCommand.
    /// </summary>
    public sealed class BlueprintCalibrationTool : MonoBehaviour
    {
        [SerializeField] private Camera viewportCamera;
        [SerializeField] private BlueprintCalibrationController calibrationController;

        private CommandHistoryManager _commandHistory;
        private FloorData _activeFloor;
        private bool _isToolActive;

        private CalibrationStep _step = CalibrationStep.Idle;
        private Vector3 _pointAWorld;
        private Vector3 _pointBWorld;
        private LineRenderer _measureLine;

        public CalibrationStep CurrentStep => _step;
        public float CurrentMeasuredSpanWorld => Vector2.Distance(
            new Vector2(_pointAWorld.x, _pointAWorld.z),
            new Vector2(_pointBWorld.x, _pointBWorld.z));

        public event Action<CalibrationStep, float> OnCalibrationStateChanged;

        public void Initialize(
            Camera cam,
            BlueprintCalibrationController controller,
            CommandHistoryManager commandHistory,
            FloorData initialFloor)
        {
            viewportCamera = cam;
            calibrationController = controller;
            _commandHistory = commandHistory;
            _activeFloor = initialFloor;
            CreateMeasurementLineRenderer();
        }

        public void SetActiveFloor(FloorData floor)
        {
            CancelCalibration();
            _activeFloor = floor;
        }

        public void ActivateCalibrationTool(bool active)
        {
            _isToolActive = active;
            if (_isToolActive)
            {
                _step = CalibrationStep.WaitingForFirstPoint;
                if (_measureLine != null) _measureLine.enabled = false;
                OnCalibrationStateChanged?.Invoke(_step, 0f);
            }
            else
            {
                CancelCalibration();
            }
        }

        public void CancelCalibration()
        {
            _isToolActive = false;
            _step = CalibrationStep.Idle;
            if (_measureLine != null) _measureLine.enabled = false;
            OnCalibrationStateChanged?.Invoke(_step, 0f);
        }

        /// <summary>
        /// Applies the user-entered real distance in meters via CommandHistoryManager (supports Ctrl+Z).
        /// </summary>
        public void ConfirmRealDistanceMeters(float realWorldMeters)
        {
            if (_step != CalibrationStep.ReadyToConfirmDistance || _activeFloor == null) return;
            if (realWorldMeters <= 0.01f) return;

            float currentWorldDist = CurrentMeasuredSpanWorld;
            if (currentWorldDist <= 1e-5f) return;

            BlueprintReferenceData bp = _activeFloor.Blueprint;
            float oldMetersPerPixel = bp.MetersPerPixel;
            float newMetersPerPixel = oldMetersPerPixel * (realWorldMeters / currentWorldDist);

            var cmd = new CalibrateBlueprintCommand(
                bp,
                oldMetersPerPixel,
                newMetersPerPixel,
                () =>
                {
                    if (calibrationController != null)
                    {
                        calibrationController.ApplyTransformFromCalibration(_activeFloor.ElevationY);
                    }
                });

            _commandHistory?.ExecuteCommand(cmd);

            _step = CalibrationStep.WaitingForFirstPoint;
            if (_measureLine != null) _measureLine.enabled = false;
            OnCalibrationStateChanged?.Invoke(_step, realWorldMeters);
        }

        private void Update()
        {
            if (!_isToolActive || _activeFloor == null || viewportCamera == null) return;

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                _step = CalibrationStep.WaitingForFirstPoint;
                if (_measureLine != null) _measureLine.enabled = false;
                OnCalibrationStateChanged?.Invoke(_step, 0f);
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (!TryProjectCursorToFloor(out Vector3 cursorWorld)) return;

            if (_step == CalibrationStep.WaitingForSecondPoint)
            {
                UpdateMeasurementLine(_pointAWorld, cursorWorld);
                OnCalibrationStateChanged?.Invoke(_step, Vector3.Distance(_pointAWorld, cursorWorld));
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (_step == CalibrationStep.WaitingForFirstPoint || _step == CalibrationStep.ReadyToConfirmDistance)
                {
                    _pointAWorld = cursorWorld;
                    _step = CalibrationStep.WaitingForSecondPoint;
                    UpdateMeasurementLine(_pointAWorld, _pointAWorld);
                    OnCalibrationStateChanged?.Invoke(_step, 0f);
                }
                else if (_step == CalibrationStep.WaitingForSecondPoint)
                {
                    _pointBWorld = cursorWorld;
                    float span = CurrentMeasuredSpanWorld;
                    if (span > 0.02f)
                    {
                        _step = CalibrationStep.ReadyToConfirmDistance;
                        UpdateMeasurementLine(_pointAWorld, _pointBWorld);
                        OnCalibrationStateChanged?.Invoke(_step, span);
                    }
                }
            }
        }

        private bool TryProjectCursorToFloor(out Vector3 worldPt)
        {
            Ray ray = viewportCamera.ScreenPointToRay(Input.mousePosition);
            float y = _activeFloor != null ? _activeFloor.ElevationY + 0.01f : 0.01f;
            var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));

            if (plane.Raycast(ray, out float enter) && enter < 500f)
            {
                worldPt = ray.GetPoint(enter);
                return true;
            }

            worldPt = Vector3.zero;
            return false;
        }

        private void UpdateMeasurementLine(Vector3 a, Vector3 b)
        {
            if (_measureLine == null) return;
            _measureLine.enabled = true;
            _measureLine.SetPosition(0, a + Vector3.up * 0.02f);
            _measureLine.SetPosition(1, b + Vector3.up * 0.02f);
        }

        private void CreateMeasurementLineRenderer()
        {
            if (_measureLine != null) return;
            var go = new GameObject("BlueprintCalibrationLine");
            go.transform.SetParent(transform, false);
            _measureLine = go.AddComponent<LineRenderer>();
            _measureLine.positionCount = 2;
            _measureLine.startWidth = 0.04f;
            _measureLine.endWidth = 0.04f;
            _measureLine.useWorldSpace = true;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _measureLine.material = new Material(shader) { color = new Color(1f, 0.55f, 0.0f, 1f) };
            }
            _measureLine.enabled = false;
        }
    }
}
