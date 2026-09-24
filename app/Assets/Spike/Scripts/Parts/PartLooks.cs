using System.Collections.Generic;
using UnityEngine;

namespace CoreEngine.Spike.Parts
{
    /// <summary>
    /// The materials of the part models, made once per session: plain ones shared by every part (gold pins, tin,
    /// black plastic, a hole's darkness) and textured ones painted with a <see cref="Raster"/> (a board's top, a
    /// chip's marking, a sticker). Textured materials copy <c>PartTextured</c>, the URP Lit material SpikeSetup
    /// saves with a normal map, a metallic map and emission switched on, so those shader variants are in the build.
    /// </summary>
    public static class PartLooks
    {
        static Material? lit, textured;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static void Init(Material litTemplate, Material? texturedTemplate)
        {
            if (lit == null) lit = litTemplate;
            if (textured == null) textured = texturedTemplate;
        }

        public static bool Ready => lit != null;

        /// <summary>A plain material: an sRGB colour, its smoothness and how metallic it is.</summary>
        public static Material Solid(string key, Color colour, float smoothness, float metallic = 0)
        {
            if (cache.TryGetValue(key, out var material) && material != null) return material;
            material = new Material(lit) { name = "Part " + key };
            material.SetTexture("_BaseMap", null);
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            cache[key] = material;
            return material;
        }

        public static Material Solid(string key, string hex, float smoothness, float metallic = 0) =>
            Solid(key, ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta, smoothness, metallic);

        /// <summary>
        /// A material painted by <paramref name="paint"/> the first time it is asked for: its colour, metallic map
        /// and the normal map from its heights (<paramref name="bumps"/> scales the slopes).
        /// </summary>
        public static Material Textured(string key, System.Func<Raster> paint, float bumps = 1, bool repeat = false)
        {
            if (cache.TryGetValue(key, out var material) && material != null) return material;
            var raster = paint();
            material = new Material(textured != null ? textured : lit) { name = "Part " + key };
            var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            var colourMap = raster.ColourTexture(key + " colour");
            var metalMap = raster.MetalTexture(key + " metal");
            var normalMap = raster.NormalTexture(key + " normal", bumps);
            colourMap.wrapMode = metalMap.wrapMode = normalMap.wrapMode = wrap;
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", colourMap);
            material.SetTexture("_MetallicGlossMap", metalMap);
            material.SetFloat("_Smoothness", 1);
            material.SetFloat("_Metallic", 1);
            material.SetTexture("_BumpMap", normalMap);
            material.SetFloat("_BumpScale", 1);
            material.SetColor("_EmissionColor", Color.black);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_EMISSION");
            cache[key] = material;
            return material;
        }

        /// <summary>
        /// A light's lens, lit or not: a small painted material (so it uses the same shader variant as the other
        /// textured ones) whose emission glows in <paramref name="glow"/> when lit.
        /// </summary>
        public static Material Lens(string key, Color body, Color glow, bool on)
        {
            string name = key + (on ? " on" : " off");
            if (cache.TryGetValue(name, out var material) && material != null) return material;
            material = Textured(name, () =>
            {
                var r = new Raster(1, 1, 4);
                r.Fill(Ink.Solid(on ? (Color32)Color.Lerp(body, glow, 0.6f) : (Color32)body, 0, 0.9f, 0));
                return r;
            });
            material.SetColor("_EmissionColor", on ? glow * 6f : Color.black);
            return material;
        }

        // ------------------------------------------------------------------ the common ones

        public static Material Gold => Solid("gold", new Color(1.00f, 0.80f, 0.42f), 0.82f, 1f);
        public static Material Tin => Solid("tin", new Color(0.80f, 0.80f, 0.78f), 0.68f, 1f);
        public static Material Nickel => Solid("nickel", new Color(0.83f, 0.83f, 0.82f), 0.86f, 1f);
        public static Material Chrome => Solid("chrome", new Color(0.92f, 0.92f, 0.93f), 0.96f, 1f);
        public static Material Aluminium => Solid("aluminium", new Color(0.84f, 0.85f, 0.86f), 0.55f, 1f);
        public static Material Brass => Solid("brass", new Color(0.86f, 0.70f, 0.40f), 0.7f, 1f);
        public static Material Copper => Solid("copper", new Color(0.93f, 0.60f, 0.45f), 0.7f, 1f);
        public static Material Anodised => Solid("anodised black", new Color(0.055f, 0.055f, 0.06f), 0.5f, 0.45f);
        public static Material BlackPlastic => Solid("black plastic", new Color(0.035f, 0.035f, 0.04f), 0.48f);
        public static Material MatteBlack => Solid("matte black", new Color(0.045f, 0.045f, 0.05f), 0.28f);
        public static Material Hole => Solid("hole", new Color(0.004f, 0.004f, 0.005f), 0.05f);
        public static Material WhitePlastic => Solid("white plastic", new Color(0.90f, 0.90f, 0.87f), 0.45f);
        public static Material Fr4Edge => Solid("fr4 edge", new Color(0.40f, 0.43f, 0.36f), 0.25f);
        public static Material Rubber => Solid("rubber", new Color(0.045f, 0.045f, 0.047f), 0.22f);
    }
}
