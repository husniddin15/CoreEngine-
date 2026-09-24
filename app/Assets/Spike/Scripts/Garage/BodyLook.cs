using System.Collections.Generic;
using CoreEngine.Sim.Design;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// How the body materials look (docs/08 §2): PLA and EVA foam in any colour, clear or tinted acrylic, birch
    /// plywood with its grain, brown corrugated cardboard, white PVC foam board and bare aluminium. One URP Lit
    /// material per material and colour, made once and shared. Wood and cardboard get textures generated here
    /// (tileable, so the box-mapped coordinates of the body meshes can repeat them every 100 mm).
    /// </summary>
    public static class BodyLook
    {
        static Material? lit, glass;
        static readonly Dictionary<(BodyMaterial, string), Material> cache = new Dictionary<(BodyMaterial, string), Material>();
        static Texture2D? wood, cardboard;

        /// <summary>
        /// The templates: the opaque Lit material the robot uses, and a transparent Lit one for acrylic (SpikeSetup
        /// makes it, so the build has the transparent shader variant). Without it acrylic is drawn opaque.
        /// </summary>
        public static void Init(Material opaque, Material? transparent)
        {
            if (lit == opaque && glass == (transparent ?? opaque)) return;
            lit = opaque;
            glass = transparent ?? opaque;
            foreach (var material in cache.Values) Object.Destroy(material);
            cache.Clear();
        }

        /// <summary>Sets up with the robot's own material when nothing else did (the acrylic is then opaque).</summary>
        public static void EnsureInit(Material opaque)
        {
            if (lit == null) Init(opaque, null);
        }

        /// <summary>True for the materials sold in colours: the palette offers colours for these.</summary>
        public static bool Coloured(BodyMaterial material) =>
            material == BodyMaterial.Pla || material == BodyMaterial.EvaFoam || material == BodyMaterial.Acrylic;

        /// <summary>The colour a material has when the player chose none.</summary>
        public static Color DefaultColour(BodyMaterial material) => material switch
        {
            BodyMaterial.Pla => new Color(0.93f, 0.93f, 0.90f),
            BodyMaterial.Acrylic => new Color(0.86f, 0.93f, 1.00f),
            BodyMaterial.Plywood => new Color(0.86f, 0.73f, 0.54f),
            BodyMaterial.Cardboard => new Color(0.66f, 0.50f, 0.33f),
            BodyMaterial.EvaFoam => new Color(0.20f, 0.46f, 0.86f),
            BodyMaterial.FoamBoard => new Color(0.95f, 0.95f, 0.93f),
            _ => new Color(0.79f, 0.80f, 0.82f), // aluminium
        };

        /// <summary>A colour from "#rrggbb", or the material's own when empty or unreadable.</summary>
        public static Color ColourOf(BodyMaterial material, string colour) =>
            colour.Length > 0 && ColorUtility.TryParseHtmlString(colour, out var c) ? c : DefaultColour(material);

        public static Material Get(BodyMaterial material, string colour)
        {
            var key = (material, Coloured(material) || material == BodyMaterial.Aluminium ? colour : "");
            if (cache.TryGetValue(key, out var made) && made != null) return made;
            if (lit == null) throw new System.InvalidOperationException("BodyLook.Init was not called");
            var m = new Material(material == BodyMaterial.Acrylic ? glass! : lit) { name = $"Body {material} {colour}" };
            var tint = ColourOf(material, key.Item2);
            m.SetFloat("_Metallic", 0);
            switch (material)
            {
                case BodyMaterial.Acrylic:
                    // Tinted acrylic lets most light through; the clear kind a little more. Glossy both ways.
                    tint.a = key.Item2.Length > 0 ? 0.55f : 0.30f;
                    m.SetFloat("_Smoothness", 0.93f);
                    break;
                case BodyMaterial.Pla:
                    m.SetFloat("_Smoothness", 0.42f);
                    break;
                case BodyMaterial.Plywood:
                    m.SetTexture("_BaseMap", wood ??= WoodTexture());
                    tint = Color.white;
                    m.SetFloat("_Smoothness", 0.22f);
                    break;
                case BodyMaterial.Cardboard:
                    m.SetTexture("_BaseMap", cardboard ??= CardboardTexture());
                    tint = Color.white;
                    m.SetFloat("_Smoothness", 0.06f);
                    break;
                case BodyMaterial.EvaFoam:
                    m.SetFloat("_Smoothness", 0.14f);
                    break;
                case BodyMaterial.FoamBoard:
                    m.SetFloat("_Smoothness", 0.20f);
                    break;
                case BodyMaterial.Aluminium:
                    m.SetFloat("_Metallic", 1);
                    m.SetFloat("_Smoothness", 0.62f);
                    break;
            }
            m.SetColor("_BaseColor", tint);
            cache[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ textures

        /// <summary>
        /// Noise that repeats at the texture's edges: four samples blended toward the neighbouring tile's, so a
        /// face covered by many repeats shows no seams.
        /// </summary>
        static float TileNoise(float u, float v, float scaleU, float scaleV, float seed)
        {
            float N(float a, float b) => Mathf.PerlinNoise(seed + a * scaleU, seed * 1.7f + b * scaleV);
            float a0 = N(u, v), b0 = N(u - 1, v), c0 = N(u, v - 1), d0 = N(u - 1, v - 1);
            return Mathf.Lerp(Mathf.Lerp(a0, b0, u), Mathf.Lerp(c0, d0, u), v);
        }

        static Texture2D NewTexture(string name, int size) => new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = name,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8,
        };

        /// <summary>Birch plywood: pale wood with wavy grain along one direction and a few darker late-wood lines.</summary>
        static Texture2D WoodTexture()
        {
            const int size = 512;
            var texture = NewTexture("Plywood", size);
            var pixels = new Color32[size * size];
            var light = new Color(0.90f, 0.78f, 0.60f);
            var dark = new Color(0.72f, 0.55f, 0.36f);
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)size;
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float wave = TileNoise(u, v, 3, 2, 11.3f) * 6;           // the grain wanders slowly
                    float rings = Mathf.Sin((v * 36 + wave) * Mathf.PI * 2) * 0.5f + 0.5f;
                    float late = Mathf.Pow(rings, 9);                         // thin dark lines
                    float fibre = TileNoise(u, v, 64, 4, 3.1f);               // fine streaks along the grain
                    float shade = 0.9f + 0.08f * fibre - 0.05f * rings;
                    pixels[y * size + x] = Color.Lerp(light * shade, dark, late * 0.55f);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        /// <summary>Kraft cardboard: brown with fibres, blotches and the faint ridges of the flutes under the liner.</summary>
        static Texture2D CardboardTexture()
        {
            const int size = 512;
            var texture = NewTexture("Cardboard", size);
            var pixels = new Color32[size * size];
            var kraft = new Color(0.70f, 0.53f, 0.35f);
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)size;
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float blotch = TileNoise(u, v, 6, 6, 5.7f);
                    float fibre = TileNoise(u, v, 120, 40, 8.9f);
                    float flutes = Mathf.Sin(v * 25 * Mathf.PI * 2) * 0.5f + 0.5f; // one ridge every 4 mm
                    float shade = 0.9f + 0.1f * blotch + 0.05f * (fibre - 0.5f) - 0.025f * flutes;
                    pixels[y * size + x] = kraft * shade;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
