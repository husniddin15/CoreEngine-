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
        IEnumerator Start()
        {
            SpikeReport.Init();
            if (!SpikeReport.Active) yield break;
            var report = SpikeReport.Text;
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
