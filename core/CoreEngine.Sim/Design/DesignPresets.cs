namespace CoreEngine.Sim.Design
{
    /// <summary>
    /// Starting points. The kit is the common 2WD obstacle-avoider build, wired as the golden ObstacleAvoider
    /// sketch expects (IN1-IN4 on D5-D8, TRIG on D9, ECHO on D10). The right TT motor is the left one turned
    /// round, so its leads are swapped (M+ on OUT4) to make both wheels drive forward: a real kit's classic lesson.
    /// </summary>
    public static class DesignPresets
    {
        /// <summary>
        /// The kit on two acrylic decks, described as the version-2 layout did (plate settings, fixed mounts) and
        /// converted, so the example robots are what they always were. <paramref name="plates"/> sets the decks.
        /// </summary>
        public static RobotDesign ObstacleAvoiderKit(BodyDesign? plates = null)
        {
            var d = new RobotDesign { Body = plates ?? new BodyDesign() };
            d.Parts.Add(new PartInstance { Id = "uno1", Part = PartCatalog.Uno, X = -28, Z = -35, Rotation = 270 });
            d.Parts.Add(new PartInstance { Id = "driver1", Part = PartCatalog.L298N, X = 30, Z = 22 });
            d.Parts.Add(new PartInstance { Id = "sonar1", Part = PartCatalog.HcSr04 });
            d.Parts.Add(new PartInstance { Id = "motor1", Part = PartCatalog.TtMotor, Slot = "left" });
            d.Parts.Add(new PartInstance { Id = "motor2", Part = PartCatalog.TtMotor, Slot = "right" });
            d.Parts.Add(new PartInstance { Id = "battery1", Part = PartCatalog.Battery4AA });
            d.Parts.Add(new PartInstance { Id = "caster1", Part = PartCatalog.Caster });

            d.AddWire("battery1", "+", "driver1", "+12V", "red");
            d.AddWire("battery1", "-", "driver1", "GND", "black");
            d.AddWire("driver1", "+5V", "uno1", "5V", "red");
            d.AddWire("driver1", "GND", "uno1", "GND.2", "black");
            d.AddWire("uno1", "D5", "driver1", "IN1", "yellow");
            d.AddWire("uno1", "D6", "driver1", "IN2", "green");
            d.AddWire("uno1", "D7", "driver1", "IN3", "blue");
            d.AddWire("uno1", "D8", "driver1", "IN4", "purple");
            d.AddWire("sonar1", "VCC", "driver1", "+5V", "red");
            d.AddWire("sonar1", "GND", "uno1", "GND.3", "black");
            d.AddWire("uno1", "D9", "sonar1", "TRIG", "orange");
            d.AddWire("uno1", "D10", "sonar1", "ECHO", "white");
            d.AddWire("motor1", "M+", "driver1", "OUT1", "red");
            d.AddWire("motor1", "M-", "driver1", "OUT2", "black");
            d.AddWire("motor2", "M+", "driver1", "OUT4", "red");
            d.AddWire("motor2", "M-", "driver1", "OUT3", "black");
            DesignMigration.Upgrade(d);
            return d;
        }

        /// <summary>
        /// The owner's first own robot (2026-09-25), with its Uno on the L298N's 5 V as it should be: one plate,
        /// the wheels at the Uno's end and the caster at the L298N's, and ENA and ENB on D5 and D10 so the golden
        /// PwmMotors sketch sets both speeds with analogWrite. For tests and the benchmark, not an example robot
        /// (docs/13 D16). Its motors' leads are the other way round from the kit's, so the kit's forward (IN1
        /// and IN3 high) drives it wheels first, toward −z.
        /// </summary>
        public static RobotDesign PwmTwoWheeler()
        {
            var d = new RobotDesign { Body = new BodyDesign { Decks = 0 } };
            // As the owner built it, raised 8.5 mm since the TT motor's shaft is halfway up its gearbox (version 4).
            d.Body.AddFeature(new BodyFeature { Kind = FeatureKind.Box, X = -15, Y = 46, Z = 42.5f, SizeX = 85, SizeY = 5, SizeZ = 185, Colour = "#2F6FD8" });
            void Part(string id, string part, float x, float y, float z, float turn) =>
                d.Parts.Add(new PartInstance { Id = id, Part = part, X = x, Y = y, Z = z, Rotation = turn });
            Part("caster1", PartCatalog.Caster, -15, 11, 115, 0);
            Part("uno1", PartCatalog.Uno, -15, 48.6f, -20, 0);
            Part("driver1", PartCatalog.L298N, 5, 48.6f, 95, 90);
            Part("battery1", PartCatalog.Battery4AA, -15, 56.1f, 40, -90);
            Part("motor1", PartCatalog.TtMotor, -55, 32.5f, -20, 0);
            Part("motor2", PartCatalog.TtMotor, 20, 32.5f, -20, 180);
            d.AddWire("battery1", "+", "driver1", "+12V", "red");
            d.AddWire("battery1", "-", "driver1", "GND", "black");
            d.AddWire("driver1", "OUT3", "motor2", "M+", "red");
            d.AddWire("driver1", "OUT4", "motor2", "M-", "black");
            d.AddWire("driver1", "OUT2", "motor1", "M+", "red");
            d.AddWire("driver1", "OUT1", "motor1", "M-", "black");
            d.AddWire("driver1", "+5V", "uno1", "5V", "red");
            d.AddWire("uno1", "GND.2", "driver1", "GND", "black");
            d.AddWire("driver1", "ENA", "uno1", "D5", "yellow");
            d.AddWire("driver1", "IN1", "uno1", "D6", "green");
            d.AddWire("driver1", "IN2", "uno1", "D7", "blue");
            d.AddWire("driver1", "IN3", "uno1", "D8", "orange");
            d.AddWire("driver1", "IN4", "uno1", "D9", "white");
            d.AddWire("driver1", "ENB", "uno1", "D10", "purple");
            return d;
        }

        /// <summary>A new robot: nothing yet. The player builds the body from shapes and places every part (docs/03 §3.1).</summary>
        public static RobotDesign Empty() => new RobotDesign { Body = new BodyDesign { Decks = 0 } };
    }
}
