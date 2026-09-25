using System.Collections.Generic;
using CoreEngine.Sim.Design;
using CoreEngine.Spike.Garage;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoreEngine.Spike
{
    /// <summary>
    /// The robot's rigid-body model (docs/07 §3), the same in the arena and in the Garage's showroom, where it settles
    /// on the turntable: one articulation whose root carries the body's shapes, the parts' boxes and the casters'
    /// balls, and a revolute wheel on every motor's shaft. Pieces not attached to it (<see cref="RobotPieces"/>) are
    /// bodies of their own that fall away: a plate dragged 17 cm off no longer drives along on an invisible arm (the
    /// owner, 2026-09-25: "where is logic and physics").
    /// </summary>
    public static class RobotPhysics
    {
        /// <summary>A group of pieces not attached to the robot: a rigid body of its own, starting where the robot starts.</summary>
        public sealed class LooseBody
        {
            public LooseBody(Rigidbody body, List<string> pieces)
            {
                Body = body;
                Pieces = pieces;
            }

            public Rigidbody Body { get; }
            public List<string> Pieces { get; }
        }

        public const float Mm = 0.001f;
        public const float WheelRadius = DesignGeometry.WheelRadius * Mm;
        public const float WheelMass = DesignGeometry.WheelMassG * Mm;

        /// <summary>The solver settings the robot's small parts and fast wheels need (10 ms steps).</summary>
        public static void ConfigureWorld()
        {
            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.defaultContactOffset = 0.002f;
            Physics.defaultMaxAngularSpeed = 200f;
            Physics.sleepThreshold = 0.001f;
        }

        /// <summary>
        /// Gives <paramref name="root"/> (the design's frame) the robot's colliders, mass and centre of mass, and a
        /// wheel body on every motor. <paramref name="wheels"/> lists each motor with its wheel.
        /// </summary>
        public static ArticulationBody Build(GameObject root, RobotDesign design, BodyMeshes bodyMeshes, double reflectedInertiaKgM2,
            out List<(PartInstance motor, ArticulationBody wheel)> wheels, out List<LooseBody> loose)
        {
            // What holds together with the wheels is the robot; each other group is a body of its own.
            var pieces = RobotPieces.Of(design);
            var holders = new Transform[pieces.Groups.Count];
            var looseGroups = new List<int>();
            for (int g = 0; g < pieces.Groups.Count; g++)
            {
                if (g == pieces.Main)
                {
                    holders[g] = root.transform;
                    continue;
                }
                var piece = new GameObject("Loose piece");
                if (piece.scene != root.scene) SceneManager.MoveGameObjectToScene(piece, root.scene);
                piece.transform.SetPositionAndRotation(root.transform.position, root.transform.rotation);
                holders[g] = piece.transform;
                looseGroups.Add(g);
            }
            Transform HolderOf(string? piece)
            {
                int g = piece == null ? -1 : pieces.GroupOf(piece);
                return g >= 0 ? holders[g] : root.transform;
            }

            // A plate or part scraping the floor slides at 0.4 / 0.35 (docs/07 §2). PhysX's patch friction applies
            // a material's value at each of the two anchors of an edge or face in contact, about twice over, so the
            // material has half of it; and the floor's 0.9 / 0.8 does not come into it (Minimum, not the average).
            // Measured with the owner's robot without a caster turning on the spot (2026-09-25): the plate's edge
            // then resists with 0.33 times its load; before, with 0.4 / 0.35 averaged with the floor's, it was stuck.
            var plastic = new PhysicsMaterial("Plastic")
            {
                staticFriction = 0.2f,
                dynamicFriction = 0.175f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };

            // Every solid piece of the body collides as its convex hull (holes are left out), and so does an
            // imported mesh that is not closed.
            var shapes = new List<(Mesh mesh, string? piece)>();
            for (int i = 0; i < bodyMeshes.Loose.Count; i++)
                shapes.Add((bodyMeshes.Loose[i], i < bodyMeshes.NotClosed.Count ? RobotPieces.ShapePrefix + bodyMeshes.NotClosed[i] : null));
            foreach (var solid in bodyMeshes.Solids) shapes.Add((solid.Mesh, RobotPieces.PieceOf(design, solid.Id)));
            foreach (var (mesh, piece) in shapes)
            {
                var shape = new GameObject("ShapeCollider");
                shape.transform.SetParent(HolderOf(piece), false);
                var hull = shape.AddComponent<MeshCollider>();
                hull.sharedMesh = mesh;
                hull.convex = true;
                hull.material = plastic;
            }

            // Parts collide as their boxes where they were put; the caster as its 20 mm ball, which barely rubs.
            var casterMaterial = new PhysicsMaterial("Caster")
            {
                staticFriction = 0.02f,
                dynamicFriction = 0.02f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };
            foreach (var part in design.Parts)
            {
                var def = PartCatalog.Get(part.Part);
                if (def == null) continue;
                if (def.Kind == PartKind.Caster)
                {
                    var c = DesignGeometry.CasterBall(part);
                    var ball = HolderOf(part.Id).gameObject.AddComponent<SphereCollider>();
                    ball.center = new Vector3(c.x, c.y, c.z) * Mm;
                    ball.radius = DesignGeometry.CasterBallRadius * Mm;
                    ball.material = casterMaterial;
                    continue;
                }
                var holder = new GameObject("PartCollider " + part.Id);
                holder.transform.SetParent(HolderOf(part.Id), false);
                holder.transform.localPosition = new Vector3(part.X, part.Y, part.Z) * Mm;
                holder.transform.localRotation = Quaternion.Euler(part.RotX, part.Rotation, part.RotZ);
                var box = holder.AddComponent<BoxCollider>();
                box.center = new Vector3(def.BoxCentre.x, def.BoxCentre.y, def.BoxCentre.z) * Mm;
                box.size = new Vector3(def.SizeX, def.SizeY, def.SizeZ) * Mm;
                box.material = plastic;
            }

            // Mass and centre of mass from the parts (docs/09 masses) and the body's exact volumes from Manifold,
            // each of its own material; the wheels are bodies of their own.
            var chassis = root.AddComponent<ArticulationBody>();
            var robot = pieces.AllAttached || pieces.Main < 0 ? design : pieces.Keep(design, pieces.Main);
            double partsGrams = design.MassKg() * 1000 - DesignGeometry.BodyMassG(design.Body);
            chassis.mass = robot == design // the body's exact volumes from Manifold, or the shapes' when the robot is only part of the design
                ? Mathf.Max(0.02f, (float)((partsGrams + bodyMeshes.MassG) / 1000) - design.Count(PartCatalog.TtMotor) * WheelMass)
                : Mathf.Max(0.02f, (float)robot.MassKg() - robot.Count(PartCatalog.TtMotor) * WheelMass);
            var com = DesignGeometry.CentreOfMass(robot, wheels: false);
            chassis.automaticCenterOfMass = false;
            chassis.centerOfMass = new Vector3(com.x, com.y, com.z) * Mm;
            chassis.linearDamping = 0f;
            chassis.angularDamping = 0.05f;
            chassis.maxAngularVelocity = 30f; // a robot turns at most a few times a second; this keeps the solver in hand
            if (robot != design)
            {
                // Only part of the design holds together. A light remainder (a lone motor, when everything else came
                // loose) would be spun up by its wheel's geared inertia past what the solver holds (the benchmark saw
                // one fly 300 m): its body gets at least twice a wheel's inertia about every axis.
                var box = PiecesBounds(robot);
                var size = new Vector3(box.max.x - box.min.x, box.max.y - box.min.y, box.max.z - box.min.z) * Mm;
                var own = new Vector3(size.y * size.y + size.z * size.z, size.x * size.x + size.z * size.z, size.x * size.x + size.y * size.y) * (chassis.mass / 12f);
                float wheel = 0.5f * WheelMass * WheelRadius * WheelRadius + (float)reflectedInertiaKgM2;
                chassis.automaticInertiaTensor = false;
                chassis.inertiaTensor = Vector3.Max(own, Vector3.one * (2f * wheel));
                chassis.inertiaTensorRotation = Quaternion.identity;
            }

            // A wheel on every motor's shaft, turning about the shaft; a loose motor's wheel falls with it.
            wheels = new List<(PartInstance, ArticulationBody)>();
            foreach (var part in robot.Parts)
            {
                if (PartCatalog.Get(part.Part)?.Kind != PartKind.Motor) continue;
                var w = DesignGeometry.WheelCentre(part);
                wheels.Add((part, BuildWheel(root.transform, new Vector3(w.x, w.y, w.z) * Mm, Quaternion.Euler(part.RotX, part.Rotation, part.RotZ), part.Id, reflectedInertiaKgM2)));
            }

            // The loose groups: each its own mass and centre of mass, a motor's wheel falling with it.
            loose = new List<LooseBody>();
            foreach (int g in looseGroups)
            {
                var group = pieces.Keep(design, g);
                var body = holders[g].gameObject.AddComponent<Rigidbody>();
                body.mass = Mathf.Max(0.005f, (float)group.MassKg());
                var centre = DesignGeometry.CentreOfMass(group);
                body.automaticCenterOfMass = false;
                body.centerOfMass = new Vector3(centre.x, centre.y, centre.z) * Mm;
                foreach (var motor in group.Parts)
                {
                    if (PartCatalog.Get(motor.Part)?.Kind != PartKind.Motor) continue;
                    var w = DesignGeometry.WheelCentre(motor);
                    var tyre = new GameObject("LooseWheel " + motor.Id);
                    tyre.transform.SetParent(holders[g], false);
                    tyre.transform.localPosition = new Vector3(w.x, w.y, w.z) * Mm;
                    tyre.AddComponent<SphereCollider>().radius = WheelRadius;
                }
                loose.Add(new LooseBody(body, pieces.Groups[g]));
            }
            return chassis;
        }

        /// <summary>The box round a design's parts and solid shapes (mm, chassis frame).</summary>
        static ((float x, float y, float z) min, (float x, float y, float z) max) PiecesBounds(RobotDesign design)
        {
            var min = (x: float.MaxValue, y: float.MaxValue, z: float.MaxValue);
            var max = (x: float.MinValue, y: float.MinValue, z: float.MinValue);
            void Take(((float x, float y, float z) min, (float x, float y, float z) max) b)
            {
                min = (Mathf.Min(min.x, b.min.x), Mathf.Min(min.y, b.min.y), Mathf.Min(min.z, b.min.z));
                max = (Mathf.Max(max.x, b.max.x), Mathf.Max(max.y, b.max.y), Mathf.Max(max.z, b.max.z));
            }
            foreach (var part in design.Parts) Take(DesignGeometry.PartBounds(part));
            foreach (var shape in design.Body.Features)
                if (shape.Kind != FeatureKind.Group && !shape.Hole) Take(DesignGeometry.FeatureBounds(shape));
            return min.x > max.x ? ((0f, 0f, 0f), (1f, 1f, 1f)) : (min, max);
        }

        /// <summary>A wheel body on a motor's shaft: its joint turns about its own x axis, which is the shaft.</summary>
        static ArticulationBody BuildWheel(Transform parent, Vector3 position, Quaternion motorTurn, string motorId, double reflectedInertiaKgM2)
        {
            var wheel = new GameObject("Wheel " + motorId);
            wheel.transform.SetParent(parent, false);
            wheel.transform.localPosition = position;
            wheel.transform.localRotation = motorTurn;

            var collider = wheel.AddComponent<SphereCollider>();
            collider.radius = WheelRadius;
            collider.material = new PhysicsMaterial("Rubber") { staticFriction = 0.9f, dynamicFriction = 0.8f };

            var body = wheel.AddComponent<ArticulationBody>();
            body.jointType = ArticulationJointType.RevoluteJoint;
            body.anchorRotation = Quaternion.identity; // revolute joints turn about the anchor's X axis
            body.mass = WheelMass;
            body.automaticInertiaTensor = false;
            float axle = 0.5f * WheelMass * WheelRadius * WheelRadius + (float)reflectedInertiaKgM2;
            body.inertiaTensor = new Vector3(axle, 1e-5f, 1e-5f);
            body.inertiaTensorRotation = Quaternion.identity;
            body.jointFriction = 0f;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.maxAngularVelocity = 200f;
            return body;
        }
    }
}
