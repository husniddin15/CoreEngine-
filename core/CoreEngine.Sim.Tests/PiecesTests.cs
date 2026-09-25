using CoreEngine.Sim.Design;

namespace CoreEngine.Sim.Tests;

public class PiecesTests
{
    [Fact]
    public void TheKitsHoldTogether()
    {
        foreach (var design in new[] { DesignPresets.ObstacleAvoiderKit(), DesignPresets.PwmTwoWheeler(), DesignPresets.NoCasterTwoWheeler() })
        {
            var pieces = RobotPieces.Of(design);
            Assert.True(pieces.AllAttached, string.Join(" | ", pieces.Groups.Select(g => string.Join(",", g))));
        }
    }

    [Fact]
    public void APlateDraggedAwayLeavesEveryPartLoose()
    {
        // The owner's third robot as it was at 21:30 on 2026-09-25: its plate dragged 17.5 cm to the left.
        var d = DesignPresets.DraggedPlate();
        var plate = d.Body.Features.Find(f => f.Kind == FeatureKind.Box)!;
        var pieces = RobotPieces.Of(d);
        Assert.False(pieces.AllAttached);
        Assert.Contains(RobotPieces.ShapePrefix + plate.Id, pieces.Loose);
        Assert.Contains(pieces.Groups[pieces.Main], g => g.StartsWith("motor")); // the robot is what has wheels
        Assert.Contains("uno1", pieces.Loose);
    }

    [Fact]
    public void APartInTheAirIsLooseAndOneOnThePlateIsNot()
    {
        var d = DesignPresets.PwmTwoWheeler();
        Assert.True(RobotPieces.Of(d).AllAttached);
        d.Find("uno1")!.Y += 10; // 10 mm above the plate
        var pieces = RobotPieces.Of(d);
        Assert.Equal(new[] { "uno1" }, pieces.Loose.ToArray());
        d.Find("uno1")!.Y -= 9; // 1 mm: taped on
        Assert.True(RobotPieces.Of(d).AllAttached);
    }

    [Fact]
    public void AGroupKeepsOnlyItsPiecesAndTheBalanceCheckLooksAtTheRobot()
    {
        var d = DesignPresets.PwmTwoWheeler();
        d.Find("uno1")!.Y += 10;
        var pieces = RobotPieces.Of(d);
        var robot = pieces.Keep(d, pieces.Main);
        Assert.Null(robot.Find("uno1"));
        Assert.Equal(d.Parts.Count - 1, robot.Parts.Count);
        Assert.Equal(d.Body.Features.Count, robot.Body.Features.Count);
        var uno = pieces.Keep(d, pieces.GroupOf("uno1"));
        Assert.Single(uno.Parts);
        Assert.Empty(uno.Body.Features);
        Assert.True(RobotStance.Of(d).Rolls); // the floating Uno is not what the robot stands on
        Assert.Equal(RobotPieces.ShapePrefix + d.Body.Features[0].Id, RobotPieces.PieceOf(d, d.Body.Features[0].Id));
    }

    [Fact]
    public void TheSensorsTiltIsMeasured()
    {
        Assert.Equal(0f, RobotPieces.SonarTilt(DesignPresets.ObstacleAvoiderKit())!.Value, 1);
        Assert.Equal(33.7f, RobotPieces.SonarTilt(DesignPresets.NoCasterTwoWheeler())!.Value, 0.2f); // on its wedge's slope, looking up
        var d = DesignPresets.Empty();
        Assert.Null(RobotPieces.SonarTilt(d));
    }
}
