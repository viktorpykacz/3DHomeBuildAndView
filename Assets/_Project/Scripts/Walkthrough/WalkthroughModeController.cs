using System;
using System.Collections.Generic;
using HomeBuilder.Core;
using Unity.Cinemachine;
using UnityEngine;

namespace HomeBuilder.Walkthrough
{
    public enum ApplicationViewMode
    {
        Floorplan2DOrtho,
        Design3DOrbit,
        Elevation2DWall,
        WalkthroughFPP
    }

    public interface IWalkthroughInteractable
    {
        string GetInteractionPrompt();
        void Interact();
    }

    /// <summary>
    /// Controls switching between CAD Design cameras (2D Ortho, 3D Orbit, 2D Elevation)
    /// and Photorealistic First-Person Walkthrough (FPP) mode with interactive light switches,
    /// hinged doors/cabinets, mouse-look, WASD movement, and real-time Sun Time-of-Day control (Sprint 5).
    /// </summary>
    public sealed class WalkthroughModeController : MonoBehaviour
    {
        [Header("Cameras & Controllers")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private CadViewportCameraController cadCameraController;
        [SerializeField] private HDRPRealtimeEnvironmentController environmentController;
        [SerializeField] private CinemachineCamera ortho2DCamera;
        [SerializeField] private CinemachineCamera orbit3DCamera;
        [SerializeField] private CinemachineCamera walkthroughFppCamera;

        [Header("Culling Masks")]
        [SerializeField] private LayerMask designCullingMask = ~0;
        [SerializeField] private LayerMask walkthroughCullingMask = ~0;

        [Header("First-Person Controller")]
        [SerializeField] private CharacterController fppCharacter;
        [SerializeField] private Transform fppCameraHeadSocket;
        [SerializeField] private float moveSpeed = 3.2f;
        [SerializeField] private float lookSensitivity = 2.2f;
        [SerializeField] private float interactionRange = 2.4f;
        [SerializeField] private LayerMask interactableLayerMask = ~0;

        private Vector3 _savedCadCameraPos;
        private Quaternion _savedCadCameraRot;
        private bool _savedCadOrtho;
        private float _fppYawDegrees;
        private float _fppPitchDegrees;

        public ApplicationViewMode CurrentMode { get; private set; } = ApplicationViewMode.Design3DOrbit;
        public bool IsWalkthroughActive => CurrentMode == ApplicationViewMode.WalkthroughFPP;

        public event Action<ApplicationViewMode> OnViewModeChanged;
        public event Action<string> OnHoverPromptChanged;

        public void Initialize(
            Camera cam,
            CadViewportCameraController cadCamCtrl,
            HDRPRealtimeEnvironmentController envCtrl)
        {
            mainCamera = cam;
            cadCameraController = cadCamCtrl;
            environmentController = envCtrl;
            EnsureFppCharacterRigExists();
        }

        public void ToggleWalkthroughMode(Vector3? spawnPosition = null)
        {
            if (IsWalkthroughActive)
            {
                SetViewMode(ApplicationViewMode.Design3DOrbit);
            }
            else
            {
                SetViewMode(ApplicationViewMode.WalkthroughFPP, spawnPosition ?? new Vector3(0.0f, 0.08f, -0.5f));
            }
        }

        public void SetViewMode(ApplicationViewMode mode, Vector3? spawnPosition = null)
        {
            EnsureFppCharacterRigExists();

            bool wasWalkthrough = IsWalkthroughActive;
            CurrentMode = mode;
            bool isWalkthrough = (mode == ApplicationViewMode.WalkthroughFPP);

            if (ortho2DCamera != null) ortho2DCamera.Priority = mode == ApplicationViewMode.Floorplan2DOrtho ? 20 : 0;
            if (orbit3DCamera != null) orbit3DCamera.Priority = mode == ApplicationViewMode.Design3DOrbit ? 20 : 0;
            if (walkthroughFppCamera != null) walkthroughFppCamera.Priority = isWalkthrough ? 20 : 0;

            if (isWalkthrough && !wasWalkthrough && mainCamera != null)
            {
                _savedCadCameraPos = mainCamera.transform.position;
                _savedCadCameraRot = mainCamera.transform.rotation;
                _savedCadOrtho = mainCamera.orthographic;
            }

            if (cadCameraController != null)
            {
                cadCameraController.enabled = !isWalkthrough;
            }

            if (environmentController != null)
            {
                environmentController.ConfigurePhysicalCamera(isWalkthrough);
                environmentController.RefreshInteriorReflectionProbe();
            }

            if (mainCamera != null)
            {
                mainCamera.cullingMask = isWalkthrough ? walkthroughCullingMask : designCullingMask;
                if (isWalkthrough)
                {
                    mainCamera.orthographic = false;
                }
                else if (wasWalkthrough)
                {
                    mainCamera.transform.SetPositionAndRotation(_savedCadCameraPos, _savedCadCameraRot);
                    mainCamera.orthographic = _savedCadOrtho;
                }
            }

            if (fppCharacter != null)
            {
                fppCharacter.gameObject.SetActive(isWalkthrough);
                if (isWalkthrough)
                {
                    Vector3 targetSpawn = spawnPosition ?? new Vector3(0.0f, 0.08f, -0.5f);
                    fppCharacter.enabled = false;
                    fppCharacter.transform.position = targetSpawn;
                    fppCharacter.enabled = true;
                    _fppYawDegrees = 0f;
                    _fppPitchDegrees = 0f;
                }
            }

            UnityEngine.Cursor.lockState = isWalkthrough ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !isWalkthrough;

            if (!isWalkthrough)
            {
                OnHoverPromptChanged?.Invoke(string.Empty);
            }

            OnViewModeChanged?.Invoke(CurrentMode);
        }

        public void SetTimeOfDay(float hour0To24)
        {
            if (environmentController != null)
            {
                environmentController.ApplyTimeOfDay(hour0To24);
            }
        }

        private void Update()
        {
            // Tab key toggles between CAD Design Mode and FPP Walkthrough Mode at any time
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                ToggleWalkthroughMode();
                return;
            }

            if (!IsWalkthroughActive) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetViewMode(ApplicationViewMode.Design3DOrbit);
                return;
            }

            // Quick floor teleport inside Walkthrough mode (1 = Ground Floor, 2 = Attic Floor)
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                TeleportPlayerToElevation(0.08f);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                TeleportPlayerToElevation(3.08f);
            }

            HandleFppMouseLook();
            HandleFppMovement();
            HandleInteractionRaycast();
        }

        private void TeleportPlayerToElevation(float elevationY)
        {
            if (fppCharacter == null) return;
            fppCharacter.enabled = false;
            Vector3 pos = fppCharacter.transform.position;
            fppCharacter.transform.position = new Vector3(pos.x, elevationY, pos.z);
            fppCharacter.enabled = true;
        }

        private void HandleFppMouseLook()
        {
            if (fppCharacter == null || mainCamera == null) return;

            float mx = Input.GetAxis("Mouse X") * lookSensitivity;
            float my = Input.GetAxis("Mouse Y") * lookSensitivity;

            _fppYawDegrees += mx;
            _fppPitchDegrees = Mathf.Clamp(_fppPitchDegrees - my, -82f, 82f);

            fppCharacter.transform.rotation = Quaternion.Euler(0f, _fppYawDegrees, 0f);

            if (fppCameraHeadSocket != null)
            {
                fppCameraHeadSocket.localRotation = Quaternion.Euler(_fppPitchDegrees, 0f, 0f);
                mainCamera.transform.SetPositionAndRotation(fppCameraHeadSocket.position, fppCameraHeadSocket.rotation);
            }
        }

        private void HandleFppMovement()
        {
            if (fppCharacter == null || !fppCharacter.enabled) return;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            Vector3 forward = fppCharacter.transform.forward;
            Vector3 right = fppCharacter.transform.right;
            Vector3 moveDir = (forward * v + right * h).normalized;

            float speed = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                ? moveSpeed * 1.55f
                : moveSpeed;

            Vector3 velocity = moveDir * speed + Vector3.down * 9.81f;
            fppCharacter.Move(velocity * Time.deltaTime);
        }

        private void HandleInteractionRaycast()
        {
            if (mainCamera == null) return;
            Ray centerRay = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            if (Physics.Raycast(centerRay, out RaycastHit hit, interactionRange, interactableLayerMask) &&
                hit.collider.TryGetComponentInParent(out IWalkthroughInteractable interactable))
            {
                OnHoverPromptChanged?.Invoke(interactable.GetInteractionPrompt());
                if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E))
                {
                    interactable.Interact();
                }
            }
            else
            {
                OnHoverPromptChanged?.Invoke(string.Empty);
            }
        }

        private void EnsureFppCharacterRigExists()
        {
            if (fppCharacter != null) return;

            var playerGo = new GameObject("WalkthroughPlayer_FPP");
            playerGo.transform.SetParent(transform, false);

            fppCharacter = playerGo.AddComponent<CharacterController>();
            fppCharacter.height = 1.76f;
            fppCharacter.radius = 0.28f;
            fppCharacter.center = new Vector3(0f, 0.88f, 0f);
            fppCharacter.stepOffset = 0.30f;

            var headGo = new GameObject("EyeLevelCameraSocket_168cm");
            headGo.transform.SetParent(playerGo.transform, false);
            headGo.transform.localPosition = new Vector3(0f, 1.68f, 0f);
            fppCameraHeadSocket = headGo.transform;

            playerGo.SetActive(false);
        }
    }

    public sealed class WallSwitchInteractable : MonoBehaviour, IWalkthroughInteractable
    {
        public Guid SwitchMEPId { get; set; }
        private readonly List<Light> _connectedLights = new();
        private bool _isTurnedOn = true;

        public void BindConnectedLights(IEnumerable<Light> lights)
        {
            _connectedLights.Clear();
            _connectedLights.AddRange(lights);
        }

        public string GetInteractionPrompt() =>
            _isTurnedOn
                ? $"[LPM / E] Wyłącz oświetlenie ({_connectedLights.Count} pkt.)"
                : $"[LPM / E] Włącz oświetlenie ({_connectedLights.Count} pkt.)";

        public void Interact()
        {
            _isTurnedOn = !_isTurnedOn;
            foreach (Light lightSource in _connectedLights)
            {
                if (lightSource != null) lightSource.enabled = _isTurnedOn;
            }
        }
    }

    public sealed class HingedDoorInteractable : MonoBehaviour, IWalkthroughInteractable
    {
        [SerializeField] private Transform hingePivot;
        [SerializeField] private float openAngleDegrees = 90f;
        [SerializeField] private float openSpeed = 4.5f;

        private bool _isOpen;
        private Quaternion _closedRotation;
        private Quaternion _openRotation;

        private void Awake()
        {
            if (hingePivot == null)
            {
                Transform childHinge = transform.Find("DoorHingePivot") ?? transform.Find("CabinetDoorHinge");
                hingePivot = childHinge != null ? childHinge : transform;
            }
            _closedRotation = hingePivot.localRotation;
            _openRotation = _closedRotation * Quaternion.Euler(0f, openAngleDegrees, 0f);
        }

        private void Update()
        {
            if (hingePivot == null) return;
            Quaternion target = _isOpen ? _openRotation : _closedRotation;
            hingePivot.localRotation = Quaternion.Slerp(hingePivot.localRotation, target, Time.deltaTime * openSpeed);
        }

        public string GetInteractionPrompt() =>
            _isOpen ? "[LPM / E] Zamknij drzwi / szafkę" : "[LPM / E] Otwórz drzwi / szafkę";

        public void Interact()
        {
            _isOpen = !_isOpen;
        }
    }
}
