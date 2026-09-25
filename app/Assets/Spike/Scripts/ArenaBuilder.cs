using UnityEngine;

namespace CoreEngine.Spike
{
    /// <summary>
    /// The obstacle field (docs/10): a 3 × 3 m laminate floor in 30 cm tiles, low walls round it and nine cardboard
    /// boxes with packing tape, placed from a fixed seed, so every run and every picture of it is the same. The
    /// arena builds it, and so does the Garage for the arena picker's picture.
    /// </summary>
    public static class ArenaBuilder
    {
        /// <summary>Builds the field under <paramref name="parent"/> (null: the scene root), the floor's top at the parent's y = 0.</summary>
        public static void ObstacleField(Transform? parent, Material floorTemplate, Material wallMaterial, Material obstacleMaterial)
        {
            var floorMat = new PhysicsMaterial("Laminate") { staticFriction = 0.9f, dynamicFriction = 0.8f };
            var laminate = new Material(floorTemplate);
            laminate.SetTexture("_BaseMap", FloorTexture());
            laminate.SetTextureScale("_BaseMap", new Vector2(10, 10)); // 30 cm tiles on the 3 m floor
            laminate.SetFloat("_Smoothness", 0.45f);
            var floor = Box(parent, "Floor", new Vector3(0, -0.01f, 0), new Vector3(3f, 0.02f, 3f), laminate);
            floor.GetComponent<Collider>().material = floorMat;

            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float offset = side % 2 == 0 ? 1.5f : -1.5f;
                var pos = alongX ? new Vector3(0, 0.05f, offset) : new Vector3(offset, 0.05f, 0);
                var size = alongX ? new Vector3(3.04f, 0.1f, 0.02f) : new Vector3(0.02f, 0.1f, 3.04f);
                Box(parent, "Wall", pos, size, wallMaterial);
            }

            var tape = new Material(obstacleMaterial);
            tape.SetColor("_BaseColor", new Color(0.78f, 0.62f, 0.38f));
            tape.SetFloat("_Smoothness", 0.7f);
            var random = new System.Random(7);
            int placed = 0;
            while (placed < 9)
            {
                var pos = new Vector3((float)(random.NextDouble() * 2.4 - 1.2), 0, (float)(random.NextDouble() * 2.4 - 1.2));
                if (pos.magnitude < 0.5f) continue; // keep the start area clear
                float w = 0.1f + (float)random.NextDouble() * 0.15f;
                float d = 0.1f + (float)random.NextDouble() * 0.15f;
                var turn = Quaternion.Euler(0, (float)random.NextDouble() * 90f, 0);
                var centre = new Vector3(pos.x, 0.075f, pos.z);
                Box(parent, "Obstacle", centre, new Vector3(w, 0.15f, d), obstacleMaterial).transform.localRotation = turn;
                // Cardboard boxes: packing tape across the lid and down the two ends.
                Strip(Box(parent, "Tape", centre + new Vector3(0, 0.0755f, 0), new Vector3(w + 0.002f, 0.001f, 0.048f), tape), turn);
                foreach (float end in new[] { -1f, 1f })
                    Strip(Box(parent, "Tape", centre + turn * Vector3.right * end * (w / 2 + 0.0005f) + new Vector3(0, 0.04f, 0),
                              new Vector3(0.001f, 0.07f, 0.048f), tape), turn);
                placed++;
            }
        }

        static void Strip(GameObject tape, Quaternion turn)
        {
            tape.transform.localRotation = turn;
            Object.Destroy(tape.GetComponent<Collider>());
        }

        /// <summary>A cube of the given size, its centre at <paramref name="position"/> in the parent's frame.</summary>
        public static GameObject Box(Transform? parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>One 30 cm laminate tile with faint grain and a dark seam, made in code (no texture files).</summary>
        static Texture2D FloorTexture()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "LaminateTile",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float grain = 0.965f + 0.035f * Mathf.PerlinNoise(0.37f, y * 0.21f);
                for (int x = 0; x < size; x++)
                {
                    float v = x < 2 || y < 2 ? 0.74f : grain * (0.985f + 0.015f * Mathf.PerlinNoise(x * 0.05f, y * 0.9f));
                    pixels[y * size + x] = new Color(0.95f * v, 0.92f * v, 0.86f * v);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
