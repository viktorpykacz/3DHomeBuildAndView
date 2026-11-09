using System;
using System.Collections.Generic;
using HomeBuilder.Assets;
using HomeBuilder.Core;
using HomeBuilder.Core.Commands;
using HomeBuilder.Data;
using HomeBuilder.Snapping;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HomeBuilder.Tools
{
    public struct FurnitureSnapTelemetry
    {
        public string ActiveAssetName;
        public Vector3 DimensionsSize;
        public Vector3 WorldPosition;
        public bool IsSnappedBackToWall;
        public bool IsEdgeSnappedToNeighbor;
        public float MountElevationAboveFloor;
        public string StatusMessage;
    }

    /// <summary>
    /// Interactive tool for placing built-in catalog cabinets/furniture and runtime-imported .glb/.gltf models (Sprint 3).
    /// Uses SnappingEngine for Back-to-Wall alignment and Edge-to-Edge cabinet snapping.
    /// </summary>
    public sealed class FurniturePlacementTool : MonoBehaviour
    {
        [SerializeField] private Camera viewportCamera;
        [SerializeField] private FloorSceneRebuilder sceneRebuilder;
        [SerializeField] private LayerMask wallLayerMask = ~0;

        private readonly SnappingEngine _snappingEngine = new();
        private readonly RuntimeFurnitureImporter _gltfImporter = new();

        private CommandHistoryManager _commandHistory;
        private FloorData _activeFloor;
        private bool _isToolActive;

        private string _activeCatalogOrCustomId = BuiltInFurnitureCatalog.IdCabinetBase60;
        private bool _isCustomExternalModel;
        private Bounds _activeNormalizedBounds;
        private float _activeMountElevationY;
        private bool _activeHasApplianceExclusionZone;
        private float _freeStandingYawDegrees;

        private GameObject _previewModelGo;
        private Material _previewGhostMaterial;

        public event Action<FurnitureSnapTelemetry> OnFurnitureTelemetryChanged;

        public string ActiveAssetId => _activeCatalogOrCustomId;

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
            CreatePreviewGhostMaterial();
            SelectBuiltInCatalogItem(BuiltInFurnitureCatalog.IdCabinetBase60);
        }

        public void SetActiveFloor(FloorData floor)
        {
            _activeFloor = floor;
            SetPreviewVisible(false);
        }

        public void ActivateFurnitureTool(bool active)
        {
            _isToolActive = active;
            SetPreviewVisible(active);
            if (active && _previewModelGo == null)
            {
                SelectBuiltInCatalogItem(_activeCatalogOrCustomId);
            }
        }

        public void SelectBuiltInCatalogItem(string catalogAssetId)
        {
            BuiltInCatalogEntry entry = BuiltInFurnitureCatalog.GetEntryOrDefault(catalogAssetId);
            _activeCatalogOrCustomId = entry.AssetId;
            _isCustomExternalModel = false;
            _activeMountElevationY = entry.DefaultMountElevationY;
            _activeHasApplianceExclusionZone = entry.HasApplianceExclusionZone;
            _activeNormalizedBounds = new Bounds(
                new Vector3(0f, entry.DimensionsSize.y * 0.5f, entry.DimensionsSize.z * 0.5f),
                entry.DimensionsSize);

            RebuildPreviewGhostObject();
        }

        /// <summary>
        /// Imports an external .glb / .gltf model from disk at runtime, normalizes its pivot to Bottom-Back-Center,
        /// and sets it as the active placement model.
        /// </summary>
        public async void ImportAndSelectCustomGltfAsync(string gltfFilePath, ProjectData project)
        {
            if (string.IsNullOrWhiteSpace(gltfFilePath)) return;

            if (_previewModelGo != null) Destroy(_previewModelGo);

            GameObject importedPreview = await _gltfImporter.ImportAndNormalizeGltfAsync(gltfFilePath, transform, gltfFilePath);
            if (importedPreview == null) return;

            _activeCatalogOrCustomId = gltfFilePath;
            _isCustomExternalModel = true;
            _activeMountElevationY = 0.0f;
            _activeHasApplianceExclusionZone = false;

            if (importedPreview.TryGetComponent(out FurnitureInstanceIdentifier idTag))
            {
                _activeNormalizedBounds = idTag.NormalizedLocalBounds;
            }
            else
            {
                _activeNormalizedBounds = new Bounds(new Vector3(0f, 0.4f, 0.3f), new Vector3(0.6f, 0.8f, 0.6f));
            }

            if (project != null && !project.CustomAssetLibrary.Exists(e => e.AssetId == gltfFilePath))
            {
                project.CustomAssetLibrary.Add(new CustomAssetManifestEntry
                {
                    AssetId = gltfFilePath,
                    OriginalFileName = System.IO.Path.GetFileName(gltfFilePath),
                    RelativeLocalPath = gltfFilePath,
                    NormalizedBoundsSize = _activeNormalizedBounds.size
                });
            }

            PreparePreviewObjectRenderersAndColliders(importedPreview);
            _previewModelGo = importedPreview;
            _previewModelGo.SetActive(_isToolActive);
        }

        private void Update()
        {
            if (!_isToolActive || _activeFloor == null || viewportCamera == null) return;

            // Rotate free-standing furniture with R key
            if (Input.GetKeyDown(KeyCode.R))
            {
                _freeStandingYawDegrees = (_freeStandingYawDegrees + 45f) % 360f;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                SetPreviewVisible(false);
                return;
            }

            Ray cursorRay = viewportCamera.ScreenPointToRay(Input.mousePosition);

            // Right-Click on any existing furniture deletes it via DeleteFurnitureCommand (Ctrl+Z reversible)
            if (Input.GetMouseButtonDown(1) &&
                Physics.Raycast(cursorRay, out RaycastHit rightHit, 200f) &&
                rightHit.collider.TryGetComponentInParent(out FurnitureInstanceIdentifier existingTag))
            {
                FurnitureData toDelete = _activeFloor.Furnitures.Find(f => f.InstanceId == existingTag.InstanceId);
                if (toDelete != null)
                {
                    var delCmd = new DeleteFurnitureCommand(
                        _activeFloor,
                        toDelete,
                        floor => sceneRebuilder.RebuildFloorFurnitures(floor));
                    _commandHistory?.ExecuteCommand(delCmd);
                }
                return;
            }

            IReadOnlyList<(Transform transform, Bounds localBounds, Guid? wallId)> placedCabinets =
                sceneRebuilder != null
                    ? sceneRebuilder.GetPlacedCabinetsOnFloor(_activeFloor.FloorId)
                    : null;

            CabinetSnapResult snap = _snappingEngine.ComputeCabinetSnap(
                cursorRay,
                _activeNormalizedBounds,
                _activeFloor.ElevationY,
                _activeMountElevationY,
                wallLayerMask,
                placedCabinets);

            Quaternion finalRotation = snap.SnappedWallId.HasValue
                ? snap.WorldRotation
                : Quaternion.Euler(0f, _freeStandingYawDegrees, 0f);

            if (_previewModelGo != null)
            {
                _previewModelGo.SetActive(true);
                _previewModelGo.transform.SetPositionAndRotation(snap.WorldPosition, finalRotation);
            }

            Vector3 sz = _activeNormalizedBounds.size;
            string snapInfo = snap.IsEdgeSnapped
                ? "[PRZYCIĄGNIĘTO BOK-DO-BOKU (Edge-to-Edge) + TYŁEM DO ŚCIANY]"
                : snap.SnappedWallId.HasValue
                    ? "[PRZYCIĄGNIĘTO TYŁEM DO ŚCIANY (Back-to-Wall)]"
                    : $"[WOLNOSTOJĄCY NA PODŁODZE | Obrót (R): {_freeStandingYawDegrees:F0}°]";

            OnFurnitureTelemetryChanged?.Invoke(new FurnitureSnapTelemetry
            {
                ActiveAssetName = _activeCatalogOrCustomId,
                DimensionsSize = sz,
                WorldPosition = snap.WorldPosition,
                IsSnappedBackToWall = snap.SnappedWallId.HasValue,
                IsEdgeSnappedToNeighbor = snap.IsEdgeSnapped,
                MountElevationAboveFloor = _activeMountElevationY,
                StatusMessage = $"Mebel: {_activeCatalogOrCustomId} ({sz.x * 100f:F0}×{sz.y * 100f:F0}×{sz.z * 100f:F0} cm) | Wys. montażu: {_activeMountElevationY:F2}m | {snapInfo} | PPM: usuń"
            });

            if (Input.GetMouseButtonDown(0))
            {
                var furnitureData = new FurnitureData
                {
                    CatalogOrCustomAssetId = _activeCatalogOrCustomId,
                    IsExternalCustomModel = _isCustomExternalModel,
                    Position = snap.WorldPosition,
                    RotationEuler = finalRotation.eulerAngles,
                    Scale = Vector3.one,
                    SnappedWallId = snap.SnappedWallId,
                    HasApplianceExclusionZone = _activeHasApplianceExclusionZone
                };

                var placeCmd = new PlaceFurnitureCommand(
                    _activeFloor,
                    furnitureData,
                    (_, _) => sceneRebuilder.RebuildFloorFurnitures(_activeFloor));

                _commandHistory?.ExecuteCommand(placeCmd);
            }
        }

        private void RebuildPreviewGhostObject()
        {
            if (_previewModelGo != null) Destroy(_previewModelGo);

            _previewModelGo = BuiltInFurnitureCatalog.CreateCatalogModelInstance(
                _activeCatalogOrCustomId,
                transform,
                _previewGhostMaterial,
                _previewGhostMaterial,
                _previewGhostMaterial,
                _previewGhostMaterial);

            PreparePreviewObjectRenderersAndColliders(_previewModelGo);
            _previewModelGo.SetActive(_isToolActive);
        }

        private void PreparePreviewObjectRenderersAndColliders(GameObject previewRoot)
        {
            if (previewRoot == null) return;

            // Disable colliders on the ghost preview so it never blocks Physics.Raycast to walls/floors!
            foreach (Collider col in previewRoot.GetComponentsInChildren<Collider>(true))
            {
                col.enabled = false;
            }

            if (_previewGhostMaterial != null)
            {
                foreach (Renderer r in previewRoot.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterial = _previewGhostMaterial;
                }
            }
        }

        private void SetPreviewVisible(bool visible)
        {
            if (_previewModelGo != null) _previewModelGo.SetActive(visible);
        }

        private void CreatePreviewGhostMaterial()
        {
            if (_previewGhostMaterial != null) return;
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            _previewGhostMaterial = new Material(shader) { name = "Mat_FurniturePreviewGhost" };
            Color cyan = new(0.15f, 0.78f, 0.95f, 0.72f);
            if (_previewGhostMaterial.HasProperty("_BaseColor")) _previewGhostMaterial.SetColor("_BaseColor", cyan);
            if (_previewGhostMaterial.HasProperty("_Color")) _previewGhostMaterial.SetColor("_Color", cyan);
        }
    }
}
