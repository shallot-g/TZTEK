using TZTEK.VispecCMM.Import.Core.Planning.SafePlane;
using TZTEK.VispecCMM.Import.Interfaces.Models;
using Xunit;

namespace TZTEK.VispecCMM.Import.Core.Tests;

public class SafetyPlaneAssignerTests
{
    [Fact]
    public void SidePoint_RejectsOppositeSafetyPlane_WhenReturnPathGoesThroughBody()
    {
        var cylinder = CreateOuterCylinder();
        var options = new MeasurementPlanOptions
        {
            SafetyClearanceMm = 2,
            CollisionSafetyMarginMm = 0,
            CollisionPrimitives = [cylinder]
        };
        var envelope = SafetyPlaneBoxBuilder.Build([cylinder], options);
        var point = new MeasurementPoint
        {
            X = 10,
            Y = 0,
            Z = 0,
            NormalX = 1,
            NormalY = 0,
            NormalZ = 0,
            ApproachDistance = 5
        };
        var context = new SafetyPlaneAssignmentContext
        {
            Envelope = envelope,
            CollisionPrimitives = [cylinder],
            Options = options,
            TargetItem = new PrimitiveToleranceItem { Primitive = cylinder }
        };

        var opposite = SafetyPlaneAssigner.ScoreFace(envelope, point, SafetyPlaneFace.NegX, null, context);
        var assigned = SafetyPlaneAssigner.SelectBestFace(envelope, point, null, context);

        Assert.True(opposite.ReturnPathCollides);
        Assert.False(opposite.IsEligible);
        Assert.Equal(SafetyPlaneFace.PosX, assigned);
    }

    private static CylinderPrimitive CreateOuterCylinder() => new()
    {
        Id = "cy",
        AxisPointX = 0,
        AxisPointY = 0,
        AxisPointZ = 0,
        AxisDirX = 0,
        AxisDirY = 0,
        AxisDirZ = 1,
        AxisStartX = 0,
        AxisStartY = 0,
        AxisStartZ = -10,
        AxisEndX = 0,
        AxisEndY = 0,
        AxisEndZ = 10,
        Length = 20,
        Radius = 10,
        IsInnerSurface = false
    };
}
