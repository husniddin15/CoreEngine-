using System;
using System.Collections;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// The benchmark's flicker check: with nothing moving in the showroom no pixel should change from one frame to
    /// the next (the owner, 2026-09-25, saw black specks "appearing and disappearing as particles": the ambient
    /// occlusion's blue noise changed every frame, about 5,700 pixels a frame at 1920 × 1080).
    /// </summary>
    public sealed partial class GarageSpike
    {
        /// <summary>Pixels a frame whose brightness changes by more than 24 of 255 over <paramref name="frames"/> frames of a still view.</summary>
        IEnumerator FlickeringPixels(int frames, Action<double> done)
        {
            byte[]? before = null;
            long changed = 0;
            for (int f = 0; f <= frames; f++)
            {
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                var pixels = shot.GetPixels32();
                Destroy(shot);
                var luma = new byte[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) luma[i] = (byte)((pixels[i].r * 77 + pixels[i].g * 150 + pixels[i].b * 29) >> 8);
                if (before != null)
                    for (int i = 0; i < luma.Length; i++)
                        if (Math.Abs(luma[i] - before[i]) > 24) changed++;
                before = luma;
            }
            done(changed / (double)frames);
        }
    }
}
