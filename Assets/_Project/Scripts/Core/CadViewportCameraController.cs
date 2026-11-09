using UnityEngine;

namespace HomeBuilder.Core
{
    /// <summary>
    /// Provides CAD-style viewport navigation for Sprint 1:
    /// - 2D Top-Down Orthographic Floorplan view (Pan with MMB, Zoom with ScrollWheel)
    /// - 3D Perspective Orbit view (Orbit with Alt+LMB or RMB, Pan with MMB, Zoom with ScrollWheel)
    /// </summary>
    public sealed class CadViewportCameraController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Vector3 pivotPoint = Vector3.zero;
        [SerializeField] private float orbitDistance = 16.0f;
        [SerializeField] private float orthoSize = 8.0f;
        [SerializeField] private float yawDegrees = -35f;
        [SerializeField] private float pitchDegrees = 48f;

        [Header("Speeds")]
        [SerializeField] private float orbitSensitivity = 3.5f;
        [SerializeField] private float panSensitivity = 0.02f;
        [SerializeField] private float zoomSensitivity = 1.2f;

        public bool Is2DOrthographicMode { get; private set; } = false;
        public bool IsWallElevation2DMode { get; private set; } = false;
        private Vector3 _elevationCameraPos;
        private Quaternion _elevationCameraRot;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = GetComponent<Camera>();
            if (targetCamera == null) targetCamera = Camera.main;
            ApplyCameraTransform();
        }

        public void Set2DOrthographicMode(bool enable2DOrtho)
        {
            Is2DOrthographicMode = enable2DOrtho;
            IsWallElevation2DMode = false;
            if (targetCamera != null)
            {
                targetCamera.orthographic = enable2DOrtho;
                targetCamera.nearClipPlane = 0.1f;
                if (enable2DOrtho)
                {
                    targetCamera.orthographicSize = orthoSize;
                }
            }
            ApplyCameraTransform();
        }

        /// <summary>
        /// Aligns the camera orthogonally to a specific wall face (2D Wall Elevation View for MEP & Kitchen layout).
        /// Positions the camera 1.35m in front of the wall face so opposite walls and center-room furniture never occlude the view.
        /// </summary>
        public void EnterWallElevation2DView(Vector2 wallStartXZ, Vector2 wallEndXZ, float floorElevationY, float wallHeight, Vector3 faceNormal)
        {
            IsWallElevation2DMode = true;
            Is2DOrthographicMode = false;

            Vector2 midXZ = (wallStartXZ + wallEndXZ) * 0.5f;
            float wallLength = Vector2.Distance(wallStartXZ, wallEndXZ);
            Vector3 wallCenter3D = new(midXZ.x, floorElevationY + wallHeight * 0.5f, midXZ.y);
            Vector3 n = new Vector3(faceNormal.x, 0f, faceNormal.z).normalized;
            if (n.sqrMagnitude < 1e-4f) n = Vector3.forward;

            // Stand 1.35m inside the room looking directly at the wall (-n)
            _elevationCameraPos = wallCenter3D + n * 1.35f;
            _elevationCameraRot = Quaternion.LookRotation(-n, Vector3.up);
            orthoSize = Mathf.Clamp(Mathf.Max(wallHeight * 0.75f, wallLength * 0.38f), 1.6f, 10f);

            if (targetCamera != null)
            {
                targetCamera.orthographic = true;
                targetCamera.orthographicSize = orthoSize;
                targetCamera.nearClipPlane = 0.05f;
            }
            ApplyCameraTransform();
        }

        public void FocusOnElevation(float elevationY)
        {
            IsWallElevation2DMode = false;
            pivotPoint.y = elevationY;
            ApplyCameraTransform();
        }

        private void LateUpdate()
        {
            if (targetCamera == null) return;

            bool isOrtho = Is2DOrthographicMode || IsWallElevation2DMode;

            // Scroll Wheel Zoom
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                if (isOrtho)
                {
                    orthoSize = Mathf.Clamp(orthoSize - scroll * zoomSensitivity * 0.5f, 1.0f, 60f);
                    targetCamera.orthographicSize = orthoSize;
                }
                else
                {
                    orbitDistance = Mathf.Clamp(orbitDistance - scroll * zoomSensitivity * 1.4f, 2.0f, 85f);
                }
                ApplyCameraTransform();
            }

            // Middle Mouse Button Pan
            if (Input.GetMouseButton(2))
            {
                float scaleFactor = isOrtho ? orthoSize : orbitDistance * 0.6f;
                float dx = -Input.GetAxis("Mouse X") * panSensitivity * scaleFactor;
                float dy = -Input.GetAxis("Mouse Y") * panSensitivity * scaleFactor;

                if (IsWallElevation2DMode)
                {
                    _elevationCameraPos += targetCamera.transform.right * dx + targetCamera.transform.up * dy;
                }
                else
                {
                    Vector3 right = targetCamera.transform.right;
                    Vector3 upPlanar = Vector3.ProjectOnPlane(targetCamera.transform.up, Vector3.up).normalized;
                    if (upPlanar.sqrMagnitude < 1e-4f)
                    {
                        upPlanar = Vector3.ProjectOnPlane(targetCamera.transform.forward, Vector3.up).normalized;
                    }
                    pivotPoint += right * dx + upPlanar * dy;
                }
                ApplyCameraTransform();
            }

            // Orbit in 3D Mode (Alt + Left Mouse Button)
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (altHeld && Input.GetMouseButton(0))
            {
                if (IsWallElevation2DMode)
                {
                    IsWallElevation2DMode = false;
                    targetCamera.orthographic = false;
                }
                if (!Is2DOrthographicMode)
                {
                    yawDegrees += Input.GetAxis("Mouse X") * orbitSensitivity;
                    pitchDegrees = Mathf.Clamp(pitchDegrees - Input.GetAxis("Mouse Y") * orbitSensitivity, 10f, 88f);
                    ApplyCameraTransform();
                }
            }
        }

        private void ApplyCameraTransform()
        {
            if (targetCamera == null) return;

            if (IsWallElevation2DMode)
            {
                targetCamera.transform.SetPositionAndRotation(_elevationCameraPos, _elevationCameraRot);
                targetCamera.orthographicSize = orthoSize;
            }
            else if (Is2DOrthographicMode)
            {
                targetCamera.transform.position = new Vector3(pivotPoint.x, pivotPoint.y + 35f, pivotPoint.z);
                targetCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                targetCamera.orthographicSize = orthoSize;
            }
            else
            {
                Quaternion rot = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
                targetCamera.transform.position = pivotPoint - (rot * Vector3.forward * orbitDistance);
                targetCamera.transform.rotation = rot;
            }
        }
    }
}
