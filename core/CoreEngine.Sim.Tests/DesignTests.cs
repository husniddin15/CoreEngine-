using CoreEngine.Sim.Design;

namespace CoreEngine.Sim.Tests;

public class DesignTests
{
    static RobotCircuit Analyse(RobotDesign design) => CircuitAnalysis.Analyse(design);

    static void Rewire(RobotDesign d, string fromPart, string fromPin, string newToPart, string newToPin)
    {
        var wire = d.Wires.Find(w => (w.FromPart == fromPart && w.FromPin == fromPin) || (w.ToPart == fromPart && w.ToPin == fromPin))!;
        d.Wires.Remove(wire);
        d.AddWire(fromPart, fromPin, newToPart, newToPin, wire.Color);
    }

    [Fact]
    public void KitIsFullyWiredWithoutProblems()
    {
        var circuit = Analyse(DesignPresets.ObstacleAvoiderKit());
        Assert.False(circuit.HasProblems, string.Join(", ", circuit.Warnings));
        Assert.True(circuit.BoardPowered);
        Assert.True(circuit.DriverPowered);
        Assert.True(circuit.SonarPowered);
        Assert.Equal(new[] { "D5", "D6", "D7", "D8", null, null }, circuit.DriverInputs);
        Assert.Equal("D9", circuit.Trig);
        Assert.Equal("D10", circuit.Echo);
        var left = circuit.Motor("left")!;
        var right = circuit.Motor("right")!;
        Assert.Equal((0, 1), (left.Channel, left.Polarity));
        Assert.Equal((1, -1), (right.Channel, right.Polarity)); // mirrored motor, swapped leads
        Assert.Contains(circuit.Warnings, w => w.Code == "driver5vLow" && w.Info);
    }

    [Fact]
    public void WithoutACommonGroundTheBoardHasNoPower()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        d.Wires.RemoveAll(w => w.ToPart == "uno1" && w.ToPin == "GND.2");
        d.Wires.RemoveAll(w => w.FromPart == "sonar1" && w.FromPin == "GND");
        var circuit = Analyse(d);
        Assert.False(circuit.BoardPowered);
        Assert.Contains(circuit.Warnings, w => w.Code == "noGround");
    }

    [Fact]
    public void FiveVoltsOnVinIsNamedAsTheMistake()
    {
        // The owner's first own robot (2026-09-25): the L298N's +5V on the Uno's VIN, which needs 7-12 V.
        var d = DesignPresets.ObstacleAvoiderKit();
        d.Wires.RemoveAll(w => w.FromPart == "driver1" && w.FromPin == "+5V" && w.ToPart == "uno1" && w.ToPin == "5V");
        d.AddWire("driver1", "+5V", "uno1", "VIN", "red");
        var circuit = Analyse(d);
        Assert.False(circuit.BoardPowered);
        Assert.Contains(circuit.Warnings, w => w.Code == "fiveVoltOnVin" && !w.Info);
        Assert.DoesNotContain(circuit.Warnings, w => w.Code == "boardUnpowered"); // the specific finding, not the general one
    }

    [Fact]
    public void BatteryOnTheFiveVoltPinDamagesTheBoard()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        d.AddWire("battery1", "+", "uno1", "5V", "red");
        var circuit = Analyse(d);
        Assert.True(circuit.BoardDamaged);
        Assert.False(circuit.BoardPowered);
        Assert.Contains(circuit.Warnings, w => w.Code == "board5vOvervoltage");
    }

    [Fact]
    public void MotorOnAnArduinoPinIsReported()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        Rewire(d, "motor1", "M+", "uno1", "D3");
        var circuit = Analyse(d);
        Assert.Null(circuit.Motor("left"));
        Assert.Contains(circuit.Warnings, w => w.Code == "motorOnGpio" && w.Args[0] == "left" && w.Args[1] == "D3");
    }

    [Fact]
    public void SwappedSensorWiresAreFollowed()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        Rewire(d, "sonar1", "TRIG", "uno1", "D10");
        Rewire(d, "sonar1", "ECHO", "uno1", "D9");
        var circuit = Analyse(d);
        Assert.Equal("D10", circuit.Trig);
        Assert.Equal("D9", circuit.Echo);
    }

    [Fact]
    public void BatteryShortIsReported()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        d.AddWire("battery1", "+", "battery1", "-", "red");
        var circuit = Analyse(d);
        Assert.Contains(circuit.Warnings, w => w.Code == "shortCircuit");
        Assert.False(circuit.DriverPowered);
    }

    [Fact]
    public void AnLedModuleLightsFromItsPin()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        var led = d.AddPart(PartCatalog.Led)!;
        var circuit = Analyse(d);
        Assert.Contains(circuit.Warnings, w => w.Code == "ledPin" && w.Args[0] == led.Id);
        Assert.Contains(circuit.Warnings, w => w.Code == "ledNoGround");
        d.AddWire("uno1", "D13", led.Id, "S", "yellow");
        d.AddWire("uno1", "GND.1", led.Id, "GND", "black");
        circuit = Analyse(d);
        Assert.False(circuit.HasProblems, string.Join(", ", circuit.Warnings));
        var link = Assert.Single(circuit.Leds);
        Assert.Equal(("D13", true), (link.Pin, link.Live));
    }

    [Fact]
    public void AServoNeedsPowerGroundAndItsSignal()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        var servo = d.AddPart(PartCatalog.Servo)!;
        d.AddWire("uno1", "D3", servo.Id, "SIG", "orange");
        var circuit = Analyse(d);
        Assert.Contains(circuit.Warnings, w => w.Code == "servoUnpowered" && w.Args[0] == servo.Id);
        d.AddWire(servo.Id, "V+", "driver1", "+12V", "red"); // the 4×AA pack, about 6 V: an SG90 takes up to 6 V
        d.AddWire(servo.Id, "GND", "uno1", "GND.1", "brown");
        circuit = Analyse(d);
        Assert.False(circuit.HasProblems, string.Join(", ", circuit.Warnings));
        var link = Assert.Single(circuit.Servos);
        Assert.Equal(("D3", true), (link.Pin, link.Powered));
    }

    [Fact]
    public void NetsJoinThroughSharedPins()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        var net = Netlist.Build(d);
        Assert.True(net.Connected("sonar1", "VCC", "uno1", "5V"));      // both on the L298N +5V terminal
        Assert.True(net.Connected("uno1", "GND.3", "battery1", "-"));    // Uno GND pins are one net
        Assert.False(net.Connected("uno1", "D5", "uno1", "D6"));
    }

    [Fact]
    public void NewPartsStandOnTheGroundBesideEachOtherAndRespectLimits()
    {
        var d = DesignPresets.Empty();
        Assert.Empty(d.Body.Features); // a new robot has no body until the player builds one
        var motor = d.AddPart(PartCatalog.TtMotor)!;
        Assert.NotNull(d.AddPart(PartCatalog.TtMotor));
        Assert.Null(d.AddPart(PartCatalog.TtMotor)); // an L298N drives two
        var uno = d.AddPart(PartCatalog.Uno)!;
        var driver = d.AddPart(PartCatalog.L298N)!;
        foreach (var part in d.Parts) Assert.False(DesignGeometry.Overlaps(d, part), part.Id);
        Assert.Equal(0, DesignGeometry.WheelCentre(motor).y - DesignGeometry.WheelRadius, 3); // the wheel on the ground
        Assert.Equal(0, uno.Y);
        Assert.Equal(0, DesignGeometry.LowestPoint(d), 3);
        d.AddWire(uno.Id, "D5", driver.Id, "IN1", "yellow");
        Assert.Null(d.AddWire(driver.Id, "IN1", uno.Id, "D5", "green")); // the same wire again
        d.RemovePart(uno.Id);
        Assert.Empty(d.Wires);
    }

    [Fact]
    public void TurnsFollowUnitysEulerAngles()
    {
        // Quaternion.Euler(0, 90, 0) turns +x to -z; Euler(90, 0, 0) turns up to forward; z turns first, then x, then y.
        Assert.Equal((0f, 0f, -1f), Round(Rot3.Euler(0, 90, 0).Apply(1, 0, 0)));
        Assert.Equal((0f, 0f, 1f), Round(Rot3.Euler(90, 0, 0).Apply(0, 1, 0)));
        Assert.Equal((0f, 0f, 1f), Round(Rot3.Euler(90, 90, 0).Apply(1, 0, 0) is var v ? (v.x, v.y, -v.z) : default)); // y after x
        var random = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            float x = random.Next(-89, 90), y = random.Next(-179, 181), z = random.Next(-179, 181);
            var back = Rot3.Euler(x, y, z).ToEuler();
            Assert.Equal((x, y, z), (back.x, back.y, back.z));
        }
        var turn = Rot3.AngleAxis(90, (0, 1, 0));
        Assert.Equal(Round(Rot3.Euler(0, 90, 0).Apply(1, 2, 3)), Round(turn.Apply(1, 2, 3)));
        Assert.Equal((1f, 2f, 3f), Round((turn.Inverse() * turn).Apply(1, 2, 3)));
    }

    static (float x, float y, float z) Round((float x, float y, float z) v) =>
        ((float)Math.Round(v.x, 4) + 0f, (float)Math.Round(v.y, 4) + 0f, (float)Math.Round(v.z, 4) + 0f);

    [Fact]
    public void PartsTurnAboutEveryAxis()
    {
        var d = DesignPresets.Empty();
        var motor = d.AddPart(PartCatalog.TtMotor)!;
        (motor.X, motor.Y, motor.Z) = (0, 40, 0);
        Assert.Equal("left", DesignGeometry.SideOf(motor));
        Assert.Equal(1, DesignGeometry.ForwardSign(motor));
        motor.Rotation = 180; // the kit's right motor: the same motor turned round
        Assert.Equal("right", DesignGeometry.SideOf(motor));
        Assert.Equal(-1, DesignGeometry.ForwardSign(motor));
        motor.Rotation = 0;
        motor.RotZ = 90; // shaft pointing up: the wheel lies flat and cannot drive
        Assert.Equal(0, DesignGeometry.ForwardSign(motor));
        var pin = DesignGeometry.PinPosition(d, motor.Id, "M+")!.Value;
        Assert.Equal((-4f, 40f, 45.5f), Round(pin)); // local (0, 4, 45.5) turned 90° about z
    }

    [Fact]
    public void AMotorsWheelGoesOnEitherEndOfItsShaft()
    {
        // The owner (2026-09-25): the second motor on the other side should make a symmetric robot. A TT motor's
        // white shaft comes out on both sides: with its wheel on the other end, an unturned motor is the mirror of
        // the left one, and + on M+ still turns its shaft, so its wheel, the same way.
        var d = DesignPresets.Empty();
        var left = d.AddPart(PartCatalog.TtMotor)!;
        (left.X, left.Y, left.Z) = (-50, 24, -30);
        var right = d.AddPart(PartCatalog.TtMotor)!;
        (right.X, right.Y, right.Z) = (50, 24, -30);
        right.WheelOtherEnd = true;
        var l = DesignGeometry.WheelCentre(left);
        var r = DesignGeometry.WheelCentre(right);
        Assert.Equal(-77.5f, l.x, 3);
        Assert.Equal(77.5f, r.x, 3);
        Assert.Equal(l.y, r.y, 3);
        Assert.Equal(l.z, r.z, 3);
        Assert.Equal("left", DesignGeometry.SideOf(left));
        Assert.Equal("right", DesignGeometry.SideOf(right));
        Assert.Equal(1, DesignGeometry.ForwardSign(right)); // not turned round: forward like the left one
        var (lmin, lmax) = DesignGeometry.PartBounds(left);
        var (rmin, rmax) = DesignGeometry.PartBounds(right);
        Assert.Equal(-lmax.x, rmin.x, 3); // motor and wheel, each the mirror of the other
        Assert.Equal(-lmin.x, rmax.x, 3);
        Assert.Equal(lmin.z, rmin.z, 3);
        Assert.Equal(lmax.z, rmax.z, 3);
        Assert.True(d.Clone().Find(right.Id)!.WheelOtherEnd);

        // The wire router sees the wheel where it now is, and lays wires again when it moves.
        var router = new WireRouter(d);
        var hit = router.Pick((300, 32.5f, -30), (-1, 0, 0), 1000)!.Value;
        Assert.True(hit.Wheel);
        Assert.Equal(right.Id, hit.Owner);
        Assert.Equal(90.5f, hit.Point.x, 1);
        right.WheelOtherEnd = false;
        Assert.NotEqual(router.Key, new WireRouter(d).Key);
    }

    [Fact]
    public void OldSavesKeepTheirRobot()
    {
        // The version-2 kit: plates 120 × 160 mm, motors on fixed mounts at the axle, 50 mm in front of the back.
        var d = DesignPresets.ObstacleAvoiderKit();
        Assert.Equal(0, d.Body.Decks); // converted, and not converted twice
        DesignMigration.Upgrade(d);
        var plates = d.Body.Features.FindAll(f => f.Kind == FeatureKind.Plate);
        Assert.Equal(2, plates.Count);
        Assert.Equal((36.5f, 63.5f), (plates[0].Y, plates[1].Y)); // 3 mm acrylic at 35 and 62 mm above the floor
        Assert.Equal(4, d.Body.Features.FindAll(f => f.Kind == FeatureKind.Cylinder).Count); // the brass standoffs
        var left = DesignGeometry.WheelCentre(d.Find("motor1")!);
        var right = DesignGeometry.WheelCentre(d.Find("motor2")!);
        Assert.Equal((-77.5f, 32.5f, -30f), Round(left));
        Assert.Equal((77.5f, 32.5f, -30f), Round(right));
        Assert.Equal(("left", "right"), (DesignGeometry.SideOf(d.Find("motor1")!), DesignGeometry.SideOf(d.Find("motor2")!)));
        Assert.Equal(0, DesignGeometry.LowestPoint(d), 3); // wheels and caster on the floor
        Assert.Equal((0f, 10f, 65f), Round(DesignGeometry.CasterBall(d.Find("caster1")!)));
        var uno = d.Find("uno1")!;
        Assert.Equal((-28f, 65f, -35f, 270f), (uno.X, uno.Y, uno.Z, uno.Rotation)); // on the top deck, as dragged there
    }

    [Fact]
    public void OldHolesStillCutThePlates()
    {
        var old = DesignPresets.ObstacleAvoiderKit();
        var body = new BodyDesign();
        body.Features.Add(new BodyFeature { Id = "f1", Kind = FeatureKind.Cylinder, Hole = true, SizeX = 20, SizeY = 30, SizeZ = 20, Y = 12 });
        var holed = DesignPresets.ObstacleAvoiderKit(body);
        var hole = holed.Body.Feature("f1")!;
        Assert.Equal(62, hole.Y);                            // the frame moved from 50 mm up to the floor
        var group = holed.Body.Parent(hole)!;
        Assert.Equal(3, holed.Body.Shapes(group).Count);      // the hole and both plates; the standoffs stay out
        Assert.True(holed.MassKg() < old.MassKg());
    }

    [Fact]
    public void PinPositionsTurnWithThePart()
    {
        var d = DesignPresets.Empty();
        var driver = d.AddPart(PartCatalog.L298N)!;
        driver.X = 0;
        driver.Z = 0;
        var before = DesignGeometry.PinPosition(d, driver.Id, "IN1")!.Value;
        driver.Rotation = 90;
        var after = DesignGeometry.PinPosition(d, driver.Id, "IN1")!.Value;
        // Unity's +90° about y maps local z to x: the header's row, z = -14.5 at the module's front, goes to x = -14.5.
        Assert.Equal(-14.5f, after.x, 3);
        Assert.Equal(before.x, -after.z, 3);
        Assert.Equal(before.y, after.y, 3);
    }

    [Fact]
    public void WiresLeaveTerminalsSidewaysAndTurnWithThePart()
    {
        var d = DesignPresets.Empty();
        var driver = d.AddPart(PartCatalog.L298N)!;
        Assert.Equal((-1f, 0f, 0f), DesignGeometry.PinExit(d, driver.Id, "OUT1")!.Value);
        Assert.Equal((0f, 1f, 0f), DesignGeometry.PinExit(d, driver.Id, "IN1")!.Value);
        driver.Rotation = 90; // OUT1 then faces +z: Unity turns (-1, 0) by +90° about y to (0, 1)
        var exit = DesignGeometry.PinExit(d, driver.Id, "OUT1")!.Value;
        Assert.Equal(0, exit.x, 3);
        Assert.Equal(1, exit.z, 3);
        Assert.Equal("D9", PartCatalog.Get(PartCatalog.Uno)!.Pin("D9")!.ShortLabel);
    }

    [Fact]
    public void HeaderPinsTakeOneJumperAndTerminalsTwo()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        Assert.False(d.HasRoomOn("uno1", "D5"));       // the IN1 jumper sits on it
        Assert.True(d.HasRoomOn("uno1", "D4"));
        Assert.False(d.HasRoomOn("driver1", "+5V"));   // the Uno's and the sensor's supply wires
        Assert.True(d.HasRoomOn("driver1", "+12V"));   // room for a second wire, for example to VIN
        Assert.False(d.HasRoomOn("battery1", "+"));    // a lead goes to one place
    }

    [Fact]
    public void KitWiresFitStandardJumpers()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        foreach (var wire in d.Wires) Assert.InRange(DesignGeometry.WireLength(d, wire), 5, 200);
    }

    [Theory]
    [InlineData("D0", 'D', 0)]
    [InlineData("D7", 'D', 7)]
    [InlineData("D9", 'B', 1)]
    [InlineData("D13", 'B', 5)]
    [InlineData("A3", 'C', 3)]
    public void UnoPinsMapToPorts(string pin, char port, int bit)
    {
        Assert.Equal((port, bit), UnoPins.PortOf(pin));
    }

    [Fact]
    public void PowerPinsAreNotPortPins()
    {
        Assert.Null(UnoPins.PortOf("5V"));
        Assert.Null(UnoPins.PortOf("GND.1"));
        Assert.Null(UnoPins.PortOf("D14"));
    }

    [Fact]
    public void StlHasOneFacetPerTriangle()
    {
        // A tetrahedron: 4 vertices, 4 triangles.
        float[] positions = { 0, 0, 0, 10, 0, 0, 0, 10, 0, 0, 0, 10 };
        int[] triangles = { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
        using var stream = new MemoryStream();
        StlWriter.Write(stream, positions, triangles, "test");
        Assert.Equal(84 + 50 * 4, stream.Length);
        Assert.Equal(4u, BitConverter.ToUInt32(stream.ToArray(), 80));
    }

    [Fact]
    public void KitMassAddsUpFromTheCatalogue()
    {
        // Plates 2 × 120 × 160 × 3 mm acrylic with 12 mm corners (135 g), four 5 × 24 mm standoffs (5 g), Uno 25 g,
        // L298N 26 g, HC-SR04 8.5 g, TT motors 2 × 30.6 g, wheels 2 × 30 g, holder 20 g with 4 × 23 g cells,
        // caster 15 g: about 448 g.
        Assert.Equal(0.448, DesignPresets.ObstacleAvoiderKit().MassKg(), 2);
    }

    // The golden sketch drives forward with IN1 and IN3 high (D5 and D7).
    static readonly Func<string, bool> Forward = pin => pin == "D5" || pin == "D7";

    /// <summary>The voltage on a side's motor times the way it rolls the robot: positive drives forward.</summary>
    static double WheelDrive(RobotDesign d, string slot, Func<string, bool> pins)
    {
        var motor = d.Parts.Find(p => p.Part == PartCatalog.TtMotor && DesignGeometry.SideOf(p) == slot)!;
        return DriveMap.MotorVolts(Analyse(d), motor.Id, pins, new Components.L298NModel { SupplyVolts = 6 }) * DesignGeometry.ForwardSign(motor);
    }

    [Fact]
    public void KitDrivesBothWheelsForward()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        Assert.True(WheelDrive(d, "left", Forward) > 0);
        Assert.True(WheelDrive(d, "right", Forward) > 0);
    }

    [Fact]
    public void MirroredMotorWithUnswappedLeadsTurnsBackwards()
    {
        // Wiring the right motor like the left one (M+ on OUT3) makes the robot spin: the classic kit mistake.
        var d = DesignPresets.ObstacleAvoiderKit();
        Rewire(d, "motor2", "M+", "driver1", "OUT3");
        Rewire(d, "motor2", "M-", "driver1", "OUT4");
        Assert.False(Analyse(d).HasProblems);
        Assert.True(WheelDrive(d, "left", Forward) > 0);
        Assert.True(WheelDrive(d, "right", Forward) < 0);
    }

    [Fact]
    public void EnableWiredToALowPinLeavesTheMotorOpen()
    {
        // A wire on ENA replaces the jumper: the channel only runs while that pin is high.
        var d = DesignPresets.ObstacleAvoiderKit();
        d.AddWire("uno1", "D3", "driver1", "ENA", "grey");
        Assert.True(double.IsNaN(WheelDrive(d, "left", Forward)));
        Assert.True(WheelDrive(d, "left", pin => Forward(pin) || pin == "D3") > 0);
        Assert.True(WheelDrive(d, "right", Forward) > 0); // ENB keeps its jumper
    }

    [Fact]
    public void UnpoweredDriverDrivesNothing()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        d.Wires.RemoveAll(w => w.ToPin == "+12V");
        Assert.True(double.IsNaN(WheelDrive(d, "left", Forward)));
    }

    [Fact]
    public void KitCentreOfMassLiesBetweenTheWheelsAndTheCaster()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        var com = DesignGeometry.CentreOfMass(d);
        Assert.InRange(com.z, DesignGeometry.WheelCentre(d.Find("motor1")!).z, DesignGeometry.CasterBall(d.Find("caster1")!).z);
        Assert.InRange(com.x, -10, 10);
        Assert.InRange(com.y, 20, 65);
    }

    [Fact]
    public void WheelsSitJustOutsideAnOldRoundBody()
    {
        var d = DesignPresets.ObstacleAvoiderKit(new BodyDesign { Shape = BodyShape.Round, WidthMm = 200 });
        var motor = d.Find("motor2")!;
        var wheel = DesignGeometry.WheelCentre(motor);
        double edge = Math.Sqrt(100 * 100 - wheel.z * wheel.z);
        Assert.InRange(wheel.x - 13, edge, edge + 10);          // the wheel's inner face clears the plate
        Assert.True(motor.X + 9.5f <= edge);                    // the gearbox stays under the plate
        Assert.Equal(100, d.Body.Features.Find(f => f.Kind == FeatureKind.Plate)!.Detail); // a disc: corners of half its width
    }
}
