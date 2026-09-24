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

        /// <summary>A new robot: nothing yet. The player builds the body from shapes and places every part (docs/03 §3.1).</summary>
        public static RobotDesign Empty() => new RobotDesign { Body = new BodyDesign { Decks = 0 } };
    }
}
