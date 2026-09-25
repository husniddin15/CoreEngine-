using CoreEngine.Sim.Design;

namespace CoreEngine.Sim.Tests;

public class StanceTests
{
    /// <summary>The owner's third robot as saved on 2026-09-25, without a ball caster.</summary>
    static RobotDesign OwnersRobot() => DesignPresets.NoCasterTwoWheeler();

    [Fact]
    public void TheKitRollsOnItsWheelsAndCaster()
    {
        foreach (var design in new[] { DesignPresets.ObstacleAvoiderKit(), DesignPresets.PwmTwoWheeler() })
        {
            var stance = RobotStance.Of(design);
            Assert.True(stance.Rolls, stance.ToString());
            Assert.True(stance.TiltDegrees < 1, stance.ToString()); // the caster is as tall as the wheels
        }
    }

    [Fact]
    public void WithoutACasterTheOwnersRobotTipsOntoItsFrontAndDragsItsPlate()
    {
        var stance = RobotStance.Of(OwnersRobot());
        Assert.False(stance.Rolls);
        Assert.Equal("front", stance.Side);
        Assert.Equal("", stance.Dragging); // the plate's front edge, 155 mm ahead of the axle
        Assert.Equal(16.2f, stance.TiltDegrees, 0.3f); // as it lay in the owner's screenshot
    }

    [Fact]
    public void ACasterUnderTheFrontHoldsItLevel()
    {
        var d = OwnersRobot();
        // The kit's caster under the plate's front: 43.5 mm tall, as the plate's underside is above the floor.
        d.Parts.Add(new PartInstance { Id = "caster1", Part = PartCatalog.Caster, X = -15, Y = 43.55f - 33.5f, Z = 110 });
        var stance = RobotStance.Of(d);
        Assert.True(stance.Rolls, stance.ToString());
        Assert.True(stance.TiltDegrees < 0.5f, stance.ToString());
    }

    [Fact]
    public void ACasterOnTheLightSideDoesNotHoldTheHeavyOne()
    {
        var d = OwnersRobot();
        d.Parts.Add(new PartInstance { Id = "caster1", Part = PartCatalog.Caster, X = -15, Y = 10, Z = -45 }); // behind the wheels
        var stance = RobotStance.Of(d);
        Assert.False(stance.Rolls);
        Assert.Equal("front", stance.Side);
    }

    [Fact]
    public void AShortCasterStillHoldsTheRobotButItLeans()
    {
        var d = OwnersRobot();
        d.Parts.Add(new PartInstance { Id = "caster1", Part = PartCatalog.Caster, X = -15, Y = 20, Z = 110 }); // its ball 10 mm up
        var stance = RobotStance.Of(d);
        Assert.True(stance.Rolls, stance.ToString());
        Assert.Equal("front", stance.Side);
        Assert.InRange(stance.TiltDegrees, 3f, 6f);
    }

    [Fact]
    public void ARobotNotOnTwoWheelsIsLeftAlone()
    {
        var d = OwnersRobot();
        d.Parts.RemoveAll(p => p.Part == PartCatalog.TtMotor);
        Assert.True(RobotStance.Of(d).Rolls);
    }
}
