using System.Collections;
using System.Collections.Generic;
using CoreEngine.Sim.Design;
using CoreEngine.Spike.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// Pictures of the catalogue's parts, rendered once from their 3D models (the owner, 2026-09-25: "use more
    /// images ... in design"): the Studio's library, its list of parts and its inspector, Wire's Look at buttons and
    /// Check &amp; repair's rows show the part itself instead of a line drawing. Each part is drawn twice, on black
    /// and on white: where the two differ is background, so a picture keeps its own transparency and sits on any
    /// panel. Until a picture is ready its line icon stands in.
    /// </summary>
    public sealed partial class GarageSpike
    {
        const int PicturePx = 160;   // the picture
        const int RenderPx = 384;    // what is drawn, cropped round the part and scaled down to the picture
        static readonly Dictionary<string, Texture2D> partPictures = new Dictionary<string, Texture2D>();

        /// <summary>How long the part pictures took to render, all of them (for the benchmark), or -1 before.</summary>
        public static double PartPicturesMs { get; private set; } = -1;

        static Texture2D? PartPicture(string partId) => partPictures.TryGetValue(partId, out var picture) ? picture : null;

        /// <summary>A part's picture as a UI element, or its line icon until the picture is ready.</summary>
        static VisualElement PartImage(string partId, Icon fallback, string className)
        {
            var picture = PartPicture(partId);
            if (picture == null) return Classed(new IconView(fallback), className);
            var image = new Image { image = picture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList(className);
            image.AddToClassList("part-picture");
            return image;
        }

        /// <summary>Renders every part not drawn yet, then lets the open panels show the pictures.</summary>
        IEnumerator RenderPartPictures()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int index = 0;
            foreach (var def in PartCatalog.All)
                if (!partPictures.ContainsKey(def.Id)) yield return RenderPartPicture(def, index++);
            PartPicturesMs = watch.Elapsed.TotalMilliseconds;
            if (studio != null && libraryTab == LibraryTab.Parts) RenderLibrary();
            renderSide?.Invoke();
        }

        /// <summary>
        /// One part on a stage far from the lab, seen from the front left and a little above, as a buyer sees it
        /// in a shop, framed to fill the picture; drawn on black and on white and the two combined.
        /// </summary>
        IEnumerator RenderPartPicture(PartDef def, int index)
        {
            var stage = new GameObject("PartPictureStage");
            stage.transform.position = new Vector3(-60f - index * 2f, 0, 0);
            var project = new RobotProject { Name = def.Name, Design = new RobotDesign { Body = new BodyDesign { Decks = 0 } } };
            project.Design.Parts.Add(new PartInstance { Id = "part", Part = def.Id });
            var visual = RobotVisuals.Build(stage.transform, null, project, litMaterial);

            var bounds = new Bounds(stage.transform.position, Vector3.zero);
            bool any = false;
            foreach (var renderer in stage.GetComponentsInChildren<Renderer>())
            {
                if (!any) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                any = true;
            }

            var target = RenderTexture.GetTemporary(RenderPx, RenderPx, 24, RenderTextureFormat.ARGB32);
            var camera = new GameObject("PartPictureCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = target;
            camera.fieldOfView = 22f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false; // black and white stay black and white
            var turn = Quaternion.Euler(def.Kind == PartKind.Board || def.Kind == PartKind.MotorDriver ? 38f : 26f, 215f, 0);
            camera.transform.rotation = turn;
            // Far enough back that every corner of the part's box is inside the picture; the picture is cropped to
            // the part itself afterwards, since a round wheel or a thin board fills little of its box.
            float reach = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.9f, distance = 0.02f;
            for (int i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var local = Quaternion.Inverse(turn) * (corner - bounds.center);
                distance = Mathf.Max(distance, Mathf.Abs(local.x) / reach - local.z, Mathf.Abs(local.y) / reach - local.z);
            }
            camera.transform.position = bounds.center - turn * Vector3.forward * distance;
            camera.nearClipPlane = Mathf.Max(0.005f, distance - bounds.extents.magnitude * 1.5f);
            camera.farClipPlane = distance + bounds.extents.magnitude * 2f;

            Texture2D? onBlack = null, onWhite = null;
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                for (int attempt = 0; attempt < 60 && (onBlack == null || onWhite == null); attempt++)
                {
                    if (depthOfField != null) depthOfField.active = false;
                    onBlack ??= Draw(camera, request, target, Color.black);
                    onWhite ??= Draw(camera, request, target, Color.white);
                    if (onBlack == null || onWhite == null) yield return null;
                }
            }
            if (onBlack != null && onWhite != null)
            {
                var picture = Combine(onBlack, onWhite);
                partPictures[def.Id] = picture;
                if (SpikeReport.Active) System.IO.File.WriteAllBytes(SpikeReport.Shot("part-" + def.Id), picture.EncodeToPNG());
            }
            if (onBlack != null) Destroy(onBlack);
            if (onWhite != null) Destroy(onWhite);
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            Destroy(camera.gameObject);
            visual.Destroy();
            Destroy(stage);
            yield return null;
        }

        /// <summary>
        /// The camera's picture on one background colour, or null when the render did not happen (URP drops render
        /// requests in the first frames after start-up; the corners then do not show the background).
        /// </summary>
        static Texture2D? Draw(Camera camera, RenderPipeline.StandardRequest request, RenderTexture target, Color background)
        {
            camera.backgroundColor = background;
            RenderPipeline.SubmitRenderRequest(camera, request);
            var image = new Texture2D(RenderPx, RenderPx, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, RenderPx, RenderPx), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            var corner = image.GetPixel(0, 0);
            if (Mathf.Abs(corner.r - background.r) < 0.02f && Mathf.Abs(corner.g - background.g) < 0.02f && Mathf.Abs(corner.b - background.b) < 0.02f) return image;
            Destroy(image);
            return null;
        }

        /// <summary>
        /// The part with its transparency, cropped round it: a pixel that is black on black and white on white is all
        /// background, one that is the same on both is all part; between them, the difference is how much background
        /// shows. The drawing on black is the part's colour already multiplied by its cover, which is what a smooth
        /// scaling down needs; the square round the part is scaled down to the picture, a little margin all round.
        /// </summary>
        static Texture2D Combine(Texture2D onBlack, Texture2D onWhite)
        {
            var black = onBlack.GetPixels();
            var white = onWhite.GetPixels();
            int n = RenderPx;
            var alpha = new float[black.Length];
            int minX = n, minY = n, maxX = -1, maxY = -1;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    var b = black[i];
                    var w = white[i];
                    float a = Mathf.Clamp01(1 - ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / 3);
                    alpha[i] = a;
                    if (a < 0.03f) continue;
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            if (maxX < 0) (minX, minY, maxX, maxY) = (0, 0, n - 1, n - 1);
            float side = Mathf.Max(maxX - minX + 1, maxY - minY + 1) * 1.08f;
            float x0 = (minX + maxX + 1) / 2f - side / 2, y0 = (minY + maxY + 1) / 2f - side / 2;

            // Each picture pixel averages the drawn pixels it covers (a box filter), for smooth edges.
            var result = new Color[PicturePx * PicturePx];
            float step = side / PicturePx;
            int samples = Mathf.Clamp(Mathf.CeilToInt(step), 1, 4);
            for (int v = 0; v < PicturePx; v++)
                for (int u = 0; u < PicturePx; u++)
                {
                    float r = 0, g = 0, bl = 0, a = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                        {
                            int x = Mathf.FloorToInt(x0 + (u + (sx + 0.5f) / samples) * step);
                            int y = Mathf.FloorToInt(y0 + (v + (sy + 0.5f) / samples) * step);
                            if (x < 0 || y < 0 || x >= n || y >= n) continue;
                            int i = y * n + x;
                            var c = black[i];
                            r += c.r;
                            g += c.g;
                            bl += c.b;
                            a += alpha[i];
                        }
                    float count = samples * samples;
                    a /= count;
                    result[v * PicturePx + u] = a < 0.004f ? Color.clear
                        : new Color(Mathf.Clamp01(r / count / a), Mathf.Clamp01(g / count / a), Mathf.Clamp01(bl / count / a), a);
                }
            var picture = new Texture2D(PicturePx, PicturePx, TextureFormat.RGBA32, true) { name = "PartPicture", wrapMode = TextureWrapMode.Clamp };
            picture.SetPixels(result);
            picture.Apply(updateMipmaps: true);
            return picture;
        }
    }
}
