using System;

namespace CoreEngine.Sim.Design
{
    /// <summary>
    /// A rotation as a 3 × 3 matrix, for placing parts and shapes in the chassis frame without an engine. Euler
    /// angles follow Unity's <c>Quaternion.Euler(x, y, z)</c>: z first, then x, then y (R = Ry · Rx · Rz), so a
    /// part turned in the Garage and the same part in the core agree to the last digit.
    /// </summary>
    public readonly struct Rot3
    {
        public readonly double M00, M01, M02, M10, M11, M12, M20, M21, M22;

        public Rot3(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22)
        {
            M00 = m00; M01 = m01; M02 = m02;
            M10 = m10; M11 = m11; M12 = m12;
            M20 = m20; M21 = m21; M22 = m22;
        }

        public static readonly Rot3 Identity = new Rot3(1, 0, 0, 0, 1, 0, 0, 0, 1);

        /// <summary>Degrees about x, y and z, applied z, then x, then y, as Unity does.</summary>
        public static Rot3 Euler(double xDegrees, double yDegrees, double zDegrees)
        {
            double x = xDegrees * Math.PI / 180, y = yDegrees * Math.PI / 180, z = zDegrees * Math.PI / 180;
            double cx = Math.Cos(x), sx = Math.Sin(x), cy = Math.Cos(y), sy = Math.Sin(y), cz = Math.Cos(z), sz = Math.Sin(z);
            return new Rot3(
                cy * cz + sy * sx * sz, -cy * sz + sy * sx * cz, sy * cx,
                cx * sz, cx * cz, -sx,
                -sy * cz + cy * sx * sz, sy * sz + cy * sx * cz, cy * cx);
        }

        /// <summary>A turn of <paramref name="degrees"/> about a unit axis (right-hand rule in the numbers, as Unity's AngleAxis).</summary>
        public static Rot3 AngleAxis(double degrees, (double x, double y, double z) axis)
        {
            double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), t = 1 - c;
            double length = Math.Sqrt(axis.x * axis.x + axis.y * axis.y + axis.z * axis.z);
            double x = axis.x / length, y = axis.y / length, z = axis.z / length;
            return new Rot3(
                t * x * x + c, t * x * y - s * z, t * x * z + s * y,
                t * x * y + s * z, t * y * y + c, t * y * z - s * x,
                t * x * z - s * y, t * y * z + s * x, t * z * z + c);
        }

        public (float x, float y, float z) Apply(float x, float y, float z) =>
            ((float)(M00 * x + M01 * y + M02 * z), (float)(M10 * x + M11 * y + M12 * z), (float)(M20 * x + M21 * y + M22 * z));

        public (float x, float y, float z) Apply((float x, float y, float z) v) => Apply(v.x, v.y, v.z);

        /// <summary>The opposite turn.</summary>
        public Rot3 Inverse() => new Rot3(M00, M10, M20, M01, M11, M21, M02, M12, M22);

        public static Rot3 operator *(Rot3 a, Rot3 b) => new Rot3(
            a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20, a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21, a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
            a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20, a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21, a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
            a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20, a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21, a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22);

        /// <summary>
        /// Euler degrees that give this rotation back through <see cref="Euler"/>: x in [-90, 90], y and z in
        /// (-180, 180], rounded to 0.001°. Straight up or down (x = ±90°) the turn about z is folded into y.
        /// </summary>
        public (float x, float y, float z) ToEuler()
        {
            double sx = Math.Max(-1, Math.Min(1, -M12));
            double x = Math.Asin(sx), y, z;
            if (Math.Abs(sx) < 0.999999)
            {
                y = Math.Atan2(M02, M22);
                z = Math.Atan2(M10, M11);
            }
            else
            {
                y = Math.Atan2(-M20, M00);
                z = 0;
            }
            return (Degrees(x), Degrees(y), Degrees(z));
        }

        static float Degrees(double radians)
        {
            double d = Math.Round(radians * 180 / Math.PI, 3);
            if (d <= -180) d += 360;
            if (d > 180) d -= 360;
            return d == 0 ? 0 : (float)d; // no -0
        }
    }
}
