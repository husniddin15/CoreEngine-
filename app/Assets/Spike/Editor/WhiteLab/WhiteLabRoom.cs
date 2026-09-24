using CoreEngine.Spike.Parts;
using UnityEngine;

namespace CoreEngine.Spike.Editor
{
    public static partial class WhiteLab
    {
        const float WindowWidth = 2200f, WindowHeight = 1500f, WindowBottom = -80f, WindowZ = 900f;

        /// <summary>The ceiling's LED panels (x, z): two over the bench, two over the room.</summary>
        static readonly (float x, float z)[] PanelPlaces = { (-700, -80), (700, -80), (-700, 1700), (700, 1700) };

        static Material WallPaint => Plain("Wall paint", new Color(0.86f, 0.865f, 0.86f), 0.12f);

        /// <summary>Floor, walls, skirting: a light vinyl floor in 600 mm tiles and white walls.</summary>
        static void Room()
        {
            var floorArt = new Raster(600, 600, 1.2f);
            floorArt.Fill(Ink.Solid(new Color32(178, 180, 182, 255), 0, 0.5f, 0));
            floorArt.Grain(0.045f, 0.08f, 3.0f, 7);
            floorArt.Grain(0.025f, 0.05f, 0.9f, 8);
            var grout = Ink.Solid(new Color32(150, 152, 155, 255), 0, 0.3f, -0.4f);
            floorArt.Rect(0, 0, 600, 1.2f, grout);
            floorArt.Rect(0, 0, 1.2f, 600, grout);
            var floor = Painted("Floor tiles", floorArt, 0.6f, repeat: true);
            float width = RightWall - LeftWall, depth = FrontWall - BackWall, height = CeilingY - FloorY;
            float midZ = (FrontWall + BackWall) / 2, midY = (FloorY + CeilingY) / 2;
            Shell(Slab("Floor", new Vector3(0, FloorY - 10, midZ), new Vector3(width, 20, depth), floor, 0, 0.6f, 1f / 600));
            Shell(Slab("CeilingSlab", new Vector3(0, CeilingY + 10, midZ), new Vector3(width, 20, depth), Plain("Ceiling paint", new Color(0.9f, 0.9f, 0.9f), 0.1f), 0, 0.5f));

            var paint = WallPaint;
            Shell(Slab("BackWall", new Vector3(0, midY, BackWall - 50), new Vector3(width + 200, height, 100), paint, 0, 0.8f));
            Shell(Slab("FrontWall", new Vector3(0, midY, FrontWall + 50), new Vector3(width + 200, height, 100), paint, 0, 0.5f));
            Shell(Slab("LeftWall", new Vector3(LeftWall - 50, midY, midZ), new Vector3(100, height, depth), paint, 0, 0.5f));
            // The right wall around the window's opening.
            float z0 = WindowZ - WindowWidth / 2, z1 = WindowZ + WindowWidth / 2, y0 = WindowBottom, y1 = WindowBottom + WindowHeight;
            Shell(Slab("RightWallBelow", new Vector3(RightWall + 50, (FloorY + y0) / 2, midZ), new Vector3(100, y0 - FloorY, depth), paint, 0, 0.5f));
            Shell(Slab("RightWallAbove", new Vector3(RightWall + 50, (y1 + CeilingY) / 2, midZ), new Vector3(100, CeilingY - y1, depth), paint, 0, 0.5f));
            Shell(Slab("RightWallBack", new Vector3(RightWall + 50, (y0 + y1) / 2, (BackWall + z0) / 2), new Vector3(100, y1 - y0, z0 - BackWall), paint, 0, 0.5f));
            Shell(Slab("RightWallFront", new Vector3(RightWall + 50, (y0 + y1) / 2, (z1 + FrontWall) / 2), new Vector3(100, y1 - y0, FrontWall - z1), paint, 0, 0.5f));

            // Skirting boards, light grey, along the walls.
            var skirting = Plain("Skirting", new Color(0.62f, 0.63f, 0.65f), 0.3f);
            Shell(Slab("SkirtingBack", new Vector3(0, FloorY + 40, BackWall + 6), new Vector3(width, 80, 12), skirting, 1, 0.3f));
            Shell(Slab("SkirtingFront", new Vector3(0, FloorY + 40, FrontWall - 6), new Vector3(width, 80, 12), skirting, 1, 0.3f));
            Shell(Slab("SkirtingLeft", new Vector3(LeftWall + 6, FloorY + 40, midZ), new Vector3(12, 80, depth), skirting, 1, 0.3f));
            Shell(Slab("SkirtingRight", new Vector3(RightWall - 6, FloorY + 40, midZ), new Vector3(12, 80, depth), skirting, 1, 0.3f));
        }

        /// <summary>
        /// The window on the right: a white frame, the bright day behind it (a glowing panel the bake counts as
        /// light) and white blinds tilted half open, whose slats stripe the light.
        /// </summary>
        static void Window()
        {
            float z0 = WindowZ - WindowWidth / 2, z1 = WindowZ + WindowWidth / 2, y0 = WindowBottom, y1 = WindowBottom + WindowHeight;
            var frame = Plain("Window frame", new Color(0.9f, 0.9f, 0.9f), 0.4f);
            var kit = new MeshKit(1);
            const float bar = 55, deep = 110;
            kit.Box(0, new Vector3(0, y0 - bar / 2, WindowZ), new Vector3(deep, bar, WindowWidth + 2 * bar), 3);
            kit.Box(0, new Vector3(0, y1 + bar / 2, WindowZ), new Vector3(deep, bar, WindowWidth + 2 * bar), 3);
            kit.Box(0, new Vector3(0, (y0 + y1) / 2, z0 - bar / 2), new Vector3(deep, WindowHeight, bar), 3);
            kit.Box(0, new Vector3(0, (y0 + y1) / 2, z1 + bar / 2), new Vector3(deep, WindowHeight, bar), 3);
            kit.Box(0, new Vector3(0, (y0 + y1) / 2, WindowZ), new Vector3(deep * 0.8f, WindowHeight, 40), 2);
            // The sill inside.
            kit.Box(0, new Vector3(-90, y0 - 12, WindowZ), new Vector3(180, 24, WindowWidth + 2 * bar + 60), 4);
            Shell(Place("WindowFrame", kit, new[] { frame }, new Vector3(RightWall + 20, 0, 0), null, 0.6f));

            // The day outside: bright and a little blue at the top.
            var sky = new Raster(40, 30, 4);
            for (int i = 0; i < 30; i++)
            {
                float t = i / 29f;
                var c = Color.Lerp(new Color(0.93f, 0.95f, 0.97f), new Color(0.72f, 0.82f, 0.95f), t);
                sky.Rect(0, i, 40, i + 1, Ink.Solid((Color32)c, 0, 0.1f, 0));
            }
            var outside = Painted("Daylight", sky, 0, glow: 2.6f);
            var glass = new MeshKit(1);
            glass.Quad(0, new Vector3(0, y0, z1), new Vector3(0, y0, z0), new Vector3(0, y1, z0), new Vector3(0, y1, z1), Vector3.left);
            Shell(Place("Daylight", glass, new[] { outside }, new Vector3(RightWall + 90, 0, 0), null, 0.2f, null, false));

            // Blinds: white aluminium slats every 25 mm, tilted 40°, under a head rail.
            var blinds = new MeshKit(1);
            for (float y = y0 + 20; y < y1 - 30; y += 25)
            {
                blinds.Push(new Vector3(0, y, WindowZ), Quaternion.Euler(0, 0, 40));
                blinds.Box(0, Vector3.zero, new Vector3(25, 0.7f, WindowWidth - 30), 0);
                blinds.Pop();
            }
            blinds.Box(0, new Vector3(0, y1 - 18, WindowZ), new Vector3(40, 36, WindowWidth), 4);
            Shell(Place("Blinds", blinds, new[] { Plain("Blinds", new Color(0.93f, 0.93f, 0.92f), 0.35f) }, new Vector3(RightWall - 70, 0, 0), null, 0.25f));
        }

        /// <summary>The ceiling's LED panels: a thin white frame round a glowing diffuser.</summary>
        static void Ceiling()
        {
            var frame = Plain("Panel frame", new Color(0.88f, 0.88f, 0.88f), 0.5f, 0.2f);
            var diffuser = Glow("Panel light", new Color(1f, 0.99f, 0.97f), 3.2f);
            foreach (var (x, z) in PanelPlaces)
            {
                var kit = new MeshKit(2);
                kit.Box(0, new Vector3(0, -6, 0), new Vector3(620, 12, 1220), 2);
                kit.Quad(1, new Vector3(-295, -12.5f, -595), new Vector3(295, -12.5f, -595), new Vector3(295, -12.5f, 595), new Vector3(-295, -12.5f, 595), Vector3.down);
                Shell(Place("CeilingPanel", kit, new[] { frame, diffuser }, new Vector3(x, CeilingY, z), null, 0.3f, null, false));
            }
        }
    }
}
