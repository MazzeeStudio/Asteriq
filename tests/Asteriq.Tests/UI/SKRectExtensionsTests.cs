using Asteriq.UI;
using SkiaSharp;

namespace Asteriq.Tests.UI;

public class SKRectExtensionsTests
{
    [Fact]
    public void HitTest_EmptyBounds_ReturnsFalse()
    {
        var empty = SKRect.Empty;
        Assert.False(empty.HitTest(50f, 50f));
    }

    [Fact]
    public void HitTest_PointInside_ReturnsTrue()
    {
        var bounds = new SKRect(10, 10, 100, 100);
        Assert.True(bounds.HitTest(50f, 50f));
    }

    [Fact]
    public void HitTest_PointOutside_ReturnsFalse()
    {
        var bounds = new SKRect(10, 10, 100, 100);
        Assert.False(bounds.HitTest(200f, 200f));
    }

    [Fact]
    public void HitTest_PointOnEdge_ReturnsTrue()
    {
        var bounds = new SKRect(10, 10, 100, 100);
        Assert.True(bounds.HitTest(10f, 10f));
    }

    [Fact]
    public void HitTest_DefaultBounds_ReturnsFalse()
    {
        var unset = default(SKRect);
        Assert.False(unset.HitTest(0f, 0f));
    }

    [Fact]
    public void HitTest_SKPointOverload_EmptyBounds_ReturnsFalse()
    {
        var empty = SKRect.Empty;
        Assert.False(empty.HitTest(new SKPoint(50f, 50f)));
    }

    [Fact]
    public void HitTest_SKPointOverload_PointInside_ReturnsTrue()
    {
        var bounds = new SKRect(10, 10, 100, 100);
        Assert.True(bounds.HitTest(new SKPoint(50f, 50f)));
    }

    [Fact]
    public void HitTest_SKPointOverload_PointOutside_ReturnsFalse()
    {
        var bounds = new SKRect(10, 10, 100, 100);
        Assert.False(bounds.HitTest(new SKPoint(200f, 200f)));
    }

    [Fact]
    public void HitTest_DoesNotStackOverflow()
    {
        // Regression: perl regex accidentally replaced the method body with a self-call
        var bounds = new SKRect(0, 0, 100, 100);
        bounds.HitTest(50f, 50f);
        bounds.HitTest(new SKPoint(50f, 50f));
        SKRect.Empty.HitTest(0f, 0f);
        // If we get here without StackOverflowException, the test passes
    }
}
