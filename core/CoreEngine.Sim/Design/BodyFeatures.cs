using System;
using System.Collections.Generic;

namespace CoreEngine.Sim.Design
{
    /// <summary>
    /// The shapes of the Body Studio (docs/08 §2), a perforated plate (a sheet with an M3 hole grid, as robot kits
    /// use), and the record of a group. The numbers are saved; new kinds go at the end.
    /// </summary>
    public enum FeatureKind { Box, RoundedBox, Cylinder, Cone, Sphere, Wedge, Tube, Extrusion, Imported, Plate, Group }

    /// <summary>
    /// One shape of the Body Studio: a solid of some material, or a hole that cuts the solids of its group
    /// (Tinkercad's way of modelling), placed in the chassis frame in millimetres with a size and a rotation.
    /// An extrusion carries its outline, an imported shape the file it came from, a plate its hole grid. A group
    /// record has no shape of its own: shapes name it in <see cref="Group"/>. The mesh is derived with Manifold
    /// (ADR-0005).
    /// </summary>
    [Serializable]
    public sealed class BodyFeature
    {
        public string Id = "";
        public FeatureKind Kind = FeatureKind.Box;
        public bool Hole;
        public float X, Y, Z;                         // centre in the chassis frame, mm
        public float RotX, RotY, RotZ;                // degrees, as Unity's Quaternion.Euler
        public float SizeX = 30, SizeY = 20, SizeZ = 30; // extent before rotation, mm
        public float Detail;                          // rounded box: corner radius (mm); cone: top radius / base radius; tube: wall (mm)
        public List<float> Outline = new List<float>(); // extrusion: x, z pairs of the outline, scaled to a 1 × 1 square
        public string MeshFile = "";                  // imported: the file's name in the robot's import folder
        public bool MirrorX;                          // imported: the mesh is mirrored left-right (a mirrored copy)
        public BodyMaterial Material = BodyMaterial.Pla;
        public string Colour = "";                    // "#rrggbb" for materials sold in colours (PLA, EVA foam, acrylic); empty: the material's own
        public string Group = "";                     // the id of the group record this shape or group is in; empty: none
        public float Pitch;                           // plate: spacing of the hole grid (0: no holes), mm
        public float HoleSize = 3.2f;                 // plate: diameter of its holes (M3 clearance), mm

        public BodyFeature Clone()
        {
            var copy = (BodyFeature)MemberwiseClone();
            copy.Outline = new List<float>(Outline);
            return copy;
        }

        /// <summary>
        /// A copy mirrored across the chassis centre plane (x = 0), for the other side of a symmetric robot. The
        /// shapes are symmetric about their own x axis, so mirroring turns the rotation about y and z the other
        /// way; an outline and an imported mesh are mirrored themselves.
        /// </summary>
        public BodyFeature MirroredX()
        {
            var m = Clone();
            m.X = -X;
            m.RotY = -RotY;
            m.RotZ = -RotZ;
            for (int i = 0; i < m.Outline.Count; i += 2) m.Outline[i] = -m.Outline[i];
            if (Kind == FeatureKind.Imported) m.MirrorX = !MirrorX;
            return m;
        }

        /// <summary>A sensible starting shape of each kind, standing on a surface at height <paramref name="floor"/>.</summary>
        public static BodyFeature Create(FeatureKind kind, bool hole, float floor, BodyMaterial material = BodyMaterial.Pla)
        {
            var f = new BodyFeature { Kind = kind, Hole = hole, Material = material };
            switch (kind)
            {
                case FeatureKind.RoundedBox:
                    f.Detail = 4;
                    break;
                case FeatureKind.Cylinder:
                case FeatureKind.Sphere:
                    f.SizeX = f.SizeY = f.SizeZ = 20;
                    break;
                case FeatureKind.Cone:
                    f.SizeX = f.SizeZ = 24;
                    f.SizeY = 20;
                    f.Detail = 0;
                    break;
                case FeatureKind.Tube:
                    f.SizeX = f.SizeZ = 24;
                    f.SizeY = 20;
                    f.Detail = 3;
                    break;
                case FeatureKind.Wedge:
                    f.SizeX = 30;
                    f.SizeY = 15;
                    f.SizeZ = 30;
                    break;
                case FeatureKind.Plate: // a kit-sized chassis plate: 120 × 160 mm, 3 mm thick, M3 holes every 15 mm
                    f.SizeX = 120;
                    f.SizeY = 3;
                    f.SizeZ = 160;
                    f.Detail = 12;
                    f.Pitch = 15;
                    break;
            }
            if (hole && kind != FeatureKind.Sphere) f.SizeY += 10; // a hole reaches through the plate below it
            f.Y = floor + f.SizeY / 2 - (hole ? 8 : 0);
            return f;
        }

        /// <summary>Approximate volume in mm³; the Garage shows Manifold's exact one.</summary>
        public double ApproximateVolume()
        {
            double box = (double)SizeX * SizeY * SizeZ;
            switch (Kind)
            {
                case FeatureKind.Box: return box;
                case FeatureKind.RoundedBox: return box * 0.95;
                case FeatureKind.Cylinder: return box * Math.PI / 4;
                case FeatureKind.Cone:
                {
                    double r = Math.Max(0, Math.Min(1, Detail));
                    return box * Math.PI / 12 * (1 + r + r * r);
                }
                case FeatureKind.Sphere: return box * Math.PI / 6;
                case FeatureKind.Wedge: return box / 2;
                case FeatureKind.Tube:
                {
                    double ix = Math.Max(0, SizeX - 2 * Detail), iz = Math.Max(0, SizeZ - 2 * Detail);
                    return (SizeX * SizeZ - ix * iz) * SizeY * Math.PI / 4;
                }
                case FeatureKind.Extrusion: return Math.Abs(OutlineArea(Outline)) * SizeX * SizeZ * SizeY;
                case FeatureKind.Plate:
                {
                    double r = Math.Max(0, Math.Min(Detail, Math.Min(SizeX, SizeZ) / 2));
                    double area = SizeX * SizeZ - (4 - Math.PI) * r * r;
                    if (Pitch >= 5 && HoleSize > 0)
                    {
                        double holes = Math.Floor((SizeX - 2 * (HoleSize / 2 + 3)) / Pitch + 1) * Math.Floor((SizeZ - 2 * (HoleSize / 2 + 3)) / Pitch + 1);
                        area -= Math.Max(0, holes) * Math.PI * HoleSize * HoleSize / 4;
                    }
                    return Math.Max(0, area) * SizeY;
                }
                case FeatureKind.Group: return 0;
                default: return box * 0.5; // an imported shape: unknown until Manifold measures it
            }
        }

        /// <summary>Signed area of x, z pairs (positive when counter-clockwise seen from above).</summary>
        public static double OutlineArea(IReadOnlyList<float> outline)
        {
            double area = 0;
            int n = outline.Count / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += (double)outline[2 * i] * outline[2 * j + 1] - (double)outline[2 * j] * outline[2 * i + 1];
            }
            return area / 2;
        }

        /// <summary>
        /// Makes an extrusion from an outline drawn in the chassis frame (x, z pairs in mm): the feature is centred
        /// on the outline's bounds and its outline scaled to a 1 × 1 square, so the size handles work as for the
        /// other shapes. Returns null for fewer than three points or no area.
        /// </summary>
        public static BodyFeature? FromOutline(IReadOnlyList<float> points, float floor, float height, bool hole)
        {
            int n = points.Count / 2;
            if (n < 3) return null;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                minX = Math.Min(minX, points[2 * i]);
                maxX = Math.Max(maxX, points[2 * i]);
                minZ = Math.Min(minZ, points[2 * i + 1]);
                maxZ = Math.Max(maxZ, points[2 * i + 1]);
            }
            float sx = maxX - minX, sz = maxZ - minZ;
            if (sx < 1 || sz < 1) return null;
            var f = new BodyFeature
            {
                Kind = FeatureKind.Extrusion,
                Hole = hole,
                X = (minX + maxX) / 2,
                Z = (minZ + maxZ) / 2,
                SizeX = sx,
                SizeY = height,
                SizeZ = sz,
            };
            for (int i = 0; i < n; i++)
            {
                f.Outline.Add((points[2 * i] - f.X) / sx);
                f.Outline.Add((points[2 * i + 1] - f.Z) / sz);
            }
            if (Math.Abs(OutlineArea(f.Outline)) < 1e-4) return null;
            f.Y = floor + height / 2 - (hole ? 5 : 0);
            return f;
        }
    }
}
