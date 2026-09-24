using System.Collections.Generic;
using CoreEngine.Sim.Design;
using UnityEngine;

namespace CoreEngine.Spike.Parts
{
    /// <summary>A part's finished look: one mesh with a material per submesh, and its lights, each on its own mesh.</summary>
    public sealed class PartModel
    {
        public Mesh Mesh = null!;
        public Material[] Materials = null!;
        public readonly List<LightLens> Lights = new List<LightLens>();

        /// <summary>An LED's lens: shown with <see cref="Off"/>, switched to <see cref="On"/> while the LED is lit.</summary>
        public sealed class LightLens
        {
            public string Name = "";
            public Mesh Mesh = null!;
            public Material Off = null!, On = null!;
        }
    }

    /// <summary>A part model being made: the mesh kit and the material behind each submesh.</summary>
    public sealed class Bench
    {
        public readonly MeshKit Kit = new MeshKit();
        readonly List<Material> materials = new List<Material>();
        public readonly PartModel Model = new PartModel();

        /// <summary>The slot (submesh) of a material, added on first use.</summary>
        public int this[Material material]
        {
            get
            {
                int i = materials.IndexOf(material);
                if (i >= 0) return i;
                materials.Add(material);
                return materials.Count - 1;
            }
        }

        /// <summary>An LED lens on its own mesh (mm, the part's frame), so its material can switch.</summary>
        public void Light(string name, Vector3 centre, Vector3 size, Color body, Color glow, bool dome = false)
        {
            var kit = new MeshKit(1);
            if (dome)
            {
                // A 5 mm LED: the rim at its base, the body and the rounded top.
                float r = size.x / 2;
                var profile = new List<Vector2> { new Vector2(0, 0), new Vector2(r + 0.4f, 0), new Vector2(r + 0.4f, 1.0f), new Vector2(r, 1.0f), new Vector2(r, size.y - r) };
                for (int i = 1; i <= 8; i++)
                {
                    float t = Mathf.PI / 2 * i / 8;
                    profile.Add(new Vector2(Mathf.Cos(t) * r, size.y - r + Mathf.Sin(t) * r));
                }
                kit.Lathe(0, centre - Vector3.up * size.y / 2, Vector3.up, profile, 24, 40);
            }
            else kit.Box(0, centre, size, Mathf.Min(size.x, size.z) * 0.12f);
            Model.Lights.Add(new PartModel.LightLens
            {
                Name = name,
                Mesh = kit.ToMesh(name),
                Off = PartLooks.Lens(name + " " + ColorUtility.ToHtmlStringRGB(glow), body, glow, false),
                On = PartLooks.Lens(name + " " + ColorUtility.ToHtmlStringRGB(glow), body, glow, true),
            });
        }

        public PartModel Finish(string name)
        {
            Kit.EnsureSlots(materials.Count);
            Model.Mesh = Kit.ToMesh(name);
            Model.Materials = materials.ToArray();
            return Model;
        }
    }

    /// <summary>
    /// The looks of the catalogue's parts (docs/09), built once per session from <see cref="Pieces"/> with painted
    /// boards: real sizes, the parts on them where the real boards have them, their pins where the catalogue
    /// puts them, so a jumper lands on the pin the player sees.
    /// </summary>
    public static class PartModels
    {
        static readonly Dictionary<string, PartModel> models = new Dictionary<string, PartModel>();

        public static PartModel? Get(string partId)
        {
            if (models.TryGetValue(partId, out var model)) return model;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            model = partId switch
            {
                PartCatalog.Uno => UnoModel.Make(),
                PartCatalog.L298N => L298NModel.Make(),
                PartCatalog.HcSr04 => SonarModel.Make(),
                PartCatalog.TtMotor => MotorModel.Make(),
                PartCatalog.Battery4AA => BatteryModel.Make(),
                PartCatalog.Caster => CasterModel.Make(),
                PartCatalog.Servo => ServoModel.Make(),
                PartCatalog.Led => LedModel.Make(),
                _ => null,
            };
            if (model != null)
            {
                models[partId] = model;
                Debug.Log($"PartModels: {partId} made in {watch.Elapsed.TotalMilliseconds:F0} ms ({model.Mesh.vertexCount} vertices, {model.Materials.Length} materials)");
            }
            return model;
        }

        /// <summary>
        /// Puts a part's look under <paramref name="parent"/> (the part's frame): its mesh and a renderer for each
        /// light, returned by name in <paramref name="lights"/>. False when the part has no model yet.
        /// </summary>
        public static bool Build(string partId, Transform parent, Dictionary<string, MeshRenderer>? lights = null)
        {
            var model = Get(partId);
            if (model == null) return false;
            var go = new GameObject("Model");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = model.Materials;
            foreach (var light in model.Lights)
            {
                var lens = new GameObject("Light " + light.Name);
                lens.transform.SetParent(parent, false);
                lens.AddComponent<MeshFilter>().sharedMesh = light.Mesh;
                var renderer = lens.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = light.Off;
                if (lights != null) lights[light.Name] = renderer;
            }
            return true;
        }

        /// <summary>Switches a light built by <see cref="Build"/> on or off.</summary>
        public static void SetLight(string partId, MeshRenderer renderer, string name, bool on)
        {
            var model = Get(partId);
            if (model == null) return;
            foreach (var light in model.Lights)
                if (light.Name == name) renderer.sharedMaterial = on ? light.On : light.Off;
        }
    }

    /// <summary>
    /// Pieces that boards share, in millimetres in the bench's current frame with y up from the board's top face:
    /// headers, chips, small SMD parts, capacitors, crystals, buttons and screw terminals, sized like the real ones.
    /// </summary>
    public static class Pieces
    {
        public const float Pitch = 2.54f;

        /// <summary>The top of a female header or a chip socket: black plastic, one funnel-mouthed hole per pitch (a repeating texture).</summary>
        public static Material SocketTop => PartLooks.Textured("socket top", () =>
        {
            var r = new Raster(Pitch, Pitch, 64);
            r.Fill(Ink.Solid(new Color32(10, 10, 11, 255), 0, 0.46f, 0));
            float c = Pitch / 2;
            for (int i = 0; i <= 6; i++)
            {
                float half = Mathf.Lerp(0.78f, 0.48f, i / 6f);
                byte shade = (byte)Mathf.Lerp(12, 3, i / 6f);
                r.RoundRect(c, c, half * 2, half * 2, 0.08f, Ink.Solid(new Color32(shade, shade, shade, 255), 0, Mathf.Lerp(0.4f, 0.1f, i / 6f), -0.08f * i));
            }
            return r;
        }, 1.2f, repeat: true);

        // ------------------------------------------------------------------ headers

        /// <summary>A female header along x: <paramref name="count"/> places from the first pin's x, 8.5 mm tall.</summary>
        public static void FemaleHeader(Bench b, float firstX, float z, int count, float height = 8.5f)
        {
            float length = count * Pitch;
            b.Kit.Box(b[PartLooks.BlackPlastic], new Vector3(firstX - Pitch / 2 + length / 2, height / 2, z), new Vector3(length - 0.08f, height, 2.5f),
                0.2f, topUv: new Rect(0, 0, count, 1), topSlot: b[SocketTop]);
        }

        /// <summary>
        /// A male header: a black base <paramref name="baseHeight"/> tall with square gold pins standing
        /// <paramref name="above"/> over it, a row along <paramref name="along"/> (x or z) from the first pin.
        /// </summary>
        public static void MaleHeader(Bench b, Vector3 firstPin, Vector3 along, int count, int rows = 1, Vector3 rowStep = default,
            float above = 6f, float baseHeight = 2.5f)
        {
            along = along.normalized;
            var across = rows > 1 ? rowStep : Vector3.zero;
            var centre = firstPin + along * (count - 1) * Pitch / 2 + across * (rows - 1) / 2;
            float alongLength = count * Pitch, acrossLength = rows * Pitch;
            var size = Mathf.Abs(along.x) > 0.5f ? new Vector3(alongLength, baseHeight, acrossLength) : new Vector3(acrossLength, baseHeight, alongLength);
            b.Kit.Box(b[PartLooks.BlackPlastic], new Vector3(centre.x, firstPin.y + baseHeight / 2, centre.z), size - new Vector3(0.1f, 0, 0.1f), 0.15f);
            for (int row = 0; row < rows; row++)
                for (int i = 0; i < count; i++)
                    Pin(b, firstPin + along * i * Pitch + across * row, baseHeight + above);
        }

        /// <summary>A square 0.64 mm pin standing <paramref name="height"/> from its foot, its tip chamfered.</summary>
        public static void Pin(Bench b, Vector3 foot, float height, float side = 0.64f)
        {
            int gold = b[PartLooks.Gold];
            float tip = 0.35f;
            b.Kit.Box(gold, foot + Vector3.up * (height - tip) / 2, new Vector3(side, height - tip, side));
            var top = foot + Vector3.up * height;
            float h = side / 2, t = side * 0.18f;
            var c = new[] { new Vector3(-h, 0, -h), new Vector3(h, 0, -h), new Vector3(h, 0, h), new Vector3(-h, 0, h) };
            var p = new[] { new Vector3(-t, 0, -t), new Vector3(t, 0, -t), new Vector3(t, 0, t), new Vector3(-t, 0, t) };
            var baseY = top - Vector3.up * tip;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                var normal = (c[i] + c[j]).normalized + Vector3.up * 0.6f;
                b.Kit.Quad(gold, baseY + c[i], baseY + c[j], top + p[j], top + p[i], normal.normalized);
            }
            b.Kit.Quad(gold, top + p[0], top + p[1], top + p[2], top + p[3], Vector3.up);
        }

        // ------------------------------------------------------------------ chips

        /// <summary>
        /// A DIP chip (<paramref name="perSide"/> legs a side, rows <paramref name="rowSpacing"/> apart) along x, in a
        /// socket when <paramref name="socket"/>; its top shows <paramref name="markUv"/> of <paramref name="mark"/>.
        /// </summary>
        public static void Dip(Bench b, Vector3 centre, int perSide, float rowSpacing, Material mark, Rect markUv, bool socket)
        {
            float length = (perSide - 1) * Pitch + 2.5f; // 35.5 mm for a DIP-28
            float seat = 0;
            if (socket)
            {
                const float socketHeight = 2.9f;
                float socketLength = perSide * Pitch + 1.6f;
                int plastic = b[PartLooks.BlackPlastic];
                // The socket tile repeats every pitch with its hole in the middle: start it so the holes meet the legs.
                float tiles = socketLength / Pitch, firstHole = 0.5f - (socketLength - (perSide - 1) * Pitch) / 2 / Pitch;
                foreach (int side in new[] { -1, 1 })
                {
                    // Each row of contacts is a strip of sockets; the ends and a middle bar join them.
                    b.Kit.Box(plastic, centre + new Vector3(0, socketHeight / 2, side * rowSpacing / 2), new Vector3(socketLength, socketHeight, 2.3f), 0.15f,
                        topUv: new Rect(firstHole, 0, tiles, 1), topSlot: b[SocketTop]);
                    b.Kit.Box(plastic, centre + new Vector3(side * (socketLength / 2 - 0.7f), socketHeight * 0.4f, 0), new Vector3(1.4f, socketHeight * 0.8f, rowSpacing), 0.15f);
                }
                b.Kit.Box(plastic, centre + new Vector3(0, 0.5f, 0), new Vector3(socketLength - 2, 1f, 1.2f), 0.1f);
                seat = socketHeight + 0.35f;
            }
            const float bodyHeight = 3.3f;
            var body = centre + new Vector3(0, seat + 0.5f + bodyHeight / 2, 0);
            b.Kit.Box(b[PartLooks.MatteBlack], body, new Vector3(length, bodyHeight, rowSpacing - 1.0f), 0.35f, topUv: markUv, topSlot: b[mark]);
            int tin = b[PartLooks.Tin];
            for (int i = 0; i < perSide; i++)
            {
                float x = centre.x - (perSide - 1) * Pitch / 2 + i * Pitch;
                foreach (int side in new[] { -1, 1 })
                {
                    float z = centre.z + side * rowSpacing / 2;
                    float shoulderZ = centre.z + side * (rowSpacing / 2 - 0.55f);
                    b.Kit.Box(tin, new Vector3(x, body.y - 0.2f, shoulderZ), new Vector3(0.55f, 0.25f, 1.3f));
                    b.Kit.Box(tin, new Vector3(x, (body.y - 0.2f + centre.y + seat - 0.4f) / 2, z), new Vector3(0.5f, body.y - 0.2f - (centre.y + seat - 0.4f), 0.25f));
                }
            }
        }

        /// <summary>A small-outline chip (SOIC, SOT-223 and the like) with gull-wing legs on two sides along x.</summary>
        public static void GullWing(Bench b, Vector3 centre, Vector3 bodySize, int perSide, float legPitch, Material mark, Rect markUv, float legLength = 1.0f)
        {
            float lift = 0.15f;
            b.Kit.Box(b[PartLooks.MatteBlack], centre + Vector3.up * (lift + bodySize.y / 2), bodySize, 0.12f, topUv: markUv, topSlot: b[mark]);
            int tin = b[PartLooks.Tin];
            for (int i = 0; i < perSide; i++)
            {
                float x = centre.x - (perSide - 1) * legPitch / 2 + i * legPitch;
                foreach (int side in new[] { -1, 1 })
                {
                    float edge = centre.z + side * bodySize.z / 2;
                    b.Kit.Box(tin, new Vector3(x, lift + bodySize.y * 0.45f, edge + side * 0.2f), new Vector3(0.42f, 0.18f, 0.45f));
                    b.Kit.Box(tin, new Vector3(x, lift + bodySize.y * 0.22f, edge + side * 0.42f), new Vector3(0.42f, bodySize.y * 0.45f, 0.18f));
                    b.Kit.Box(tin, new Vector3(x, 0.09f, edge + side * (0.42f + legLength / 2)), new Vector3(0.42f, 0.18f, legLength));
                }
            }
        }

        /// <summary>A small SMD part along x: a resistor (black top), a capacitor (tan) or the like, with tinned ends.</summary>
        public static void Smd(Bench b, Vector3 centre, float length, float width, float height, Material body, bool alongZ = false)
        {
            var size = alongZ ? new Vector3(width, height, length) : new Vector3(length, height, width);
            b.Kit.Box(b[body], centre + Vector3.up * height / 2, size * 0.999f, 0.05f);
            float cap = length * 0.2f;
            foreach (int side in new[] { -1, 1 })
            {
                var offset = (alongZ ? Vector3.forward : Vector3.right) * side * (length / 2 - cap / 2);
                var capSize = alongZ ? new Vector3(width * 1.02f, height * 1.03f, cap) : new Vector3(cap, height * 1.03f, width * 1.02f);
                b.Kit.Box(b[PartLooks.Tin], centre + offset + Vector3.up * height / 2, capSize, 0.05f);
            }
        }

        public static Material ResistorBody => PartLooks.Solid("smd resistor", new Color(0.05f, 0.05f, 0.055f), 0.35f);
        public static Material CapacitorBody => PartLooks.Solid("smd capacitor", new Color(0.55f, 0.45f, 0.33f), 0.4f);

        // ------------------------------------------------------------------ passives

        /// <summary>
        /// An aluminium electrolytic capacitor standing on the board: a printed sleeve with the stripe on its minus
        /// side toward <paramref name="minusAngle"/>, the scored aluminium top and the rolled groove near it.
        /// </summary>
        public static void Electrolytic(Bench b, Vector3 foot, float diameter, float height, Material sleeve, float minusAngle = 0)
        {
            float r = diameter / 2;
            var sides = new List<Vector2>
            {
                new Vector2(r * 0.92f, 0.4f), new Vector2(r, 0.9f), new Vector2(r, height - 1.6f),
                new Vector2(r * 0.93f, height - 1.25f), new Vector2(r, height - 0.9f), new Vector2(r, height - 0.25f), new Vector2(r * 0.9f, height),
            };
            b.Kit.Lathe(b[sleeve], foot, Vector3.up, sides, 40, 40, minusAngle, -360); // turning backwards: the print reads from outside
            Disc(b, b[CapTop], foot + Vector3.up * height, Vector3.up, r * 0.9f, 0.45f / (r * 0.9f), 40, new Vector2(0.5f, 0.5f));
            b.Kit.Cylinder(b[PartLooks.Rubber], foot, foot + Vector3.up * 0.45f, r * 0.9f, 32);
        }

        /// <summary>An electrolytic's aluminium top with its scored vent, a cross; mapped from its middle (0.5, 0.5) to its rim.</summary>
        public static Material CapTop => PartLooks.Textured("cap top", () =>
        {
            var r = new Raster(10f, 10f, 40);
            r.Fill(Ink.Solid(new Color32(168, 170, 172, 255), 1, 0.42f, 0));
            var score = Ink.Solid(new Color32(92, 94, 96, 255), 1, 0.3f, -0.12f);
            r.Line(1.5f, 5, 8.5f, 5, 0.35f, score);
            r.Line(5, 1.5f, 5, 8.5f, 0.35f, score);
            r.Grain(0.05f, 0.08f, 0.4f, 9);
            return r;
        }, 1.2f);

        /// <summary>The sleeve of an electrolytic capacitor: its colour, the lighter minus stripe and the printed value.</summary>
        public static Material CapSleeve(string key, Color32 colour, Color32 print, string value) => PartLooks.Textured("sleeve " + key, () =>
        {
            // u goes once round (31.4 mm for a 10 mm can), v up the side at 0.1 per mm (the lathe's uv).
            var r = new Raster(31.4f, 10f, 24);
            r.Fill(Ink.Solid(colour, 0, 0.62f, 0));
            r.Rect(0, 0, 5.5f, 10, Ink.Paint(print, 0, 0.5f));
            for (float y = 1.2f; y < 10; y += 2.2f) r.Line(1.6f, y, 3.9f, y, 0.5f, Ink.Paint(colour, 0, 0.5f));
            int space = value.IndexOf(' ');
            r.Text(space > 0 ? value.Substring(0, space) : value, 18, 6.2f, 1.4f, Ink.Paint(print, 0, 0.45f), Align.Centre);
            if (space > 0) r.Text(value.Substring(space + 1), 18, 4.0f, 1.4f, Ink.Paint(print, 0, 0.45f), Align.Centre);
            r.Grain(0.03f, 0.05f, 0.7f, 5);
            return r;
        });

        /// <summary>A quartz crystal in an HC-49/S can along x, standing on its flange; its top shows <paramref name="markUv"/>.</summary>
        public static void Crystal(Bench b, Vector3 foot, Material mark, Rect markUv)
        {
            b.Kit.Box(b[PartLooks.Nickel], foot + new Vector3(0, 0.25f, 0), new Vector3(11.6f, 0.5f, 4.9f), 0.2f);
            b.Kit.Box(b[PartLooks.Nickel], foot + new Vector3(0, 0.5f + 1.75f, 0), new Vector3(11.0f, 3.5f, 4.4f), 1.6f, topUv: markUv, topSlot: b[mark]);
        }

        /// <summary>A 6 × 6 mm tactile switch: black body, steel cover with its four legs, the round black button.</summary>
        public static void TactileSwitch(Bench b, Vector3 foot)
        {
            b.Kit.Box(b[PartLooks.BlackPlastic], foot + new Vector3(0, 1.4f, 0), new Vector3(6, 2.8f, 6), 0.3f);
            b.Kit.Box(b[PartLooks.Nickel], foot + new Vector3(0, 2.9f, 0), new Vector3(6.1f, 0.25f, 6.1f), 0.1f);
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    b.Kit.Box(b[PartLooks.Nickel], foot + new Vector3(sx * 3.1f, 1.5f, sz * 2.2f), new Vector3(0.25f, 3.0f, 0.8f));
            b.Kit.Cylinder(b[PartLooks.MatteBlack], foot + Vector3.up * 3.0f, foot + Vector3.up * 4.3f, 1.75f, 28, 0.35f);
        }

        // ------------------------------------------------------------------ terminals

        /// <summary>
        /// A screw terminal block of <paramref name="ways"/> ways, <paramref name="pitch"/> apart along
        /// <paramref name="along"/>, its wire openings facing <paramref name="facing"/>: moulded body, a screw on top of
        /// each way and the dark clamp opening in front.
        /// </summary>
        public static void ScrewTerminal(Bench b, Vector3 foot, Vector3 along, Vector3 facing, int ways, float pitch, Material body)
        {
            along = along.normalized;
            facing = facing.normalized;
            float length = ways * pitch, depth = 7.6f, height = 10f;
            var rotation = Quaternion.LookRotation(facing, Vector3.up);
            b.Kit.Push(foot, rotation);
            // In this frame: +z is where the wires go in, x runs along the block.
            float sign = Vector3.Dot(rotation * Vector3.right, along) >= 0 ? 1 : -1;
            b.Kit.Box(b[body], new Vector3(0, height * 0.42f, -0.6f), new Vector3(length - 0.1f, height * 0.84f, depth - 1.2f), 0.4f);
            b.Kit.Box(b[body], new Vector3(0, height * 0.35f, depth / 2 - 1.0f), new Vector3(length - 0.1f, height * 0.7f, 2.0f), 0.3f);
            for (int i = 0; i < ways; i++)
            {
                float x = sign * (-length / 2 + pitch / 2 + i * pitch);
                // The screw in its well: the well's dark rim round a zinc-plated head with its slot across the block.
                float top = height * 0.84f;
                b.Kit.Lathe(b[PartLooks.Hole], new Vector3(x, top + 0.005f, -0.9f), Vector3.up, new List<Vector2> { new Vector2(2.1f, 0), new Vector2(1.7f, 0) }, 28, 10);
                b.Kit.Cylinder(b[PartLooks.Nickel], new Vector3(x, top - 0.6f, -0.9f), new Vector3(x, top - 0.02f, -0.9f), 1.7f, 28, 0.3f);
                b.Kit.Box(b[PartLooks.Hole], new Vector3(x, top - 0.01f, -0.9f), new Vector3(0.5f, 0.06f, 3.0f));
                // The clamp's opening: a dark square with the tinned clamp inside.
                b.Kit.Box(b[PartLooks.Hole], new Vector3(x, height * 0.35f, depth / 2 - 0.01f), new Vector3(3.0f, 3.0f, 0.1f));
                b.Kit.Box(b[PartLooks.Tin], new Vector3(x, height * 0.35f - 1.1f, depth / 2 - 0.4f), new Vector3(2.6f, 0.5f, 0.8f));
                if (i > 0)
                    b.Kit.Box(b[body], new Vector3(sign * (-length / 2 + i * pitch), height * 0.84f + 0.35f, -0.9f), new Vector3(0.6f, 0.7f, depth - 2.2f), 0.2f);
            }
            b.Kit.Pop();
        }

        /// <summary>A flat disc facing <paramref name="normal"/>, its texture laid flat at <paramref name="uvPerMm"/> (a mesh, a printed top).</summary>
        public static void Disc(Bench b, int slot, Vector3 centre, Vector3 normal, float radius, float uvPerMm, int segments = 32, Vector2 uvCentre = default)
        {
            normal.Normalize();
            var u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(normal, u);
            int middle = b.Kit.Vertex(centre, normal, uvCentre);
            int rim = b.Kit.VertexCount;
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * 2 * i / segments;
                var offset = (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
                b.Kit.Vertex(centre + offset, normal, uvCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * uvPerMm);
            }
            for (int i = 0; i < segments; i++) b.Kit.Triangle(slot, middle, rim + i, rim + i + 1);
        }

        // ------------------------------------------------------------------ boards

        /// <summary>
        /// A circuit board: <paramref name="outline"/> (x, z in mm, the bench's frame) from <paramref name="bottom"/> to
        /// <paramref name="top"/>, its top painted by <paramref name="topMaterial"/> through <paramref name="uv"/>.
        /// </summary>
        public static void Board(Bench b, IList<Vector2> outline, float bottom, float top, Material topMaterial, System.Func<Vector2, Vector2> uv, Material bottomMaterial)
        {
            b.Kit.Extrude(outline, bottom, top, b[topMaterial], b[PartLooks.Fr4Edge], b[bottomMaterial], uv, 20);
        }
    }
}
