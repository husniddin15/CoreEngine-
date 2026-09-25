using System.Collections.Generic;
using CoreEngine.Sim.Components;
using CoreEngine.Sim.Design;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// The robot let go on a flat floor, in a physics scene of its own, built as the arena builds it
    /// (<see cref="RobotPhysics"/>): where its frame is, step by step, until it comes to rest. The showroom plays
    /// that on the turntable, so a robot without a caster tips onto its front there as it does in the arena (the
    /// owner, 2026-09-25: "in main page add physics there, only in building and wiring there will be no physics").
    /// </summary>
    public static class RobotSettle
    {
        public const float Step = 0.01f;
        const int MaxSteps = 250;
        static int scenes;

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

        /// <summary>
        /// The frame's pose every 10 ms from where the robot stands on its lowest point, half a millimetre up, until
        /// it has been still for 0.2 s (2.5 s at most). A design without parts or shapes stays where it is.
        /// </summary>
        public static List<Pose> Drop(RobotDesign design, BodyMeshes body)
        {
            var start = new Vector3(0, (0.5f - DesignGeometry.LowestPoint(design)) * RobotPhysics.Mm, 0);
            var poses = new List<Pose> { new Pose(start, Quaternion.identity) };
            if (design.Parts.Count == 0 && body.Solids.Count == 0 && body.Loose.Count == 0) return poses;

            RobotPhysics.ConfigureWorld();
            var scene = SceneManager.CreateScene("RobotSettle " + ++scenes, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physics = scene.GetPhysicsScene();
            var floor = new GameObject("Floor");
            SceneManager.MoveGameObjectToScene(floor, scene);
            var top = floor.AddComponent<BoxCollider>();
            top.center = new Vector3(0, -0.01f, 0);
            top.size = new Vector3(2f, 0.02f, 2f);
            top.material = new PhysicsMaterial("Turntable rubber") { staticFriction = 0.9f, dynamicFriction = 0.8f };
            var root = new GameObject("Robot");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = start;
            var chassis = RobotPhysics.Build(root, design, body, DcMotorModel.TtGearMotor148().ReflectedInertiaKgM2, out _);

            int still = 0;
            for (int i = 0; i < MaxSteps && still < 20; i++)
            {
                physics.Simulate(Step);
                poses.Add(new Pose(root.transform.position, root.transform.rotation));
                bool resting = chassis.velocity.sqrMagnitude < 1e-6f && chassis.angularVelocity.sqrMagnitude < 1e-4f;
                still = resting && i > 10 ? still + 1 : 0;
            }
            SceneManager.UnloadSceneAsync(scene);
            return poses;
        }
    }
}
