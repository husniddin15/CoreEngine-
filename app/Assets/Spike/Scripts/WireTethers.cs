using System;
using System.Collections.Generic;
using CoreEngine.Sim.Design;
using CoreEngine.Spike.Garage;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoreEngine.Spike
{
    /// <summary>
    /// The wires between bodies that move apart (D26: only what is attached moves with the robot). A wire from the
    /// robot to a piece that fell off, or between two loose pieces, hangs between its two pins and carries current
    /// while it reaches; once the pins are farther apart than the wire is long it pulls out of its pin and carries
    /// nothing (the owner, 2026-09-26: a lone motor kept turning with its battery lying elsewhere and its wires
    /// hidden, "how the heck it is rotating without power"). A wire is as long as it was laid in the design.
    /// </summary>
    public sealed class WireTethers
    {
        /// <summary>How far past its laid length a wire gives before it comes off its pin (m).</summary>
        public const float Give = 0.005f;

        const int Samples = 16;
        const float Floor = RobotVisuals.WireRadius; // the arena's floor is at y = 0

        sealed class Tether
        {
            public Tether(int wire, Transform a, Vector3 localA, Transform b, Vector3 localB, float length, LineRenderer line)
            {
                Wire = wire;
                A = a;
                LocalA = localA;
                B = b;
                LocalB = localB;
                Length = length;
                Line = line;
            }

            public int Wire { get; }
            public Transform A { get; }
            public Vector3 LocalA { get; }
            public Transform B { get; }
            public Vector3 LocalB { get; }
            public float Length { get; }
            public LineRenderer Line { get; }
        }

        readonly List<Tether> hanging = new List<Tether>();
        readonly List<int> pulledOut = new List<int>();
        readonly Vector3[] points = new Vector3[Samples + 1];

        /// <summary>Wires still hanging between two bodies.</summary>
        public int Hanging => hanging.Count;

        /// <summary>The wires that came off, in the order they did (indices into the design's wires).</summary>
        public IReadOnlyList<int> PulledOut => pulledOut;

        /// <summary>Whether a wire of the design hangs between two bodies right now.</summary>
        public bool IsHanging(int wire) => hanging.Exists(t => t.Wire == wire);

        /// <summary>
        /// A tether for every wire whose ends are held by different bodies (<paramref name="holderOf"/>: the body
        /// holding a part, all starting in the design's frame). Its length is the wire as laid round the parts
        /// (<see cref="RobotVisuals.WirePaths"/>), at least the straight way through its points.
        /// </summary>
        public static WireTethers Build(RobotDesign design, RobotVisuals visuals, Func<string, Transform> holderOf)
        {
            var tethers = new WireTethers();
            for (int i = 0; i < design.Wires.Count; i++)
            {
                var wire = design.Wires[i];
                Transform a = holderOf(wire.FromPart), b = holderOf(wire.ToPart);
                if (a == b) continue;
                var pinA = DesignGeometry.PinPosition(design, wire.FromPart, wire.FromPin);
                var pinB = DesignGeometry.PinPosition(design, wire.ToPart, wire.ToPin);
                if (pinA == null || pinB == null) continue;
                float length = (float)DesignGeometry.WireLength(design, wire) * RobotPhysics.Mm;
                var laid = i < visuals.WirePaths.Count ? visuals.WirePaths[i] : null;
                if (laid != null)
                {
                    float along = 0;
                    for (int k = 1; k < laid.Length; k++) along += Vector3.Distance(laid[k - 1], laid[k]);
                    length = Mathf.Max(length, along);
                }

                var line = new GameObject("Hanging wire " + i).AddComponent<LineRenderer>();
                line.sharedMaterial = visuals.WireMaterial(wire.Color);
                line.widthMultiplier = 2 * RobotVisuals.WireRadius;
                line.positionCount = Samples + 1;
                line.numCapVertices = 2;
                line.generateLightingData = true;
                line.shadowCastingMode = ShadowCastingMode.Off;
                tethers.hanging.Add(new Tether(i, a, ToVector(pinA.Value), b, ToVector(pinB.Value), length, line));
            }
            tethers.Draw();
            return tethers;
        }

        static Vector3 ToVector((float x, float y, float z) mm) => new Vector3(mm.x, mm.y, mm.z) * RobotPhysics.Mm;

        /// <summary>Takes the hanging wires away (the showroom shows another robot).</summary>
        public void Clear()
        {
            foreach (var t in hanging) if (t.Line != null) UnityEngine.Object.Destroy(t.Line.gameObject);
            hanging.Clear();
        }

        /// <summary>Pulls out every wire stretched past its length; true when one came off.</summary>
        public bool Pull()
        {
            bool any = false;
            for (int k = hanging.Count - 1; k >= 0; k--)
            {
                var t = hanging[k];
                float apart = Vector3.Distance(t.A.TransformPoint(t.LocalA), t.B.TransformPoint(t.LocalB));
                if (apart <= t.Length + Give) continue;
                pulledOut.Add(t.Wire);
                UnityEngine.Object.Destroy(t.Line.gameObject);
                hanging.RemoveAt(k);
                any = true;
            }
            return any;
        }

        /// <summary>
        /// Draws each hanging wire through the lowest point a wire of its length would sag to (as two straight
        /// halves; a real one curves a little less), so a slack wire hangs or lies on the floor and a taut one runs
        /// straight.
        /// </summary>
        public void Draw()
        {
            foreach (var t in hanging)
            {
                Vector3 a = t.A.TransformPoint(t.LocalA), b = t.B.TransformPoint(t.LocalB);
                float half = 0.5f * t.Length, halfSpan = 0.5f * Vector3.Distance(a, b);
                float sag = half > halfSpan ? Mathf.Sqrt(half * half - halfSpan * halfSpan) : 0;
                var middle = 0.5f * (a + b);
                var lowest = middle + Vector3.down * sag;
                var control = 2 * lowest - middle; // a quadratic curve through the lowest point at its middle
                for (int i = 0; i <= Samples; i++)
                {
                    float s = i / (float)Samples, u = 1 - s;
                    var p = u * u * a + 2 * u * s * control + s * s * b;
                    if (p.y < Floor) p.y = Floor;
                    points[i] = p;
                }
                t.Line.SetPositions(points);
            }
        }
    }
}
