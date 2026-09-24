using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoreEngine.Spike.Editor
{
    /// <summary>
    /// The Garage as a photographed robotics lab (docs/03 §3.1, D19): a real lab HDRI (Poly Haven's Vintage
    /// Measuring Lab) for the background, the ambient light and the reflections; a concrete floor and
    /// photo-scanned props from Poly Haven's "The Shed" (CC0), fetched by tools/fetch-lab-assets.ps1; a key
    /// light that matches the photo's skylights; a baked reflection probe; and a camera look with filmic
    /// tonemapping and depth of field. Without the downloaded files the Garage keeps its drawn desk room.
    /// </summary>
    public static class SpikeLab
    {
        public const string Folder = "Assets/ThirdParty/PolyHaven";
        const string HdriPath = Folder + "/HDRI/vintage_measuring_lab_4k.hdr";
        const float BenchHeight = 0.83f; // the sideboard's top is the Garage's y = 0

        public static bool AssetsPresent => File.Exists(Path.Combine(Application.dataPath, "ThirdParty/PolyHaven/HDRI/vintage_measuring_lab_4k.hdr"));

        /// <summary>Import settings: the HDRI at full width for the background, normal maps as normal maps.</summary>
        public static void ConfigureImports()
        {
            if (AssetImporter.GetAtPath(HdriPath) is TextureImporter hdri &&
                (hdri.maxTextureSize != 4096 || hdri.textureCompression != TextureImporterCompression.CompressedHQ || hdri.wrapModeV != TextureWrapMode.Clamp))
            {
                hdri.textureShape = TextureImporterShape.Texture2D;
                hdri.maxTextureSize = 4096;
                hdri.textureCompression = TextureImporterCompression.CompressedHQ; // BC6H keeps the high dynamic range
                hdri.mipmapEnabled = true;
                hdri.wrapModeU = TextureWrapMode.Repeat;
                hdri.wrapModeV = TextureWrapMode.Clamp;
                hdri.SaveAndReimport();
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder + "/Textures" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                bool normal = path.Contains("_nor_gl_");
                bool data = path.Contains("_arm_") || path.Contains("_mask_");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType == type && importer.sRGBTexture == (!normal && !data) && importer.anisoLevel == 8) continue;
                importer.textureType = type;
                importer.sRGBTexture = !normal && !data;
                importer.maxTextureSize = 2048;
                importer.anisoLevel = 8;
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// Builds the lab around the turntable spot (the world origin, 83 cm above the floor) and returns its root.
        /// The Garage finds it by name and then only adds the turntable and the robot.
        /// </summary>
        public static GameObject Build(string materials)
        {
            var root = new GameObject("LabSet");

            // Sky, ambient light and reflections from the photographed lab.
            var sky = LoadOrCreate(materials, "LabSky", "Skybox/Panoramic");
            sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(HdriPath));
            sky.SetFloat("_Mapping", 1);   // latitude-longitude
            sky.SetFloat("_ImageType", 0); // 360 degrees
            sky.SetFloat("_Exposure", 1.0f);
            sky.SetFloat("_Rotation", 0);  // the photo's skylights then come from the camera's front left
            sky.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            sky.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.defaultReflectionResolution = 256;

            // Concrete floor under the bench (Poly Haven concrete_floor_02, 2 m per repeat).
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0, -BenchHeight, 0);
            floor.transform.localScale = new Vector3(0.9f, 1, 0.9f); // 9 × 9 m
            floor.GetComponent<Renderer>().sharedMaterial = FloorMaterial(materials, 4.5f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());

            // The bench: a worn sideboard; its top is y = 0, the turntable stands near the front edge.
            Prop(root.transform, "WoodenTable_03", "2k", new Vector3(-0.08f, 0, -0.1f), Vector3.zero, alignTop: true);
            // An anti-static (ESD) mat, as on every electronics bench.
            var mat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mat.name = "EsdMat";
            mat.transform.SetParent(root.transform, false);
            mat.transform.localPosition = new Vector3(0.02f, 0.0015f, 0.0f);
            mat.transform.localScale = new Vector3(0.62f, 0.003f, 0.42f);
            mat.GetComponent<Renderer>().sharedMaterial = Lit(materials, "EsdMat", new Color(0.19f, 0.25f, 0.30f), 0.22f, 0);
            Object.DestroyImmediate(mat.GetComponent<Collider>());

            // Tools and parts on the bench, the way a maker leaves them.
            Prop(root.transform, "desk_lamp_arm_01", "1k", new Vector3(-0.52f, 0, -0.26f), new Vector3(0, 35, 0));
            Prop(root.transform, "bench_vice_01", "1k", new Vector3(0.49f, 0, -0.06f), new Vector3(0, -90, 0));
            Prop(root.transform, "Drill_01", "1k", new Vector3(0.36f, 0, -0.29f), new Vector3(0, 200, 0));
            // The chrome shaft points away from the camera: seen side-on, its thin highlight broke into a row of bokeh dots.
            Prop(root.transform, "flathead_screwdriver", "1k", new Vector3(-0.30f, 0, 0.13f), new Vector3(0, -65, 90));
            Prop(root.transform, "ratchet_wrench", "1k", new Vector3(-0.39f, 0, 0.05f), new Vector3(90, 60, 0));
            Prop(root.transform, "measuring_tape_01", "1k", new Vector3(0.27f, 0, 0.14f), new Vector3(0, -40, 0));
            Prop(root.transform, "spray_paint_bottles", "1k", new Vector3(-0.30f, 0, -0.30f), new Vector3(0, 15, 0));
            Prop(root.transform, "small_oil_can_01", "1k", new Vector3(0.12f, 0, -0.33f), new Vector3(0, 120, 0));

            // The room around the bench: tool chest, shelving, boxes and a stool.
            Prop(root.transform, "metal_tool_chest", "1k", new Vector3(1.05f, -BenchHeight, -0.25f), new Vector3(0, -80, 0));
            Prop(root.transform, "steel_frame_shelves_01", "1k", new Vector3(-0.15f, -BenchHeight, -1.25f), Vector3.zero, scale: 0.1f);
            Prop(root.transform, "cardboard_box_01", "1k", new Vector3(-1.05f, -BenchHeight, -0.55f), new Vector3(0, 20, 0));
            Prop(root.transform, "cardboard_box_01", "1k", new Vector3(-1.1f, -BenchHeight + 0.342f, -0.6f), new Vector3(0, -8, 0));
            Prop(root.transform, "wooden_stool_01", "1k", new Vector3(-0.8f, -BenchHeight, 0.6f), new Vector3(0, 30, 0));

            // Light: a soft key from the photo's skylights (45° up, from the camera's front left), a warm task
            // lamp over the robot, and a probe that sees the bench for the robot's reflections.
            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.transform.SetParent(root.transform, false);
            key.type = LightType.Directional;
            key.color = new Color(0.97f, 0.98f, 1f);
            key.intensity = 1.1f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.85f;
            key.transform.rotation = Quaternion.LookRotation(-new Vector3(0.697f, 0.707f, 0.122f));

            var lamp = new GameObject("TaskLamp").AddComponent<Light>();
            lamp.transform.SetParent(root.transform, false);
            lamp.type = LightType.Spot;
            lamp.color = new Color(1f, 0.83f, 0.62f); // about 3200 K
            lamp.intensity = 1.2f;
            lamp.range = 1.6f;
            lamp.spotAngle = 70f;
            lamp.innerSpotAngle = 35f;
            lamp.shadows = LightShadows.Soft;
            lamp.transform.position = new Vector3(-0.28f, 0.52f, 0.02f);
            lamp.transform.LookAt(new Vector3(0, 0.05f, 0));

            var probe = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(root.transform, false);
            probe.transform.localPosition = new Vector3(0, 0.15f, 0);
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(4, 3, 4);
            probe.resolution = 256;
            probe.boxProjection = false;

            foreach (var t in root.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.BatchingStatic);
            return root;
        }

        /// <summary>The Garage's own camera look: filmic tone curve, depth of field on the robot, a little grain.</summary>
        public static VolumeProfile PostProfile(string folder)
        {
            string path = folder + "/GaragePost.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            T Component<T>() where T : VolumeComponent
            {
                if (profile.TryGet(out T existing)) return existing;
                var added = profile.Add<T>(true);
                added.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(added, profile);
                return added;
            }
            Component<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var color = Component<ColorAdjustments>();
            color.postExposure.Override(0.12f);
            color.contrast.Override(6f);
            color.saturation.Override(4f);
            var white = Component<WhiteBalance>();
            white.temperature.Override(4f);
            var bloom = Component<Bloom>();
            bloom.threshold.Override(1.4f);
            bloom.intensity.Override(0.1f);
            bloom.scatter.Override(0.65f);
            var dof = Component<DepthOfField>();
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(0.72f);
            dof.focalLength.Override(55f);
            dof.aperture.Override(3.2f);
            dof.bladeCount.Override(6);
            var vignette = Component<Vignette>();
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.5f);
            var grain = Component<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.18f);
            grain.response.Override(0.8f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>Bakes the ambient light and reflections from the sky, and the reflection probe that sees the bench.</summary>
        public static void BakeLighting(string folder)
        {
            string path = folder + "/GarageLighting.lighting";
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (settings == null)
            {
                settings = new LightingSettings { name = "GarageLighting" };
                AssetDatabase.CreateAsset(settings, path);
            }
            settings.bakedGI = false;     // no lightmaps: the environment and the probe are enough
            settings.realtimeGI = false;
            Lightmapping.lightingSettings = settings;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool ok = Lightmapping.Bake();
            Debug.Log($"SpikeLab: lighting baked ({(ok ? "OK" : "FAILED")}) in {watch.Elapsed.TotalSeconds:F1} s");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Places a Poly Haven model with its bounds' bottom centre (or top, for the bench) at a point.</summary>
        static GameObject? Prop(Transform parent, string id, string res, Vector3 at, Vector3 euler, float scale = 1, bool alignTop = false)
        {
            string path = $"{Folder}/Models/{id}/{id}_{res}.gltf";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogWarning($"SpikeLab: {path} was not imported; run tools/fetch-lab-assets.ps1");
                return null;
            }
            var go = (GameObject)Object.Instantiate(asset, parent);
            go.name = id;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = Vector3.zero;
            var bounds = WorldBounds(go);
            var anchor = new Vector3(bounds.center.x, alignTop ? bounds.max.y : bounds.min.y, bounds.center.z);
            go.transform.position += at - anchor;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.On;
            Debug.Log($"SpikeLab: {id} size {bounds.size.x:F2} × {bounds.size.y:F2} × {bounds.size.z:F2} m");
            return go;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        /// <summary>
        /// URP Lit with the floor's photo maps. Poly Haven packs ambient occlusion, roughness and metalness in one
        /// "arm" map; URP Lit reads metalness from red, occlusion from green and smoothness from alpha, so the map
        /// is repacked once into a mask next to the download.
        /// </summary>
        static Material FloorMaterial(string folder, float tiles)
        {
            string textures = Folder + "/Textures/concrete_floor_02";
            var material = LoadOrCreate(folder, "LabFloor", "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.92f));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(textures + "/concrete_floor_02_diff_2k.jpg"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(textures + "/concrete_floor_02_nor_gl_2k.jpg"));
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
            var mask = RepackArm(textures + "/concrete_floor_02_arm_2k.jpg", textures + "/concrete_floor_02_mask_2k.png");
            if (mask != null)
            {
                material.SetTexture("_MetallicGlossMap", mask);
                material.SetTexture("_OcclusionMap", mask);
                material.SetFloat("_Smoothness", 1f);      // scales the mask's smoothness
                material.SetFloat("_OcclusionStrength", 1f);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.EnableKeyword("_OCCLUSIONMAP");
            }
            material.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D? RepackArm(string armPath, string maskPath)
        {
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, armPath);
            if (!File.Exists(full)) return null;
            string maskFull = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, maskPath);
            if (!File.Exists(maskFull))
            {
                var arm = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                arm.LoadImage(File.ReadAllBytes(full));
                var pixels = arm.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i]; // r = occlusion, g = roughness, b = metalness
                    pixels[i] = new Color32(p.b, p.r, 0, (byte)(255 - p.g));
                }
                var mask = new Texture2D(arm.width, arm.height, TextureFormat.RGBA32, false, true);
                mask.SetPixels32(pixels);
                File.WriteAllBytes(maskFull, mask.EncodeToPNG());
                Object.DestroyImmediate(arm);
                Object.DestroyImmediate(mask);
                AssetDatabase.ImportAsset(maskPath);
                if (AssetImporter.GetAtPath(maskPath) is TextureImporter importer)
                {
                    importer.sRGBTexture = false;
                    importer.maxTextureSize = 2048;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
        }

        static Material Lit(string folder, string name, Color color, float smoothness, float metallic)
        {
            var material = LoadOrCreate(folder, name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material LoadOrCreate(string folder, string name, string shader)
        {
            string path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }
    }
}
