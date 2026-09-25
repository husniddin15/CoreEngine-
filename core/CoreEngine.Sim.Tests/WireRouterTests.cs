using System.Diagnostics;
using CoreEngine.Sim.Design;
using Xunit.Abstractions;

namespace CoreEngine.Sim.Tests;

/// <summary>Tests that time themselves run after the others, alone, so the emulator tests do not slow them down.</summary>
[CollectionDefinition("Timed", DisableParallelization = true)]
public class TimedCollection
{
}

[Collection("Timed")]
public class WireRouterTests
{
    readonly ITestOutputHelper output;

    public WireRouterTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// Checks a route against the design's solids with plain inside tests, independent of the router's field:
    /// every point every 0.5 mm, except near its two ends, where the wire leaves its own part, must be outside
    /// every part block, every body shape and above the floor.
    /// </summary>
    static void AssertClear(RobotDesign design, List<(float x, float y, float z)> route, string what, float guard = 16f)
    {
        var start = route[0];
        var end = route[route.Count - 1];
        float floor = DesignGeometry.LowestPoint(design);
        for (int i = 0; i + 1 < route.Count; i++)
        {
            var p = route[i];
            var q = route[i + 1];
            float length = Distance(p, q);
            int steps = Math.Max(1, (int)(length / 0.5f));
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                var x = (p.x + (q.x - p.x) * t, p.y + (q.y - p.y) * t, p.z + (q.z - p.z) * t);
                if (Distance(x, start) < guard || Distance(x, end) < guard) continue;
                Assert.True(x.Item2 > floor, $"{what}: under the floor at {x}");
                foreach (var part in design.Parts)
                {
                    var def = PartCatalog.Get(part.Part)!;
                    foreach (var block in def.Solids)
                        Assert.False(InsideBlock(part, block, x), $"{what}: through {part.Id} at {x}");
                }
                foreach (var f in design.Body.Features)
                {
                    if (f.Hole || f.Kind == FeatureKind.Group || !InsideShape(f, x)) continue;
                    // A hole of the same group takes the material away there.
                    var group = design.Body.TopLevel(f);
                    bool cut = group != f && design.Body.Shapes(group).Exists(h => h.Hole && InsideShape(h, x, grow: true));
                    Assert.True(cut, $"{what}: through body shape {f.Id} ({f.Kind}) at {x}");
                }
            }
        }
    }

    static float Distance((float x, float y, float z) a, (float x, float y, float z) b) =>
        MathF.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y) + (a.z - b.z) * (a.z - b.z));

    static bool InsideBlock(PartInstance part, PartBlock block, (float x, float y, float z) p)
    {
        var turn = part.Turn;
        var c = turn.Apply(block.Centre);
        var local = turn.Inverse().Apply(p.x - part.X - c.x, p.y - part.Y - c.y, p.z - part.Z - c.z);
        const float shrink = 0.3f; // a wire touching a block's face is not through it
        return Math.Abs(local.x) < block.Size.x / 2 - shrink && Math.Abs(local.y) < block.Size.y / 2 - shrink && Math.Abs(local.z) < block.Size.z / 2 - shrink;
    }

    /// <summary>Inside a plate, box or cylinder of the body (the shapes these tests use), less a hair (more one, for a hole).</summary>
    static bool InsideShape(BodyFeature f, (float x, float y, float z) p, bool grow = false)
    {
        var local = Rot3.Euler(f.RotX, f.RotY, f.RotZ).Inverse().Apply(p.x - f.X, p.y - f.Y, p.z - f.Z);
        float shrink = grow ? -0.3f : 0.3f;
        if (Math.Abs(local.y) >= f.SizeY / 2 - shrink) return false;
        if (f.Kind == FeatureKind.Cylinder)
        {
            float r = f.SizeX / 2 - shrink;
            return local.x * local.x + local.z * local.z < r * r;
        }
        return Math.Abs(local.x) < f.SizeX / 2 - shrink && Math.Abs(local.z) < f.SizeZ / 2 - shrink;
    }

    [Fact]
    public void EveryKitWireGoesRoundThePartsAndThePlates()
    {
        var design = DesignPresets.ObstacleAvoiderKit();
        var router = new WireRouter(design);
        var watch = Stopwatch.StartNew();
        foreach (var wire in design.Wires)
        {
            var route = router.Route(design, wire);
            string what = $"{wire.FromPart}.{wire.FromPin} to {wire.ToPart}.{wire.ToPin}";
            Assert.True(route != null, what + ": no way found");
            AssertClear(design, route!, what);
        }
        output.WriteLine($"16 kit wires routed in {watch.Elapsed.TotalMilliseconds:F1} ms, {router.Evaluations} field evaluations");
    }

    /// <summary>The sharpest bend along a route: the largest angle between one short step and the next.</summary>
    static double SharpestBendDegrees(List<(float x, float y, float z)> route)
    {
        double sharpest = 0;
        (double x, double y, double z)? before = null;
        for (int i = 0; i + 1 < route.Count; i++)
        {
            double dx = route[i + 1].x - route[i].x, dy = route[i + 1].y - route[i].y, dz = route[i + 1].z - route[i].z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 0.05) continue;
            var direction = (dx / length, dy / length, dz / length);
            if (before != null)
            {
                double dot = before.Value.x * direction.Item1 + before.Value.y * direction.Item2 + before.Value.z * direction.Item3;
                sharpest = Math.Max(sharpest, Math.Acos(Math.Clamp(dot, -1, 1)) * 180 / Math.PI);
            }
            before = direction;
        }
        return sharpest;
    }

    /// <summary>
    /// The owner's first robot as it stood on 2026-09-25: the two-wheeler with the sonar at the L298N's end and
    /// the Uno on VIN. Its motor leads leave the L298N's terminals away from the motors and turn back at once.
    /// </summary>
    static RobotDesign OwnersRobot()
    {
        var design = DesignPresets.PwmTwoWheeler();
        design.Parts.Add(new PartInstance { Id = "sonar1", Part = PartCatalog.HcSr04, X = -15, Y = 56, Z = 135 });
        design.Wires.RemoveAll(w => w.FromPart == "driver1" && w.FromPin == "+5V");
        design.AddWire("driver1", "+12V", "uno1", "VIN", "red");
        design.AddWire("sonar1", "VCC", "driver1", "+5V", "red");
        design.AddWire("sonar1", "TRIG", "uno1", "D2", "orange");
        design.AddWire("uno1", "D3", "sonar1", "ECHO", "white");
        design.AddWire("sonar1", "GND", "uno1", "GND.1", "black");
        return design;
    }

    public static IEnumerable<object[]> Robots() => new[] { new object[] { "kit" }, new object[] { "owner's" } };

    [Theory]
    [MemberData(nameof(Robots))]
    public void EveryWireBendsSmoothlyWithoutCorners(string robot)
    {
        // The owner (2026-09-25): wires "with sharp edges... should be smooth like real life". Points lie about
        // 2 mm apart, so 32° between one step and the next is a bend of about 3.6 mm radius, the tightest allowed:
        // a wire that must turn right back as it leaves its pin, round the edge of a part, bends about that tight.
        var design = robot == "kit" ? DesignPresets.ObstacleAvoiderKit() : OwnersRobot();
        var router = new WireRouter(design);
        foreach (var wire in design.Wires)
        {
            var route = router.Route(design, wire);
            string what = $"{wire.FromPart}.{wire.FromPin} to {wire.ToPart}.{wire.ToPin}";
            Assert.True(route != null, what + ": no way found");
            double sharpest = SharpestBendDegrees(route!);
            output.WriteLine($"{what}: {route!.Count} points, sharpest bend {sharpest:F1}°");
            Assert.True(sharpest <= 32, $"{what} bends {sharpest:F1}° in one step");
            AssertClear(design, route, what);
        }
    }

    [Fact]
    public void MotorLeadsLeaveTheUndersideRoundAnEdge()
    {
        // The motors hang under the bottom plate and their leads end on the L298N on the top deck: the leads
        // must go out past a plate's edge (the plates are 120 × 160 mm) and come back up.
        var design = DesignPresets.ObstacleAvoiderKit();
        var router = new WireRouter(design);
        var wire = design.Wires.Find(w => w.FromPart == "motor1" && w.FromPin == "M+")!;
        var route = router.Route(design, wire)!;
        Assert.Contains(route, p => Math.Abs(p.x) > 60 || Math.Abs(p.z) > 80);
        Assert.True(route[^1].y > 65, "it ends above the top deck");
    }

    /// <summary>A 150 mm plate 40 mm up with a 30 mm hole in the middle, and a foot so the floor is at 0.</summary>
    static RobotDesign PlateWithHole(bool grouped)
    {
        var design = new RobotDesign { Body = new BodyDesign { Decks = 0 } };
        var body = design.Body;
        var plate = body.AddFeature(new BodyFeature { Kind = FeatureKind.Box, SizeX = 150, SizeY = 3, SizeZ = 150, Y = 40 });
        var hole = body.AddFeature(new BodyFeature { Kind = FeatureKind.Cylinder, Hole = true, SizeX = 30, SizeY = 13, SizeZ = 30, Y = 40 });
        body.AddFeature(new BodyFeature { Kind = FeatureKind.Box, SizeX = 10, SizeY = 38.5f, SizeZ = 10, X = 60, Y = 19.25f, Z = 60 });
        if (grouped) body.Group(new[] { plate.Id, hole.Id });
        return design;
    }

    [Fact]
    public void AWireGoesThroughAHoleWhenThereIsOne()
    {
        var design = PlateWithHole(grouped: true);
        var route = new WireRouter(design).Route((0, 20, 0), (0, 1, 0), 4, (0, 60, 0), (0, -1, 0), 4)!;
        Assert.NotNull(route);
        foreach (var p in route) Assert.True(MathF.Sqrt(p.x * p.x + p.z * p.z) < 15, $"through the hole, not round the plate: {p}");
        AssertClear(design, route, "through the hole", guard: 6);
    }

    [Fact]
    public void WithoutAHoleTheWireGoesRoundThePlate()
    {
        // A hole on its own cuts nothing (Tinkercad's rule), so here the plate is whole.
        var design = PlateWithHole(grouped: false);
        var route = new WireRouter(design).Route((0, 20, 0), (0, 1, 0), 4, (0, 60, 0), (0, -1, 0), 4)!;
        Assert.NotNull(route);
        Assert.Contains(route, p => Math.Abs(p.x) > 75 || Math.Abs(p.z) > 75);
        AssertClear(design, route, "round the plate", guard: 6);
    }

    [Fact]
    public void AWireArchesOverAPartInItsWay()
    {
        var design = new RobotDesign { Body = new BodyDesign { Decks = 0 } };
        var battery = design.AddPart(PartCatalog.Battery4AA)!;
        battery.X = 0;
        battery.Y = 7.5f;
        battery.Z = 0;
        var route = new WireRouter(design).Route((-45, 5, 0), (-1, 0, 0), 4, (45, 5, 0), (1, 0, 0), 4)!;
        Assert.NotNull(route);
        AssertClear(design, route, "over the holder", guard: 4);
        Assert.Contains(route, p => p.y > 15); // over the top of the 15 mm holder, or round it
    }

    [Fact]
    public void RoutingTheKitIsQuick()
    {
        var design = DesignPresets.ObstacleAvoiderKit();
        double best = double.MaxValue;
        for (int run = 0; run < 5; run++)
        {
            var watch = Stopwatch.StartNew();
            var router = new WireRouter(design);
            foreach (var wire in design.Wires) router.Route(design, wire);
            double ms = watch.Elapsed.TotalMilliseconds;
            best = Math.Min(best, ms);
            output.WriteLine($"run {run}: {ms:F1} ms, {router.Expansions} expansions, {router.Evaluations} evaluations");
        }
        Assert.True(best < 60, $"the kit's 16 wires took {best:F1} ms at best");
    }

    [Fact]
    public void TheSameDesignGivesTheSameKeyAndAMovedPartANewOne()
    {
        var design = DesignPresets.ObstacleAvoiderKit();
        string key = new WireRouter(design).Key;
        Assert.Equal(key, new WireRouter(design.Clone()).Key);
        design.Find("uno1")!.X += 1;
        Assert.NotEqual(key, new WireRouter(design).Key);
    }
}
