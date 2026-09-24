using System;
using System.Collections.Generic;
using System.IO;
using CoreEngine.Sim.Components;
using CoreEngine.Sim.Design;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// One robot in the Garage (ADR-0009, docs/04 §6 Project): body, finishes, sketch and firmware, and the
    /// state that travels to the arena and back (battery charge, motor winding temperatures and damage).
    /// What the robot is made of is its <see cref="RobotDesign"/> (body, parts, wires), edited in the
    /// Garage's Build, Wire and Body modes and turned into the arena robot and its circuit.
    /// </summary>
    [Serializable]
    public sealed class RobotProject
    {
        public string Name = "Robot";
        public string Id = Guid.NewGuid().ToString("N"); // names the folder of the robot's imported models
        public RobotDesign Design = DesignPresets.ObstacleAvoiderKit();
        public string SketchFile = "ObstacleAvoider.ino";
        public string SketchText = "";    // the player's edited sketch; empty means the file in StreamingAssets
        public string UploadedText = "";  // the source of the loaded firmware; empty means the file
        public string FirmwarePath = "";  // a hex uploaded from the Garage; empty means the golden hex
        public int ProgramBytes = 2954;
        public string BodyFinish = Finishes.AsBuilt; // each shape in its own material; a paint finish covers them all
        public string WheelFinish = "yellow-hubs";
        [NonSerialized] public string TriedBodyFinish = "";   // pack finishes being tried: shown everywhere, never saved
        [NonSerialized] public string TriedWheelFinish = "";
        public double BatteryCharge = 1;
        public double LeftMotorC = MotorWinding.AmbientC, LeftMotorPeakC = MotorWinding.AmbientC;
        public double RightMotorC = MotorWinding.AmbientC, RightMotorPeakC = MotorWinding.AmbientC;
        public bool LeftMotorBurnt, RightMotorBurnt;
        public bool BoardBurnt, SonarBurnt;  // killed by overvoltage in the arena (F7, F26); replaced in Check & repair

        [NonSerialized] BatteryPack? battery;
        [NonSerialized] MotorWinding? leftMotor, rightMotor;

        public BatteryPack Battery
        {
            get
            {
                if (battery == null)
                {
                    battery = new BatteryPack(4);
                    battery.Restore(BatteryCharge);
                }
                return battery;
            }
        }

        public MotorWinding LeftMotor => leftMotor ??= Winding(LeftMotorC, LeftMotorPeakC, LeftMotorBurnt);
        public MotorWinding RightMotor => rightMotor ??= Winding(RightMotorC, RightMotorPeakC, RightMotorBurnt);

        static MotorWinding Winding(double temperatureC, double peakC, bool burnt)
        {
            var winding = new MotorWinding();
            winding.Restore(temperatureC, peakC, burnt);
            return winding;
        }

        public string ActiveBodyFinish => TriedBodyFinish.Length > 0 ? TriedBodyFinish : BodyFinish;
        public string ActiveWheelFinish => TriedWheelFinish.Length > 0 ? TriedWheelFinish : WheelFinish;
        public bool IsTrying => TriedBodyFinish.Length > 0 || TriedWheelFinish.Length > 0;
        /// <summary>True when the robot has a board to run code on.</summary>
        public bool Electronics => Design.Count(PartCatalog.Uno) > 0;
        public bool HasSketch => Electronics && SketchFile.Length > 0;
        public bool CodeNotUploaded => SketchText.Length > 0 && SketchText != UploadedText && !RunsFactoryBlink;

        public const string GoldenSketch = "ObstacleAvoider.ino";

        /// <summary>The uploaded hex; else the golden one for the kit's sketch; else Blink, which a new Uno runs from the factory.</summary>
        public string FirmwareFullPath => FirmwarePath.Length > 0 && File.Exists(FirmwarePath)
            ? FirmwarePath
            : Path.Combine(Application.streamingAssetsPath, "Firmware", SketchFile == GoldenSketch ? "ObstacleAvoider.hex" : "Blink.hex");

        /// <summary>True when the board still runs its factory Blink: nothing was uploaded to it yet.</summary>
        public bool RunsFactoryBlink => !(FirmwarePath.Length > 0 && File.Exists(FirmwarePath)) && SketchFile != GoldenSketch;

        /// <summary>Gives a robot with a board its first, empty sketch (the Arduino IDE's new-sketch text).</summary>
        public void EnsureSketch()
        {
            if (!Electronics || SketchFile.Length > 0) return;
            SketchFile = "Sketch.ino";
            SketchText = "void setup() {\n  // put your setup code here, to run once:\n\n}\n\nvoid loop() {\n  // put your main code here, to run repeatedly:\n\n}\n";
            UploadedText = "";
            ProgramBytes = 0;
        }

        /// <summary>Real product names; they are not translated (docs/10 §5).</summary>
        public IReadOnlyList<string> PartNames
        {
            get
            {
                var names = new List<string>();
                foreach (var part in Design.Parts) names.Add(PartCatalog.Get(part.Part)?.Name ?? part.Part);
                return names;
            }
        }

        /// <summary>Parts, wheels (one per motor) and the body's shapes and groups.</summary>
        public int PartCount => Design.Parts.Count + Design.Count(PartCatalog.TtMotor) + Design.Body.Members(null).Count;

        public double MassKg => Design.MassKg();

        /// <summary>STL and OBJ files imported into the Body Studio are copied here, so the robot keeps them.</summary>
        public string ImportFolder => Path.Combine(Application.persistentDataPath, "Imports", Id);

        /// <summary>Fills in what older saves lack.</summary>
        public void Upgrade()
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            Design ??= DesignPresets.ObstacleAvoiderKit();
            Design.Body ??= new BodyDesign();
            Design.Body.Features ??= new List<BodyFeature>();
            Design.Parts ??= new List<PartInstance>();
            Design.Wires ??= new List<WireInstance>();
        }

        /// <summary>Copies the live battery and motor state into the serialised fields.</summary>
        public void Sync()
        {
            BatteryCharge = Battery.StateOfCharge;
            LeftMotorC = LeftMotor.TemperatureC;
            LeftMotorPeakC = LeftMotor.PeakC;
            LeftMotorBurnt = LeftMotor.Burnt;
            RightMotorC = RightMotor.TemperatureC;
            RightMotorPeakC = RightMotor.PeakC;
            RightMotorBurnt = RightMotor.Burnt;
        }
    }

    /// <summary>
    /// The player's robots, kept across scene loads and saved to garage.json in the player's data folder.
    /// The prototype starts with two test robots; the shipped game starts empty (docs/13 D16).
    /// </summary>
    public static class GarageState
    {
        public static readonly List<RobotProject> Robots = new List<RobotProject>();
        public static int Selected;
        public static int Arena;
        static bool loaded;
        static bool noSaving;

        public static RobotProject Current => Robots[Mathf.Clamp(Selected, 0, Robots.Count - 1)];

        static string SavePath => Path.Combine(Application.persistentDataPath, "garage.json");

        /// <param name="fresh">Benchmark runs start from the default robots and never save.</param>
        public static void Load(bool fresh)
        {
            if (loaded) return;
            loaded = true;
            noSaving = fresh;
            if (!fresh && File.Exists(SavePath))
            {
                try
                {
                    var file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(SavePath));
                    if (file?.Robots != null) Robots.AddRange(file.Robots);
                    Selected = file?.Selected ?? 0;
                    Arena = file?.Arena ?? 0;
                    if ((file?.Version ?? 0) < 2) RestoreHoledChassis();
                    if ((file?.Version ?? 0) < DesignMigration.Version)
                        foreach (var robot in Robots) if (robot.Design != null) DesignMigration.Upgrade(robot.Design);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("GarageState: could not read " + SavePath + ": " + e.Message);
                }
            }
            foreach (var robot in Robots) robot.Upgrade();
            if (Robots.Count == 0)
            {
                // The test robots, in real materials: tinted acrylic decks, and orange PLA printed ones with walls.
                var kit = DesignPresets.ObstacleAvoiderKit();
                Tint(kit, "#2F6FD8");
                Robots.Add(new RobotProject { Name = "Obstacle avoider", Design = kit });
                var holed = DesignPresets.ObstacleAvoiderKit(new BodyDesign { HoleGrid = true, WallHeightMm = 22, Material = BodyMaterial.Pla });
                Tint(holed, "#F07A1A");
                Robots.Add(new RobotProject { Name = "Holed chassis", Design = holed, WheelFinish = "black-hubs" });
            }
        }

        /// <summary>Colours the plates and walls of a kit (not its aluminium standoffs).</summary>
        static void Tint(RobotDesign design, string colour)
        {
            foreach (var shape in design.Body.Features)
                if (shape.Material != BodyMaterial.Aluminium && shape.Kind != FeatureKind.Group) shape.Colour = colour;
        }

        /// <summary>
        /// Saves from before the design model kept only a body kind, and the first conversion turned the
        /// "Holed chassis" test robot into a plain one: its hole grid and walls come back.
        /// </summary>
        static void RestoreHoledChassis()
        {
            foreach (var robot in Robots)
            {
                var body = robot.Design?.Body;
                if (robot.Name != "Holed chassis" || body == null || body.HoleGrid || body.WallHeightMm > 0) continue;
                body.HoleGrid = true;
                body.WallHeightMm = 22;
            }
        }

        public static RobotProject NewRobot()
        {
            var robot = new RobotProject
            {
                Name = "Robot " + (Robots.Count + 1),
                Design = DesignPresets.Empty(),
                SketchFile = "",
                ProgramBytes = 0,
                BodyFinish = Finishes.AsBuilt,
            };
            Robots.Add(robot);
            Selected = Robots.Count - 1;
            Save();
            return robot;
        }

        public static void Save()
        {
            foreach (var robot in Robots) robot.Sync();
            if (noSaving) return;
            try
            {
                var file = new SaveFile { Version = DesignMigration.Version, Robots = new List<RobotProject>(Robots), Selected = Selected, Arena = Arena };
                File.WriteAllText(SavePath, JsonUtility.ToJson(file, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("GarageState: could not save " + SavePath + ": " + e.Message);
            }
        }

        [Serializable]
        sealed class SaveFile
        {
            public int Version; // 2: the design model; 3: free shapes and parts (both 2026-09-24); files without it read as 0
            public List<RobotProject> Robots = new List<RobotProject>();
            public int Selected;
            public int Arena;
        }
    }
}
