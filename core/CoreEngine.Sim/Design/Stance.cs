using System;
using System.Collections.Generic;

namespace CoreEngine.Sim.Design
{
    /// <summary>
    /// How a robot rests on the floor (docs/03 §5.2): on its wheels and ball casters alone, or tipped over onto
    /// something that scrapes the floor as it drives. A robot on two wheels needs a third point that rolls, a ball
    /// caster, on the side its weight is. Without one it tips onto that end and drags it, as a real robot does,
    /// and turning on the spot then takes far more push than driving straight: the dragging end swings round a
    /// wide circle while the wheels only roll round a small one (the owner's robot, 2026-09-25, drove forward but
    /// would not turn).
    /// </summary>
    public sealed class RobotStance
    {
        public static readonly RobotStance Steady = new RobotStance(null, "", 0);

        RobotStance(string? dragging, string side, float tiltDegrees)
        {
            Dragging = dragging;
            Side = side;
            TiltDegrees = tiltDegrees;
        }

        /// <summary>True when only wheels and ball casters touch the floor (and for a robot not on two wheels).</summary>
        public bool Rolls => Dragging == null;

        /// <summary>What scrapes the floor when the robot does not roll: a part's id, or "" for the body; null when it rolls.</summary>
        public string? Dragging { get; }

        /// <summary>The way it tips: "front" (+z, the view cube's FRONT), "back", "left" (−x) or "right"; "" when it does not.</summary>
        public string Side { get; }

        /// <summary>How far it leans from standing on its wheels, in degrees.</summary>
        public float TiltDegrees { get; }

        public override string ToString() => Rolls
            ? (TiltDegrees >= 0.5f ? $"rolls, leaning {TiltDegrees:F1}° to its {Side}" : "rolls")
            : $"tips {TiltDegrees:F1}° onto its {Side}, dragging {(Dragging == "" ? "the body" : Dragging)}";

        /// <summary>
        /// Stands the robot on its two wheels and lets its weight tip it round their axle (the wheels roll as it
        /// tips) until something touches the floor: a caster's ball, which rolls, or a corner of a part or of a
        /// body shape, which drags. Robots on one wheel or more than two are left alone for now.
        /// </summary>
        public static RobotStance Of(RobotDesign design)
        {
            var wheels = new List<PartInstance>();
            foreach (var part in design.Parts)
                if (PartCatalog.Get(part.Part)?.Kind == PartKind.Motor && Math.Abs(DesignGeometry.WheelAxis(part).y) < 0.5f) wheels.Add(part);
            if (wheels.Count != 2) return Steady;
            var a = DesignGeometry.WheelCentre(wheels[0]);
            var b = DesignGeometry.WheelCentre(wheels[1]);
            double lx = b.x - a.x, lz = b.z - a.z, apart = Math.Sqrt(lx * lx + lz * lz);
            if (apart < 20) return Steady;
            lx /= apart;
            lz /= apart;

            // The weight pulls across the line through the wheels, toward the centre of mass.
            var com = DesignGeometry.CentreOfMass(design);
            double nx = -lz, nz = lx;
            if ((com.x - a.x) * nx + (com.z - a.z) * nz < 0)
            {
                nx = -nx;
                nz = -nz;
            }
            string side = Math.Abs(nz) >= Math.Abs(nx) ? (nz > 0 ? "front" : "back") : (nx > 0 ? "right" : "left");

            // Tipping across the way the wheels roll turns the robot about their axle, the wheels rolling under
            // it; tipping along it (wheels one behind the other) turns it about their contact line on the floor.
            var axis = DesignGeometry.WheelAxis(wheels[0]);
            double roll = Math.Abs(-axis.z * nx + axis.x * nz) / Math.Max(1e-6, Math.Sqrt(axis.x * axis.x + axis.z * axis.z));
            double floor = Math.Min(a.y, b.y) - DesignGeometry.WheelRadius;
            double pivot = roll > 0.7 ? DesignGeometry.WheelRadius : 0;

            double first = 90; // the smallest tip at which something touches; if nothing does, the robot falls on its face
            string? dragging = "";
            void Try((float x, float y, float z) p, float radius, string? owner)
            {
                double u = (p.x - a.x) * nx + (p.z - a.z) * nz; // out from the axle, toward the weight
                double v = p.y - floor - pivot;                 // up from the axle
                if (u <= 0.5) return;
                // Tipped by t, the point is pivot + v cos t − u sin t above the floor; it touches at its radius
                // (a caster's ball), at once when it is already that low.
                double reach = Math.Sqrt(u * u + v * v), k = pivot - radius, t;
                if (pivot + v <= radius) t = 0;
                else if (Math.Abs(k) > reach) return;
                else t = Math.Max(0, (Math.Atan2(v, u) + Math.Asin(k / reach)) * 180 / Math.PI);
                if (t >= first) return;
                first = t;
                dragging = owner;
            }

            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                if (def.Kind == PartKind.Caster)
                {
                    Try(DesignGeometry.CasterBall(part), DesignGeometry.CasterBallRadius, null);
                    continue;
                }
                foreach (var block in def.Solids)
                    foreach (var corner in Corners(part.Turn, (part.X, part.Y, part.Z), block.Centre, block.Size))
                        Try(corner, 0, part.Id);
            }
            foreach (var shape in design.Body.Features)
            {
                if (shape.Kind == FeatureKind.Group || shape.Hole) continue;
                foreach (var corner in Corners(Rot3.Euler(shape.RotX, shape.RotY, shape.RotZ), (shape.X, shape.Y, shape.Z), (0, 0, 0), (shape.SizeX, shape.SizeY, shape.SizeZ)))
                    Try(corner, 0, "");
            }
            return new RobotStance(dragging, first >= 0.5 || dragging != null ? side : "", (float)first);
        }

        static IEnumerable<(float x, float y, float z)> Corners(Rot3 turn, (float x, float y, float z) at, (float x, float y, float z) centre, (float x, float y, float z) size)
        {
            for (int i = 0; i < 8; i++)
            {
                var r = turn.Apply(centre.x + ((i & 1) == 0 ? -0.5f : 0.5f) * size.x,
                                   centre.y + ((i & 2) == 0 ? -0.5f : 0.5f) * size.y,
                                   centre.z + ((i & 4) == 0 ? -0.5f : 0.5f) * size.z);
                yield return (at.x + r.x, at.y + r.y, at.z + r.z);
            }
        }
    }
}
