using System;
using HomeBuilder.Data;
using HomeBuilder.Walkthrough;
using UnityEngine;

namespace HomeBuilder.Geometry
{
    public sealed class MEPInstanceIdentifier : MonoBehaviour
    {
        public Guid MEPId;
        public Guid HostSurfaceId;
        public MEPCategory Category;
        public Light AttachedLightSource;
        public WallSwitchInteractable AttachedSwitchInteractable;
    }

    /// <summary>
    /// Generates 3D MEP installation fixtures (230V/400V sockets, single/double light switches,
    /// ceiling/wall luminaires with real Unity Light sources, and cold/hot water & sewage valves).
    /// Pivot (0,0,0) lies flush on the mounting surface with local +Z pointing along the surface normal.
    /// </summary>
    public static class ProceduralMEPDeviceGenerator
    {
        public static GameObject CreateMEPDeviceInstance(
            MEPData mepData,
            Transform parent,
            Material whitePlasticMat,
            Material darkAccentMat,
            Material emissiveLightMat,
            Material coldWaterBlueMat,
            Material hotWaterRedMat,
            Material sewageGrayMat)
        {
            var rootGo = new GameObject($"MEP_{mepData.Category}_{mepData.MEPId.ToString()[..6]}");
            if (parent != null) rootGo.transform.SetParent(parent, false);

            Vector3 worldPos = mepData.WorldPosition.ToVector3();
            Vector3 normal = mepData.SurfaceNormal.ToVector3();
            if (normal.sqrMagnitude < 1e-4f) normal = Vector3.forward;

            Vector3 upAxis = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            rootGo.transform.SetPositionAndRotation(worldPos, Quaternion.LookRotation(normal, upAxis));

            var identifier = rootGo.AddComponent<MEPInstanceIdentifier>();
            identifier.MEPId = mepData.MEPId;
            identifier.HostSurfaceId = mepData.HostSurfaceId;
            identifier.Category = mepData.Category;

            var selectionCollider = rootGo.AddComponent<BoxCollider>();
            selectionCollider.center = new Vector3(0f, 0f, 0.025f);
            selectionCollider.size = new Vector3(0.11f, 0.11f, 0.06f);

            switch (mepData.Category)
            {
                case MEPCategory.ElectricalSocket230V:
                case MEPCategory.ElectricalSocket400V:
                    BuildElectricalSocket(rootGo.transform, mepData.Category == MEPCategory.ElectricalSocket400V, whitePlasticMat, darkAccentMat, hotWaterRedMat);
                    break;

                case MEPCategory.LightSwitchSingle:
                case MEPCategory.LightSwitchDouble:
                    BuildLightSwitch(rootGo, mepData, whitePlasticMat, darkAccentMat, identifier);
                    break;

                case MEPCategory.CeilingLightPoint:
                case MEPCategory.WallLightPoint:
                    BuildLuminaireWithLight(rootGo, mepData, whitePlasticMat, emissiveLightMat, identifier);
                    break;

                case MEPCategory.WaterSupplyCold:
                    BuildPlumbingPoint(rootGo.transform, coldWaterBlueMat, darkAccentMat, diameter: 0.045f);
                    break;

                case MEPCategory.WaterSupplyHot:
                    BuildPlumbingPoint(rootGo.transform, hotWaterRedMat, darkAccentMat, diameter: 0.045f);
                    break;

                case MEPCategory.SewageDrain50mm:
                case MEPCategory.SewageDrain110mm:
                    float d = mepData.Category == MEPCategory.SewageDrain110mm ? 0.11f : 0.055f;
                    BuildPlumbingPoint(rootGo.transform, sewageGrayMat, darkAccentMat, diameter: d);
                    break;
            }

            return rootGo;
        }

        private static void BuildElectricalSocket(
            Transform root,
            bool is400VForce,
            Material frameMat,
            Material innerMat,
            Material forceRedMat)
        {
            float size = is400VForce ? 0.095f : 0.082f;
            CreateBoxPart("SocketBezel", root, new Vector3(0f, 0f, 0.008f), new Vector3(size, size, 0.016f), is400VForce ? forceRedMat : frameMat);
            CreateBoxPart("SocketWell", root, new Vector3(0f, 0f, 0.015f), new Vector3(size * 0.56f, size * 0.56f, 0.008f), innerMat);
            // Grounding pin
            CreateBoxPart("GroundPin", root, new Vector3(0f, 0.012f, 0.019f), new Vector3(0.005f, 0.005f, 0.012f), frameMat);
        }

        private static void BuildLightSwitch(
            GameObject rootGo,
            MEPData mepData,
            Material frameMat,
            Material accentMat,
            MEPInstanceIdentifier identifier)
        {
            Transform root = rootGo.transform;
            CreateBoxPart("SwitchBezel", root, new Vector3(0f, 0f, 0.007f), new Vector3(0.084f, 0.084f, 0.014f), frameMat);

            if (mepData.Category == MEPCategory.LightSwitchDouble)
            {
                CreateBoxPart("RockerLeft", root, new Vector3(-0.014f, 0f, 0.016f), new Vector3(0.025f, 0.054f, 0.010f), frameMat);
                CreateBoxPart("RockerRight", root, new Vector3(0.014f, 0f, 0.016f), new Vector3(0.025f, 0.054f, 0.010f), frameMat);
                CreateBoxPart("RockerDivider", root, new Vector3(0f, 0f, 0.016f), new Vector3(0.003f, 0.056f, 0.011f), accentMat);
            }
            else
            {
                CreateBoxPart("RockerSingle", root, new Vector3(0f, 0f, 0.016f), new Vector3(0.054f, 0.054f, 0.010f), frameMat);
            }

            var switchInteractable = rootGo.AddComponent<WallSwitchInteractable>();
            switchInteractable.SwitchMEPId = mepData.MEPId;
            identifier.AttachedSwitchInteractable = switchInteractable;
        }

        private static void BuildLuminaireWithLight(
            GameObject rootGo,
            MEPData mepData,
            Material housingMat,
            Material emissiveDiffuserMat,
            MEPInstanceIdentifier identifier)
        {
            Transform root = rootGo.transform;
            float fixtureSize = mepData.Category == MEPCategory.CeilingLightPoint ? 0.28f : 0.16f;

            CreateBoxPart("LuminaireHousing", root, new Vector3(0f, 0f, 0.02f), new Vector3(fixtureSize, fixtureSize, 0.04f), housingMat);
            CreateBoxPart("EmissiveDiffuser", root, new Vector3(0f, 0f, 0.042f), new Vector3(fixtureSize * 0.82f, fixtureSize * 0.82f, 0.008f), emissiveDiffuserMat);

            var lightGo = new GameObject("MEPPointLightSource");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.12f);

            var pointLight = lightGo.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(1.0f, 0.94f, 0.84f); // Warm 3000K interior LED
            pointLight.range = 7.5f;
            pointLight.intensity = 2.2f;
            pointLight.shadows = LightShadows.Soft;

            identifier.AttachedLightSource = pointLight;
        }

        private static void BuildPlumbingPoint(Transform root, Material valveMat, Material pipeMat, float diameter)
        {
            CreateBoxPart("WallFlange", root, new Vector3(0f, 0f, 0.008f), new Vector3(diameter * 1.35f, diameter * 1.35f, 0.016f), pipeMat);
            CreateBoxPart("ValveOutlet", root, new Vector3(0f, 0f, 0.032f), new Vector3(diameter, diameter, 0.048f), valveMat);
        }

        private static GameObject CreateBoxPart(
            string name,
            Transform parent,
            Vector3 localPos,
            Vector3 scale,
            Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPos;
            box.transform.localScale = scale;

            if (box.TryGetComponent(out Collider col))
            {
                UnityEngine.Object.Destroy(col);
            }

            if (box.TryGetComponent(out MeshRenderer mr) && material != null)
            {
                mr.sharedMaterial = material;
            }

            return box;
        }
    }
}
