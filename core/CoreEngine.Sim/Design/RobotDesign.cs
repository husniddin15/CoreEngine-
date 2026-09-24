using System;
using System.Collections.Generic;

namespace CoreEngine.Sim.Design
{
    public enum BodyShape { Rectangle, Rounded, Round }

    public enum BodyMaterial { Acrylic, Pla, Plywood }

    /// <summary>
    /// The chassis made in the Body Studio (docs/08), first version: a plate outline with optional M3 hole
    /// grid and side walls, one or two decks. Millimetres. The mesh is derived (Manifold, ADR-0005).
    /// Fields are public so Unity's JsonUtility can save them.
    /// </summary>
    [Serializable]
    public sealed class BodyDesign
    {
        public BodyShape Shape = BodyShape.Rectangle;
        public float LengthMm = 160;
        public float WidthMm = 120;
        public float ThicknessMm = 3;
        public float CornerRadiusMm = 12;
        public int Decks = 2;
        public bool HoleGrid;
        public float HolePitchMm = 15;
        public float HoleDiameterMm = 3.2f; // M3 clearance
        public float WallHeightMm;
        public BodyMaterial Material = BodyMaterial.Acrylic;

        /// <summary>Shapes added in the Body Studio: solids joined to the plates and holes cut through everything.</summary>
        public List<BodyFeature> Features = new List<BodyFeature>();

        /// <summary>Round bodies use the width as their diameter.</summary>
        public float EffectiveLength => Shape == BodyShape.Round ? WidthMm : LengthMm;

        public static float DensityGPerCm3(BodyMaterial material) => material switch
        {
            BodyMaterial.Pla => 1.24f,
            BodyMaterial.Plywood => 0.68f,
            _ => 1.18f,
        };

        public BodyDesign Clone()
        {
            var copy = (BodyDesign)MemberwiseClone();
            copy.Features = new List<BodyFeature>();
            foreach (var feature in Features) copy.Features.Add(feature.Clone());
            return copy;
        }

        public BodyFeature? Feature(string id)
        {
            foreach (var feature in Features) if (feature.Id == id) return feature;
            return null;
        }

        /// <summary>Adds a feature with a new id ("f1", "f2", …) and returns it.</summary>
        public BodyFeature AddFeature(BodyFeature feature)
        {
            for (int n = 1; ; n++)
            {
                if (Feature("f" + n) != null) continue;
                feature.Id = "f" + n;
                break;
            }
            Features.Add(feature);
            return feature;
        }
    }

    /// <summary>One part on the robot: a catalogue id, and its place (a slot for fixed mounts, x/z/rotation on the deck).</summary>
    [Serializable]
    public sealed class PartInstance
    {
        public string Id = "";
        public string Part = "";
        public string Slot = "";   // "left"/"right" for motors
        public float X;            // mm on the deck, chassis centre at 0, +x right, +z forward
        public float Z;
        public int Rotation;       // degrees about +y, as Unity's Quaternion.Euler(0, rotation, 0)
    }

    /// <summary>A jumper wire (or lead) from one pin to another (docs/03 §6).</summary>
    [Serializable]
    public sealed class WireInstance
    {
        public string FromPart = "";
        public string FromPin = "";
        public string ToPart = "";
        public string ToPin = "";
        public string Color = "yellow";
    }

    /// <summary>
    /// What the player builds: the body, the parts and the wires. The Garage edits it, the arena builds
    /// the robot and its circuit from it, and it is saved with the project (docs/04 §6).
    /// </summary>
    [Serializable]
    public sealed class RobotDesign
    {
        public BodyDesign Body = new BodyDesign();
        public List<PartInstance> Parts = new List<PartInstance>();
        public List<WireInstance> Wires = new List<WireInstance>();

        public PartInstance? Find(string id)
        {
            foreach (var part in Parts) if (part.Id == id) return part;
            return null;
        }

        public int Count(string partId)
        {
            int n = 0;
            foreach (var part in Parts) if (part.Part == partId) n++;
            return n;
        }

        /// <summary>Adds a part at its default place. Returns null when the catalogue allows no more of it.</summary>
        public PartInstance? AddPart(string partId)
        {
            var def = PartCatalog.Get(partId);
            if (def == null || Count(partId) >= def.MaxCount) return null;
            var instance = new PartInstance { Id = NewId(def), Part = partId };
            if (def.Mount == MountKind.Motor) instance.Slot = HasSlot(partId, "left") ? "right" : "left";
            if (def.Mount == MountKind.Deck)
            {
                // First free spot on a 20 mm grid from the rear, so a new part never lands on another.
                var spot = FreeDeckSpot(def);
                instance.X = spot.x;
                instance.Z = spot.z;
            }
            Parts.Add(instance);
            return instance;
        }

        /// <summary>Removes a part and every wire attached to it.</summary>
        public void RemovePart(string id)
        {
            Parts.RemoveAll(p => p.Id == id);
            Wires.RemoveAll(w => w.FromPart == id || w.ToPart == id);
        }

        /// <summary>Adds a wire unless it would join a pin to itself or duplicate an existing wire.</summary>
        public WireInstance? AddWire(string fromPart, string fromPin, string toPart, string toPin, string color)
        {
            if (fromPart == toPart && fromPin == toPin) return null;
            foreach (var w in Wires)
                if ((w.FromPart == fromPart && w.FromPin == fromPin && w.ToPart == toPart && w.ToPin == toPin) ||
                    (w.FromPart == toPart && w.FromPin == toPin && w.ToPart == fromPart && w.ToPin == fromPin))
                    return null;
            var wire = new WireInstance { FromPart = fromPart, FromPin = fromPin, ToPart = toPart, ToPin = toPin, Color = color };
            Wires.Add(wire);
            return wire;
        }

        /// <summary>Wires that end on a pin.</summary>
        public int WiresOn(string partId, string pinId)
        {
            int n = 0;
            foreach (var w in Wires)
            {
                if (w.FromPart == partId && w.FromPin == pinId) n++;
                if (w.ToPart == partId && w.ToPin == pinId) n++;
            }
            return n;
        }

        /// <summary>True when the pin can take one more wire (see <see cref="PinDef.Capacity"/>).</summary>
        public bool HasRoomOn(string partId, string pinId)
        {
            var part = Find(partId);
            var pin = part == null ? null : PartCatalog.Get(part.Part)?.Pin(pinId);
            return pin != null && WiresOn(partId, pinId) < pin.Capacity;
        }

        public bool HasSlot(string partId, string slot)
        {
            foreach (var part in Parts) if (part.Part == partId && part.Slot == slot) return true;
            return false;
        }

        public double MassKg()
        {
            double grams = DesignGeometry.BodyMassG(Body);
            foreach (var part in Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                grams += def.MassG;
                if (def.Kind == PartKind.Motor) grams += DesignGeometry.WheelMassG;
                if (def.Kind == PartKind.Battery) grams += DesignGeometry.CellsMassG;
            }
            return grams / 1000.0;
        }

        public RobotDesign Clone()
        {
            var copy = new RobotDesign { Body = Body.Clone() };
            foreach (var p in Parts) copy.Parts.Add(new PartInstance { Id = p.Id, Part = p.Part, Slot = p.Slot, X = p.X, Z = p.Z, Rotation = p.Rotation });
            foreach (var w in Wires) copy.Wires.Add(new WireInstance { FromPart = w.FromPart, FromPin = w.FromPin, ToPart = w.ToPart, ToPin = w.ToPin, Color = w.Color });
            return copy;
        }

        string NewId(PartDef def)
        {
            string stem = def.Kind switch
            {
                PartKind.Board => "uno",
                PartKind.MotorDriver => "driver",
                PartKind.Ultrasonic => "sonar",
                PartKind.Motor => "motor",
                PartKind.Battery => "battery",
                _ => "caster",
            };
            for (int n = 1; ; n++)
                if (Find(stem + n) == null) return stem + n;
        }

        (float x, float z) FreeDeckSpot(PartDef def)
        {
            float halfL = Body.EffectiveLength / 2, halfW = Body.WidthMm / 2;
            for (float z = -halfL + def.SizeZ / 2 + 5; z <= halfL - def.SizeZ / 2; z += 20)
                for (float x = -halfW + def.SizeX / 2 + 5; x <= halfW - def.SizeX / 2; x += 20)
                    if (!DesignGeometry.OverlapsDeckPart(this, def, x, z, 0, null)) return (x, z);
            return (0, 0);
        }
    }

    /// <summary>
    /// Where things are, in millimetres in the chassis frame: origin at the chassis centre 50 mm above the
    /// floor (the physics root), y up, +z forward. Shared by the Garage, the arena and the wire lengths.
    /// </summary>
    public static class DesignGeometry
    {
        public const float WheelRadius = 32.5f;
        public const float WheelMassG = 30;         // 65 mm TT wheel with its tyre
        public const float CellsMassG = 4 * 23;     // four alkaline AA cells in the holder
        public const float BottomPlateTop = -12;   // the bottom plate sits on the motors
        public const float TopDeckBottom = 12;     // 24 mm standoffs between the decks

        public static float DeckTop(BodyDesign body) =>
            body.Decks >= 2 ? TopDeckBottom + body.ThicknessMm : BottomPlateTop;

        public static float BottomPlateBottom(BodyDesign body) => BottomPlateTop - body.ThicknessMm;

        /// <summary>The motors' axle line, 50 mm in front of the rear edge.</summary>
        public static float AxleZ(BodyDesign body) => -body.EffectiveLength / 2 + 50;

        /// <summary>Half the body's width at a given z; a round body is narrower away from its middle.</summary>
        public static float SideHalfWidth(BodyDesign body, float z)
        {
            float half = body.WidthMm / 2;
            return body.Shape == BodyShape.Round ? (float)Math.Sqrt(Math.Max(0, half * half - z * z)) : half;
        }

        /// <summary>Wheel centre of a motor slot: the 26 mm wide wheel sits on the shaft just outside the plate edge.</summary>
        public static (float x, float y, float z) WheelCentre(BodyDesign body, string slot)
        {
            float side = slot == "right" ? 1 : -1, z = AxleZ(body);
            return (side * (SideHalfWidth(body, z) + 17.5f), WheelRadius - 50, z);
        }

        /// <summary>A TT motor hangs under the bottom plate, 10 mm inside its edge, with the shaft pointing out.</summary>
        public static (float x, float y, float z) MotorCentre(BodyDesign body, string slot)
        {
            float side = slot == "right" ? 1 : -1, z = AxleZ(body);
            return (side * (SideHalfWidth(body, z) - 10), -26, z);
        }

        public static (float x, float y, float z) CasterCentre(BodyDesign body) => (0, -40, body.EffectiveLength / 2 - 15);

        public static (float x, float y, float z) SonarCentre(BodyDesign body) =>
            (0, DeckTop(body) + 15, body.EffectiveLength / 2 + 5);

        public static (float x, float y, float z) LowerCentre(BodyDesign body) =>
            body.Decks >= 2 ? (0, BottomPlateTop + 7.5f, -5) : (0, BottomPlateTop + 7.5f, -body.EffectiveLength / 2 + 40);

        /// <summary>The part's origin and rotation in the chassis frame.</summary>
        public static (float x, float y, float z, int rotation) Place(RobotDesign design, PartInstance part)
        {
            var def = PartCatalog.Get(part.Part);
            var body = design.Body;
            switch (def?.Mount)
            {
                case MountKind.Motor:
                    var m = MotorCentre(body, part.Slot);
                    return (m.x, m.y, m.z, 0);
                case MountKind.Front:
                    var s = SonarCentre(body);
                    return (s.x, s.y, s.z, 0);
                case MountKind.Caster:
                    var c = CasterCentre(body);
                    return (c.x, c.y, c.z, 0);
                case MountKind.Lower:
                    var l = LowerCentre(body);
                    return (l.x, l.y, l.z, 0);
                default:
                    return (part.X, DeckTop(body), part.Z, part.Rotation);
            }
        }

        /// <summary>A pin's position in the chassis frame.</summary>
        public static (float x, float y, float z)? PinPosition(RobotDesign design, string partId, string pinId)
        {
            var part = design.Find(partId);
            var def = part == null ? null : PartCatalog.Get(part.Part);
            var pin = def?.Pin(pinId);
            if (part == null || def == null || pin == null) return null;
            var place = Place(design, part);
            var (x, z) = Rotate(pin.X, pin.Z, place.rotation);
            return (place.x + x, place.y + pin.Y, place.z + z);
        }

        /// <summary>The direction a wire leaves a pin, in the chassis frame (unit length).</summary>
        public static (float x, float y, float z)? PinExit(RobotDesign design, string partId, string pinId)
        {
            var part = design.Find(partId);
            var pin = part == null ? null : PartCatalog.Get(part.Part)?.Pin(pinId);
            if (part == null || pin == null) return null;
            var (x, z) = Rotate(pin.ExitX, pin.ExitZ, Place(design, part).rotation);
            return (x, pin.ExitY, z);
        }

        /// <summary>Unity's rotation about +y: (x, z) → (x cos θ + z sin θ, −x sin θ + z cos θ).</summary>
        public static (float x, float z) Rotate(float x, float z, int degrees)
        {
            double r = degrees * Math.PI / 180;
            double c = Math.Cos(r), s = Math.Sin(r);
            return ((float)(x * c + z * s), (float)(-x * s + z * c));
        }

        /// <summary>Footprint half sizes on the deck after rotating by a multiple of 90°.</summary>
        public static (float halfX, float halfZ) Footprint(PartDef def, int rotation) =>
            (rotation / 90) % 2 == 0 ? (def.SizeX / 2, def.SizeZ / 2) : (def.SizeZ / 2, def.SizeX / 2);

        public static bool OverlapsDeckPart(RobotDesign design, PartDef def, float x, float z, int rotation, string? ignoreId)
        {
            var (hx, hz) = Footprint(def, rotation);
            foreach (var other in design.Parts)
            {
                if (other.Id == ignoreId) continue;
                var otherDef = PartCatalog.Get(other.Part);
                if (otherDef == null || !TakesDeckRoom(design.Body, otherDef)) continue;
                var place = Place(design, other);
                var (ox, oz) = Footprint(otherDef, place.rotation);
                if (Math.Abs(x - place.x) < hx + ox && Math.Abs(z - place.z) < hz + oz) return true;
            }
            return false;
        }

        /// <summary>Deck parts, and the battery holder when there is only one deck for it to stand on.</summary>
        public static bool TakesDeckRoom(BodyDesign body, PartDef def) =>
            def.Mount == MountKind.Deck || (def.Mount == MountKind.Lower && body.Decks < 2);

        /// <summary>Keeps a deck part's footprint on the deck (inside the disc for a round body).</summary>
        public static (float x, float z) ClampToDeck(BodyDesign body, PartDef def, float x, float z, int rotation)
        {
            var (hx, hz) = Footprint(def, rotation);
            float maxX = Math.Max(0, body.WidthMm / 2 - hx), maxZ = Math.Max(0, body.EffectiveLength / 2 - hz);
            x = Math.Max(-maxX, Math.Min(maxX, x));
            z = Math.Max(-maxZ, Math.Min(maxZ, z));
            if (body.Shape != BodyShape.Round) return (x, z);
            float r = body.WidthMm / 2;
            bool OnDisc(float s) => Square(Math.Abs(s * x) + hx) + Square(Math.Abs(s * z) + hz) <= r * r;
            if (OnDisc(1)) return (x, z);
            float lo = 0, hi = 1; // pull the part toward the centre until its far corner is on the disc
            for (int i = 0; i < 20; i++)
            {
                float mid = (lo + hi) / 2;
                if (OnDisc(mid)) lo = mid;
                else hi = mid;
            }
            return (lo * x, lo * z);
        }

        static float Square(float v) => v * v;

        /// <summary>
        /// Centre of mass in the chassis frame (mm) of the plates, parts and cells, and of the wheels unless
        /// <paramref name="wheels"/> is false (the arena gives the wheels bodies of their own).
        /// </summary>
        public static (float x, float y, float z) CentreOfMass(RobotDesign design, bool wheels = true)
        {
            var body = design.Body;
            double m = 0, x = 0, y = 0, z = 0;
            void Add(double grams, float px, float py, float pz)
            {
                m += grams;
                x += grams * px;
                y += grams * py;
                z += grams * pz;
            }
            double density = BodyDesign.DensityGPerCm3(body.Material) / 1000.0; // g per mm³
            double features = 0;
            foreach (var feature in body.Features)
            {
                if (feature.Hole) continue;
                double grams = feature.ApproximateVolume() * density;
                Add(grams, feature.X, feature.Y, feature.Z);
                features += grams;
            }
            double plates = Math.Max(0, BodyMassG(body) - features);
            float t = body.ThicknessMm;
            if (body.Decks >= 2)
            {
                Add(plates / 2, 0, BottomPlateTop - t / 2, 0);
                Add(plates / 2, 0, TopDeckBottom + t / 2, 0);
            }
            else
            {
                Add(plates, 0, BottomPlateTop - t / 2, 0);
            }
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                var p = Place(design, part);
                float cy = def.Mount == MountKind.Deck ? p.y + def.SizeY / 2 : p.y; // deck parts stand on the deck
                Add(def.MassG + (def.Kind == PartKind.Battery ? CellsMassG : 0), p.x, cy, p.z);
                if (wheels && def.Kind == PartKind.Motor)
                {
                    var w = WheelCentre(body, part.Slot);
                    Add(WheelMassG, w.x, w.y, w.z);
                }
            }
            return m <= 0 ? (0f, 0f, 0f) : ((float)(x / m), (float)(y / m), (float)(z / m));
        }

        /// <summary>Approximate plate mass before holes (the Garage refines it with Manifold's exact volume).</summary>
        public static double BodyMassG(BodyDesign body)
        {
            double area = body.Shape == BodyShape.Round
                ? Math.PI * body.WidthMm * body.WidthMm / 4
                : body.LengthMm * body.WidthMm;
            double volumeMm3 = area * body.ThicknessMm * Math.Max(1, body.Decks);
            if (body.WallHeightMm > 0 && body.Shape != BodyShape.Round) volumeMm3 += 2 * body.LengthMm * body.ThicknessMm * body.WallHeightMm;
            foreach (var feature in body.Features)
                volumeMm3 += (feature.Hole ? -0.5 : 1) * feature.ApproximateVolume(); // a hole cuts only where there is material
            return Math.Max(0, volumeMm3) / 1000.0 * BodyDesign.DensityGPerCm3(body.Material);
        }

        /// <summary>Length of a wire in millimetres (straight line; a jumper's slack comes on top).</summary>
        public static double WireLength(RobotDesign design, WireInstance wire)
        {
            var a = PinPosition(design, wire.FromPart, wire.FromPin);
            var b = PinPosition(design, wire.ToPart, wire.ToPin);
            if (a == null || b == null) return 0;
            double dx = a.Value.x - b.Value.x, dy = a.Value.y - b.Value.y, dz = a.Value.z - b.Value.z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
