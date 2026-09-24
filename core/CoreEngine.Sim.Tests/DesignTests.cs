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
    public void NetsJoinThroughSharedPins()
    {
        var d = DesignPresets.ObstacleAvoiderKit();
        var net = Netlist.Build(d);
        Assert.True(net.Connected("sonar1", "VCC", "uno1", "5V"));      // both on the L298N +5V terminal
        Assert.True(net.Connected("uno1", "GND.3", "battery1", "-"));    // Uno GND pins are one net
        Assert.False(net.Connected("uno1", "D5", "uno1", "D6"));
    }

    [Fact]
    public void AddingPartsFillsSlotsAndRespectsLimits()
    {
        var d = DesignPresets.EmptyChassis();
        Assert.Equal("left", d.AddPart(PartCatalog.TtMotor)!.Slot);
        Assert.Equal("right", d.AddPart(PartCatalog.TtMotor)!.Slot);
        Assert.Null(d.AddPart(PartCatalog.TtMotor));
        var uno = d.AddPart(PartCatalog.Uno)!;
        var driver = d.AddPart(PartCatalog.L298N)!;
        Assert.False(DesignGeometry.OverlapsDeckPart(d, PartCatalog.Get(PartCatalog.L298N)!, driver.X, driver.Z, 0, driver.Id));
        d.AddWire(uno.Id, "D5", driver.Id, "IN1", "yellow");
        Assert.Null(d.AddWire(driver.Id, "IN1", uno.Id, "D5", "green")); // the same wire again
        d.RemovePart(uno.Id);
        Assert.Empty(d.Wires);
    }

    [Fact]
    public void PinPositionsTurnWithThePart()
    {
        var d = DesignPresets.EmptyChassis();
        var driver = d.AddPart(PartCatalog.L298N)!;
        driver.X = 0;
        driver.Z = 0;
        var before = DesignGeometry.PinPosition(d, driver.Id, "IN1")!.Value;
        driver.Rotation = 90;
        var after = DesignGeometry.PinPosition(d, driver.Id, "IN1")!.Value;
        // Unity's +90° about y maps local +z (the header side, z = 19) to +x.
        Assert.Equal(19, after.x, 3);
        Assert.Equal(before.x, -after.z, 3);
        Assert.Equal(before.y, after.y, 3);
    }

    [Fact]
    public void WiresLeaveTerminalsSidewaysAndTurnWithThePart()
    {
        var d = DesignPresets.EmptyChassis();
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
        // Plates 2 × 120 × 160 × 3 mm acrylic (136 g), Uno 25 g, L298N 26 g, HC-SR04 8.5 g, TT motors 2 × 30.6 g,
        // wheels 2 × 30 g, holder 20 g with 4 × 23 g cells, caster 15 g: about 444 g.
        Assert.Equal(0.4436, DesignPresets.ObstacleAvoiderKit().MassKg(), 3);
    }

    // The golden sketch drives forward with IN1 and IN3 high (D5 and D7).
    static readonly Func<string, bool> Forward = pin => pin == "D5" || pin == "D7";

    static double WheelDrive(RobotDesign d, string slot, Func<string, bool> pins) =>
        DriveMap.MotorVolts(Analyse(d), slot, pins, new Components.L298NModel { SupplyVolts = 6 }) * DriveMap.MountSign(slot);

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
        Assert.InRange(com.z, DesignGeometry.AxleZ(d.Body), DesignGeometry.CasterCentre(d.Body).z);
        Assert.InRange(com.x, -10, 10);
        Assert.InRange(com.y, -30, 15);
    }

    [Fact]
    public void WheelsSitJustOutsideARoundBody()
    {
        var body = new BodyDesign { Shape = BodyShape.Round, WidthMm = 200 };
        var wheel = DesignGeometry.WheelCentre(body, "right");
        var motor = DesignGeometry.MotorCentre(body, "right");
        float edge = DesignGeometry.SideHalfWidth(body, wheel.z);
        Assert.InRange(wheel.x - 13, edge, edge + 10);      // the wheel's inner face clears the plate
        Assert.True(motor.x + 9.5f <= edge);                // the gearbox stays under the plate
    }

    [Fact]
    public void RoundDeckKeepsPartsOnTheDisc()
    {
        var body = new BodyDesign { Shape = BodyShape.Round, WidthMm = 140 };
        var uno = PartCatalog.Get(PartCatalog.Uno)!;
        var (x, z) = DesignGeometry.ClampToDeck(body, uno, 60, 60, 0);
        float far = (float)Math.Sqrt(Math.Pow(Math.Abs(x) + uno.SizeX / 2, 2) + Math.Pow(Math.Abs(z) + uno.SizeZ / 2, 2));
        Assert.True(far <= 70.01f, $"corner at {far} mm");
        Assert.True(x > 0 && z > 0);
    }

    [Fact]
    public void OnOneDeckTheBatteryHolderTakesDeckRoom()
    {
        var d = DesignPresets.EmptyChassis();
        d.Body.Decks = 1;
        d.AddPart(PartCatalog.Battery4AA);
        var battery = DesignGeometry.Place(d, d.Parts[0]);
        var driver = PartCatalog.Get(PartCatalog.L298N)!;
        Assert.True(DesignGeometry.OverlapsDeckPart(d, driver, battery.x, battery.z, 0, null));
        var added = d.AddPart(PartCatalog.L298N)!;
        Assert.False(DesignGeometry.OverlapsDeckPart(d, driver, added.X, added.Z, 0, added.Id));
    }
}
