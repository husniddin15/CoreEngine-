using System;
using System.Collections.Generic;

namespace CoreEngine.Sim.Design
{
    /// <summary>
    /// What holds together (docs/03 §5.2). The body's shapes and the parts touch one another in groups: a board
    /// stands on a plate, a motor hangs under it, standoffs join two decks. The group with the wheels is the robot;
    /// every other group is not attached to it and falls off, as a part left in the air would on a desk (the owner,
    /// 2026-09-25: a plate dragged 17 cm away still drove along with the robot, "where is logic and physics").
    /// Pieces touch when their boxes come within <see cref="Gap"/> of each other; wires hold nothing.
    /// </summary>
    public sealed class RobotPieces
    {
        /// <summary>How close two pieces' boxes must come to count as fixed together (mm): screws, tape, glue.</summary>
        public const float Gap = 1.5f;

        /// <summary>A piece: a part's id, or <see cref="ShapePrefix"/> and a body shape's id.</summary>
        public const string ShapePrefix = "shape:";

        RobotPieces(List<List<string>> groups, int main)
        {
            Groups = groups;
            Main = main;
        }

        /// <summary>The groups of pieces that hold together, the robot's first.</summary>
        public IReadOnlyList<List<string>> Groups { get; }

        /// <summary>The robot's group: the one with the most wheels, then the heaviest; -1 for an empty design.</summary>
        public int Main { get; }

        /// <summary>Every piece outside the robot's group.</summary>
        public IEnumerable<string> Loose
        {
            get
            {
                for (int g = 0; g < Groups.Count; g++)
                    if (g != Main)
                        foreach (var id in Groups[g]) yield return id;
            }
        }

        public bool AllAttached => Groups.Count <= 1;

        /// <summary>The group a piece is in, or -1.</summary>
        public int GroupOf(string piece)
        {
            for (int g = 0; g < Groups.Count; g++)
                if (Groups[g].Contains(piece)) return g;
            return -1;
        }

        public static RobotPieces Of(RobotDesign design)
        {
            var ids = new List<string>();
            var boxes = new List<((float x, float y, float z) min, (float x, float y, float z) max)>();
            var grams = new List<double>();
            var wheels = new List<int>();
            foreach (var shape in design.Body.Features)
            {
                if (shape.Kind == FeatureKind.Group || shape.Hole) continue;
                ids.Add(ShapePrefix + shape.Id);
                boxes.Add(DesignGeometry.FeatureBounds(shape));
                grams.Add(shape.ApproximateVolume() / 1000.0 * BodyDesign.DensityGPerCm3(shape.Material));
                wheels.Add(0);
            }
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                ids.Add(part.Id);
                boxes.Add(DesignGeometry.PartBounds(part, wheel: false)); // a turning wheel holds nothing
                grams.Add(def.MassG);
                wheels.Add(def.Kind == PartKind.Motor ? 1 : 0);
            }

            // Union the pieces whose boxes touch.
            var parent = new int[ids.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i)
            {
                while (parent[i] != i) i = parent[i] = parent[parent[i]];
                return i;
            }
            for (int i = 0; i < ids.Count; i++)
                for (int j = i + 1; j < ids.Count; j++)
                    if (Touch(boxes[i], boxes[j])) parent[Find(i)] = Find(j);

            var byRoot = new Dictionary<int, List<int>>();
            for (int i = 0; i < ids.Count; i++)
            {
                int root = Find(i);
                if (!byRoot.TryGetValue(root, out var members)) byRoot[root] = members = new List<int>();
                members.Add(i);
            }
            var groups = new List<List<int>>(byRoot.Values);
            // The robot first: the most wheels, then the heaviest; the rest in design order.
            int best = -1;
            for (int g = 0; g < groups.Count; g++)
            {
                if (best < 0) { best = g; continue; }
                int w = Count(groups[g], wheels), bw = Count(groups[best], wheels);
                if (w > bw || (w == bw && Sum(groups[g], grams) > Sum(groups[best], grams))) best = g;
            }
            var ordered = new List<List<string>>();
            if (best >= 0) ordered.Add(Names(groups[best], ids));
            for (int g = 0; g < groups.Count; g++) if (g != best) ordered.Add(Names(groups[g], ids));
            return new RobotPieces(ordered, best >= 0 ? 0 : -1);
        }

        static bool Touch(((float x, float y, float z) min, (float x, float y, float z) max) a, ((float x, float y, float z) min, (float x, float y, float z) max) b) =>
            a.min.x <= b.max.x + Gap && b.min.x <= a.max.x + Gap &&
            a.min.y <= b.max.y + Gap && b.min.y <= a.max.y + Gap &&
            a.min.z <= b.max.z + Gap && b.min.z <= a.max.z + Gap;

        static int Count(List<int> members, List<int> wheels)
        {
            int n = 0;
            foreach (int i in members) n += wheels[i];
            return n;
        }

        static double Sum(List<int> members, List<double> grams)
        {
            double sum = 0;
            foreach (int i in members) sum += grams[i];
            return sum;
        }

        static List<string> Names(List<int> members, List<string> ids)
        {
            var names = new List<string>();
            foreach (int i in members) names.Add(ids[i]);
            return names;
        }

        /// <summary>
        /// The piece a body solid belongs to: its own shape, or for a Tinkercad group (the solid's id is the group's)
        /// the first shape in it; null when there is none.
        /// </summary>
        public static string? PieceOf(RobotDesign design, string topLevelId)
        {
            var feature = design.Body.Feature(topLevelId);
            if (feature != null && feature.Kind != FeatureKind.Group) return ShapePrefix + topLevelId;
            foreach (var shape in design.Body.Features)
            {
                if (shape.Kind == FeatureKind.Group || shape.Hole) continue;
                for (string group = shape.Group; group.Length > 0; group = design.Body.Feature(group)?.Group ?? "")
                    if (group == topLevelId) return ShapePrefix + shape.Id;
            }
            return null;
        }

        /// <summary>A copy of the design with only the pieces of one group (the holes of kept shapes' groups stay).</summary>
        public RobotDesign Keep(RobotDesign design, int group)
        {
            var kept = new HashSet<string>(Groups[group]);
            var copy = design.Clone();
            copy.Parts.RemoveAll(p => !kept.Contains(p.Id));
            var keptGroups = new HashSet<string>();
            foreach (var shape in copy.Body.Features)
                if (shape.Kind != FeatureKind.Group && !shape.Hole && kept.Contains(ShapePrefix + shape.Id) && shape.Group.Length > 0) keptGroups.Add(shape.Group);
            copy.Body.Features.RemoveAll(f => f.Kind != FeatureKind.Group &&
                (f.Hole ? !keptGroups.Contains(f.Group) : !kept.Contains(ShapePrefix + f.Id)));
            return copy;
        }

        /// <summary>How far the HC-SR04 looks up (+) or down (−) from level, in degrees, or null without one.</summary>
        public static float? SonarTilt(RobotDesign design)
        {
            foreach (var part in design.Parts)
            {
                if (part.Part != PartCatalog.HcSr04) continue;
                var aim = DesignGeometry.SonarAim(part);
                double level = Math.Sqrt(aim.x * aim.x + aim.z * aim.z);
                return (float)(Math.Atan2(aim.y, level) * 180 / Math.PI);
            }
            return null;
        }
    }
}
