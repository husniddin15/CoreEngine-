using System;
using System.Collections.Generic;

namespace CoreEngine.Sim.Design
{
    public enum BodyShape { Rectangle, Rounded, Round }

    /// <summary>
    /// What a body shape is made of (docs/08 §2): its look and its density. The first three keep the numbers
    /// older saves use (Acrylic 0, PLA 1, Plywood 2).
    /// </summary>
    public enum BodyMaterial { Acrylic, Pla, Plywood, Cardboard, EvaFoam, FoamBoard, Aluminium }

    /// <summary>
    /// The robot's body: shapes the player builds it from in the Body Studio (docs/08), each of its own
    /// material, in millimetres in the chassis frame (y up from the floor the robot stands on, +z forward).
    /// Shapes can be grouped as in Tinkercad: a hole cuts only the shapes of its own group.
    /// Fields are public so Unity's JsonUtility can save them.
    /// </summary>
    [Serializable]
    public sealed class BodyDesign
    {
        /// <summary>Every shape and group record, in the order they were made.</summary>
        public List<BodyFeature> Features = new List<BodyFeature>();

        // A version-2 body (before 2026-09-24) was two ready-made plates with these settings. They are read
        // only to turn such a body into shapes (DesignMigration); a new body has no plates.
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

        /// <summary>A version-2 body's length: round bodies use the width as their diameter.</summary>
        public float EffectiveLength => Shape == BodyShape.Round ? WidthMm : LengthMm;

        public static float DensityGPerCm3(BodyMaterial material) => material switch
        {
            BodyMaterial.Pla => 1.24f,
            BodyMaterial.Plywood => 0.68f,
            BodyMaterial.Cardboard => 0.15f,  // corrugated board
            BodyMaterial.EvaFoam => 0.10f,    // craft foam sheet ("fomiks")
            BodyMaterial.FoamBoard => 0.50f,  // PVC foam board
            BodyMaterial.Aluminium => 2.70f,
            _ => 1.18f,                       // acrylic
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

        /// <summary>Adds a shape or group with a new id ("f1", "f2", …) and returns it.</summary>
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

        /// <summary>The group a shape or group is in, or null at the top level (a missing group counts as none).</summary>
        public BodyFeature? Parent(BodyFeature feature) =>
            feature.Group.Length == 0 ? null : Feature(feature.Group) is { Kind: FeatureKind.Group } group ? group : null;

        /// <summary>The shapes and groups directly in a group; with null, the top level.</summary>
        public List<BodyFeature> Members(BodyFeature? group)
        {
            var members = new List<BodyFeature>();
            foreach (var feature in Features)
                if (Parent(feature) == group) members.Add(feature);
            return members;
        }

        /// <summary>Every shape (not group record) inside a group, however deep.</summary>
        public List<BodyFeature> Shapes(BodyFeature group)
        {
            var shapes = new List<BodyFeature>();
            foreach (var member in Members(group))
            {
                if (member.Kind == FeatureKind.Group) shapes.AddRange(Shapes(member));
                else shapes.Add(member);
            }
            return shapes;
        }

        /// <summary>The top-level shape or group that contains a feature (the feature itself at the top level).</summary>
        public BodyFeature TopLevel(BodyFeature feature)
        {
            var top = feature;
            for (var parent = Parent(top); parent != null; parent = Parent(top)) top = parent;
            return top;
        }

        /// <summary>
        /// Groups top-level shapes and groups (Tinkercad's Group): a new group record takes them in. Holes among them
        /// then cut the solids among them. Returns null for fewer than two.
        /// </summary>
        public BodyFeature? Group(IEnumerable<string> ids)
        {
            var chosen = new List<BodyFeature>();
            foreach (string id in ids)
            {
                var feature = Feature(id);
                if (feature != null && !chosen.Contains(TopLevel(feature))) chosen.Add(TopLevel(feature));
            }
            if (chosen.Count < 2) return null;
            var group = AddFeature(new BodyFeature { Kind = FeatureKind.Group });
            foreach (var member in chosen) member.Group = group.Id;
            return group;
        }

        /// <summary>Takes a group apart (Tinkercad's Ungroup): its members go back to where the group was.</summary>
        public void Ungroup(string id)
        {
            var group = Feature(id);
            if (group == null || group.Kind != FeatureKind.Group) return;
            foreach (var member in Members(group)) member.Group = group.Group;
            Features.Remove(group);
        }

        /// <summary>Removes a shape, or a group with everything in it.</summary>
        public void Remove(string id)
        {
            var feature = Feature(id);
            if (feature == null) return;
            if (feature.Kind == FeatureKind.Group)
                foreach (var member in Members(feature)) Remove(member.Id);
            Features.Remove(feature);
        }
    }

    /// <summary>
    /// One part on the robot: a catalogue id and where the player put it. Parts keep their real size; they are
    /// moved and turned freely (since 2026-09-24).
    /// </summary>
    [Serializable]
    public sealed class PartInstance
    {
        public string Id = "";
        public string Part = "";
        public string Slot = "";   // version 2: the "left" or "right" motor mount; now the side follows the wheel's place
        public float X, Y, Z;      // mm, chassis frame: where the part's own frame origin is
        public float Rotation;     // degrees about y (the name older saves use); with RotX and RotZ as Unity's Quaternion.Euler
        public float RotX, RotZ;

        /// <summary>
        /// A TT motor's wheel is on the other end of its double shaft: +x of the motor's frame instead of −x. A
        /// real TT motor's white shaft comes out on both sides, so a builder puts the wheel on whichever side faces
        /// out; the right motor is then the mirror of the left without being turned round (the owner, 2026-09-25).
        /// </summary>
        public bool WheelOtherEnd;

        public Rot3 Turn => Rot3.Euler(RotX, Rotation, RotZ);

        public PartInstance Clone() => (PartInstance)MemberwiseClone();
    }

    /// <summary>
    /// A jumper wire (or lead) from one pin to another (docs/03 §6), through the points the player gave it, in
    /// order from its first pin to its second.
    /// </summary>
    [Serializable]
    public sealed class WireInstance
    {
        public string FromPart = "";
        public string FromPin = "";
        public string ToPart = "";
        public string ToPin = "";
        public string Color = "yellow";
        public List<WirePoint> Points = new List<WirePoint>();

        public WireInstance Clone()
        {
            var copy = (WireInstance)MemberwiseClone();
            copy.Points = new List<WirePoint>();
            if (Points != null) foreach (var point in Points) copy.Points.Add(point.Clone());
            return copy;
        }
    }

    /// <summary>
    /// A point the player gave a wire (docs/03 §6.2): the wire passes through it, as a builder bends a jumper
    /// round where it should go, or, glued, lies on a surface there under a blob of hot glue. A free point is in
    /// the chassis frame (mm). A glued point belongs to the part or body shape it is glued to and moves with it:
    /// on a part, in the part's own frame (mm); on a shape, as shares of the shape's half sizes in its own frame,
    /// so it stays on the same face when the shape is made bigger or smaller.
    /// </summary>
    [Serializable]
    public sealed class WirePoint
    {
        public float X, Y, Z;
        public bool Glued;
        public string Part = "";      // glued to this part…
        public string Shape = "";     // …or to this body shape
        public float NX, NY = 1, NZ;  // the surface's outward normal there, in the same frame (a glued point)

        public WirePoint Clone() => (WirePoint)MemberwiseClone();
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

        /// <summary>
        /// Adds a part standing on the ground beside the others (the Garage then puts it where the mouse is).
        /// Returns null when the catalogue allows no more of it.
        /// </summary>
        public PartInstance? AddPart(string partId)
        {
            var def = PartCatalog.Get(partId);
            if (def == null || Count(partId) >= def.MaxCount) return null;
            var instance = new PartInstance { Id = NewId(def), Part = partId, Y = DesignGeometry.RestHeight(def) };
            var spot = DesignGeometry.FreeSpot(this, instance);
            instance.X = spot.x;
            instance.Z = spot.z;
            Parts.Add(instance);
            return instance;
        }

        /// <summary>Removes a part, every wire attached to it and every wire's glue on it.</summary>
        public void RemovePart(string id)
        {
            Parts.RemoveAll(p => p.Id == id);
            Wires.RemoveAll(w => w.FromPart == id || w.ToPart == id);
            foreach (var w in Wires) w.Points?.RemoveAll(p => p.Glued && p.Part == id);
        }

        /// <summary>Takes away the wires' glue on parts and body shapes that are gone (after shapes are deleted).</summary>
        public void DropLooseGlue()
        {
            foreach (var w in Wires) w.Points?.RemoveAll(p => p.Glued && !GlueHolds(p));
        }

        bool GlueHolds(WirePoint p) => p.Part.Length > 0 ? Find(p.Part) != null : Body.Feature(p.Shape) is { Kind: not FeatureKind.Group };

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

        /// <summary>True when a motor's wheel is on that side ("left" or "right") of the robot.</summary>
        public bool HasSlot(string partId, string slot)
        {
            foreach (var part in Parts)
                if (part.Part == partId && DesignGeometry.SideOf(part) == slot) return true;
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
            foreach (var p in Parts) copy.Parts.Add(p.Clone());
            foreach (var w in Wires) copy.Wires.Add(w.Clone());
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
                PartKind.Servo => "servo",
                PartKind.Led => "led",
                _ => "caster",
            };
            for (int n = 1; ; n++)
                if (Find(stem + n) == null) return stem + n;
        }
    }

    /// <summary>
    /// Where things are, in millimetres in the chassis frame: y up from the floor the robot stands on (y = 0),
    /// +x right, +z forward. Everything follows from where the player put each part and shape: the wheels are
    /// on the motors' shafts, the caster ball where the caster is, the sensor looks where it faces.
    /// </summary>
    public static class DesignGeometry
    {
        public const float WheelRadius = 32.5f;
        public const float WheelWidth = 26;
        public const float WheelMassG = 30;         // 65 mm TT wheel with its tyre
        public const float CellsMassG = 4 * 23;     // four alkaline AA cells in the holder
        public const float CasterBallRadius = 10;

        /// <summary>The wheel's centre in a TT motor's frame: on the shaft, 8.5 mm above the gearbox middle, on the −x side.</summary>
        public static readonly (float x, float y, float z) WheelInMotor = (-27.5f, 8.5f, 0);

        /// <summary>Where a motor's wheel is in its frame: on the −x end of the shaft, or the +x end (<see cref="PartInstance.WheelOtherEnd"/>).</summary>
        public static (float x, float y, float z) WheelOffset(PartInstance motor) =>
            motor.WheelOtherEnd ? (-WheelInMotor.x, WheelInMotor.y, WheelInMotor.z) : WheelInMotor;

        /// <summary>The middle of the HC-SR04's transducer faces in its frame; it looks along +z.</summary>
        public static readonly (float x, float y, float z) SonarFaceInPart = (0, 0, 13);

        public static (float x, float y, float z) ToChassis(PartInstance part, (float x, float y, float z) local)
        {
            var r = part.Turn.Apply(local);
            return (part.X + r.x, part.Y + r.y, part.Z + r.z);
        }

        public static (float x, float y, float z) Direction(PartInstance part, (float x, float y, float z) local) => part.Turn.Apply(local);

        /// <summary>A pin's position in the chassis frame.</summary>
        public static (float x, float y, float z)? PinPosition(RobotDesign design, string partId, string pinId)
        {
            var part = design.Find(partId);
            var pin = part == null ? null : PartCatalog.Get(part.Part)?.Pin(pinId);
            if (part == null || pin == null) return null;
            return ToChassis(part, (pin.X, pin.Y, pin.Z));
        }

        /// <summary>The direction a wire leaves a pin, in the chassis frame (unit length).</summary>
        public static (float x, float y, float z)? PinExit(RobotDesign design, string partId, string pinId)
        {
            var part = design.Find(partId);
            var pin = part == null ? null : PartCatalog.Get(part.Part)?.Pin(pinId);
            if (part == null || pin == null) return null;
            return Direction(part, (pin.ExitX, pin.ExitY, pin.ExitZ));
        }

        /// <summary>Unity's rotation about +y: (x, z) → (x cos θ + z sin θ, −x sin θ + z cos θ).</summary>
        public static (float x, float z) Rotate(float x, float z, float degrees)
        {
            double r = degrees * Math.PI / 180;
            double c = Math.Cos(r), s = Math.Sin(r);
            return ((float)(x * c + z * s), (float)(-x * s + z * c));
        }

        // ------------------------------------------------------------------ what the parts do where they are

        public static (float x, float y, float z) WheelCentre(PartInstance motor) => ToChassis(motor, WheelOffset(motor));

        /// <summary>
        /// The motor shaft's direction (unit length): a positive voltage on M+ turns the wheel about it by the
        /// right-hand rule, whichever end of the shaft the wheel is on.
        /// </summary>
        public static (float x, float y, float z) WheelAxis(PartInstance motor) => Direction(motor, (1, 0, 0));

        /// <summary>"left" or "right": the side the motor's wheel is on.</summary>
        public static string SideOf(PartInstance motor) => WheelCentre(motor).x < 0 ? "left" : "right";

        /// <summary>
        /// +1 when a positive voltage on the motor's M+ lead drives the robot forward (+z), −1 when it drives it back,
        /// 0 when the shaft does not point sideways and the wheel cannot drive. A wheel turning about +x rolls
        /// forward, so a motor turned round (its shaft toward −x, as the right motor of a kit) needs its leads swapped.
        /// </summary>
        public static int ForwardSign(PartInstance motor)
        {
            var axis = WheelAxis(motor);
            return Math.Abs(axis.x) < 0.5f ? 0 : axis.x > 0 ? 1 : -1;
        }

        public static (float x, float y, float z) CasterBall(PartInstance caster) => ToChassis(caster, (0, 0, 0));

        public static (float x, float y, float z) SonarFace(PartInstance sonar) => ToChassis(sonar, SonarFaceInPart);

        public static (float x, float y, float z) SonarAim(PartInstance sonar) => Direction(sonar, (0, 0, 1));

        // ------------------------------------------------------------------ bounds

        /// <summary>The corners of a box of the given size about a centre, turned and placed.</summary>
        static IEnumerable<(float x, float y, float z)> Corners(Rot3 turn, (float x, float y, float z) at, (float x, float y, float z) centre, (float x, float y, float z) size)
        {
            foreach (float sx in new[] { -0.5f, 0.5f })
                foreach (float sy in new[] { -0.5f, 0.5f })
                    foreach (float sz in new[] { -0.5f, 0.5f })
                    {
                        var r = turn.Apply(centre.x + sx * size.x, centre.y + sy * size.y, centre.z + sz * size.z);
                        yield return (at.x + r.x, at.y + r.y, at.z + r.z);
                    }
        }

        /// <summary>A part's box in the chassis frame (min and max corners), with its wheel for a motor.</summary>
        public static ((float x, float y, float z) min, (float x, float y, float z) max) PartBounds(PartInstance part)
        {
            var def = PartCatalog.Get(part.Part);
            var min = (x: float.MaxValue, y: float.MaxValue, z: float.MaxValue);
            var max = (x: float.MinValue, y: float.MinValue, z: float.MinValue);
            void Take((float x, float y, float z) p)
            {
                min = (Math.Min(min.x, p.x), Math.Min(min.y, p.y), Math.Min(min.z, p.z));
                max = (Math.Max(max.x, p.x), Math.Max(max.y, p.y), Math.Max(max.z, p.z));
            }
            if (def == null)
            {
                Take((part.X, part.Y, part.Z));
                return (min, max);
            }
            foreach (var corner in Corners(part.Turn, (part.X, part.Y, part.Z), def.BoxCentre, (def.SizeX, def.SizeY, def.SizeZ))) Take(corner);
            if (def.Kind == PartKind.Motor) // the wheel: 26 mm wide along the shaft, 65 mm across
                foreach (var corner in Corners(part.Turn, (part.X, part.Y, part.Z), WheelOffset(part), (WheelWidth, 2 * WheelRadius, 2 * WheelRadius))) Take(corner);
            return (min, max);
        }

        /// <summary>A shape's turned box in the chassis frame (min and max corners).</summary>
        public static ((float x, float y, float z) min, (float x, float y, float z) max) FeatureBounds(BodyFeature f)
        {
            var min = (x: float.MaxValue, y: float.MaxValue, z: float.MaxValue);
            var max = (x: float.MinValue, y: float.MinValue, z: float.MinValue);
            foreach (var p in Corners(Rot3.Euler(f.RotX, f.RotY, f.RotZ), (f.X, f.Y, f.Z), (0, 0, 0), (f.SizeX, f.SizeY, f.SizeZ)))
            {
                min = (Math.Min(min.x, p.x), Math.Min(min.y, p.y), Math.Min(min.z, p.z));
                max = (Math.Max(max.x, p.x), Math.Max(max.y, p.y), Math.Max(max.z, p.z));
            }
            return (min, max);
        }

        /// <summary>
        /// The lowest point of the robot (mm): the bottom of a wheel, the caster ball, or a part or solid shape
        /// hanging lower. The arena and the Garage's turntable stand the robot there; 0 for an empty design.
        /// </summary>
        public static float LowestPoint(RobotDesign design)
        {
            float lowest = float.MaxValue;
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def?.Kind == PartKind.Motor)
                {
                    var centre = WheelCentre(part);
                    var axis = WheelAxis(part);
                    lowest = Math.Min(lowest, centre.y - WheelRadius * (float)Math.Sqrt(Math.Max(0, 1 - axis.y * axis.y)));
                }
                if (def?.Kind == PartKind.Caster) lowest = Math.Min(lowest, CasterBall(part).y - CasterBallRadius);
                lowest = Math.Min(lowest, PartBounds(part).min.y);
            }
            foreach (var feature in design.Body.Features)
            {
                if (feature.Kind == FeatureKind.Group || feature.Hole) continue;
                lowest = Math.Min(lowest, FeatureBounds(feature).min.y);
            }
            return lowest == float.MaxValue ? 0 : lowest;
        }

        // ------------------------------------------------------------------ placing new parts

        /// <summary>
        /// How high a part's frame is when it stands on the ground unturned: its lowest point (a motor's wheel,
        /// the caster's ball, a board's underside) on y = 0.
        /// </summary>
        public static float RestHeight(PartDef def)
        {
            float lowest = def.BoxCentre.y - def.SizeY / 2;
            if (def.Kind == PartKind.Motor) lowest = Math.Min(lowest, WheelInMotor.y - WheelRadius);
            return -lowest;
        }

        /// <summary>A place on the ground, on a ring of 45 mm steps round the middle, where the part meets no other part.</summary>
        public static (float x, float z) FreeSpot(RobotDesign design, PartInstance part)
        {
            for (int ring = 0; ring < 8; ring++)
            {
                int steps = ring == 0 ? 1 : ring * 8;
                for (int i = 0; i < steps; i++)
                {
                    double angle = 2 * Math.PI * i / steps;
                    part.X = (float)Math.Round(ring * 45 * Math.Sin(angle));
                    part.Z = (float)Math.Round(ring * 45 * Math.Cos(angle));
                    if (!Overlaps(design, part)) return (part.X, part.Z);
                }
            }
            return (0, 0);
        }

        /// <summary>True when a part's box meets another part's box (seen from above).</summary>
        public static bool Overlaps(RobotDesign design, PartInstance part)
        {
            var (min, max) = PartBounds(part);
            foreach (var other in design.Parts)
            {
                if (other == part || other.Id == part.Id) continue;
                var (omin, omax) = PartBounds(other);
                if (min.x < omax.x && max.x > omin.x && min.z < omax.z && max.z > omin.z && min.y < omax.y && max.y > omin.y) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ mass

        /// <summary>
        /// Centre of mass in the chassis frame (mm) of the shapes, parts and cells, and of the wheels unless
        /// <paramref name="wheels"/> is false (the arena gives the wheels bodies of their own).
        /// </summary>
        public static (float x, float y, float z) CentreOfMass(RobotDesign design, bool wheels = true)
        {
            double m = 0, x = 0, y = 0, z = 0;
            void Add(double grams, (float x, float y, float z) p)
            {
                m += grams;
                x += grams * p.x;
                y += grams * p.y;
                z += grams * p.z;
            }
            foreach (var feature in design.Body.Features)
            {
                if (feature.Kind == FeatureKind.Group || feature.Hole) continue;
                Add(feature.ApproximateVolume() / 1000.0 * BodyDesign.DensityGPerCm3(feature.Material), (feature.X, feature.Y, feature.Z));
            }
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                Add(def.MassG + (def.Kind == PartKind.Battery ? CellsMassG : 0), ToChassis(part, def.BoxCentre));
                if (wheels && def.Kind == PartKind.Motor) Add(WheelMassG, WheelCentre(part));
            }
            return m <= 0 ? (0f, 0f, 0f) : ((float)(x / m), (float)(y / m), (float)(z / m));
        }

        /// <summary>
        /// The body's mass estimated from the shapes' boxes; the Garage shows Manifold's exact volumes. A hole takes
        /// away half its volume of its group's material (it cuts only where there is material); a hole on its own
        /// cuts nothing, as in Tinkercad.
        /// </summary>
        public static double BodyMassG(BodyDesign body)
        {
            double grams = 0;
            foreach (var feature in body.Features)
            {
                if (feature.Kind == FeatureKind.Group) continue;
                double cm3 = feature.ApproximateVolume() / 1000.0;
                if (!feature.Hole)
                {
                    grams += cm3 * BodyDesign.DensityGPerCm3(feature.Material);
                    continue;
                }
                var group = body.Parent(feature);
                if (group == null) continue;
                BodyMaterial material = BodyMaterial.Pla;
                foreach (var shape in body.Shapes(body.TopLevel(group)))
                {
                    if (shape.Hole) continue;
                    material = shape.Material;
                    break;
                }
                grams -= 0.5 * cm3 * BodyDesign.DensityGPerCm3(material);
            }
            return Math.Max(0, grams);
        }

        /// <summary>
        /// Length of a wire in millimetres: straight from pin to pin, through its points (a jumper's slack and its
        /// way round the parts come on top).
        /// </summary>
        public static double WireLength(RobotDesign design, WireInstance wire)
        {
            var a = PinPosition(design, wire.FromPart, wire.FromPin);
            var b = PinPosition(design, wire.ToPart, wire.ToPin);
            if (a == null || b == null) return 0;
            double length = 0;
            var from = a.Value;
            if (wire.Points != null)
                foreach (var point in wire.Points)
                {
                    var place = PointPlace(design, point);
                    if (place == null) continue;
                    length += Distance(from, place.Value.at);
                    from = place.Value.at;
                }
            return length + Distance(from, b.Value);
        }

        static double Distance((float x, float y, float z) a, (float x, float y, float z) b)
        {
            double dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // ------------------------------------------------------------------ the points players give wires

        /// <summary>
        /// Where a wire's point is in the chassis frame (mm) and the outward normal of the surface a glued point
        /// is on (straight up for a free one); null when the part or shape it was glued to is gone.
        /// </summary>
        public static ((float x, float y, float z) at, (float x, float y, float z) normal)? PointPlace(RobotDesign design, WirePoint point)
        {
            if (!point.Glued) return ((point.X, point.Y, point.Z), (0f, 1f, 0f));
            if (point.Part.Length > 0)
            {
                var part = design.Find(point.Part);
                if (part == null) return null;
                return (ToChassis(part, (point.X, point.Y, point.Z)), Unit(Direction(part, (point.NX, point.NY, point.NZ))));
            }
            var f = design.Body.Feature(point.Shape);
            if (f == null || f.Kind == FeatureKind.Group) return null;
            var turn = Rot3.Euler(f.RotX, f.RotY, f.RotZ);
            var r = turn.Apply(point.X * f.SizeX / 2, point.Y * f.SizeY / 2, point.Z * f.SizeZ / 2);
            return ((f.X + r.x, f.Y + r.y, f.Z + r.z), Unit(turn.Apply(point.NX, point.NY, point.NZ)));
        }

        /// <summary>
        /// A glued point at a spot (chassis frame, mm) on a part or a body shape, with the surface's outward normal
        /// there; null when there is no such part or shape.
        /// </summary>
        public static WirePoint? GluePoint(RobotDesign design, string owner, (float x, float y, float z) at, (float x, float y, float z) normal)
        {
            var part = design.Find(owner);
            if (part != null)
            {
                var back = part.Turn.Inverse();
                var local = back.Apply(at.x - part.X, at.y - part.Y, at.z - part.Z);
                var n = back.Apply(normal);
                return new WirePoint { Glued = true, Part = owner, X = local.x, Y = local.y, Z = local.z, NX = n.x, NY = n.y, NZ = n.z };
            }
            var f = design.Body.Feature(owner);
            if (f == null || f.Kind == FeatureKind.Group) return null;
            var inverse = Rot3.Euler(f.RotX, f.RotY, f.RotZ).Inverse();
            var l = inverse.Apply(at.x - f.X, at.y - f.Y, at.z - f.Z);
            var m = inverse.Apply(normal);
            return new WirePoint
            {
                Glued = true, Shape = owner,
                X = l.x / Math.Max(0.05f, f.SizeX / 2), Y = l.y / Math.Max(0.05f, f.SizeY / 2), Z = l.z / Math.Max(0.05f, f.SizeZ / 2),
                NX = m.x, NY = m.y, NZ = m.z,
            };
        }

        static (float x, float y, float z) Unit((float x, float y, float z) v)
        {
            float length = (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            return length > 1e-6f ? (v.x / length, v.y / length, v.z / length) : (0f, 1f, 0f);
        }
    }
}
