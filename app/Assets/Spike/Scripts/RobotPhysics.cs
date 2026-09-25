using System.Collections.Generic;
using CoreEngine.Sim.Design;
using CoreEngine.Spike.Garage;
using UnityEngine;

namespace CoreEngine.Spike
{
    /// <summary>
    /// The robot's rigid-body model (docs/07 §3), the same in the arena and in the Garage's showroom, where it settles
    /// on the turntable: one articulation whose root carries the body's shapes, the parts' boxes and the casters'
    /// balls, and a revolute wheel on every motor's shaft.
    /// </summary>
    public static class RobotPhysics
    {
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
            out List<(PartInstance motor, ArticulationBody wheel)> wheels)
        {
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
            var shapes = new List<Mesh>(bodyMeshes.Loose);
            foreach (var solid in bodyMeshes.Solids) shapes.Add(solid.Mesh);
            foreach (var mesh in shapes)
            {
                var shape = new GameObject("ShapeCollider");
                shape.transform.SetParent(root.transform, false);
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
                    var ball = root.AddComponent<SphereCollider>();
                    ball.center = new Vector3(c.x, c.y, c.z) * Mm;
                    ball.radius = DesignGeometry.CasterBallRadius * Mm;
                    ball.material = casterMaterial;
                    continue;
                }
                var holder = new GameObject("PartCollider " + part.Id);
                holder.transform.SetParent(root.transform, false);
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
            double partsGrams = design.MassKg() * 1000 - DesignGeometry.BodyMassG(design.Body);
            chassis.mass = Mathf.Max(0.02f, (float)((partsGrams + bodyMeshes.MassG) / 1000) - design.Count(PartCatalog.TtMotor) * WheelMass);
            var com = DesignGeometry.CentreOfMass(design, wheels: false);
            chassis.automaticCenterOfMass = false;
            chassis.centerOfMass = new Vector3(com.x, com.y, com.z) * Mm;
            chassis.linearDamping = 0f;
            chassis.angularDamping = 0.05f;

            // A wheel on every motor's shaft, turning about the shaft.
            wheels = new List<(PartInstance, ArticulationBody)>();
            foreach (var part in design.Parts)
            {
                if (PartCatalog.Get(part.Part)?.Kind != PartKind.Motor) continue;
                var w = DesignGeometry.WheelCentre(part);
                wheels.Add((part, BuildWheel(root.transform, new Vector3(w.x, w.y, w.z) * Mm, Quaternion.Euler(part.RotX, part.Rotation, part.RotZ), part.Id, reflectedInertiaKgM2)));
            }
            return chassis;
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
