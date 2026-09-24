using System.IO;
using CoreEngine.Spike.Garage;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.Editor
{
    /// <summary>
    /// Sets up the Phase 0 spike project (URP, physics, player settings, scene) and builds the
    /// Windows IL2CPP player. Run from the CoreEngine menu or in batch mode with -executeMethod.
    /// </summary>
    public static class SpikeSetup
    {
        const string SpikeFolder = "Assets/Spike";
        const string ScenePath = "Assets/Spike/RobotSpike.unity";
        const string GarageScenePath = "Assets/Spike/Garage.unity";

        // The Garage is the main screen (ADR-0009), so it is the first scene of the build.
        static readonly string[] Scenes = { GarageScenePath, ScenePath };

        [MenuItem("CoreEngine/Spike/Configure Project")]
        public static void ConfigureProject()
        {
            var pipeline = CreatePipeline();
            ConfigurePhysics();
            ConfigurePlayer();
            ConfigurePlugins();
            CopyStreamingAssets();
            CreateScene();
            CreateGarageScene();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(GarageScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true),
            };
            AssetDatabase.SaveAssets();
            Debug.Log($"SpikeSetup: project configured, render pipeline {pipeline.name}");
        }

        [MenuItem("CoreEngine/Spike/Build Windows IL2CPP")]
        public static void BuildWindowsIl2cpp()
        {
            string exe = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Spike", "CoreEngineSpike.exe"));
            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"SpikeSetup: build {summary.result} in {summary.totalTime.TotalSeconds:F0} s, " +
                      $"{summary.totalSize / 1e6:F1} MB, {summary.totalErrors} errors -> {exe}");
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>Fast Mono build for debugging; the IL2CPP build stays the reference for speed.</summary>
        [MenuItem("CoreEngine/Spike/Build Windows Mono (debug)")]
        public static void BuildWindowsMono()
        {
            var standalone = NamedBuildTarget.Standalone;
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            try
            {
                string exe = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "SpikeMono", "CoreEngineSpike.exe"));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = Scenes,
                    locationPathName = exe,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                });
                Debug.Log($"SpikeSetup: Mono build {report.summary.result} in {report.summary.totalTime.TotalSeconds:F0} s -> {exe}");
                if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.IL2CPP);
            }
        }

        static UniversalRenderPipelineAsset CreatePipeline()
        {
            string folder = EnsureFolder(SpikeFolder + "/Rendering");
            string pipelinePath = folder + "/SpikePipeline.asset";
            string rendererPath = folder + "/SpikeRenderer.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                var created = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(created, rendererPath);
                pipeline = UniversalRenderPipelineAsset.Create(created);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            // Picture quality (docs/04 §10): 4x MSAA and HDR; soft shadows from a 2048 map in two cascades over
            // 4 m (about 1 mm per shadow texel near the robot), and the Garage's spot lights cast shadows too.
            pipeline.msaaSampleCount = 4;
            pipeline.supportsHDR = true;
            pipeline.shadowDistance = 4f;
            pipeline.shadowCascadeCount = 2;
            pipeline.cascade2Split = 0.25f;
            pipeline.mainLightShadowmapResolution = 2048;
            var quality = new SerializedObject(pipeline);
            SetBool(quality, "m_SoftShadowsSupported", true);
            SetInt(quality, "m_SoftShadowQuality", 2);                // SoftShadowQuality.Medium
            SetBool(quality, "m_AdditionalLightShadowsSupported", true);
            SetBool(quality, "m_AnyShadowsSupported", true);
            SetInt(quality, "m_AdditionalLightsShadowmapResolution", 2048);
            SetInt(quality, "m_ColorGradingMode", 1);                 // grading in HDR, before tonemapping
            quality.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            ConfigureRenderer(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath));

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
            return pipeline;
        }

        /// <summary>Post-processing data (tonemapping, bloom, grading) and screen-space ambient occlusion on the renderer.</summary>
        static void ConfigureRenderer(UniversalRendererData renderer)
        {
            if (renderer == null) return;
            if (renderer.postProcessData == null)
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            // Ambient occlusion darkens creases and contact points, so parts sit on the deck instead of floating.
            var ssao = renderer.rendererFeatures.Find(f => f is ScreenSpaceAmbientOcclusion) as ScreenSpaceAmbientOcclusion;
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "SSAO";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out _, out long localId);
                var data = new SerializedObject(renderer);
                var features = data.FindProperty("m_RendererFeatures");
                var map = data.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ssao;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            var settings = new SerializedObject(ssao);
            SetFloat(settings, "m_Settings.Intensity", 1.6f);
            SetFloat(settings, "m_Settings.Radius", 0.03f);       // 3 cm: the scale of the robot's parts
            SetFloat(settings, "m_Settings.DirectLightingStrength", 0.3f);
            SetBool(settings, "m_Settings.Downsample", true);     // half resolution: a quarter of the cost
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ssao);
            EditorUtility.SetDirty(renderer);
        }

        /// <summary>
        /// The look of both scenes: a neutral tone curve keeps bright plastic from clipping to flat white, with a
        /// little more contrast and colour, a soft bloom on bright highlights and a light vignette.
        /// </summary>
        static VolumeProfile CreatePostProfile()
        {
            string path = EnsureFolder(SpikeFolder + "/Rendering") + "/SpikePost.asset";
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
            Component<Tonemapping>().mode.Override(TonemappingMode.Neutral);
            var color = Component<ColorAdjustments>();
            color.postExposure.Override(0.15f);
            color.contrast.Override(12f);
            color.saturation.Override(8f);
            var bloom = Component<Bloom>();
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            var vignette = Component<Vignette>();
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static void AddPostProcessing(VolumeProfile profile)
        {
            var volume = new GameObject("PostProcessing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        static void ConfigurePhysics()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");
            if (assets.Length > 0)
            {
                var settings = new SerializedObject(assets[0]);
                SetInt(settings, "m_SolverType", 1); // Temporal Gauss-Seidel (docs/07 §2)
                SetInt(settings, "m_DefaultSolverIterations", 12);
                SetInt(settings, "m_DefaultSolverVelocityIterations", 4);
                SetFloat(settings, "m_DefaultContactOffset", 0.002f);
                SetFloat(settings, "m_DefaultMaxAngularSpeed", 200f);
                SetFloat(settings, "m_SleepThreshold", 0.001f);
                SetBool(settings, "m_EnableEnhancedDeterminism", true);
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            Time.fixedDeltaTime = 0.01f;
        }

        static void ConfigurePlayer()
        {
            var standalone = NamedBuildTarget.Standalone;
            PlayerSettings.companyName = "CoreEngine";
            PlayerSettings.productName = "CoreEngine Spike";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(standalone, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetIl2CppCodeGeneration(standalone, Il2CppCodeGeneration.OptimizeSpeed);
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.enableFrameTimingStats = true; // GPU and CPU time per frame for the benchmark
        }

        /// <summary>manifoldc.dll (native/manifold/build.ps1) loads in the Windows editor and 64-bit Windows players only.</summary>
        static void ConfigurePlugins()
        {
            const string path = "Assets/Plugins/x86_64/manifoldc.dll";
            if (!(AssetImporter.GetAtPath(path) is PluginImporter importer))
            {
                Debug.LogWarning($"SpikeSetup: {path} not found; run native/manifold/build.ps1");
                return;
            }
            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetEditorData("CPU", "x86_64");
            importer.SetEditorData("OS", "Windows");
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
            importer.SaveAndReimport();
        }

        /// <summary>Firmware and sketch files come from the golden tests in core/, so there is one source of truth.</summary>
        static void CopyStreamingAssets()
        {
            string golden = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "core", "CoreEngine.Sim.Tests", "Golden"));
            string firmware = Path.Combine(Application.streamingAssetsPath, "Firmware");
            string sketches = Path.Combine(Application.streamingAssetsPath, "Sketches");
            Directory.CreateDirectory(firmware);
            Directory.CreateDirectory(sketches);
            foreach (string name in new[] { "Blink", "ObstacleAvoider" })
                File.Copy(Path.Combine(golden, "Hex", name + ".hex"), Path.Combine(firmware, name + ".hex"), true);
            File.Copy(Path.Combine(golden, "Sketches", "ObstacleAvoider", "ObstacleAvoider.ino"), Path.Combine(sketches, "ObstacleAvoider.ino"), true);
            AssetDatabase.Refresh();
        }

        /// <summary>Runtime theme and panel settings for the UI Toolkit spike.</summary>
        static PanelSettings CreatePanelSettings()
        {
            string folder = EnsureFolder(SpikeFolder + "/UI");
            string themePath = folder + "/SpikeTheme.tss";
            if (!File.Exists(themePath))
            {
                File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(themePath);
            }
            string settingsPath = folder + "/SpikePanel.asset";
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(settingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        static void CreateScene()
        {
            string materials = EnsureFolder(SpikeFolder + "/Materials");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            AddPostProcessing(CreatePostProfile());

            var go = new GameObject("Spike");
            var spike = go.AddComponent<RobotSpike>();
            spike.floorMaterial = Lit(materials, "Floor", new Color(0.92f, 0.92f, 0.88f), 0.4f);
            spike.wallMaterial = Lit(materials, "Wall", new Color(0.55f, 0.6f, 0.68f), 0.2f);
            spike.obstacleMaterial = Lit(materials, "Obstacle", new Color(0.85f, 0.55f, 0.25f), 0.2f);
            spike.chassisMaterial = Lit(materials, "Chassis", new Color(0.15f, 0.35f, 0.75f), 0.6f);
            spike.wheelMaterial = Lit(materials, "Wheel", new Color(0.08f, 0.08f, 0.08f), 0.1f);
            spike.sensorMaterial = Lit(materials, "Sensor", new Color(0.1f, 0.55f, 0.85f), 0.5f);
            spike.acrylicMaterial = Transparent(materials, "BodyAcrylic");
            spike.partMaterial = PartTextured(materials, "PartTextured");
            spike.rayMaterial = Unlit(materials, "SonarRay");
            var csg = go.AddComponent<CsgSpike>();
            csg.bodyMaterial = Lit(materials, "Body", new Color(0.95f, 0.45f, 0.1f), 0.35f); // orange PLA
            go.AddComponent<SpikeBenchmark>();

            var uiObject = new GameObject("UI");
            uiObject.AddComponent<UIDocument>().panelSettings = CreatePanelSettings();
            var ui = uiObject.AddComponent<UiSpike>();
            ui.styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(SpikeFolder + "/UI/UiSpike.uss");
            ui.robot = spike;

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void CreateGarageScene()
        {
            string materials = EnsureFolder(SpikeFolder + "/Materials");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            // The photographed robotics lab when its CC0 files are there (tools/fetch-lab-assets.ps1), else the drawn room.
            bool lab = SpikeLab.AssetsPresent;
            if (lab)
            {
                SpikeLab.ConfigureImports();
                SpikeLab.Build(materials);
                AddPostProcessing(SpikeLab.PostProfile(EnsureFolder(SpikeFolder + "/Rendering")));
            }
            else
            {
                AddPostProcessing(CreatePostProfile());
                Debug.LogWarning("SpikeSetup: the lab files are missing (tools/fetch-lab-assets.ps1); the Garage keeps its drawn room");
            }

            var go = new GameObject("Garage");
            go.AddComponent<UIDocument>().panelSettings = CreatePanelSettings();
            var garage = go.AddComponent<GarageSpike>();
            garage.litMaterial = Lit(materials, "GarageLit", Color.white, 0.5f); // template for runtime materials
            // The Body Studio's see-through shapes, handles and grid (Shaders/): referenced here so that the build has them.
            garage.overlayMaterial = LoadOrCreate(materials, "StudioOverlay", "CoreEngine/StudioOverlay");
            garage.acrylicMaterial = Transparent(materials, "BodyAcrylic");
            garage.partMaterial = PartTextured(materials, "PartTextured");
            garage.gridMaterial = LoadOrCreate(materials, "StudioGrid", "CoreEngine/StudioGrid");
            garage.styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(SpikeFolder + "/UI/Garage.uss");
            garage.editorStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(SpikeFolder + "/UI/UiSpike.uss");
            EditorSceneManager.SaveScene(scene, GarageScenePath);
            if (lab)
            {
                SpikeLab.BakeLighting(EnsureFolder(SpikeFolder + "/Rendering"));
                EditorSceneManager.SaveScene(scene, GarageScenePath);
            }
        }

        static Material Lit(string folder, string name, Color color, float smoothness)
        {
            var material = LoadOrCreate(folder, name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// URP Lit set to transparent, premultiplied as glass is (its reflections stay bright): acrylic body shapes
        /// copy it, and being in a scene it keeps the transparent shader variant in the build.
        /// </summary>
        static Material Transparent(string folder, string name)
        {
            var material = LoadOrCreate(folder, name, "Universal Render Pipeline/Lit");
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 1);
            material.SetFloat("_AlphaClip", 0);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(0.86f, 0.93f, 1f, 0.3f));
            material.SetFloat("_Smoothness", 0.93f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// URP Lit with a normal map, a metallic map and emission switched on: the part models' painted materials
        /// copy it (PartLooks), and being in a scene it keeps those shader variants in the build.
        /// </summary>
        static Material PartTextured(string folder, string name)
        {
            var material = LoadOrCreate(folder, name, "Universal Render Pipeline/Lit");
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 1f);
            material.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Unlit(string folder, string name)
        {
            // The particle shader multiplies by vertex colour, so each LineRenderer can have its own colour.
            var material = LoadOrCreate(folder, name, "Universal Render Pipeline/Particles/Unlit");
            material.SetColor("_BaseColor", Color.white);
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

        static string EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
            return path;
        }

        static void SetInt(SerializedObject so, string name, int value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.intValue = value;
            else Debug.LogWarning($"SpikeSetup: setting {name} not found");
        }

        static void SetFloat(SerializedObject so, string name, float value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.floatValue = value;
            else Debug.LogWarning($"SpikeSetup: setting {name} not found");
        }

        static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = value;
            else Debug.LogWarning($"SpikeSetup: setting {name} not found");
        }
    }
}
