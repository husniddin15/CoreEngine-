using System;
using System.Collections.Generic;
using System.IO;
using CoreEngine.Sim.Components;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    public enum BodyKind { TwoLayerPlate, HoledPlate, EmptyPlate }

    /// <summary>
    /// One robot in the Garage (ADR-0009, docs/04 §6 Project): body, finishes, sketch and firmware, and the
    /// state that travels to the arena and back (battery charge, motor winding temperatures and damage).
    /// Prototype model: a robot either carries the obstacle-avoider electronics or is an empty plate.
    /// </summary>
    [Serializable]
    public sealed class RobotProject
    {
        public string Name = "Robot";
        public BodyKind Body = BodyKind.TwoLayerPlate;
        public bool Electronics = true;
        public string SketchFile = "ObstacleAvoider.ino";
        public string SketchText = "";    // the player's edited sketch; empty means the file in StreamingAssets
        public string UploadedText = "";  // the source of the loaded firmware; empty means the file
        public string FirmwarePath = "";  // a hex uploaded from the Garage; empty means the golden hex
        public int ProgramBytes = 2954;
        public string BodyFinish = "blue-acrylic";
        public string WheelFinish = "yellow-hubs";
        [NonSerialized] public string TriedBodyFinish = "";   // pack finishes being tried: shown everywhere, never saved
        [NonSerialized] public string TriedWheelFinish = "";
        public double BatteryCharge = 1;
        public double LeftMotorC = MotorWinding.AmbientC, LeftMotorPeakC = MotorWinding.AmbientC;
        public double RightMotorC = MotorWinding.AmbientC, RightMotorPeakC = MotorWinding.AmbientC;
        public bool LeftMotorBurnt, RightMotorBurnt;

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
        public bool HasSketch => Electronics && SketchFile.Length > 0;
        public bool CodeNotUploaded => SketchText.Length > 0 && SketchText != UploadedText;

        public string FirmwareFullPath => FirmwarePath.Length > 0 && File.Exists(FirmwarePath)
            ? FirmwarePath
            : Path.Combine(Application.streamingAssetsPath, "Firmware", "ObstacleAvoider.hex");

        /// <summary>Real product names; they are not translated (docs/10 §5).</summary>
        public IReadOnlyList<string> PartNames => Electronics
            ? new[] { "Arduino Uno R3", "L298N", "HC-SR04", "TT motor 1:48 ×2", "Wheel 65 mm ×2", "Caster ball", "Battery holder 4×AA", "Chassis plates ×2", "Jumper wires ×6" }
            : new[] { "Chassis plate" };

        public int PartCount => Electronics ? 12 : 1;

        /// <summary>The physics mass of the spike robot, or one 120 × 160 × 3 mm acrylic plate (1.19 g/cm³).</summary>
        public double MassKg => Electronics ? 0.96 : 0.069;

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
                }
                catch (Exception e)
                {
                    Debug.LogWarning("GarageState: could not read " + SavePath + ": " + e.Message);
                }
            }
            if (Robots.Count == 0)
            {
                Robots.Add(new RobotProject { Name = "Obstacle avoider" });
                Robots.Add(new RobotProject { Name = "Holed chassis", Body = BodyKind.HoledPlate, BodyFinish = "orange-pla", WheelFinish = "black-hubs" });
            }
        }

        public static RobotProject NewRobot()
        {
            var robot = new RobotProject
            {
                Name = "Robot " + (Robots.Count + 1),
                Body = BodyKind.EmptyPlate,
                Electronics = false,
                SketchFile = "",
                ProgramBytes = 0,
                BodyFinish = "white-pla",
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
                var file = new SaveFile { Robots = new List<RobotProject>(Robots), Selected = Selected, Arena = Arena };
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
            public List<RobotProject> Robots = new List<RobotProject>();
            public int Selected;
            public int Arena;
        }
    }
}
