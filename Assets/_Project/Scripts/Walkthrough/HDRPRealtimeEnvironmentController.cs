using HomeBuilder.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace HomeBuilder.Walkthrough
{
    /// <summary>
    /// Configures Unity 6 HDRP real-time architectural lighting without offline lightmap baking (Sprint 5):
    /// - Physical Camera (36x24mm Full Frame sensor, 20mm wide-angle interior lens, f/8.0)
    /// - Real-time Solar Position & Color Temperature (Time-of-Day 00:00 - 24:00, Latitude, North Azimuth)
    /// - On-Demand Realtime Reflection Probe updated automatically when BIM geometry changes
    /// </summary>
    public sealed class HDRPRealtimeEnvironmentController : MonoBehaviour
    {
        [Header("Scene Lighting & Camera")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Light sunDirectionalLight;
        [SerializeField] private ReflectionProbe interiorReflectionProbe;

        [Header("Physical Camera Settings (Architectural 20mm)")]
        [SerializeField] private Vector2 sensorSizeMm = new(36f, 24f);
        [SerializeField] private float designFocalLengthMm = 28f;
        [SerializeField] private float walkthroughFocalLengthMm = 20f;

        private EnvironmentSettingsData _envSettings = new();

        public float CurrentTimeOfDayHours => _envSettings.TimeOfDayHours;

        public void Initialize(Camera cam, EnvironmentSettingsData environmentData)
        {
            targetCamera = cam;
            if (environmentData != null) _envSettings = environmentData;

            EnsureSunLightExists();
            EnsureRealtimeReflectionProbeExists();
            ConfigurePhysicalCamera(isWalkthroughMode: false);
            ApplyTimeOfDay(_envSettings.TimeOfDayHours);
            RefreshInteriorReflectionProbe();
        }

        public void ConfigurePhysicalCamera(bool isWalkthroughMode)
        {
            if (targetCamera == null) return;

            targetCamera.usePhysicalProperties = true;
            targetCamera.sensorSize = sensorSizeMm;
            targetCamera.focalLength = isWalkthroughMode ? walkthroughFocalLengthMm : designFocalLengthMm;
            targetCamera.nearClipPlane = isWalkthroughMode ? 0.08f : 0.10f;
            targetCamera.farClipPlane = 500f;
        }

        /// <summary>
        /// Updates solar elevation, azimuth, color temperature, and intensity in real time.
        /// </summary>
        public void ApplyTimeOfDay(float hour0To24)
        {
            _envSettings.TimeOfDayHours = Mathf.Clamp(hour0To24, 0f, 24f);
            EnsureSunLightExists();

            // Solar trajectory: 06:00 sunrise, 13:00 solar noon, 20:00 sunset
            float dayProgress = (_envSettings.TimeOfDayHours - 6.0f) / 14.0f;
            float maxNoonElevation = Mathf.Clamp(90f - (_envSettings.Latitude - 23.5f), 25f, 75f);
            float elevationDeg = Mathf.Sin(dayProgress * Mathf.PI) * maxNoonElevation;
            float azimuthDeg = Mathf.Lerp(75f, 285f, dayProgress) + _envSettings.NorthAngleDegrees;

            sunDirectionalLight.transform.rotation = Quaternion.Euler(elevationDeg, azimuthDeg, 0f);

            bool isDaytime = elevationDeg > -3.0f;
            sunDirectionalLight.enabled = true;

            if (isDaytime)
            {
                float horizonFactor = Mathf.Clamp01(elevationDeg / 35f);
                // Warm golden sunset/sunrise (1.0, 0.68, 0.42) -> Neutral daylight (1.0, 0.97, 0.92)
                sunDirectionalLight.color = Color.Lerp(
                    new Color(1.0f, 0.65f, 0.40f),
                    new Color(1.0f, 0.97f, 0.92f),
                    horizonFactor);

                sunDirectionalLight.intensity = Mathf.Lerp(0.4f, 1.6f, horizonFactor);
                RenderSettings.ambientIntensity = Mathf.Lerp(0.45f, 1.05f, horizonFactor);
            }
            else
            {
                // Night / Evening moonlight so interior MEP ceiling lights stand out dramatically
                sunDirectionalLight.transform.rotation = Quaternion.Euler(35f, 210f, 0f);
                sunDirectionalLight.color = new Color(0.35f, 0.45f, 0.70f);
                sunDirectionalLight.intensity = 0.08f;
                RenderSettings.ambientIntensity = 0.20f;
            }
        }

        /// <summary>
        /// Triggers an on-demand cubemap update on the interior ReflectionProbe when walls or furniture change.
        /// Avoids per-frame GPU overhead while keeping reflections accurate without baking lightmaps.
        /// </summary>
        public void RefreshInteriorReflectionProbe()
        {
            if (interiorReflectionProbe != null && interiorReflectionProbe.gameObject.activeInHierarchy)
            {
                interiorReflectionProbe.RenderProbe();
            }
        }

        private void EnsureSunLightExists()
        {
            if (sunDirectionalLight != null) return;

            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (Light l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    sunDirectionalLight = l;
                    break;
                }
            }

            if (sunDirectionalLight == null)
            {
                var sunGo = new GameObject("DirectionalSunLight_HDRP");
                sunGo.transform.SetParent(transform, false);
                sunDirectionalLight = sunGo.AddComponent<Light>();
                sunDirectionalLight.type = LightType.Directional;
            }

            sunDirectionalLight.shadows = LightShadows.Soft;
            sunDirectionalLight.shadowBias = 0.02f;
            sunDirectionalLight.shadowNormalBias = 0.3f;
        }

        private void EnsureRealtimeReflectionProbeExists()
        {
            if (interiorReflectionProbe != null) return;

            var probeGo = new GameObject("InteriorRealtimeReflectionProbe");
            probeGo.transform.SetParent(transform, false);
            probeGo.transform.position = new Vector3(0f, 1.50f, 0f);

            interiorReflectionProbe = probeGo.AddComponent<ReflectionProbe>();
            interiorReflectionProbe.mode = ReflectionProbeMode.Realtime;
            interiorReflectionProbe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            interiorReflectionProbe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            interiorReflectionProbe.size = new Vector3(12f, 8f, 12f);
            interiorReflectionProbe.boxProjection = true;
            interiorReflectionProbe.resolution = 256;
        }
    }
}
