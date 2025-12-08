using Asteriq.Models;

namespace Asteriq.Tests.Models;

public class AxisCurveTests
{
    [Fact]
    public void Linear_NoModifications_ReturnsInput()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.Linear,
            Deadzone = 0f,
            Saturation = 1f,
            Curvature = 0f
        };

        Assert.Equal(0.5f, curve.Apply(0.5f), 3);
        Assert.Equal(-0.5f, curve.Apply(-0.5f), 3);
        Assert.Equal(1.0f, curve.Apply(1.0f), 3);
        Assert.Equal(-1.0f, curve.Apply(-1.0f), 3);
    }

    [Fact]
    public void Linear_WithDeadzone_ReturnsZeroInDeadzone()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.Linear,
            Deadzone = 0.1f,
            Saturation = 1f
        };

        // Values within deadzone should return 0
        Assert.Equal(0f, curve.Apply(0.05f));
        Assert.Equal(0f, curve.Apply(-0.05f));
        Assert.Equal(0f, curve.Apply(0.09f));
    }

    [Fact]
    public void Linear_WithDeadzone_ScalesOutput()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.Linear,
            Deadzone = 0.1f,
            Saturation = 1f
        };

        // Value just outside deadzone
        float result = curve.Apply(0.1f);
        Assert.Equal(0f, result, 2);

        // Full deflection should still reach 1.0
        result = curve.Apply(1.0f);
        Assert.Equal(1f, result, 2);
    }

    [Fact]
    public void Linear_WithSaturation_ClampsAtSaturation()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.Linear,
            Deadzone = 0f,
            Saturation = 0.8f
        };

        // At saturation point, output should be 1.0
        float result = curve.Apply(0.8f);
        Assert.Equal(1f, result, 2);

        // Beyond saturation should still be clamped to 1.0
        result = curve.Apply(1.0f);
        Assert.Equal(1f, result, 2);
    }

    [Fact]
    public void SCurve_PositiveCurvature_FlattensCenter()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.SCurve,
            Curvature = 0.5f,
            Deadzone = 0f,
            Saturation = 1f
        };

        // At center, small input should produce smaller output (flatter center)
        float linear = 0.3f;
        float scurve = curve.Apply(0.3f);

        // S-curve with positive curvature should be less than linear at center
        Assert.True(scurve < linear, $"S-curve {scurve} should be less than linear {linear} for center values");

        // At extremes, should still reach full deflection
        Assert.Equal(1f, curve.Apply(1.0f), 2);
        Assert.Equal(-1f, curve.Apply(-1.0f), 2);
    }

    [Fact]
    public void SCurve_NegativeCurvature_SteepensCenter()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.SCurve,
            Curvature = -0.5f,
            Deadzone = 0f,
            Saturation = 1f
        };

        // Negative curvature: small input produces larger output (steeper center)
        float linear = 0.3f;
        float scurve = curve.Apply(0.3f);

        Assert.True(scurve > linear, $"S-curve {scurve} should be greater than linear {linear} for center values with negative curvature");
    }

    [Fact]
    public void Exponential_PositiveCurvature_ReducesSensitivityAtCenter()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.Exponential,
            Curvature = 0.5f,
            Deadzone = 0f,
            Saturation = 1f
        };

        // Exponential with positive curvature: x^power where power > 1
        float input = 0.5f;
        float result = curve.Apply(input);

        // Should be less than linear at midpoint
        Assert.True(result < input, $"Exponential {result} should be less than linear {input}");
    }

    [Fact]
    public void Exponential_NegativeCurvature_IncreasesSensitivityAtCenter()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.Exponential,
            Curvature = -0.5f,
            Deadzone = 0f,
            Saturation = 1f
        };

        float input = 0.5f;
        float result = curve.Apply(input);

        // Should be greater than linear at midpoint
        Assert.True(result > input, $"Exponential {result} should be greater than linear {input}");
    }

    [Fact]
    public void Apply_PreservesSign()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.SCurve,
            Curvature = 0.3f,
            Deadzone = 0.05f,
            Saturation = 1f
        };

        // Positive input should give positive output
        Assert.True(curve.Apply(0.5f) > 0);

        // Negative input should give negative output
        Assert.True(curve.Apply(-0.5f) < 0);
    }

    [Fact]
    public void Apply_ZeroInput_ReturnsZero()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.SCurve,
            Curvature = 0.5f,
            Deadzone = 0f,
            Saturation = 1f
        };

        Assert.Equal(0f, curve.Apply(0f));
    }

    [Theory]
    [InlineData(CurveType.Linear)]
    [InlineData(CurveType.SCurve)]
    [InlineData(CurveType.Exponential)]
    public void Apply_FullDeflection_ReachesMaximum(CurveType type)
    {
        var curve = new AxisCurve
        {
            Type = type,
            Curvature = 0.3f,
            Deadzone = 0f,
            Saturation = 1f
        };

        Assert.Equal(1f, curve.Apply(1.0f), 2);
        Assert.Equal(-1f, curve.Apply(-1.0f), 2);
    }

    [Fact]
    public void Custom_WithControlPoints_InterpolatesCorrectly()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.Custom,
            Deadzone = 0f,
            Saturation = 1f,
            ControlPoints = new List<(float input, float output)>
            {
                (0f, 0f),
                (0.5f, 0.25f),  // Half input maps to quarter output
                (1f, 1f)
            }
        };

        // At control point
        float result = curve.Apply(0.5f);
        Assert.Equal(0.25f, result, 2);

        // Between control points (0.25 should interpolate between 0 and 0.25)
        result = curve.Apply(0.25f);
        Assert.Equal(0.125f, result, 2);
    }

    [Fact]
    public void Custom_WithoutControlPoints_ReturnsLinear()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.Custom,
            Deadzone = 0f,
            Saturation = 1f,
            ControlPoints = null
        };

        Assert.Equal(0.5f, curve.Apply(0.5f), 2);
    }
}
