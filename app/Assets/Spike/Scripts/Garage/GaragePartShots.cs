using System.Collections;
using System.IO;
using CoreEngine.Sim.Design;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.Garage
{
    public sealed partial class GarageSpike
    {
        bool photographingParts; // no depth of field: a close-up of a small part is sharp all over

        /// <summary>
        /// <c>-partShots &lt;folder&gt;</c>: photographs every catalogue part alone on the turntable, close up from
        /// three sides, the way the Garage shows it, to judge the part models; then quits. No UI, no robot.
        /// </summary>
        IEnumerator PartShots(string folder)
        {
            Directory.CreateDirectory(folder);
            photographingParts = true;
            yield return null;
            root.style.display = DisplayStyle.None;
            shown?.Destroy();
            shown = null;
            idleSeconds = float.MinValue; // the turntable stays still
            var stage = new GameObject("PartStage").transform;
            stage.position = robotAnchor.position;
            foreach (var def in PartCatalog.All)
            {
                var project = new RobotProject { Name = def.Name, Design = DesignPresets.Empty() };
                var part = project.Design.AddPart(def.Id);
                if (part == null) continue;
                (part.X, part.Y, part.Z) = (0, part.Y, 0);
                var visual = RobotVisuals.Build(stage, null, project, litMaterial);
                // Stand it on its lowest point, whatever hangs below its frame.
                visual.Root.transform.localPosition = new Vector3(0, -DesignGeometry.LowestPoint(project.Design) * 0.001f, 0);
                yield return null;
                var box = RendererBounds(visual.Root);
                orbitTarget = box.center;
                float size = box.size.magnitude;
                foreach (var (up, around, zoom, name) in new[] { (30f, 200f, 1.15f, "a"), (16f, 125f, 1.25f, "b"), (89.5f, 0f, 0.95f, "c") })
                {
                    pitch = up;
                    yaw = around;
                    distance = Mathf.Max(0.05f, size * zoom);
                    yield return null;
                    yield return null;
                    yield return SpikeReport.Capture(Path.Combine(folder, $"{def.Id}-{name}.png"));
                }
                visual.Destroy();
            }
            Application.Quit();
        }

        static Bounds RendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one * 0.05f);
            var box = renderers[0].bounds;
            foreach (var r in renderers) box.Encapsulate(r.bounds);
            return box;
        }
    }
}
