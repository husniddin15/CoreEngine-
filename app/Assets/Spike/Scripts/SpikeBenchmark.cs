using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using CoreEngine.Sim.Avr;
using CoreEngine.Spike.Garage;
using UnityEngine;

namespace CoreEngine.Spike
{
    /// <summary>
    /// The arena part of a <c>-spikeBench &lt;report file&gt;</c> run (Phases 0.4–0.6): the emulator alone,
    /// frame rate and emulator cost while the robot drives, mesh booleans and the UI panels. When the run
    /// started in the Garage (the first scene), it then returns there; otherwise it writes the report and quits.
    /// </summary>
    public sealed class SpikeBenchmark : MonoBehaviour
    {
        /// <summary>
        /// A robot whose sketch sets its motors' speed with analogWrite (ENA on D5, ENB on D10, both at 180 of
        /// 255): the board runs, both motors get the averaged voltage, and it drives.
        /// </summary>
        IEnumerator PwmRun(StringBuilder report)
        {
            var spike = GetComponent<RobotSpike>();
            yield return new WaitForSecondsRealtime(1f);
            var start = spike.RobotPosition;
            double left = 0, right = 0, full = 0; // full: the bridge's whole output (supply less its drop) as the battery sags
            int samples = 0;
            float from = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - from < 3f)
            {
                yield return new WaitForFixedUpdate();
                if (!double.IsNaN(spike.LeftVolts)) left += spike.LeftVolts;
                if (!double.IsNaN(spike.RightVolts)) right += spike.RightVolts;
                full += spike.SupplyVolts - 1.8;
                samples++;
            }
            left /= Math.Max(1, samples);
            right /= Math.Max(1, samples);
            full /= Math.Max(1, samples);
            yield return SpikeReport.Capture(SpikeReport.Shot("pwm-robot"));
            report.AppendLine($"PWM robot (the owner's first robot with its Uno on 5V; PwmMotors.ino, analogWrite 180 on ENA and ENB): board {UI.SpikeStrings.Get(spike.BoardStatusKey)}; " +
                              $"motors at {left:F2} V and {right:F2} V on average, {Math.Abs(left) / full * 100:F0} % and {Math.Abs(right) / full * 100:F0} % of the full " +
                              $"{full:F2} V (180/255 = 71 %); moved {Vector3.Distance(start, spike.RobotPosition):F2} m in 3 s; screenshot -pwm-robot");
        }

        IEnumerator Start()
        {
            SpikeReport.Init();
            if (!SpikeReport.Active) yield break;
            var report = SpikeReport.Text;
            if (SpikeReport.Stage == 3)
            {
                yield return PwmRun(report);
                SpikeReport.Finish();
                yield break;
            }
            bool fromGarage = SpikeReport.Stage == 1;
            if (SpikeReport.Transition != null)
                report.AppendLine($"arena scene loaded in {SpikeReport.Transition.Elapsed.TotalMilliseconds:F0} ms");

            // 1. Emulator alone: 10 emulated seconds of Blink after 1 s of warm-up.
            var mcu = new Atmega328P();
            mcu.LoadHex(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Firmware", "Blink.hex")));
            mcu.RunSeconds(1);
            var watch = Stopwatch.StartNew();
            long startCycles = mcu.Cpu.Cycles;
            mcu.RunSeconds(10);
            watch.Stop();
            double rate = (mcu.Cpu.Cycles - startCycles) / watch.Elapsed.TotalSeconds;
            report.AppendLine($"emulator alone (Blink): {rate / 1e6:F1} M cycles/s = {rate / Atmega328P.ClockHz:F1}x real time");

            // 2. The robot scene: settle for 3 s, then measure for 10 s. The UI panels stay hidden here,
            //    so the numbers compare with earlier runs; they are measured in step 4.
            var ui = FindAnyObjectByType<UiSpike>();
            if (ui != null) ui.Visible = false;
            var spike = GetComponent<RobotSpike>();
            report.AppendLine($"robot: {spike.Project.Name}, firmware {spike.FirmwareName}, battery {spike.Project.Battery.StateOfCharge * 100:F1} % at the start");
            var design = spike.Project.Design;
            report.AppendLine($"  built from its design: {design.Body.Shape} {design.Body.WidthMm:F0} × {design.Body.EffectiveLength:F0} mm, {design.Parts.Count} parts, " +
                              $"{design.Wires.Count} wires, {spike.Project.MassKg * 1000:F0} g; wiring: " +
                              (spike.Circuit.Warnings.Count == 0 ? "no findings" : string.Join(", ", spike.Circuit.Warnings)));
            yield return new WaitForSecondsRealtime(3f);
            var startPosition = spike.RobotPosition;
            double startEmulated = spike.Mcu?.Seconds ?? 0;
            int startFrame = Time.frameCount;
            float startTime = Time.realtimeSinceStartup;
            double worstFrameMs = 0;
            int slowFrames = 0;
            var slowList = new StringBuilder(); // when the slow frames came and whether a garbage collection ran in them
            int collections = GC.CollectionCount(0);
            var cost = new SpikeReport.CostMeter();
            int afterCapture = 0; // the frames after a screenshot include its PNG encoding and file write
            float elapsed = 0;
            var log = new StringBuilder();
            float nextLog = 0;
            bool followShot = false;
            while (elapsed < 10f)
            {
                yield return null;
                cost.Sample();
                double frameMs = Time.unscaledDeltaTime * 1000.0;
                int nowCollections = GC.CollectionCount(0);
                if (afterCapture == 0)
                {
                    worstFrameMs = Math.Max(worstFrameMs, frameMs);
                    if (frameMs > 20)
                    {
                        slowFrames++;
                        slowList.Append($" {Time.realtimeSinceStartup - startTime:F2} s {frameMs:F0} ms{(nowCollections != collections ? " (GC)" : "")};");
                    }
                }
                collections = nowCollections;
                if (afterCapture > 0) afterCapture--;
                elapsed = Time.realtimeSinceStartup - startTime;
                if (elapsed >= nextLog)
                {
                    log.AppendLine("  " + spike.Telemetry());
                    nextLog += 0.5f;
                }
                if (!followShot && elapsed >= 5f)
                {
                    followShot = true;
                    yield return SpikeReport.Capture(SpikeReport.Shot("follow"));
                    afterCapture = 3;
                }
            }
            double fps = (Time.frameCount - startFrame) / elapsed;
            report.AppendLine($"robot scene: {fps:F1} fps average, worst frame {worstFrameMs:F1} ms, {slowFrames} frames over 20 ms " +
                              "(the three frames after the screenshot are not counted); " + cost + (slowList.Length > 0 ? ";" + slowList : ""));
            report.AppendLine($"emulator in scene: {spike.EmulatorMsPerFixedStep:F2} ms per 10 ms physics step");
            report.AppendLine($"emulated time in 10 s: {(spike.Mcu?.Seconds ?? 0) - startEmulated:F2} s (board {UI.SpikeStrings.Get(spike.BoardStatusKey)})");
            report.AppendLine($"robot moved: {Vector3.Distance(startPosition, spike.RobotPosition):F2} m straight-line from its start");

            report.AppendLine("telemetry every 0.5 s:");
            report.Append(log);

            spike.TopView = true;
            yield return null;
            yield return SpikeReport.Capture(SpikeReport.Shot("top"));

            // The arena's six viewpoints (ArenaCamera), each photographed where the robot now is.
            foreach (ArenaView view in System.Enum.GetValues(typeof(ArenaView)))
            {
                spike.ShowView(view, now: true);
                yield return null;
                yield return null;
                yield return SpikeReport.Capture(SpikeReport.Shot("view-" + view.ToString().ToLowerInvariant()));
            }
            spike.TopView = true;
            report.AppendLine("arena viewpoints: follow, orbit, top, side, eye, arena; screenshots -view-follow ... -view-arena");

            // 3. Mesh booleans, then a close-up of the chassis they produced.
            var csg = GetComponent<CsgSpike>();
            if (csg != null)
            {
                report.Append(csg.RunBenchmark());
                if (csg.Body != null)
                {
                    var camera = new GameObject("CsgCamera").AddComponent<Camera>();
                    camera.depth = 10;
                    camera.nearClipPlane = 0.01f;
                    camera.fieldOfView = 40f;
                    var target = csg.Body.transform.position + new Vector3(0, 0.012f, 0);
                    camera.transform.position = target + new Vector3(0.17f, 0.2f, -0.26f);
                    camera.transform.LookAt(target);
                    yield return null;
                    yield return SpikeReport.Capture(SpikeReport.Shot("csg"));
                    Destroy(camera.gameObject);
                }
            }

            // 4. UI Toolkit panels over the running robot scene.
            if (ui != null)
            {
                spike.TopView = false;
                yield return ui.RunBenchmark(report, SpikeReport.ShotPrefix, SpikeReport.Capture);
            }

            report.AppendLine($"after the arena run: battery {spike.Project.Battery.StateOfCharge * 100:F2} %, " +
                              $"motor peaks {spike.Project.LeftMotor.PeakC:F1} / {spike.Project.RightMotor.PeakC:F1} °C");
            if (fromGarage && ui != null)
            {
                SpikeReport.Stage = 2;
                ui.ReturnToGarage();
            }
            else
            {
                SpikeReport.Finish();
            }
        }
    }
}
