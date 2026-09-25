using System.Collections.Generic;
using CoreEngine.Sim.Design;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// The Balance view in the Body Studio (research R5: KSP's centre-of-mass marker, Webots' support polygon): the
    /// robot's centre of mass as a yellow ball with a plumb line to the floor, and the patch its wheels and casters
    /// stand on — green when the robot stands on them, red when it tips (<see cref="RobotStance"/>). It is geometry
    /// only, so building stays free of physics (the owner, 2026-09-25). It comes on by itself when a change makes
    /// the robot tip, and with the Balance button; a line over the view says what it means.
    /// </summary>
    public sealed partial class GarageSpike
    {
        const float BalanceEvery = 0.1f; // s: the view follows a part being dragged

        bool balanceOn, balanceTipped, balanceLoose;
        readonly List<Transform> looseBoxes = new List<Transform>(); // a red box round each piece not attached to the robot
        Material? balanceLooseMat;
        float balanceAt;
        Transform? balanceRoot, comBall, plumbLine, plumbFoot;
        MeshFilter? supportPatch;
        Material? balanceGood, balanceBad, balanceGoodFoot, balanceBadFoot, balanceBallMat, balanceLineMat;
        Label? balanceNote;
        Button? balanceButton;

        void ToggleBalance()
        {
            balanceOn = !balanceOn;
            balanceAt = 0;
            UpdateBalance();
        }

        /// <summary>Called every frame in the Studio; redraws ten times a second.</summary>
        void UpdateBalance()
        {
            bool open = mode == EditMode.Body && studioScene != null;
            if (!open)
            {
                if (balanceRoot != null) balanceRoot.gameObject.SetActive(false);
                if (balanceNote != null) balanceNote.style.display = DisplayStyle.None;
                return;
            }
            if (Time.unscaledTime < balanceAt) return;
            balanceAt = Time.unscaledTime + BalanceEvery;

            var design = Design;
            var pieces = RobotPieces.Of(design);
            bool loose = !pieces.AllAttached;
            var robot = loose ? pieces.Keep(design, pieces.Main) : design; // what the wheels carry
            var stance = RobotStance.Of(design);
            bool tips = !stance.Rolls;
            if ((tips && !balanceTipped) || (loose && !balanceLoose)) balanceOn = true; // it has just started to tip or come apart: show why
            balanceTipped = tips;
            balanceLoose = loose;
            balanceButton?.EnableInClassList("tool-button--active", balanceOn);
            bool hasWheels = CountWheels(robot) >= 1;
            bool show = balanceOn && design.Parts.Count > 0;
            EnsureBalanceObjects();
            balanceRoot!.gameObject.SetActive(show);
            if (balanceNote != null)
            {
                balanceNote.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                balanceNote.text = loose ? LooseWarning(pieces, design) : tips ? StanceWarning(stance, design) : "✓ " + Tr(hasWheels ? "stance.rolls" : "balance.noWheels");
                balanceNote.EnableInClassList("balance-note--bad", tips || loose);
            }
            if (!show) return;
            ShowLooseBoxes(pieces, design);

            // The centre of mass, its plumb line and where it meets the floor (the design's y = 0 is the mat).
            var com = DesignGeometry.CentreOfMass(robot);
            var centre = new Vector3(com.x, com.y, com.z) * 0.001f;
            comBall!.localPosition = centre;
            float height = Mathf.Max(0.001f, centre.y);
            plumbLine!.localPosition = new Vector3(centre.x, height / 2, centre.z);
            plumbLine.localScale = new Vector3(0.0012f, height / 2, 0.0012f);
            plumbFoot!.localPosition = new Vector3(centre.x, 0.0006f, centre.z);
            plumbFoot.GetComponent<MeshRenderer>().sharedMaterial = tips ? balanceBadFoot : balanceGoodFoot;

            // The patch under the wheels' tyres and the casters' balls.
            var hull = ConvexHull(Contacts(robot));
            supportPatch!.sharedMesh = PatchMesh(supportPatch.sharedMesh, hull);
            supportPatch.GetComponent<MeshRenderer>().sharedMaterial = tips ? balanceBad : balanceGood;
        }

        /// <summary>A red see-through box round each piece that nothing holds to the robot (<see cref="RobotPieces"/>).</summary>
        void ShowLooseBoxes(RobotPieces pieces, RobotDesign design)
        {
            int used = 0;
            foreach (var piece in pieces.Loose)
            {
                ((float x, float y, float z) min, (float x, float y, float z) max) box;
                if (piece.StartsWith(RobotPieces.ShapePrefix))
                {
                    var shape = design.Body.Feature(piece.Substring(RobotPieces.ShapePrefix.Length));
                    if (shape == null) continue;
                    box = DesignGeometry.FeatureBounds(shape);
                }
                else
                {
                    var part = design.Find(piece);
                    if (part == null) continue;
                    box = DesignGeometry.PartBounds(part);
                }
                if (used == looseBoxes.Count)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "LoosePiece";
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(balanceRoot, false);
                    var renderer = go.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = balanceLooseMat;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    looseBoxes.Add(go.transform);
                }
                var t = looseBoxes[used++];
                t.gameObject.SetActive(true);
                t.localPosition = new Vector3(box.min.x + box.max.x, box.min.y + box.max.y, box.min.z + box.max.z) * 0.0005f;
                t.localScale = new Vector3(box.max.x - box.min.x + 3, box.max.y - box.min.y + 3, box.max.z - box.min.z + 3) * 0.001f;
            }
            for (int i = used; i < looseBoxes.Count; i++) looseBoxes[i].gameObject.SetActive(false);
        }

        static int CountWheels(RobotDesign design)
        {
            int n = 0;
            foreach (var part in design.Parts) if (PartCatalog.Get(part.Part)?.Kind == PartKind.Motor) n++;
            return n;
        }

        /// <summary>Where the robot meets the floor (mm, x and z): each tyre across its width, each caster's ball.</summary>
        static List<Vector2> Contacts(RobotDesign design)
        {
            var points = new List<Vector2>();
            foreach (var part in design.Parts)
            {
                var kind = PartCatalog.Get(part.Part)?.Kind;
                if (kind == PartKind.Motor)
                {
                    var w = DesignGeometry.WheelCentre(part);
                    var axis = DesignGeometry.WheelAxis(part);
                    var across = new Vector2(axis.x, axis.z);
                    if (across.sqrMagnitude < 0.25f) continue; // a shaft pointing up rolls nowhere
                    across = across.normalized * (DesignGeometry.WheelWidth / 2);
                    points.Add(new Vector2(w.x, w.z) + across);
                    points.Add(new Vector2(w.x, w.z) - across);
                }
                else if (kind == PartKind.Caster)
                {
                    var c = DesignGeometry.CasterBall(part);
                    for (int i = 0; i < 8; i++) // the ball's small footprint, so one caster still draws a spot
                    {
                        float a = i * Mathf.PI / 4;
                        points.Add(new Vector2(c.x + 4 * Mathf.Cos(a), c.z + 4 * Mathf.Sin(a)));
                    }
                }
            }
            return points;
        }

        /// <summary>The convex hull, counter-clockwise (Andrew's monotone chain).</summary>
        static List<Vector2> ConvexHull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (points.Count < 3) return points;
            var hull = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            foreach (var p in points)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            int lower = hull.Count + 1;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                var p = points[i];
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        /// <summary>A flat fan over the hull, just above the mat, in metres; a thin strip for two points.</summary>
        static Mesh PatchMesh(Mesh? mesh, List<Vector2> hull)
        {
            mesh ??= new Mesh { name = "SupportPatch" };
            mesh.Clear();
            if (hull.Count < 2) return mesh;
            if (hull.Count == 2) // two contacts: a 4 mm strip between them
            {
                var d = (hull[1] - hull[0]).normalized;
                var n = new Vector2(-d.y, d.x) * 2;
                hull = new List<Vector2> { hull[0] - n, hull[1] - n, hull[1] + n, hull[0] + n };
            }
            var vertices = new Vector3[hull.Count];
            for (int i = 0; i < hull.Count; i++) vertices[i] = new Vector3(hull[i].x, 0.4f, hull[i].y) * 0.001f;
            var triangles = new List<int>();
            for (int i = 1; i < hull.Count - 1; i++)
            {
                triangles.Add(0);
                triangles.Add(i + 1); // wound to face up
                triangles.Add(i);
            }
            mesh.vertices = vertices;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void EnsureBalanceObjects()
        {
            if (balanceRoot != null) return;
            balanceGood = OverlayMat(new Color(0.30f, 0.85f, 0.45f, 0.38f), true);
            balanceBad = OverlayMat(new Color(1.0f, 0.32f, 0.26f, 0.42f), true);
            balanceGoodFoot = OverlayMat(new Color(0.30f, 0.85f, 0.45f, 1f), true);
            balanceBadFoot = OverlayMat(new Color(1.0f, 0.32f, 0.26f, 1f), true);
            balanceBallMat = OverlayMat(new Color(1.0f, 0.82f, 0.10f, 1f), true);
            balanceLineMat = OverlayMat(new Color(1.0f, 0.90f, 0.45f, 1f), true);
            balanceLineMat.SetFloat("_Shade", 0);
            balanceLineMat.SetFloat("_Rim", 0);
            balanceLooseMat = OverlayMat(new Color(1.0f, 0.25f, 0.2f, 0.28f), true);

            balanceRoot = new GameObject("Balance").transform;
            balanceRoot.SetParent(studioScene, false);
            Transform Piece(PrimitiveType type, string name, Material material)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                Destroy(go.GetComponent<Collider>()); // clicks pass through to the robot
                go.transform.SetParent(balanceRoot, false);
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return go.transform;
            }
            comBall = Piece(PrimitiveType.Sphere, "CentreOfMass", balanceBallMat);
            comBall.localScale = Vector3.one * 0.014f;
            plumbLine = Piece(PrimitiveType.Cylinder, "PlumbLine", balanceLineMat);
            plumbFoot = Piece(PrimitiveType.Cylinder, "PlumbFoot", balanceGoodFoot);
            plumbFoot.localScale = new Vector3(0.012f, 0.0004f, 0.012f);
            var patch = new GameObject("SupportPatch");
            patch.transform.SetParent(balanceRoot, false);
            supportPatch = patch.AddComponent<MeshFilter>();
            var patchRenderer = patch.AddComponent<MeshRenderer>();
            patchRenderer.sharedMaterial = balanceGood;
            patchRenderer.shadowCastingMode = ShadowCastingMode.Off;
            patchRenderer.receiveShadows = false;
        }

        /// <summary>The Studio's scene was rebuilt: the Balance view's objects went with it.</summary>
        void ForgetBalanceObjects()
        {
            balanceRoot = null;
            comBall = plumbLine = plumbFoot = null;
            looseBoxes.Clear();
            supportPatch = null;
            balanceAt = 0;
        }
    }
}
