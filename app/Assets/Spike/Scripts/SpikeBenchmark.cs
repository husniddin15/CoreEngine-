using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using CoreEngine.Sim.Avr;
using UnityEngine;

namespace CoreEngine.Spike
{
    /// <summary>
    /// Automated measurements for Phase 0.4/0.5. Run the player with
    /// <c>-spikeBench &lt;report file&gt;</c>: it benchmarks the emulator alone, then measures frame rate
    /// and emulator cost while the robot drives, writes the report and quits.
    /// </summary>
    public sealed class SpikeBenchmark : MonoBehaviour
    {
        IEnumerator Start()
        {
            string? reportPath = ArgumentAfter("-spikeBench");
            if (reportPath == null) yield break;

            var report = new StringBuilder();
            report.AppendLine($"unity: {Application.unityVersion}");
#if ENABLE_IL2CPP
            report.AppendLine("scripting backend: IL2CPP");
#else
            report.AppendLine("scripting backend: Mono");
#endif
            report.AppendLine($"cpu: {SystemInfo.processorType} ({SystemInfo.processorCount} threads)");
            report.AppendLine($"gpu: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
            report.AppendLine($"resolution: {Screen.width}x{Screen.height}, vsync {QualitySettings.vSyncCount}");

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

            // Screenshots go next to the report: <report>-follow.png, -top.png and -csg.png.
            string shots = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? ".",
                                        Path.GetFileNameWithoutExtension(reportPath));

            // 2. The robot scene: settle for 3 s, then measure for 10 s. The UI panels stay hidden here,
            //    so the numbers compare with earlier runs; they are measured in step 4.
            var ui = FindAnyObjectByType<UiSpike>();
            if (ui != null) ui.Visible = false;
            var spike = GetComponent<RobotSpike>();
            yield return new WaitForSecondsRealtime(3f);
            var startPosition = spike.RobotPosition;
            double startEmulated = spike.Mcu.Seconds;
            int startFrame = Time.frameCount;
            float startTime = Time.realtimeSinceStartup;
            double worstFrameMs = 0;
            int slowFrames = 0;
            bool afterCapture = false; // the frame after a screenshot includes the PNG encoding
            float elapsed = 0;
            var log = new StringBuilder();
            float nextLog = 0;
            bool followShot = false;
            while (elapsed < 10f)
            {
                yield return null;
                double frameMs = Time.unscaledDeltaTime * 1000.0;
                if (!afterCapture)
                {
                    worstFrameMs = Math.Max(worstFrameMs, frameMs);
                    if (frameMs > 20) slowFrames++;
                }
                afterCapture = false;
                elapsed = Time.realtimeSinceStartup - startTime;
                if (elapsed >= nextLog)
                {
                    log.AppendLine("  " + spike.Telemetry());
                    nextLog += 0.5f;
                }
                if (!followShot && elapsed >= 5f)
                {
                    followShot = true;
                    yield return Capture(shots + "-follow.png");
                    afterCapture = true;
                }
            }
            double fps = (Time.frameCount - startFrame) / elapsed;
            report.AppendLine($"robot scene: {fps:F1} fps average, worst frame {worstFrameMs:F1} ms, {slowFrames} frames over 20 ms " +
                              "(the frame after the screenshot is not counted)");
            report.AppendLine($"emulator in scene: {spike.EmulatorMsPerFixedStep:F2} ms per 10 ms physics step");
            report.AppendLine($"emulated time in 10 s: {spike.Mcu.Seconds - startEmulated:F2} s");
            report.AppendLine($"robot moved: {Vector3.Distance(startPosition, spike.RobotPosition):F2} m straight-line from its start");

            report.AppendLine("telemetry every 0.5 s:");
            report.Append(log);

            spike.TopView = true;
            yield return null;
            yield return Capture(shots + "-top.png");

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
                    yield return Capture(shots + "-csg.png");
                    Destroy(camera.gameObject);
                }
            }

            // 4. UI Toolkit panels over the running robot scene.
            if (ui != null)
            {
                spike.TopView = false;
                yield return ui.RunBenchmark(report, shots, Capture);
            }

            File.WriteAllText(reportPath, report.ToString());
            UnityEngine.Debug.Log("SpikeBenchmark report:\n" + report);
            Application.Quit();
        }

        static IEnumerator Capture(string path)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Destroy(texture);
        }

        static string? ArgumentAfter(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
