using System;
using System.Collections.Generic;

namespace CoreEngine.Sim.Design
{
    /// <summary>
    /// Turns a version-2 design (before 2026-09-24) into the free one the Body Studio edits: the two ready-made
    /// plates become shapes (perforated plates, side walls, standoffs), every part gets the place and turn its
    /// fixed mount gave it, and the frame's origin moves from the old chassis centre, 50 mm up, to the floor.
    /// The robot looks, weighs and drives as it did.
    /// </summary>
    public static class DesignMigration
    {
        /// <summary>The design version this code writes; the Garage's save file carries it.</summary>
        public const int Version = 3;

        /// <summary>How high the version-2 frame's origin was above the floor.</summary>
        public const float OldOriginHeight = 50;

        // The version-2 layout, in its own frame.
        const float BottomPlateTop = -12, TopDeckBottom = 12, WallThickness = 3;

        /// <summary>Converts a version-2 design in place. A body already converted (no decks left) is left alone.</summary>
        public static void Upgrade(RobotDesign design)
        {
            var body = design.Body;
            if (body.Decks <= 0) return;
            foreach (var part in design.Parts) PlaceAsBefore(design, part);

            var old = new List<BodyFeature>(body.Features);
            bool holes = old.Exists(f => f.Hole);
            foreach (var feature in old)
            {
                feature.Y += OldOriginHeight;
                feature.Material = body.Material;
            }
            var made = new List<BodyFeature>();
            foreach (var shape in PlatesAsShapes(body)) made.Add(body.AddFeature(shape));
            // The standoffs were drawn but not part of the body; they stay out of the group below.
            var standoffs = new List<BodyFeature>();
            foreach (var standoff in Standoffs(body)) standoffs.Add(body.AddFeature(standoff));

            // A version-2 hole cut every solid and the plates: one group keeps that.
            if (holes)
            {
                var ids = new List<string>();
                foreach (var f in old) ids.Add(f.Id);
                foreach (var f in made) ids.Add(f.Id);
                body.Group(ids);
            }
            body.Decks = 0; // the plates are shapes now
        }

        /// <summary>The plates (one or two decks), with their hole grid and side walls, as shapes in the new frame.</summary>
        static IEnumerable<BodyFeature> PlatesAsShapes(BodyDesign b)
        {
            float t = b.ThicknessMm;
            bool round = b.Shape == BodyShape.Round;
            float corner = round ? b.WidthMm / 2 : b.Shape == BodyShape.Rounded ? Radius(b) : 0;
            BodyFeature Plate(float bottom) => new BodyFeature
            {
                Kind = FeatureKind.Plate,
                SizeX = b.WidthMm,
                SizeY = t,
                SizeZ = b.EffectiveLength,
                Y = bottom + t / 2 + OldOriginHeight,
                Detail = corner,
                Pitch = b.HoleGrid ? b.HolePitchMm : 0,
                HoleSize = b.HoleDiameterMm,
                Material = b.Material,
            };
            float wallsOn;
            if (b.Decks >= 2)
            {
                yield return Plate(BottomPlateTop - t);
                yield return Plate(TopDeckBottom);
                wallsOn = TopDeckBottom;
            }
            else
            {
                yield return Plate(BottomPlateTop - t);
                wallsOn = BottomPlateTop - t;
            }
            if (b.WallHeightMm <= 0 || round) yield break;
            float inset = b.Shape == BodyShape.Rounded ? Radius(b) : 0;
            float height = t + b.WallHeightMm;
            foreach (float side in new[] { -1f, 1f })
                yield return new BodyFeature
                {
                    Kind = FeatureKind.Box,
                    SizeX = WallThickness,
                    SizeY = height,
                    SizeZ = b.EffectiveLength - 2 * inset,
                    X = side * (b.WidthMm / 2 - WallThickness / 2),
                    Y = wallsOn + height / 2 + OldOriginHeight,
                    Material = b.Material,
                };
        }

        /// <summary>The four 24 mm brass standoffs between two decks, as aluminium cylinders in brass colour.</summary>
        static IEnumerable<BodyFeature> Standoffs(BodyDesign b)
        {
            if (b.Decks < 2) yield break;
            foreach (var (x, z) in StandoffPlaces(b))
                yield return new BodyFeature
                {
                    Kind = FeatureKind.Cylinder,
                    SizeX = 5,
                    SizeY = TopDeckBottom - BottomPlateTop,
                    SizeZ = 5,
                    X = x,
                    Y = (TopDeckBottom + BottomPlateTop) / 2 + OldOriginHeight,
                    Z = z,
                    Material = BodyMaterial.Aluminium,
                    Colour = "#C8A04A",
                };
        }

        public static IEnumerable<(float x, float z)> StandoffPlaces(BodyDesign b)
        {
            if (b.Shape == BodyShape.Round)
            {
                float r = b.WidthMm / 2 - 10;
                for (int i = 0; i < 4; i++)
                {
                    double angle = (45 + 90 * i) * Math.PI / 180;
                    yield return ((float)(r * Math.Cos(angle)), (float)(r * Math.Sin(angle)));
                }
                yield break;
            }
            float inset = b.Shape == BodyShape.Rounded ? Math.Max(10, b.CornerRadiusMm * 0.6f) : 10;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    yield return (sx * (b.WidthMm / 2 - inset), sz * (b.EffectiveLength / 2 - inset));
        }

        static float Radius(BodyDesign b) => Math.Max(1, Math.Min(b.CornerRadiusMm, Math.Min(b.WidthMm, b.EffectiveLength) / 2 - 0.5f));

        /// <summary>
        /// The place a version-2 part had: on the top deck where it was dragged, or at its fixed mount. The right
        /// motor was the left one turned round, so it is turned 180° here and its wheel lands on the right.
        /// </summary>
        static void PlaceAsBefore(RobotDesign design, PartInstance part)
        {
            var b = design.Body;
            var def = PartCatalog.Get(part.Part);
            float deckTop = b.Decks >= 2 ? TopDeckBottom + b.ThicknessMm : BottomPlateTop;
            float axleZ = -b.EffectiveLength / 2 + 50;
            (float x, float y, float z) at;
            float turn = 0;
            switch (def?.Mount)
            {
                case MountKind.Motor:
                {
                    float side = part.Slot == "right" ? 1 : -1;
                    at = (side * (SideHalfWidth(b, axleZ) - 10), -26, axleZ);
                    turn = part.Slot == "right" ? 180 : 0;
                    break;
                }
                case MountKind.Front:
                    at = (0, deckTop + 15, b.EffectiveLength / 2 + 5);
                    break;
                case MountKind.Caster:
                    at = (0, -40, b.EffectiveLength / 2 - 15);
                    break;
                case MountKind.Lower:
                    at = b.Decks >= 2 ? (0, BottomPlateTop + 7.5f, -5) : (0, BottomPlateTop + 7.5f, -b.EffectiveLength / 2 + 40);
                    break;
                default:
                    at = (part.X, deckTop, part.Z);
                    turn = part.Rotation;
                    break;
            }
            part.X = at.x;
            part.Y = at.y + OldOriginHeight;
            part.Z = at.z;
            part.Rotation = turn;
            part.RotX = 0;
            part.RotZ = 0;
        }

        /// <summary>Half the version-2 body's width at a given z; a round body is narrower away from its middle.</summary>
        static float SideHalfWidth(BodyDesign b, float z)
        {
            float half = b.WidthMm / 2;
            return b.Shape == BodyShape.Round ? (float)Math.Sqrt(Math.Max(0, half * half - z * z)) : half;
        }
    }
}
