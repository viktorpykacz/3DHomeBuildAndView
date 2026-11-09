using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using UnityEngine.Rendering;

namespace HomeBuilder.Assets
{
    /// <summary>
    /// Runtime 3D Asset Importer (.glb / .gltf via glTFast).
    /// Normalizes the imported model's Pivot Point to Bottom-Back-Center,
    /// generates an accurate BoxCollider, and ensures HDRP/Lit material compatibility.
    /// </summary>
    public sealed class RuntimeFurnitureImporter
    {
        public const int MaxRecommendedTriangleCount = 150000;
        private readonly Shader _hdrpLitShader;

        public RuntimeFurnitureImporter()
        {
            _hdrpLitShader = Shader.Find("HDRP/Lit");
            if (_hdrpLitShader == null) _hdrpLitShader = Shader.Find("Universal Render Pipeline/Lit");
            if (_hdrpLitShader == null) _hdrpLitShader = Shader.Find("Standard");
        }

        public async Task<GameObject> ImportAndNormalizeGltfAsync(
            string filePath,
            Transform parent = null,
            string customAssetId = null)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[RuntimeFurnitureImporter] Plik modelu 3D nie istnieje: {filePath}");
                return null;
            }

            var gltf = new GltfImport();
            bool loaded = await gltf.Load(filePath);
            if (!loaded)
            {
                Debug.LogError($"[RuntimeFurnitureImporter] Nie udało się wczytać modelu glTF: {filePath}");
                return null;
            }

            var rootPivotGo = new GameObject($"CustomFurniture_{Path.GetFileNameWithoutExtension(filePath)}");
            if (parent != null)
            {
                rootPivotGo.transform.SetParent(parent, false);
            }

            var geometryHolderGo = new GameObject("GeometryContainer");
            geometryHolderGo.transform.SetParent(rootPivotGo.transform, false);

            bool instantiated = await gltf.InstantiateMainSceneAsync(geometryHolderGo.transform);
            if (!instantiated)
            {
                Object.Destroy(rootPivotGo);
                return null;
            }

            NormalizePivotBottomBackCenter(rootPivotGo, geometryHolderGo, out Bounds localNormalizedBounds);
            ConfigureHdrpMaterials(geometryHolderGo);
            AttachBoundingCollider(rootPivotGo, localNormalizedBounds);
            ValidateTriangleBudget(geometryHolderGo, filePath);

            var identifier = rootPivotGo.AddComponent<FurnitureInstanceIdentifier>();
            identifier.CatalogOrCustomAssetId = string.IsNullOrEmpty(customAssetId) ? filePath : customAssetId;
            identifier.NormalizedLocalBounds = localNormalizedBounds;
            identifier.HasApplianceExclusionZone = false;

            return rootPivotGo;
        }

        private static void ValidateTriangleBudget(GameObject geometryRoot, string filePath)
        {
            MeshFilter[] filters = geometryRoot.GetComponentsInChildren<MeshFilter>(includeInactive: true);
            int totalTris = 0;
            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh != null)
                {
                    totalTris += mf.sharedMesh.triangles.Length / 3;
                }
            }

            if (totalTris > MaxRecommendedTriangleCount)
            {
                Debug.LogWarning(
                    $"[RuntimeFurnitureImporter] UWAGA: Zaimportowany model '{Path.GetFileName(filePath)}' posiada {totalTris:N0} trójkątów " +
                    $"(zalecany limit: {MaxRecommendedTriangleCount:N0}). Może to wpłynąć na wydajność renderowania w czasie rzeczywistym.");
            }
        }

        /// <summary>
        /// Shifts the child geometry container so that rootPivotGo's local (0,0,0) is located at:
        /// X = Center, Y = Bottom (MinY), Z = Back (MinZ).
        /// </summary>
        public static void NormalizePivotBottomBackCenter(
            GameObject rootPivotGo,
            GameObject geometryChildGo,
            out Bounds normalizedBounds)
        {
            Renderer[] renderers = geometryChildGo.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers.Length == 0)
            {
                normalizedBounds = new Bounds(Vector3.zero, Vector3.one * 0.5f);
                return;
            }

            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                worldBounds.Encapsulate(renderers[i].bounds);
            }

            Vector3 desiredPivotWorld = new(
                worldBounds.center.x,
                worldBounds.min.y,
                worldBounds.min.z
            );

            Vector3 offset = rootPivotGo.transform.position - desiredPivotWorld;
            geometryChildGo.transform.position += offset;

            Vector3 size = worldBounds.size;
            normalizedBounds = new Bounds(new Vector3(0f, size.y * 0.5f, size.z * 0.5f), size);
        }

        private static void AttachBoundingCollider(GameObject rootPivotGo, Bounds normalizedBounds)
        {
            if (!rootPivotGo.TryGetComponent(out BoxCollider boxCollider))
            {
                boxCollider = rootPivotGo.AddComponent<BoxCollider>();
            }

            boxCollider.center = normalizedBounds.center;
            boxCollider.size = normalizedBounds.size;
        }

        private void ConfigureHdrpMaterials(GameObject targetRoot)
        {
            if (_hdrpLitShader == null) return;
            Renderer[] renderers = targetRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (Renderer r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                foreach (Material mat in r.materials)
                {
                    if (mat == null) continue;
                    if (mat.shader.name == "Standard" || mat.shader.name.Contains("InternalErrorShader"))
                    {
                        Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                        Color baseColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                        mat.shader = _hdrpLitShader;
                        if (mainTex != null) mat.SetTexture("_BaseColorMap", mainTex);
                        mat.SetColor("_BaseColor", baseColor);
                    }
                }
            }
        }
    }
}
