using System;
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
    /// Real pictures instead of line drawings (the owner, 2026-09-25: "use more images ... we need real images"),
    /// all rendered once from the game's own 3D models when the Garage opens:
    /// <list type="bullet">
    /// <item>every catalogue part, for the Studio's library, list and inspector, Wire's Look at buttons and Check
    /// &amp; repair's rows;</item>
    /// <item>every body shape, for the Studio's shape tiles;</item>
    /// <item>the Garage's action cards: parts laid out (Build), an Uno wired to an L298N (Wire), the lab's monitor
    /// with code on it (Code), a bare two-deck chassis (Body), the robot itself (Customize) and the lab's multimeter
    /// (Check &amp; repair).</item>
    /// </list>
    /// A staged picture is drawn twice, on black and on white: where the two differ is background, so it keeps its
    /// own transparency and sits on any panel; it is cropped round what it shows and scaled down smoothly. A lab
    /// object is photographed where it stands, in the lab's light. Until a picture is ready a line icon stands in.
    /// </summary>
    public sealed partial class GarageSpike
    {
        const int PicturePx = 160;          // a part's or shape's picture
        const int CardWidth = 320, CardHeight = 160;
        const int Oversize = 3;             // staged pictures are drawn this many times larger, then cropped and scaled down
        static readonly Dictionary<string, Texture2D> partPictures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<FeatureKind, Texture2D> shapePictures = new Dictionary<FeatureKind, Texture2D>();
        static readonly Dictionary<string, Texture2D> cardPictures = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, VisualElement> cardImages = new Dictionary<string, VisualElement>();
        int stagesUsed;

        /// <summary>How long all the pictures took to render (for the benchmark), or -1 before.</summary>
        public static double PicturesMs { get; private set; } = -1;

        static Texture2D? PartPicture(string partId) => partPictures.TryGetValue(partId, out var picture) ? picture : null;

        /// <summary>A part's picture as a UI element, or its line icon until the picture is ready.</summary>
        static VisualElement PartImage(string partId, Icon fallback, string className)
        {
            var picture = PartPicture(partId);
            return picture == null ? Classed(new IconView(fallback), className) : Picture(picture, className);
        }

        /// <summary>A shape's picture as a UI element, or its line icon until the picture is ready.</summary>
        static VisualElement ShapeImage(FeatureKind kind, Icon fallback, string className) =>
            shapePictures.TryGetValue(kind, out var picture) ? Picture(picture, className) : Classed(new IconView(fallback), className);

        static VisualElement Picture(Texture2D picture, string className)
        {
            var image = new Image { image = picture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList(className);
            image.AddToClassList("part-picture");
            return image;
        }

        // ------------------------------------------------------------------ what is rendered

        /// <summary>Renders every picture not drawn yet, then lets the open panels and the action cards show them.</summary>
        IEnumerator RenderPictures()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var def in PartCatalog.All)
            {
                if (partPictures.ContainsKey(def.Id)) continue;
                var design = Stage();
                design.Parts.Add(new PartInstance { Id = "part", Part = def.Id });
                bool flat = def.Kind == PartKind.Board || def.Kind == PartKind.MotorDriver;
                yield return DrawDesign(design, Quaternion.Euler(flat ? 38f : 26f, 215f, 0), PicturePx, PicturePx, picture =>
                {
                    partPictures[def.Id] = picture;
                    Keep("part-" + def.Id, picture);
                });
            }
            foreach (var kind in PaletteKinds)
            {
                if (shapePictures.ContainsKey(kind)) continue;
                var design = Stage();
                var shape = BodyFeature.Create(kind, false, 0, BodyMaterial.Pla);
                shape.Colour = "#DCE3EC"; // light grey PLA: the shape reads by its shading on a dark tile
                design.Body.AddFeature(shape);
                float above = kind == FeatureKind.Plate ? 55f : 30f; // a thin plate shows its holes only from high up
                yield return DrawDesign(design, Quaternion.Euler(above, 215f, 0), PicturePx, PicturePx, picture =>
                {
                    shapePictures[kind] = picture;
                    Keep("shape-" + kind.ToString().ToLowerInvariant(), picture);
                });
            }
            yield return RenderCardPictures();
            yield return RenderArenaPicture();
            PicturesMs = watch.Elapsed.TotalMilliseconds;
            if (studio != null) RenderLibrary();
            renderSide?.Invoke();
        }

        static RobotDesign Stage() => new RobotDesign { Body = new BodyDesign { Decks = 0 } };

        /// <summary>Saves a picture beside the benchmark's report (-part-…, -shape-…, -card-…), to look at.</summary>
        static void Keep(string name, Texture2D picture)
        {
            if (SpikeReport.Active) System.IO.File.WriteAllBytes(SpikeReport.Shot(name), picture.EncodeToPNG());
        }

        /// <summary>The action cards' pictures; Customize shows the robot's own thumbnail (<see cref="ShowCardPictures"/>).</summary>
        IEnumerator RenderCardPictures()
        {
            // Build: the kit's parts laid out on the bench, as they come out of the box.
            var parts = Stage();
            void Lay(string id, string part, float x, float z, float turn)
            {
                var def = PartCatalog.Get(part)!;
                parts.Parts.Add(new PartInstance { Id = id, Part = part, X = x, Y = DesignGeometry.RestHeight(def), Z = z, Rotation = turn });
            }
            Lay("uno1", PartCatalog.Uno, -95, 0, 0);
            Lay("driver1", PartCatalog.L298N, -25, 15, 0);
            Lay("sonar1", PartCatalog.HcSr04, 35, 45, 0);
            Lay("battery1", PartCatalog.Battery4AA, 40, -45, 90);
            Lay("motor1", PartCatalog.TtMotor, 115, 5, 0);
            yield return DrawDesign(parts, Quaternion.Euler(30f, 205f, 0), CardWidth, CardHeight, picture => cardPictures["act.build"] = picture);

            // Wire: an Uno joined to an L298N by six jumpers.
            var wired = Stage();
            wired.Parts.Add(new PartInstance { Id = "uno1", Part = PartCatalog.Uno, X = -45, Y = DesignGeometry.RestHeight(PartCatalog.Get(PartCatalog.Uno)!), Z = 0, Rotation = 90 });
            wired.Parts.Add(new PartInstance { Id = "driver1", Part = PartCatalog.L298N, X = 45, Y = DesignGeometry.RestHeight(PartCatalog.Get(PartCatalog.L298N)!), Z = 0, Rotation = 0 });
            wired.AddWire("uno1", "D5", "driver1", "IN1", "yellow");
            wired.AddWire("uno1", "D6", "driver1", "IN2", "green");
            wired.AddWire("uno1", "D7", "driver1", "IN3", "blue");
            wired.AddWire("uno1", "D8", "driver1", "IN4", "orange");
            wired.AddWire("driver1", "+5V", "uno1", "5V", "red");
            wired.AddWire("driver1", "GND", "uno1", "GND.2", "black");
            yield return DrawDesign(wired, Quaternion.Euler(50f, 195f, 0), CardWidth, CardHeight, picture => cardPictures["act.wire"] = picture);

            // Body: the kit's two blue acrylic decks on their standoffs, nothing on them.
            var chassis = DesignPresets.ObstacleAvoiderKit();
            chassis.Parts.Clear();
            chassis.Wires.Clear();
            foreach (var shape in chassis.Body.Features)
                if (shape.Material != BodyMaterial.Aluminium && shape.Kind != FeatureKind.Group) shape.Colour = "#2F6FD8";
            yield return DrawDesign(chassis, Quaternion.Euler(24f, 215f, 0), CardWidth, CardHeight, picture => cardPictures["act.body"] = picture);

            // Code and Check & repair: the lab's own monitor, code on its screen, and its multimeter.
            var lab = GameObject.Find("LabSet");
            var home = HomeCameraPosition();
            var monitor = lab == null ? null : lab.transform.Find("Monitor");
            if (monitor != null) yield return DrawInPlace(monitor, home, 0.15f, 1.02f, picture => cardPictures["act.code"] = picture);
            var meter = lab == null ? null : lab.transform.Find("Multimeter");
            if (meter != null) yield return DrawInPlace(meter, home, 1.4f, 1.1f, picture => cardPictures["act.repair"] = picture);
            foreach (var card in cardPictures) Keep("card-" + card.Key.Substring("act.".Length), card.Value);
            ShowCardPictures();
        }

        static Texture2D? arenaPicture;
        const int ArenaWidth = 480, ArenaHeight = 270;

        /// <summary>
        /// The obstacle field seen from above one corner with the chosen robot on its start pad: the arena card's
        /// picture (research R5: a picture of what the player will see, CS2-style map cards). Built from the arena's
        /// own code on a stage far from the lab, under a sky-blue background, once a session.
        /// </summary>
        IEnumerator RenderArenaPicture()
        {
            if (arenaPicture != null) yield break;
            var stage = new GameObject("ArenaStage");
            stage.transform.position = new Vector3(0, 0, -300f);
            Material Coloured(Color colour, float smoothness)
            {
                var material = new Material(litMaterial);
                material.SetColor("_BaseColor", colour);
                material.SetFloat("_Smoothness", smoothness);
                roomMaterials.Add(material);
                return material;
            }
            ArenaBuilder.ObstacleField(stage.transform, Coloured(new Color(0.92f, 0.92f, 0.88f), 0.4f),
                Coloured(new Color(0.55f, 0.6f, 0.68f), 0.2f), Coloured(new Color(0.85f, 0.55f, 0.25f), 0.2f));
            var pad = new GameObject("StartPad").transform;
            pad.SetParent(stage.transform, false);
            pad.localRotation = Quaternion.Euler(0, 20f, 0);
            var visual = RobotVisuals.Build(pad, null, Robot, litMaterial);
            var rest = Settle(Robot.Design, visual.Body);
            if (rest != null) PlaceAtRest(visual, rest, pad);

            var target = RenderTexture.GetTemporary(ArenaWidth * 2, ArenaHeight * 2, 24, RenderTextureFormat.ARGB32);
            var camera = PictureCamera(target, 34f, postProcessing: true);
            camera.backgroundColor = new Color(0.62f, 0.76f, 0.92f);
            // Close enough to see the robot on its pad, with the boxes round it and the far wall behind.
            var look = stage.transform.position + new Vector3(0.05f, 0.03f, 0.35f);
            camera.transform.position = look + Quaternion.Euler(27f, 205f, 0) * new Vector3(0, 0, -1.55f);
            camera.transform.LookAt(look);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 12f;
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = previous;
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    var drawn = Read(target);
                    var corner = drawn.GetPixel(2, drawn.height - 3); // the sky in the top corner: drawn, not the clear colour
                    if (corner.r + corner.g + corner.b > 0.6f)
                    {
                        arenaPicture = Shrink(drawn, ArenaWidth, ArenaHeight);
                        Keep("arena-obstacles", arenaPicture);
                        Destroy(drawn);
                        break;
                    }
                    Destroy(drawn);
                    yield return null;
                }
            }
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            Destroy(camera.gameObject);
            visual.Destroy();
            Destroy(stage);
            ShowArenaChip();
        }

        /// <summary>Where the showroom's camera stands at Home (world), so the lab's objects are seen as a visitor sees them.</summary>
        static Vector3 HomeCameraPosition() => DefaultTarget - Quaternion.Euler(14f, 215f, 0) * Vector3.forward * 0.62f;

        /// <summary>Puts the rendered pictures on the action cards, and the robot's thumbnail on Customize.</summary>
        void ShowCardPictures()
        {
            foreach (var entry in cardImages)
            {
                Texture2D? picture = entry.Key == "act.customize"
                    ? (GarageState.Selected < thumbnails.Count ? thumbnails[GarageState.Selected] : null)
                    : cardPictures.TryGetValue(entry.Key, out var card) ? card : null;
                if (picture == null) continue;
                var holder = entry.Value;
                var image = holder.Q<Image>();
                if (image == null)
                {
                    image = new Image { pickingMode = PickingMode.Ignore };
                    image.AddToClassList("tile-picture");
                    holder.Insert(0, image);
                }
                image.image = picture;
                // A staged picture is shown whole; a photograph of the lab or of the robot fills the card.
                bool staged = entry.Key == "act.build" || entry.Key == "act.wire" || entry.Key == "act.body";
                image.scaleMode = staged ? ScaleMode.ScaleToFit : ScaleMode.ScaleAndCrop;
                holder.EnableInClassList("tile-image--pictured", true);
            }
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>
        /// A design on a stage far from the lab, seen along <paramref name="turn"/>, drawn on black and on white,
        /// cropped round it with a little margin into a <paramref name="width"/> × <paramref name="height"/> picture
        /// with its own transparency. <paramref name="done"/> gets nothing when the render never happened.
        /// </summary>
        IEnumerator DrawDesign(RobotDesign design, Quaternion turn, int width, int height, Action<Texture2D> done)
        {
            var stage = new GameObject("PictureStage");
            stage.transform.position = new Vector3(-60f - (stagesUsed++ % 40) * 3f, 0, 0);
            var project = new RobotProject { Name = "Picture", Design = design };
            var visual = RobotVisuals.Build(stage.transform, null, project, litMaterial);
            var bounds = BoundsOf(stage);

            int renderWidth = width * Oversize, renderHeight = height * Oversize;
            var target = RenderTexture.GetTemporary(renderWidth, renderHeight, 24, RenderTextureFormat.ARGB32);
            var camera = PictureCamera(target, 22f, postProcessing: false);
            Frame(camera, bounds, turn, 0.92f);

            Texture2D? onBlack = null, onWhite = null;
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                for (int attempt = 0; attempt < 60 && (onBlack == null || onWhite == null); attempt++)
                {
                    onBlack ??= Draw(camera, request, target, Color.black);
                    onWhite ??= Draw(camera, request, target, Color.white);
                    if (onBlack == null || onWhite == null) yield return null;
                }
            }
            if (onBlack != null && onWhite != null) done(Combine(onBlack, onWhite, width, height));
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
        /// A photograph of something in the lab where it stands, from the side of <paramref name="viewer"/> and
        /// <paramref name="rise"/> higher (times the distance), framed to fill the card with a <paramref name="margin"/>.
        /// </summary>
        IEnumerator DrawInPlace(Transform subject, Vector3 viewer, float rise, float margin, Action<Texture2D> done)
        {
            var bounds = BoundsOf(subject.gameObject);
            var toward = viewer - bounds.center;
            toward.y = 0;
            var from = (toward.normalized + Vector3.up * rise).normalized;
            var turn = Quaternion.LookRotation(-from, Vector3.up);
            var target = RenderTexture.GetTemporary(CardWidth * 2, CardHeight * 2, 24, RenderTextureFormat.ARGB32);
            var camera = PictureCamera(target, 30f, postProcessing: true);
            camera.clearFlags = CameraClearFlags.Skybox;
            Frame(camera, bounds, turn, 0.98f / margin);
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = previous;
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    var drawn = Read(target);
                    var middle = drawn.GetPixel(drawn.width / 2, drawn.height / 2);
                    if (middle.a > 0.5f || middle.r + middle.g + middle.b > 0.03f) // the clear colour is gone: it was drawn
                    {
                        done(Shrink(drawn, CardWidth, CardHeight));
                        Destroy(drawn);
                        break;
                    }
                    Destroy(drawn);
                    yield return null;
                }
            }
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            Destroy(camera.gameObject);
        }

        static Bounds BoundsOf(GameObject root)
        {
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            bool any = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!any) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                any = true;
            }
            return bounds;
        }

        Camera PictureCamera(RenderTexture target, float fieldOfView, bool postProcessing)
        {
            var camera = new GameObject("PictureCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = target;
            camera.aspect = target.width / (float)target.height;
            camera.fieldOfView = fieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = postProcessing; // black and white stay black and white
            return camera;
        }

        /// <summary>Puts the camera on the far side of <paramref name="turn"/> just far enough that the box fits (<paramref name="fill"/> of the picture).</summary>
        static void Frame(Camera camera, Bounds bounds, Quaternion turn, float fill)
        {
            float tanV = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * fill, tanH = tanV * camera.aspect, distance = 0.02f;
            for (int i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var local = Quaternion.Inverse(turn) * (corner - bounds.center);
                distance = Mathf.Max(distance, Mathf.Abs(local.x) / tanH - local.z, Mathf.Abs(local.y) / tanV - local.z);
            }
            camera.transform.SetPositionAndRotation(bounds.center - turn * Vector3.forward * distance, turn);
            float reach = bounds.extents.magnitude;
            camera.nearClipPlane = Mathf.Max(0.005f, distance - reach * 1.5f);
            camera.farClipPlane = distance + reach * 2f;
        }

        /// <summary>
        /// The camera's picture on one background colour, or null when the render did not happen (URP drops render
        /// requests in the first frames after start-up; the corners then do not show the background).
        /// </summary>
        static Texture2D? Draw(Camera camera, RenderPipeline.StandardRequest request, RenderTexture target, Color background)
        {
            camera.backgroundColor = background;
            RenderPipeline.SubmitRenderRequest(camera, request);
            var image = Read(target);
            var corner = image.GetPixel(0, 0);
            if (Mathf.Abs(corner.r - background.r) < 0.02f && Mathf.Abs(corner.g - background.g) < 0.02f && Mathf.Abs(corner.b - background.b) < 0.02f) return image;
            Destroy(image);
            return null;
        }

        static Texture2D Read(RenderTexture target)
        {
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            return image;
        }

        /// <summary>
        /// The drawing with its transparency, cropped round it: a pixel that is black on black and white on white is
        /// all background, one that is the same on both is all drawing; between them, the difference is how much
        /// background shows. The drawing on black is its colour already multiplied by its cover, which is what a
        /// smooth scaling down needs. The crop has the picture's shape, a little margin all round.
        /// </summary>
        static Texture2D Combine(Texture2D onBlack, Texture2D onWhite, int width, int height)
        {
            var black = onBlack.GetPixels();
            var white = onWhite.GetPixels();
            int w = onBlack.width, h = onBlack.height;
            var alpha = new float[black.Length];
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    var b = black[i];
                    var c = white[i];
                    float a = Mathf.Clamp01(1 - ((c.r - b.r) + (c.g - b.g) + (c.b - b.b)) / 3);
                    alpha[i] = a;
                    if (a < 0.03f) continue;
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            if (maxX < 0) (minX, minY, maxX, maxY) = (0, 0, w - 1, h - 1);
            float aspect = width / (float)height;
            float cropWidth = Mathf.Max(maxX - minX + 1, (maxY - minY + 1) * aspect) * 1.08f, cropHeight = cropWidth / aspect;
            float x0 = (minX + maxX + 1) / 2f - cropWidth / 2, y0 = (minY + maxY + 1) / 2f - cropHeight / 2;
            return Sample(black, alpha, w, h, x0, y0, cropWidth / width, width, height);
        }

        /// <summary>An opaque picture scaled down to the given size (each pixel the mean of those it covers).</summary>
        static Texture2D Shrink(Texture2D drawn, int width, int height)
        {
            var pixels = drawn.GetPixels();
            var alpha = new float[pixels.Length];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = 1;
            return Sample(pixels, alpha, drawn.width, drawn.height, 0, 0, drawn.width / (float)width, width, height);
        }

        /// <summary>
        /// Each picture pixel averages the drawn pixels it covers (a box filter), for smooth edges; the colours come
        /// multiplied by their cover (<paramref name="alpha"/>) and leave it divided out again.
        /// </summary>
        static Texture2D Sample(Color[] colour, float[] alpha, int w, int h, float x0, float y0, float step, int width, int height)
        {
            var result = new Color[width * height];
            int samples = Mathf.Clamp(Mathf.CeilToInt(step), 1, 4);
            for (int v = 0; v < height; v++)
                for (int u = 0; u < width; u++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                        {
                            int x = Mathf.FloorToInt(x0 + (u + (sx + 0.5f) / samples) * step);
                            int y = Mathf.FloorToInt(y0 + (v + (sy + 0.5f) / samples) * step);
                            if (x < 0 || y < 0 || x >= w || y >= h) continue;
                            int i = y * w + x;
                            var c = colour[i]; // already multiplied by its cover
                            r += c.r;
                            g += c.g;
                            b += c.b;
                            a += alpha[i];
                        }
                    float count = samples * samples;
                    a /= count;
                    result[v * width + u] = a < 0.004f ? Color.clear
                        : new Color(Mathf.Clamp01(r / count / a), Mathf.Clamp01(g / count / a), Mathf.Clamp01(b / count / a), a);
                }
            var picture = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = "Picture", wrapMode = TextureWrapMode.Clamp };
            picture.SetPixels(result);
            picture.Apply(updateMipmaps: true);
            return picture;
        }
    }
}
