using System;
using System.Collections.Generic;
using HomeBuilder.Assets;
using HomeBuilder.Data;
using HomeBuilder.Geometry;
using HomeBuilder.Snapping;
using UnityEngine;

namespace HomeBuilder.Core
{
    /// <summary>
    /// Synchronizes the BIM domain model (FloorData -> Walls, Openings, Rooms, Roofs, Furnitures)
    /// with Unity 3D scene GameObjects.
    /// </summary>
    public sealed class FloorSceneRebuilder : MonoBehaviour
    {
        [Header("Default Materials (HDRP/Lit)")]
        [SerializeField] private Material defaultWallFrontMaterial;
        [SerializeField] private Material defaultWallBackMaterial;
        [SerializeField] private Material defaultWallEdgeMaterial;
        [SerializeField] private Material defaultFloorMaterial;
        [SerializeField] private Material defaultSlabEdgeMaterial;
        [SerializeField] private Material defaultOpeningFrameMaterial;
        [SerializeField] private Material defaultWindowGlassMaterial;
        [SerializeField] private Material defaultDoorLeafMaterial;
        [SerializeField] private Material defaultRoofMaterial;
        [SerializeField] private Material defaultCabinetBodyMaterial;
        [SerializeField] private Material defaultCabinetFrontMaterial;
        [SerializeField] private Material defaultCountertopMaterial;
        [SerializeField] private Material defaultApplianceMetalMaterial;
        [SerializeField] private Material defaultMEPWhiteMaterial;
        [SerializeField] private Material defaultMEPDarkMaterial;
        [SerializeField] private Material defaultMEPEmissiveMaterial;
        [SerializeField] private Material defaultMEPColdWaterMaterial;
        [SerializeField] private Material defaultMEPHotWaterMaterial;
        [SerializeField] private Material defaultMEPSewageMaterial;

        [Header("Layer Configuration")]
        [SerializeField] private int wallLayer = 0;
        [SerializeField] private int floorLayer = 0;
        [SerializeField] private int applianceHazardLayer = 0;

        private readonly Dictionary<Guid, Transform> _floorRoots = new();
        private readonly Dictionary<Guid, GameObject> _wallObjects = new();
        private readonly Dictionary<Guid, List<GameObject>> _wallOpeningObjects = new();
        private readonly Dictionary<Guid, List<GameObject>> _floorSlabObjects = new();
        private readonly Dictionary<Guid, List<GameObject>> _roofPlaneObjects = new();
        private readonly Dictionary<Guid, List<GameObject>> _floorFurnitureObjects = new();
        private readonly Dictionary<Guid, List<GameObject>> _floorMEPObjects = new();
        private readonly RuntimeFurnitureImporter _gltfImporter = new();
        private bool _showRoofSlabsInDesignMode = true;
        private bool _showMEPWiringLinks = true;

        public event Action<FloorData, int, float> OnRoomsRecalculated; // FloorData, RoomCount, TotalAreaM2

        private void Awake()
        {
            EnsureFallbackMaterials();
        }

        public void RebuildAllFloors(ProjectData project)
        {
            if (project == null) return;
            ClearAllSceneGeometry();
            foreach (FloorData floor in project.Floors)
            {
                RebuildFloorGeometry(floor);
            }
        }

        public void RebuildFloorGeometry(FloorData floor)
        {
            if (floor == null) return;
            EnsureFallbackMaterials();

            Transform floorRoot = GetOrCreateFloorRoot(floor);
            floorRoot.localPosition = new Vector3(0f, floor.ElevationY, 0f);

            // 1. Solve all wall corners (Miter Joins at L, T, X junctions)
            Dictionary<Guid, WallCornerMiter> miters = WallTopologyGraphSolver.SolveFloorWallMiters(floor.Walls);
            List<Plane> roofPlanes = floor.IsAttic
                ? ProceduralWallMeshBuilder.BuildRoofClippingPlanes(floor.RoofPlanes, 0f)
                : null;

            // Remove deleted walls from scene
            var activeWallIds = new HashSet<Guid>();
            foreach (WallData w in floor.Walls) activeWallIds.Add(w.WallId);

            var toRemove = new List<Guid>();
            foreach (var kvp in _wallObjects)
            {
                if (!activeWallIds.Contains(kvp.Key))
                {
                    if (kvp.Value != null) Destroy(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (Guid id in toRemove) _wallObjects.Remove(id);

            // 2. Build or update each wall's 3D mesh, collider, and WallSurfaceIdentifier
            foreach (WallData wall in floor.Walls)
            {
                if (!miters.TryGetValue(wall.WallId, out WallCornerMiter miter)) continue;

                Mesh wallMesh = ProceduralWallMeshBuilder.GenerateWallMesh(wall, miter, roofPlanes);
                GameObject wallGo = GetOrCreateWallGameObject(wall, floorRoot);
                wallGo.layer = wallLayer;

                var mf = wallGo.GetComponent<MeshFilter>();
                if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
                mf.sharedMesh = wallMesh;

                var mr = wallGo.GetComponent<MeshRenderer>();
                mr.sharedMaterials = new[]
                {
                    defaultWallFrontMaterial,
                    defaultWallBackMaterial,
                    defaultWallEdgeMaterial
                };

                var mc = wallGo.GetComponent<MeshCollider>();
                mc.sharedMesh = null;
                mc.sharedMesh = wallMesh;

                var identifier = wallGo.GetComponent<WallSurfaceIdentifier>();
                Vector2 a = wall.StartPoint.ToVector2();
                Vector2 b = wall.EndPoint.ToVector2();
                Vector2 dir = (b - a).normalized;
                identifier.WallId = wall.WallId;
                identifier.FrontStartCornerXZ = miter.StartLeft;
                identifier.FrontEndCornerXZ = miter.EndLeft;
                identifier.BackStartCornerXZ = miter.StartRight;
                identifier.BackEndCornerXZ = miter.EndRight;
                identifier.FrontNormal = new Vector3(-dir.y, 0f, dir.x);

                // Rebuild 3D Window & Door Frames inside this wall's cutouts
                RebuildWallOpeningFrames(wall, floorRoot);
            }

            // 3. Detect closed room cycles and rebuild procedural floor slabs
            RebuildFloorSlabs(floor, floorRoot);

            // 4. Rebuild 3D sloped roof planes if this floor is an attic
            RebuildRoofPlanes(floor, floorRoot);

            // 5. Rebuild 3D furniture & kitchen cabinets (built-in catalog + runtime glTF imports)
            RebuildFloorFurnitures(floor, floorRoot);

            // 6. Rebuild MEP installations (sockets, switches, lights, plumbing, and switch->light wiring links)
            RebuildFloorMEPInstallations(floor, floorRoot);
        }

        public void SetMEPWiringLinksVisible(bool visible)
        {
            _showMEPWiringLinks = visible;
        }

        public void RebuildFloorMEPInstallations(FloorData floor, Transform floorRoot = null)
        {
            if (floor == null) return;
            EnsureFallbackMaterials();
            if (floorRoot == null) floorRoot = GetOrCreateFloorRoot(floor);

            if (_floorMEPObjects.TryGetValue(floor.FloorId, out List<GameObject> existingMepList))
            {
                foreach (GameObject go in existingMepList)
                {
                    if (go != null) Destroy(go);
                }
                existingMepList.Clear();
            }
            else
            {
                existingMepList = new List<GameObject>();
                _floorMEPObjects[floor.FloorId] = existingMepList;
            }

            var mepLookup = new Dictionary<Guid, MEPInstanceIdentifier>();

            // 1. Spawn Wall-Mounted MEP items (sockets, switches, wall lights, water/sewage valves)
            foreach (WallData wall in floor.Walls)
            {
                foreach (MEPData mep in wall.MountedMEPItems)
                {
                    GameObject mepGo = ProceduralMEPDeviceGenerator.CreateMEPDeviceInstance(
                        mep,
                        floorRoot,
                        defaultMEPWhiteMaterial,
                        defaultMEPDarkMaterial,
                        defaultMEPEmissiveMaterial,
                        defaultMEPColdWaterMaterial,
                        defaultMEPHotWaterMaterial,
                        defaultMEPSewageMaterial);

                    existingMepList.Add(mepGo);
                    if (mepGo.TryGetComponent(out MEPInstanceIdentifier idTag))
                    {
                        mepLookup[mep.MEPId] = idTag;
                    }
                }
            }

            // 2. Spawn Room Ceiling Light Points
            foreach (RoomData room in floor.Rooms)
            {
                foreach (MEPData ceilingMep in room.CeilingMEPItems)
                {
                    GameObject mepGo = ProceduralMEPDeviceGenerator.CreateMEPDeviceInstance(
                        ceilingMep,
                        floorRoot,
                        defaultMEPWhiteMaterial,
                        defaultMEPDarkMaterial,
                        defaultMEPEmissiveMaterial,
                        defaultMEPColdWaterMaterial,
                        defaultMEPHotWaterMaterial,
                        defaultMEPSewageMaterial);

                    existingMepList.Add(mepGo);
                    if (mepGo.TryGetComponent(out MEPInstanceIdentifier idTag))
                    {
                        mepLookup[ceilingMep.MEPId] = idTag;
                    }
                }
            }

            // 3. Bind Switch -> Light links (MEPConnectionLink) and draw wiring lines
            var switchToLightsMap = new Dictionary<Guid, List<Light>>();
            Shader lineShader = Shader.Find("Sprites/Default");

            foreach (MEPConnectionLink link in floor.MEPLinks)
            {
                if (!mepLookup.TryGetValue(link.SwitchMEPId, out MEPInstanceIdentifier switchNode) ||
                    !mepLookup.TryGetValue(link.TargetLightMEPId, out MEPInstanceIdentifier lightNode))
                {
                    continue;
                }

                if (lightNode.AttachedLightSource != null)
                {
                    if (!switchToLightsMap.TryGetValue(link.SwitchMEPId, out List<Light> boundLights))
                    {
                        boundLights = new List<Light>();
                        switchToLightsMap[link.SwitchMEPId] = boundLights;
                    }
                    boundLights.Add(lightNode.AttachedLightSource);
                }

                if (_showMEPWiringLinks && lineShader != null)
                {
                    var wireGo = new GameObject($"MEPLinkWire_{link.LinkId.ToString()[..6]}");
                    wireGo.transform.SetParent(floorRoot, false);
                    var lr = wireGo.AddComponent<LineRenderer>();
                    lr.positionCount = 3;
                    lr.startWidth = 0.018f;
                    lr.endWidth = 0.018f;
                    lr.useWorldSpace = true;
                    lr.material = new Material(lineShader) { color = new Color(1.0f, 0.82f, 0.15f, 0.88f) };

                    Vector3 pSwitch = switchNode.transform.position + switchNode.transform.forward * 0.02f;
                    Vector3 pLight = lightNode.transform.position + lightNode.transform.forward * 0.02f;
                    Vector3 pCorner = new(pSwitch.x, pLight.y - 0.03f, pSwitch.z);

                    lr.SetPosition(0, pSwitch);
                    lr.SetPosition(1, pCorner);
                    lr.SetPosition(2, pLight);
                    existingMepList.Add(wireGo);
                }
            }

            foreach (var kvp in switchToLightsMap)
            {
                if (mepLookup.TryGetValue(kvp.Key, out MEPInstanceIdentifier switchNode) &&
                    switchNode.AttachedSwitchInteractable != null)
                {
                    switchNode.AttachedSwitchInteractable.BindConnectedLights(kvp.Value);
                }
            }
        }

        public IReadOnlyList<(Transform transform, Bounds localBounds, Guid? wallId)> GetPlacedCabinetsOnFloor(Guid floorId)
        {
            var result = new List<(Transform, Bounds, Guid?)>();
            if (!_floorFurnitureObjects.TryGetValue(floorId, out List<GameObject> furnitureGos))
            {
                return result;
            }

            foreach (GameObject go in furnitureGos)
            {
                if (go == null) continue;
                if (go.TryGetComponent(out FurnitureInstanceIdentifier idTag))
                {
                    result.Add((go.transform, idTag.NormalizedLocalBounds, idTag.SnappedWallId));
                }
            }
            return result;
        }

        public async void RebuildFloorFurnitures(FloorData floor, Transform floorRoot = null)
        {
            if (floor == null) return;
            EnsureFallbackMaterials();
            if (floorRoot == null) floorRoot = GetOrCreateFloorRoot(floor);

            if (_floorFurnitureObjects.TryGetValue(floor.FloorId, out List<GameObject> existingList))
            {
                foreach (GameObject go in existingList)
                {
                    if (go != null) Destroy(go);
                }
                existingList.Clear();
            }
            else
            {
                existingList = new List<GameObject>();
                _floorFurnitureObjects[floor.FloorId] = existingList;
            }

            foreach (FurnitureData furn in floor.Furnitures)
            {
                GameObject instanceGo;
                if (furn.IsExternalCustomModel && !string.IsNullOrEmpty(furn.CatalogOrCustomAssetId))
                {
                    instanceGo = await _gltfImporter.ImportAndNormalizeGltfAsync(
                        furn.CatalogOrCustomAssetId,
                        floorRoot,
                        furn.CatalogOrCustomAssetId);
                }
                else
                {
                    instanceGo = BuiltInFurnitureCatalog.CreateCatalogModelInstance(
                        furn.CatalogOrCustomAssetId,
                        floorRoot,
                        defaultCabinetBodyMaterial,
                        defaultCabinetFrontMaterial,
                        defaultCountertopMaterial,
                        defaultApplianceMetalMaterial,
                        applianceHazardLayer);
                }

                if (instanceGo == null) continue;

                instanceGo.transform.position = furn.Position.ToVector3();
                instanceGo.transform.rotation = Quaternion.Euler(furn.RotationEuler.ToVector3());
                instanceGo.transform.localScale = furn.Scale.ToVector3();

                if (instanceGo.TryGetComponent(out FurnitureInstanceIdentifier idTag))
                {
                    idTag.InstanceId = furn.InstanceId;
                    idTag.SnappedWallId = furn.SnappedWallId;
                }

                existingList.Add(instanceGo);
            }
        }

        public void SetRoofSlabsVisible(bool visible)
        {
            _showRoofSlabsInDesignMode = visible;
            foreach (var kvp in _roofPlaneObjects)
            {
                foreach (GameObject go in kvp.Value)
                {
                    if (go != null) go.SetActive(visible);
                }
            }
        }

        public void SetUpperFloorsVisibleAbove(int activeFloorLevelIndex, ProjectData project, bool showUpperFloors)
        {
            if (project == null) return;
            foreach (FloorData floor in project.Floors)
            {
                if (_floorRoots.TryGetValue(floor.FloorId, out Transform root) && root != null)
                {
                    bool shouldShow = showUpperFloors || floor.LevelIndex <= activeFloorLevelIndex;
                    root.gameObject.SetActive(shouldShow);
                }
            }
        }

        private void RebuildWallOpeningFrames(WallData wall, Transform floorRoot)
        {
            if (_wallOpeningObjects.TryGetValue(wall.WallId, out List<GameObject> existingFrames))
            {
                foreach (GameObject go in existingFrames)
                {
                    if (go != null) Destroy(go);
                }
                existingFrames.Clear();
            }
            else
            {
                existingFrames = new List<GameObject>();
                _wallOpeningObjects[wall.WallId] = existingFrames;
            }

            foreach (OpeningData op in wall.Openings)
            {
                GameObject frameGo = ProceduralOpeningFrameGenerator.CreateOpeningInstance(
                    wall,
                    op,
                    floorRoot,
                    defaultOpeningFrameMaterial,
                    defaultWindowGlassMaterial,
                    defaultDoorLeafMaterial);
                existingFrames.Add(frameGo);
            }
        }

        private void RebuildRoofPlanes(FloorData floor, Transform floorRoot)
        {
            if (_roofPlaneObjects.TryGetValue(floor.FloorId, out List<GameObject> existingRoofs))
            {
                foreach (GameObject go in existingRoofs)
                {
                    if (go != null) Destroy(go);
                }
                existingRoofs.Clear();
            }
            else
            {
                existingRoofs = new List<GameObject>();
                _roofPlaneObjects[floor.FloorId] = existingRoofs;
            }

            if (!floor.IsAttic || floor.RoofPlanes == null || floor.RoofPlanes.Count == 0) return;

            // Estimate half-span horizontal run from roof eaves
            float horizontalRun = 2.6f;
            if (floor.RoofPlanes.Count >= 2)
            {
                Vector2 e0 = floor.RoofPlanes[0].EaveStart.ToVector2();
                Vector2 e1 = floor.RoofPlanes[1].EaveEnd.ToVector2();
                float distBetweenEaves = Vector2.Distance(e0, e1);
                if (distBetweenEaves > 0.5f)
                {
                    horizontalRun = distBetweenEaves * 0.5f;
                }
            }

            for (int i = 0; i < floor.RoofPlanes.Count; i++)
            {
                RoofPlaneData rp = floor.RoofPlanes[i];
                Mesh roofMesh = ProceduralRoofMeshBuilder.GenerateRoofSlabMesh(rp, horizontalRun);

                var roofGo = new GameObject($"RoofSlope_{i + 1}");
                roofGo.transform.SetParent(floorRoot, false);
                roofGo.SetActive(_showRoofSlabsInDesignMode);

                var mf = roofGo.AddComponent<MeshFilter>();
                mf.sharedMesh = roofMesh;

                var mr = roofGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = defaultRoofMaterial;

                existingRoofs.Add(roofGo);
            }
        }

        private void RebuildFloorSlabs(FloorData floor, Transform floorRoot)
        {
            if (_floorSlabObjects.TryGetValue(floor.FloorId, out List<GameObject> existingSlabs))
            {
                foreach (GameObject go in existingSlabs)
                {
                    if (go != null) Destroy(go);
                }
                existingSlabs.Clear();
            }
            else
            {
                existingSlabs = new List<GameObject>();
                _floorSlabObjects[floor.FloorId] = existingSlabs;
            }

            // Preserve any existing CeilingMEPItems before regenerating RoomData polygons
            var preservedCeilingLights = new List<MEPData>();
            foreach (RoomData oldRoom in floor.Rooms)
            {
                if (oldRoom.CeilingMEPItems != null && oldRoom.CeilingMEPItems.Count > 0)
                {
                    preservedCeilingLights.AddRange(oldRoom.CeilingMEPItems);
                }
            }

            List<List<Vector2>> detectedPolygons = ProceduralRoomFloorBuilder.DetectClosedRoomPolygonsFromWalls(floor.Walls);
            floor.Rooms.Clear();

            float totalAreaM2 = 0f;
            for (int i = 0; i < detectedPolygons.Count; i++)
            {
                List<Vector2> poly = detectedPolygons[i];
                float area = Mathf.Abs(ProceduralRoomFloorBuilder.ComputeSignedArea2D(poly));
                totalAreaM2 += area;

                var roomData = new RoomData
                {
                    Name = $"Pomieszczenie {i + 1} ({area:F1} m²)"
                };
                foreach (Vector2 pt in poly) roomData.PolygonVertices2D.Add(pt);
                if (i == 0 && preservedCeilingLights.Count > 0)
                {
                    roomData.CeilingMEPItems.AddRange(preservedCeilingLights);
                }
                floor.Rooms.Add(roomData);

                Mesh slabMesh = ProceduralRoomFloorBuilder.GenerateFloorSlabMesh(poly, 0f, floor.SlabThickness);
                var slabGo = new GameObject($"FloorSlab_{roomData.Name}");
                slabGo.layer = floorLayer;
                slabGo.transform.SetParent(floorRoot, false);

                var mf = slabGo.AddComponent<MeshFilter>();
                mf.sharedMesh = slabMesh;

                var mr = slabGo.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { defaultFloorMaterial, defaultSlabEdgeMaterial };

                var mc = slabGo.AddComponent<MeshCollider>();
                mc.sharedMesh = slabMesh;

                existingSlabs.Add(slabGo);
            }

            if (floor.Rooms.Count == 0 && preservedCeilingLights.Count > 0)
            {
                var fallbackRoom = new RoomData { Name = "Strefa Sufitowa" };
                fallbackRoom.CeilingMEPItems.AddRange(preservedCeilingLights);
                floor.Rooms.Add(fallbackRoom);
            }

            OnRoomsRecalculated?.Invoke(floor, detectedPolygons.Count, totalAreaM2);
        }

        public void ClearAllSceneGeometry()
        {
            foreach (var kvp in _wallObjects)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _wallObjects.Clear();

            foreach (var kvp in _wallOpeningObjects)
            {
                foreach (GameObject go in kvp.Value)
                {
                    if (go != null) Destroy(go);
                }
            }
            _wallOpeningObjects.Clear();

            foreach (var kvp in _floorSlabObjects)
            {
                foreach (GameObject go in kvp.Value)
                {
                    if (go != null) Destroy(go);
                }
            }
            _floorSlabObjects.Clear();

            foreach (var kvp in _roofPlaneObjects)
            {
                foreach (GameObject go in kvp.Value)
                {
                    if (go != null) Destroy(go);
                }
            }
            _roofPlaneObjects.Clear();

            foreach (var kvp in _floorFurnitureObjects)
            {
                foreach (GameObject go in kvp.Value)
                {
                    if (go != null) Destroy(go);
                }
            }
            _floorFurnitureObjects.Clear();

            foreach (var kvp in _floorMEPObjects)
            {
                foreach (GameObject go in kvp.Value)
                {
                    if (go != null) Destroy(go);
                }
            }
            _floorMEPObjects.Clear();

            foreach (var kvp in _floorRoots)
            {
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            }
            _floorRoots.Clear();
        }

        private Transform GetOrCreateFloorRoot(FloorData floor)
        {
            if (_floorRoots.TryGetValue(floor.FloorId, out Transform root) && root != null)
            {
                return root;
            }

            var go = new GameObject($"Floor_{floor.LevelIndex}_{floor.Name}");
            go.transform.SetParent(transform, false);
            _floorRoots[floor.FloorId] = go.transform;
            return go.transform;
        }

        private GameObject GetOrCreateWallGameObject(WallData wall, Transform floorRoot)
        {
            if (_wallObjects.TryGetValue(wall.WallId, out GameObject existing) && existing != null)
            {
                return existing;
            }

            var go = new GameObject($"Wall_{wall.WallId.ToString()[..8]}");
            go.transform.SetParent(floorRoot, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>();
            go.AddComponent<WallSurfaceIdentifier>();

            _wallObjects[wall.WallId] = go;
            return go;
        }

        private void EnsureFallbackMaterials()
        {
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            if (defaultWallFrontMaterial == null)
            {
                defaultWallFrontMaterial = CreateMaterial(shader, "Mat_Wall_Front", new Color(0.95f, 0.95f, 0.93f));
            }
            if (defaultWallBackMaterial == null)
            {
                defaultWallBackMaterial = CreateMaterial(shader, "Mat_Wall_Back", new Color(0.92f, 0.92f, 0.90f));
            }
            if (defaultWallEdgeMaterial == null)
            {
                defaultWallEdgeMaterial = CreateMaterial(shader, "Mat_Wall_Edge", new Color(0.75f, 0.75f, 0.75f));
            }
            if (defaultFloorMaterial == null)
            {
                defaultFloorMaterial = CreateMaterial(shader, "Mat_Floor_Parquet", new Color(0.72f, 0.56f, 0.38f));
            }
            if (defaultSlabEdgeMaterial == null)
            {
                defaultSlabEdgeMaterial = CreateMaterial(shader, "Mat_Slab_Concrete", new Color(0.60f, 0.60f, 0.62f));
            }
            if (defaultOpeningFrameMaterial == null)
            {
                defaultOpeningFrameMaterial = CreateMaterial(shader, "Mat_Opening_AnthraciteFrame", new Color(0.20f, 0.21f, 0.23f));
            }
            if (defaultWindowGlassMaterial == null)
            {
                defaultWindowGlassMaterial = CreateMaterial(shader, "Mat_Window_Glass", new Color(0.68f, 0.85f, 0.96f, 0.40f));
            }
            if (defaultDoorLeafMaterial == null)
            {
                defaultDoorLeafMaterial = CreateMaterial(shader, "Mat_Door_WoodLeaf", new Color(0.64f, 0.45f, 0.28f));
            }
            if (defaultRoofMaterial == null)
            {
                defaultRoofMaterial = CreateMaterial(shader, "Mat_Roof_AnthraciteTiles", new Color(0.24f, 0.25f, 0.28f));
            }
            if (defaultCabinetBodyMaterial == null)
            {
                defaultCabinetBodyMaterial = CreateMaterial(shader, "Mat_Cabinet_Body", new Color(0.88f, 0.88f, 0.86f));
            }
            if (defaultCabinetFrontMaterial == null)
            {
                defaultCabinetFrontMaterial = CreateMaterial(shader, "Mat_Cabinet_FrontMatte", new Color(0.26f, 0.32f, 0.38f));
            }
            if (defaultCountertopMaterial == null)
            {
                defaultCountertopMaterial = CreateMaterial(shader, "Mat_Cabinet_OakCountertop", new Color(0.76f, 0.58f, 0.36f));
            }
            if (defaultApplianceMetalMaterial == null)
            {
                defaultApplianceMetalMaterial = CreateMaterial(shader, "Mat_Appliance_InoxSteel", new Color(0.70f, 0.73f, 0.76f));
            }
            if (defaultMEPWhiteMaterial == null)
            {
                defaultMEPWhiteMaterial = CreateMaterial(shader, "Mat_MEP_WhitePlastic", new Color(0.96f, 0.96f, 0.97f));
            }
            if (defaultMEPDarkMaterial == null)
            {
                defaultMEPDarkMaterial = CreateMaterial(shader, "Mat_MEP_DarkAccent", new Color(0.18f, 0.19f, 0.21f));
            }
            if (defaultMEPEmissiveMaterial == null)
            {
                defaultMEPEmissiveMaterial = CreateMaterial(shader, "Mat_MEP_WarmEmissiveLED", new Color(1.0f, 0.95f, 0.80f));
                if (defaultMEPEmissiveMaterial.HasProperty("_EmissiveColor"))
                {
                    defaultMEPEmissiveMaterial.SetColor("_EmissiveColor", new Color(4.0f, 3.6f, 2.8f));
                }
            }
            if (defaultMEPColdWaterMaterial == null)
            {
                defaultMEPColdWaterMaterial = CreateMaterial(shader, "Mat_MEP_ColdWaterBlue", new Color(0.12f, 0.48f, 0.92f));
            }
            if (defaultMEPHotWaterMaterial == null)
            {
                defaultMEPHotWaterMaterial = CreateMaterial(shader, "Mat_MEP_HotWaterRed", new Color(0.90f, 0.20f, 0.18f));
            }
            if (defaultMEPSewageMaterial == null)
            {
                defaultMEPSewageMaterial = CreateMaterial(shader, "Mat_MEP_SewagePVC", new Color(0.45f, 0.47f, 0.50f));
            }
        }

        private static Material CreateMaterial(Shader shader, string name, Color color)
        {
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }
    }
}
