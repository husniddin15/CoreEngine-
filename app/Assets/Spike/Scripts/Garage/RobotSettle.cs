using System.Collections.Generic;
using CoreEngine.Sim.Components;
using CoreEngine.Sim.Design;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// The robot let go on a flat floor or on a turntable standing on a desk, in a physics scene of its own, built as
    /// the arena builds it (<see cref="RobotPhysics"/>): where its frame is, step by step, until it comes to rest. The
    /// showroom plays that on the turntable, so a robot without a caster tips onto its front there as it does in the
    /// arena (the owner, 2026-09-25: "in main page add physics there, only in building and wiring there will be no
    /// physics"), and a piece not attached to it past the turntable's edge falls off onto the desk.
    /// </summary>
    public static class RobotSettle
    {
        public const float Step = 0.01f;
        const int MaxSteps = 250;
        static int scenes;
        static PhysicsMaterial? rubber, mat;
        static Mesh? stand;
        static (float radius, float height) standSize;

        /// <summary>A pose of the robot's frame over the floor (the floor's top is y = 0).</summary>
        public readonly struct Pose
        {
            public Pose(Vector3 position, Quaternion rotation)
            {
                Position = position;
                Rotation = rotation;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }

        /// <summary>The robot's poses as it comes to rest, and those of each group of pieces not attached to it, falling off.</summary>
        public sealed class Settling
        {
            public List<Pose> Robot { get; } = new List<Pose>();
            public List<(List<string> Pieces, List<Pose> Poses)> Loose { get; } = new List<(List<string>, List<Pose>)>();

            /// <summary>The loose groups (their indices in <see cref="Loose"/>) that fell off the turntable's edge onto the desk.</summary>
            public List<int> OnTheDesk { get; } = new List<int>();

            /// <summary>The last pose of each: where everything lies once still.</summary>
            public Pose RobotAtRest => Robot[Robot.Count - 1];
        }

        /// <summary>
        /// Every 10 ms, from where the robot stands on its lowest point, half a millimetre up, until everything has
        /// been still for 0.2 s (2.5 s at most): the robot's frame and each loose group's (both in the design's frame
        /// at the start). The floor (y = 0) is flat, or with <paramref name="standRadius"/> the top of a turntable of
        /// that radius and <paramref name="standHeight"/> high standing on a desk. A design without parts or shapes
        /// stays where it is.
        /// </summary>
        public static Settling Drop(RobotDesign design, BodyMeshes body, float standRadius = 0, float standHeight = 0)
        {
            var start = new Vector3(0, (0.5f - DesignGeometry.LowestPoint(design)) * RobotPhysics.Mm, 0);
            var result = new Settling();
            result.Robot.Add(new Pose(start, Quaternion.identity));
            if (design.Parts.Count == 0 && body.Solids.Count == 0 && body.Loose.Count == 0) return result;

            RobotPhysics.ConfigureWorld();
            var scene = SceneManager.CreateScene("RobotSettle " + ++scenes, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physics = scene.GetPhysicsScene();
            var floor = new GameObject("Floor");
            SceneManager.MoveGameObjectToScene(floor, scene);
            rubber ??= new PhysicsMaterial("Turntable rubber") { staticFriction = 0.9f, dynamicFriction = 0.8f };
            var top = floor.AddComponent<BoxCollider>();
            top.center = new Vector3(0, -0.01f, 0);
            top.size = new Vector3(2f, 0.02f, 2f);
            top.material = rubber;
            if (standRadius > 0)
            {
                // The turntable's disc on the desk: a piece past its edge falls onto the desk's mat. The robot itself
                // stands on it whatever its size (one wider than the turntable hangs over its edge, as on a flat
                // floor): only pieces not attached to it fall off.
                var pieces = RobotPieces.Of(design);
                var (min, max) = RobotPhysics.PiecesBounds(pieces.AllAttached ? design : pieces.Keep(design, pieces.Main));
                float reach = new Vector2(Mathf.Max(-min.x, max.x), Mathf.Max(-min.z, max.z)).magnitude * RobotPhysics.Mm;
                standRadius = Mathf.Max(standRadius, reach + 0.002f);
                mat ??= new PhysicsMaterial("Desk mat") { staticFriction = 0.7f, dynamicFriction = 0.6f };
                top.center = new Vector3(0, -standHeight - 0.01f, 0);
                top.material = mat;
                var disc = floor.AddComponent<MeshCollider>();
                disc.sharedMesh = Stand(standRadius, standHeight);
                disc.convex = true;
                disc.material = rubber;
            }
            var root = new GameObject("Robot");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = start;
            var chassis = RobotPhysics.Build(root, design, body, DcMotorModel.TtGearMotor148().ReflectedInertiaKgM2, out _, out var loose);
            foreach (var piece in loose) result.Loose.Add((piece.Pieces, new List<Pose> { new Pose(start, Quaternion.identity) }));

            int still = 0;
            for (int i = 0; i < MaxSteps && still < 20; i++)
            {
                physics.Simulate(Step);
                result.Robot.Add(new Pose(root.transform.position, root.transform.rotation));
                bool resting = chassis.velocity.sqrMagnitude < 1e-6f && chassis.angularVelocity.sqrMagnitude < 1e-4f;
                for (int k = 0; k < loose.Count; k++)
                {
                    var t = loose[k].Body.transform;
                    result.Loose[k].Poses.Add(new Pose(t.position, t.rotation));
                    resting &= loose[k].Body.linearVelocity.sqrMagnitude < 1e-6f && loose[k].Body.angularVelocity.sqrMagnitude < 1e-4f;
                }
                still = resting && i > 10 ? still + 1 : 0;
            }
            if (standRadius > 0)
                for (int k = 0; k < loose.Count; k++)
                {
                    // At rest with its centre of mass past the edge, a piece can only lie on the desk.
                    var centre = loose[k].Body.worldCenterOfMass;
                    if (new Vector2(centre.x, centre.z).magnitude > standRadius) result.OnTheDesk.Add(k);
                }
            SceneManager.UnloadSceneAsync(scene);
            return result;
        }

        /// <summary>A 48-sided disc from y = -height to 0, for a convex collider; made once for each size.</summary>
        static Mesh Stand(float radius, float height)
        {
            if (stand != null && standSize == (radius, height)) return stand;
            const int sides = 48;
            var vertices = new Vector3[sides * 2];
            var triangles = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float angle = i * 2f * Mathf.PI / sides;
                vertices[i] = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                vertices[sides + i] = vertices[i] - new Vector3(0, height, 0);
                int next = (i + 1) % sides;
                triangles.AddRange(new[] { i, next, sides + i, next, sides + next, sides + i });
                if (i > 1) triangles.AddRange(new[] { 0, i, i - 1, sides, sides + i - 1, sides + i });
            }
            if (stand != null) Object.Destroy(stand);
            stand = new Mesh { name = "Turntable", vertices = vertices, triangles = triangles.ToArray() };
            standSize = (radius, height);
            return stand;
        }
    }
}
