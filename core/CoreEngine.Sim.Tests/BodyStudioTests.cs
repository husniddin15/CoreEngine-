using System.Text;
using CoreEngine.Sim.Design;

namespace CoreEngine.Sim.Tests;

public class BodyStudioTests
{
    [Fact]
    public void FeaturesGetIdsAndCloneIndependently()
    {
        var body = new BodyDesign();
        var box = body.AddFeature(BodyFeature.Create(FeatureKind.Box, false, 15));
        var hole = body.AddFeature(BodyFeature.Create(FeatureKind.Cylinder, true, 15));
        Assert.Equal(("f1", "f2"), (box.Id, hole.Id));
        var copy = body.Clone();
        copy.Features[0].SizeX = 99;
        Assert.Equal(30, body.Features[0].SizeX);
        Assert.True(hole.SizeY > 20); // a hole reaches through the plate it stands on
    }

    [Fact]
    public void SolidsAddMassAndHolesTakeItAway()
    {
        var plain = new BodyDesign();
        var withBox = new BodyDesign();
        withBox.AddFeature(new BodyFeature { Kind = FeatureKind.Box, SizeX = 10, SizeY = 10, SizeZ = 10 }); // 1 cm³
        Assert.Equal(DesignGeometry.BodyMassG(plain) + 1.18, DesignGeometry.BodyMassG(withBox), 3);
        var withHole = new BodyDesign();
        withHole.AddFeature(new BodyFeature { Kind = FeatureKind.Cylinder, Hole = true, SizeX = 10, SizeY = 10, SizeZ = 10 });
        Assert.True(DesignGeometry.BodyMassG(withHole) < DesignGeometry.BodyMassG(plain));
    }

    [Fact]
    public void AnOutlineBecomesAnExtrusionScaledToItsBounds()
    {
        // An L-shaped outline, drawn clockwise or anticlockwise, 40 × 30 mm.
        float[] points = { 0, 0, 40, 0, 40, 10, 10, 10, 10, 30, 0, 30 };
        var f = BodyFeature.FromOutline(points, 15, 12, false)!;
        Assert.Equal((20f, 15f, 40f, 30f, 12f), (f.X, f.Z, f.SizeX, f.SizeZ, f.SizeY));
        Assert.Equal(40 * 10 + 10 * 20, f.ApproximateVolume() / 12, 3); // area 600 mm²
        Assert.Null(BodyFeature.FromOutline(new float[] { 0, 0, 10, 0 }, 15, 12, false));
    }

    [Fact]
    public void StlComesBackWhereItWasExported()
    {
        // A tetrahedron written by the game's STL writer (y up turned to z up) and read back (z up turned to y up).
        float[] positions = { 0, 0, 0, 10, 0, 0, 0, 10, 0, 0, 0, 10 };
        int[] triangles = { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
        using var stream = new MemoryStream();
        StlWriter.Write(stream, positions, triangles, "tetra");
        var mesh = MeshFile.Weld(MeshFile.ReadStl(stream.ToArray()), 1e-4f);
        Assert.Equal(4, mesh.VertexCount);   // STL repeats corners; welding joins them again
        Assert.Equal(4, mesh.TriangleCount);
        Assert.Equal((0f, 0f, 0f), mesh.Min);
        Assert.Equal((10f, 10f, 10f), mesh.Max);
    }

    [Fact]
    public void AsciiStlAndObjAreRead()
    {
        string stl = "solid t\n facet normal 0 0 1\n  outer loop\n   vertex 0 0 0\n   vertex 5 0 0\n   vertex 0 5 0\n  endloop\n endfacet\nendsolid t\n";
        var fromStl = MeshFile.ReadStl(Encoding.ASCII.GetBytes(stl));
        Assert.Equal(1, fromStl.TriangleCount);
        Assert.Equal(-5, fromStl.Positions[8], 3); // file y = 5 is the game's z = -5

        string obj = "# a square\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1/1 2/2 3/3 4/4\n";
        var fromObj = MeshFile.ReadObj(obj);
        Assert.Equal(2, fromObj.TriangleCount); // the quad is split in two
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, fromObj.Triangles);
    }
}
