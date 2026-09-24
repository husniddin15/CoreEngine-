using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// The benchmark report for a <c>-spikeBench &lt;file&gt;</c> run. It follows the robot through the scenes:
    /// Garage (stage 0), arena (stage 1), and back in the Garage (stage 2), where the file is written.
    /// </summary>
    public static class SpikeReport
    {
        static bool initialised;

        public static string? FilePath { get; private set; }
        public static bool Active => FilePath != null;

        /// <summary>The folder of a <c>-partShots &lt;folder&gt;</c> run: photographs of every part model, then quit.</summary>
        public static string? PartShotsFolder { get; private set; }
        public static readonly StringBuilder Text = new StringBuilder();
        public static int Stage;

        /// <summary>Started when a scene change begins, so the next scene can report the load time.</summary>
        public static System.Diagnostics.Stopwatch? Transition;

        /// <summary>Screenshots go next to the report: &lt;report&gt;-name.png.</summary>
        public static string Shot(string name) => ShotPrefix + "-" + name + ".png";

        public static string ShotPrefix =>
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(FilePath!)) ?? ".", Path.GetFileNameWithoutExtension(FilePath!));

        public static void Init()
        {
            if (initialised) return;
            initialised = true;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-spikeBench") FilePath = args[i + 1];
                if (args[i] == "-partShots") PartShotsFolder = args[i + 1];
            }
            if (FilePath == null) return;

            Text.AppendLine($"unity: {Application.unityVersion}");
#if ENABLE_IL2CPP
            Text.AppendLine("scripting backend: IL2CPP");
#else
            Text.AppendLine("scripting backend: Mono");
#endif
            Text.AppendLine($"cpu: {SystemInfo.processorType} ({SystemInfo.processorCount} threads)");
            Text.AppendLine($"gpu: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
            Text.AppendLine($"resolution: {Screen.width}x{Screen.height}, vsync {QualitySettings.vSyncCount}");
        }

        public static void Finish()
        {
            if (FilePath == null) return;
            File.WriteAllText(FilePath, Text.ToString());
            Debug.Log("SpikeReport:\n" + Text);
            Application.Quit();
        }

        public static string FrameStats(System.Collections.Generic.List<double> frames)
        {
            double sum = 0, worst = 0;
            int over20 = 0;
            foreach (double f in frames)
            {
                sum += f;
                worst = Math.Max(worst, f);
                if (f > 20) over20++;
            }
            return $"{frames.Count} frames, {1000.0 * frames.Count / Math.Max(sum, 1e-6):F0} fps average, worst {worst:F1} ms, {over20} over 20 ms";
        }

        static readonly FrameTiming[] timings = new FrameTiming[1];

        /// <summary>GPU time and main-thread CPU time of the latest finished frame in ms, or null when not measured.</summary>
        public static (double gpu, double cpu)? FrameCost()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) < 1 || timings[0].gpuFrameTime <= 0) return null;
            return (timings[0].gpuFrameTime, timings[0].cpuMainThreadFrameTime);
        }

        /// <summary>Adds up <see cref="FrameCost"/> over a measurement.</summary>
        public sealed class CostMeter
        {
            double gpu, cpu, worstGpu;
            int count;

            public void Sample()
            {
                var cost = FrameCost();
                if (cost == null) return;
                gpu += cost.Value.gpu;
                cpu += cost.Value.cpu;
                worstGpu = Math.Max(worstGpu, cost.Value.gpu);
                count++;
            }

            public override string ToString() => count == 0 ? "GPU time not available"
                : $"GPU {gpu / count:F1} ms a frame on average (worst {worstGpu:F1}), main thread {cpu / count:F1} ms";
        }

        public static IEnumerator Capture(string path)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
        }
    }
}
