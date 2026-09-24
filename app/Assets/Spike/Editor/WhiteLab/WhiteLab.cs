using System.Collections.Generic;
using System.IO;
using CoreEngine.Spike.Parts;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoreEngine.Spike.Editor
{
    /// <summary>
    /// The Garage as a bright engineering lab (docs/03 §3.1, D20): white walls, a light floor, LED panels in the
    /// ceiling and a window with white blinds; a white bench with a measuring mat, an instrument shelf (scope,
    /// bench supply, soldering station), a monitor and an LED lamp; a pegboard of tools, parts shelves, a
    /// whiteboard, a pinout poster and a 3D printer. Everything is modelled here with the part toolkit
    /// (<see cref="MeshKit"/>, <see cref="Raster"/>), saved as assets, and lit by a bake with bounced light, so no
    /// file has to be downloaded. The Garage finds the set by its name, "LabSet", and adds the turntable and the
    /// robot: the bench top is y = 0, the turntable stands at the world origin near the bench's front edge.
    /// Lengths in millimetres unless a name says otherwise.
    /// </summary>
    public static partial class WhiteLab
    {
        const string Root = "Assets/Spike/Lab";
        const float FloorY = -800f, CeilingY = 2250f;
        const float BackWall = -760f, FrontWall = 3000f, LeftWall = -2500f, RightWall = 2500f;

        /// <summary>
        /// The room's shell (floor, walls, ceiling, window, blinds, ceiling panels) is on this layer, which the
        /// daylight key leaves out: the key then lights the bench and the robot as daylight would, without the
        /// walls putting everything in their shadow or a gap in the blinds drawing a line of sun across the room.
        /// </summary>
        public const int ShellLayer = 8;

        static Transform set = null!;
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static int meshCount;

        public static GameObject Build()
        {
            // Start clean: the lab's meshes, textures and materials are all made again.
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            Folder(Root);
            Folder(Root + "/Meshes");
            Folder(Root + "/Textures");
            Folder(Root + "/Materials");
            materials.Clear();
            meshCount = 0;

            set = new GameObject("LabSet").transform;
            Sky();
            Room();
            Window();
            Ceiling();
            Bench();
            Pegboard();
            Shelves();
            Instruments();
            DeskThings();
            Corners();
            Lights();
            Probes();
            AssetDatabase.SaveAssets();
            return set.gameObject;
        }

        // ------------------------------------------------------------------ assets

        static string Folder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
            return path;
        }

        /// <summary>A plain URP Lit material, saved.</summary>
        static Material Plain(string name, Color colour, float smoothness, float metallic = 0)
        {
            if (materials.TryGetValue(name, out var existing)) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            AssetDatabase.CreateAsset(material, $"{Root}/Materials/{name}.mat");
            materials[name] = material;
            return material;
        }

        static Material Plain(string name, string hex, float smoothness, float metallic = 0) =>
            Plain(name, ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta, smoothness, metallic);

        /// <summary>
        /// A light that glows: an unlit-looking emissive surface (LED panels, the daylight behind the blinds), which the
        /// bake also counts as a light source.
        /// </summary>
        static Material Glow(string name, Color colour, float strength)
        {
            if (materials.TryGetValue(name, out var existing)) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", 0.2f);
            material.SetColor("_EmissionColor", colour * strength);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            AssetDatabase.CreateAsset(material, $"{Root}/Materials/{name}.mat");
            materials[name] = material;
            return material;
        }

        /// <summary>
        /// A painted material: the raster's colour, metallic and normal maps saved as PNGs and imported (the normal
        /// map as a normal map); a screen glows with its own picture when <paramref name="glow"/> is above 0.
        /// </summary>
        static Material Painted(string name, Raster raster, float bumps = 1, bool repeat = false, float glow = 0, float smoothnessScale = 1)
        {
            if (materials.TryGetValue(name, out var existing)) return existing;
            var colour = Save(raster.ColourTexture(name), name + "_colour", false, repeat);
            var metal = Save(raster.MetalTexture(name), name + "_metal", true, repeat);
            var normal = Save(raster.NormalTexture(name, bumps), name + "_normal", true, repeat, isNormal: true);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", colour);
            material.SetTexture("_MetallicGlossMap", metal);
            material.SetFloat("_Smoothness", smoothnessScale);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_NORMALMAP");
            if (glow > 0)
            {
                material.SetTexture("_EmissionMap", colour);
                material.SetColor("_EmissionColor", Color.white * glow);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            AssetDatabase.CreateAsset(material, $"{Root}/Materials/{name}.mat");
            materials[name] = material;
            return material;
        }

        static Texture2D Save(Texture2D texture, string name, bool linear, bool repeat, bool isNormal = false)
        {
            string path = $"{Root}/Textures/{name}.png";
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, path), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !linear;
            importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Saves a kit's mesh (with lightmap UVs) and places it under the set: static for the bake, its size in
        /// the lightmap scaled by <paramref name="lightmapScale"/>.
        /// </summary>
        static GameObject Place(string name, MeshKit kit, Material[] slots, Vector3 positionMm = default, Quaternion? rotation = null,
            float lightmapScale = 1, Transform? parent = null, bool castShadows = true)
        {
            kit.EnsureSlots(slots.Length);
            var mesh = kit.ToMesh(name);
            var settings = new UnwrapParam();
            UnwrapParam.SetDefaults(out settings);
            settings.packMargin = 0.01f;
            Unwrapping.GenerateSecondaryUVSet(mesh, settings);
            AssetDatabase.CreateAsset(mesh, $"{Root}/Meshes/{++meshCount:D3}_{name}.asset");
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? set, false);
            go.transform.localPosition = positionMm * 0.001f;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = slots;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveGI = ReceiveGI.Lightmaps;
            var serialized = new SerializedObject(renderer);
            var scale = serialized.FindProperty("m_ScaleInLightmap");
            if (scale != null)
            {
                scale.floatValue = lightmapScale;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic |
                                                       StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        /// <summary>Puts an object of the room's shell on its layer (see <see cref="ShellLayer"/>).</summary>
        static GameObject Shell(GameObject go)
        {
            go.layer = ShellLayer;
            return go;
        }

        /// <summary>One box as its own object: the quickest way to a wall, a shelf or a leg.</summary>
        static GameObject Slab(string name, Vector3 centre, Vector3 size, Material material, float radius = 0, float lightmapScale = 1, float uvPerMm = 0.001f)
        {
            var kit = new MeshKit(1);
            kit.Box(0, Vector3.zero, size, radius, uvPerMm);
            return Place(name, kit, new[] { material }, centre, null, lightmapScale);
        }

        // ------------------------------------------------------------------ sky, lights, probes, bake, look

        /// <summary>
        /// A neutral studio sky, light grey above and darker below: the room's own probe gives the reflections
        /// inside it, and anything lit outside it (the robots' thumbnails) sees a clean grey, not a blue sky.
        /// </summary>
        static void Sky()
        {
            var gradient = new Raster(512, 256, 1);
            for (int y = 0; y < 256; y++)
            {
                float t = y / 255f; // 0 straight down, 1 straight up
                float level = t < 0.5f ? Mathf.Lerp(0.36f, 0.78f, Mathf.SmoothStep(0, 1, t * 2)) : Mathf.Lerp(0.78f, 0.9f, (t - 0.5f) * 2);
                var c = new Color(level, level * 0.995f, level * 0.985f);
                gradient.Rect(0, y, 512, y + 1, Ink.Solid((Color32)c, 0, 0.1f, 0));
            }
            var texture = Save(gradient.ColourTexture("LabSky"), "LabSky_colour", false, true);
            var sky = new Material(Shader.Find("Skybox/Panoramic")) { name = "LabSky" };
            sky.SetTexture("_MainTex", texture);
            sky.SetFloat("_Mapping", 1);
            sky.SetFloat("_ImageType", 0);
            sky.SetFloat("_Exposure", 1.0f);
            sky.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            AssetDatabase.CreateAsset(sky, $"{Root}/Materials/LabSky.mat");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.6f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = false;
        }

        static void Lights()
        {
            // The ceiling's LED panels, baked as area lights: soft light from above all over the room.
            foreach (var (x, z) in PanelPlaces)
            {
                var panel = new GameObject("CeilingPanelLight").AddComponent<Light>();
                panel.transform.SetParent(set, false);
                panel.type = LightType.Rectangle;
                panel.lightmapBakeType = LightmapBakeType.Baked;
                panel.areaSize = new Vector2(0.6f, 1.2f);
                panel.color = new Color(1f, 0.98f, 0.96f);
                panel.intensity = 2.4f;
                panel.transform.localPosition = new Vector3(x, CeilingY - 12, z) * 0.001f;
                panel.transform.localRotation = Quaternion.Euler(90, 0, 0);
            }
            // Daylight through the blinds, baked: a big soft source on the right.
            var day = new GameObject("WindowLight").AddComponent<Light>();
            day.transform.SetParent(set, false);
            day.type = LightType.Rectangle;
            day.lightmapBakeType = LightmapBakeType.Baked;
            day.areaSize = new Vector2(WindowWidth * 0.001f, WindowHeight * 0.001f);
            day.color = new Color(0.86f, 0.92f, 1f);
            day.intensity = 2.2f;
            day.transform.localPosition = new Vector3(RightWall - 25, WindowBottom + WindowHeight / 2, WindowZ) * 0.001f;
            day.transform.localRotation = Quaternion.Euler(0, -90, 0);

            // Mixed lights: direct light and shadows on the robot in real time, their bounce baked.
            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.transform.SetParent(set, false);
            key.type = LightType.Directional;
            key.lightmapBakeType = LightmapBakeType.Mixed;
            key.color = new Color(0.98f, 0.98f, 1f);
            key.intensity = 0.75f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.55f;
            key.cullingMask = ~(1 << ShellLayer);
            key.transform.rotation = Quaternion.LookRotation(new Vector3(-0.72f, -0.62f, -0.3f));

            var lamp = new GameObject("TaskLamp").AddComponent<Light>();
            lamp.transform.SetParent(set, false);
            lamp.type = LightType.Spot;
            lamp.lightmapBakeType = LightmapBakeType.Mixed;
            lamp.color = new Color(1f, 0.96f, 0.9f); // about 4500 K
            lamp.intensity = 0.95f;
            lamp.range = 1.4f;
            lamp.spotAngle = 100f;
            lamp.innerSpotAngle = 40f;
            lamp.shadows = LightShadows.Soft;
            lamp.shadowStrength = 0.6f;
            lamp.transform.position = LampHead * 0.001f + Vector3.down * 0.02f;
            lamp.transform.LookAt(new Vector3(0, 0.04f, 0));
        }

        static void Probes()
        {
            var reflections = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
            reflections.transform.SetParent(set, false);
            reflections.transform.localPosition = new Vector3(0, 0.25f, 0.4f);
            reflections.mode = ReflectionProbeMode.Baked;
            reflections.boxProjection = true;
            reflections.size = new Vector3((RightWall - LeftWall) * 0.001f, (CeilingY - FloorY) * 0.001f, (FrontWall - BackWall) * 0.001f);
            reflections.center = new Vector3(0, (CeilingY + FloorY) * 0.0005f - 0.25f, (FrontWall + BackWall) * 0.0005f - 0.4f);
            reflections.resolution = 512;
            reflections.hdr = true;

            // Light probes: close over the bench, where the robot turns, and sparse through the room.
            var positions = new List<Vector3>();
            for (float x = -900; x <= 900; x += 225)
                for (float y = 20; y <= 620; y += 150)
                    for (float z = -500; z <= 400; z += 225)
                        positions.Add(new Vector3(x, y, z) * 0.001f);
            for (float x = LeftWall + 300; x <= RightWall - 300; x += 800)
                for (float y = FloorY + 200; y <= CeilingY - 200; y += 700)
                    for (float z = BackWall + 300; z <= FrontWall - 300; z += 800)
                        positions.Add(new Vector3(x, y, z) * 0.001f);
            var group = new GameObject("LightProbes").AddComponent<LightProbeGroup>();
            group.transform.SetParent(set, false);
            group.probePositions = positions.ToArray();
        }

        /// <summary>The Garage's camera look in the bright lab: a filmic curve, a cool clean white, depth of field on the robot.</summary>
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
            var colour = Component<ColorAdjustments>();
            colour.postExposure.Override(0.4f);
            colour.contrast.Override(8f);
            colour.saturation.Override(6f);
            var white = Component<WhiteBalance>();
            white.temperature.Override(-6f);
            white.tint.Override(0f);
            var bloom = Component<Bloom>();
            bloom.threshold.Override(1.15f);
            bloom.intensity.Override(0.16f);
            bloom.scatter.Override(0.7f);
            var dof = Component<DepthOfField>();
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(0.72f);
            dof.focalLength.Override(50f);
            dof.aperture.Override(2.8f);
            dof.bladeCount.Override(7);
            var vignette = Component<Vignette>();
            vignette.intensity.Override(0.14f);
            vignette.smoothness.Override(0.5f);
            var grain = Component<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.1f);
            grain.response.Override(0.8f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>
        /// Bakes the lab: bounced light into lightmaps (progressive CPU lightmapper, three bounces, denoised), the
        /// mixed lights' indirect part, the light probes and the reflection probe.
        /// </summary>
        public static void Bake(string folder)
        {
            string path = folder + "/GarageLighting.lighting";
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (settings == null)
            {
                settings = new LightingSettings { name = "GarageLighting" };
                AssetDatabase.CreateAsset(settings, path);
            }
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.lightmapResolution = 28;
            settings.lightmapPadding = 3;
            settings.lightmapMaxSize = 2048;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.maxBounces = 3;
            settings.directSampleCount = 64;
            settings.indirectSampleCount = 384;
            settings.environmentSampleCount = 256;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            settings.ao = true;
            settings.aoMaxDistance = 0.25f;
            settings.aoExponentIndirect = 1f;
            settings.aoExponentDirect = 0f;
            settings.lightmapCompression = LightmapCompression.HighQuality;
            settings.albedoBoost = 1f;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool ok = Lightmapping.Bake();
            Debug.Log($"WhiteLab: lighting baked ({(ok ? "OK" : "FAILED")}) in {watch.Elapsed.TotalSeconds:F1} s");
        }
    }
}
