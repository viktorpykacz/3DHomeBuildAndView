using System;
using System.IO;
using HomeBuilder.Data;
using UnityEngine;

namespace HomeBuilder.Floorplan
{
    /// <summary>
    /// Loads a 2D architectural floorplan (PNG/JPG) from disk and calibrates its real-world scale
    /// using 2 user-clicked reference points and a user-supplied real length in meters (1 Unity Unit = 1 Meter).
    /// </summary>
    public sealed class BlueprintCalibrationController : MonoBehaviour
    {
        [SerializeField] private MeshRenderer blueprintQuadRenderer;

        private Texture2D _loadedTexture;
        private BlueprintReferenceData _currentBlueprintData;

        private void Awake()
        {
            if (blueprintQuadRenderer == null)
            {
                blueprintQuadRenderer = GetComponent<MeshRenderer>();
            }
        }

        public void LoadBlueprintImageFromDisk(string filePath, BlueprintReferenceData blueprintData, float floorElevationY)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[BlueprintCalibrationController] Plik rzutu nie istnieje: {filePath}");
                return;
            }

            byte[] fileBytes = File.ReadAllBytes(filePath);
            if (_loadedTexture != null) Destroy(_loadedTexture);

            _loadedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true);
            if (!_loadedTexture.LoadImage(fileBytes))
            {
                Debug.LogError($"[BlueprintCalibrationController] Nie udało się zdekodować obrazu: {filePath}");
                return;
            }

            _currentBlueprintData = blueprintData;
            _currentBlueprintData.FilePath = filePath;

            if (blueprintQuadRenderer != null)
            {
                Material mat = blueprintQuadRenderer.material;
                mat.mainTexture = _loadedTexture;
                if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", _loadedTexture);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", _loadedTexture);
                ApplyTransformFromCalibration(floorElevationY);
            }
        }

        /// <summary>
        /// Calibrates the scale given two world points clicked on the uncalibrated quad and the real-world distance in meters.
        /// Ensures 1 Unity unit = 1 real-world meter.
        /// </summary>
        public void ApplyTwoPointScaleCalibration(
            Vector3 worldPointA,
            Vector3 worldPointB,
            float realWorldDistanceMeters,
            float floorElevationY)
        {
            if (_loadedTexture == null || _currentBlueprintData == null) return;
            if (realWorldDistanceMeters <= 0.001f) return;

            float currentWorldDistance = Vector2.Distance(
                new Vector2(worldPointA.x, worldPointA.z),
                new Vector2(worldPointB.x, worldPointB.z));

            if (currentWorldDistance <= 1e-5f) return;

            float scaleCorrectionRatio = realWorldDistanceMeters / currentWorldDistance;
            _currentBlueprintData.MetersPerPixel *= scaleCorrectionRatio;

            ApplyTransformFromCalibration(floorElevationY);
        }

        public void ApplyTransformFromCalibration(float floorElevationY)
        {
            if (_loadedTexture == null || _currentBlueprintData == null || blueprintQuadRenderer == null) return;

            float widthMeters = _loadedTexture.width * _currentBlueprintData.MetersPerPixel;
            float heightMeters = _loadedTexture.height * _currentBlueprintData.MetersPerPixel;

            Transform t = blueprintQuadRenderer.transform;
            Vector2 offset = _currentBlueprintData.WorldOffset.ToVector2();
            t.position = new Vector3(offset.x, floorElevationY + 0.002f, offset.y);
            t.rotation = Quaternion.Euler(90f, _currentBlueprintData.RotationDegrees, 0f);
            t.localScale = new Vector3(widthMeters, heightMeters, 1f);
            blueprintQuadRenderer.enabled = _currentBlueprintData.IsVisible;
        }
    }
}
