using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void DrawCurveVisualization(SKCanvas canvas, SKRect bounds)
    {
        // Background - darker than the panel
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background0);
        canvas.DrawRect(bounds, bgPaint);

        // Grid lines (10% increments) - visible but subtle
        using var gridPaint = FUIRenderer.CreateStrokePaint(new SKColor(60, 70, 80));

        for (float t = 0.1f; t < 1f; t += 0.1f)
        {
            // Skip 50% line - we'll draw it brighter
            if (Math.Abs(t - 0.5f) < 0.01f) continue;

            float x = bounds.Left + t * bounds.Width;
            float y = bounds.Bottom - t * bounds.Height;
            canvas.DrawLine(x, bounds.Top, x, bounds.Bottom, gridPaint);
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, gridPaint);
        }

        // Center lines (brighter, 50% mark)
        using var centerPaint = FUIRenderer.CreateStrokePaint(new SKColor(80, 95, 110));
        canvas.DrawLine(bounds.MidX, bounds.Top, bounds.MidX, bounds.Bottom, centerPaint);
        canvas.DrawLine(bounds.Left, bounds.MidY, bounds.Right, bounds.MidY, centerPaint);

        // Reference linear line (dashed diagonal)
        using var refPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.FrameSubtle,
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 4f }, 0)
        };
        canvas.DrawLine(bounds.Left, bounds.Bottom, bounds.Right, bounds.Top, refPaint);

        // Draw the curve
        DrawCurvePath(canvas, bounds);

        // Draw control points (only for custom curve)
        if (_curve.SelectedType == CurveType.Custom)
        {
            DrawCurveControlPoints(canvas, bounds);
        }

        // Frame
        using var framePaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
        canvas.DrawRect(bounds, framePaint);

        // Tick marks and labels on edges
        using var tickPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame.WithAlpha(150));

        float tickLen = 4f;
        float labelOffset = 3f;

        // Draw tick marks at 0%, 50%, 100% on bottom edge (IN axis)
        float[] tickPositions = { 0f, 0.5f, 1f };
        string[] tickLabels = { "0", "50", "100" };

        for (int i = 0; i < tickPositions.Length; i++)
        {
            float t = tickPositions[i];
            float x = bounds.Left + t * bounds.Width;

            // Bottom tick
            canvas.DrawLine(x, bounds.Bottom, x, bounds.Bottom + tickLen, tickPaint);

            // Label below tick
            float labelX = x - (t < 0.001f ? 0 : (t > 0.999f ? 12 : 6));
            FUIRenderer.DrawText(canvas, tickLabels[i], new SKPoint(labelX, bounds.Bottom + tickLen + labelOffset + 7), FUIColors.TextDim, 12f);
        }

        // Draw tick marks at 0%, 50%, 100% on left edge (OUT axis)
        for (int i = 0; i < tickPositions.Length; i++)
        {
            float t = tickPositions[i];
            float y = bounds.Bottom - t * bounds.Height;

            // Left tick
            canvas.DrawLine(bounds.Left - tickLen, y, bounds.Left, y, tickPaint);

            // Label left of tick
            float labelY = y + (t < 0.001f ? 3 : (t > 0.999f ? 7 : 3));
            float labelX = bounds.Left - tickLen - labelOffset - (tickLabels[i].Length > 1 ? 12 : 6);
            FUIRenderer.DrawText(canvas, tickLabels[i], new SKPoint(labelX, labelY), FUIColors.TextDim, 12f);
        }

    }

    private float DrawAxisMovementIndicator(SKCanvas canvas, float leftMargin, float rightMargin, float y, AxisMapping axisMapping)
    {
        float width = rightMargin - leftMargin;
        float startY = y;

        // Get current raw input values for all input sources
        float rawInput = 0f;
        bool hasInput = false;

        if (axisMapping.Inputs.Count > 0)
        {
            var inputValues = new List<float>();

            foreach (var input in axisMapping.Inputs)
            {
                // Find the physical device
                var device = _ctx.Devices.FirstOrDefault(d => d.InstanceGuid.ToString() == input.DeviceId);
                if (device is null) continue;

                // Get the device state from InputService
                var state = _ctx.InputService.GetDeviceState(device.DeviceIndex);
                if (state is null || input.Index >= state.Axes.Length) continue;

                inputValues.Add(state.Axes[input.Index]);
                hasInput = true;
            }

            // Merge multiple inputs according to merge operation
            if (inputValues.Count > 0)
            {
                rawInput = axisMapping.MergeOp switch
                {
                    MergeOperation.Average => inputValues.Average(),
                    MergeOperation.Maximum => inputValues.Max(),
                    MergeOperation.Minimum => inputValues.Min(),
                    MergeOperation.Sum => Math.Clamp(inputValues.Sum(), -1f, 1f),
                    _ => inputValues[0]
                };
            }
        }

        // Apply the curve to get processed output
        float processedOutput = hasInput ? axisMapping.Curve.Apply(rawInput) : 0f;

        // Check if this is a centered axis (joystick) or end-only (throttle/slider)
        // Auto-detect based on output axis type if mode is set to default Centered
        bool isCentered;
        if (axisMapping.Curve.DeadzoneMode == DeadzoneMode.Centered)
        {
            // Auto-detect: Z axis and sliders are typically end-only (throttles)
            // X, Y, RX, RY, RZ are typically centered (joysticks)
            int outputIndex = axisMapping.Output.Index;
            isCentered = outputIndex switch
            {
                2 => false,  // Z axis - throttle
                6 => false,  // Slider1
                7 => false,  // Slider2
                _ => true    // X, Y, RX, RY, RZ - joystick axes
            };
        }
        else
        {
            isCentered = axisMapping.Curve.DeadzoneMode == DeadzoneMode.Centered;
        }

        // Convert to percentages for display
        float rawPercent, outPercent;
        if (isCentered)
        {
            // Centered: -100% to +100%
            rawPercent = rawInput * 100f;
            outPercent = processedOutput * 100f;
        }
        else
        {
            // End-only: 0% to 100% (convert from -1..1 to 0..100)
            rawPercent = (rawInput + 1f) * 50f;
            outPercent = (processedOutput + 1f) * 50f;
        }

        // Draw section header with live values
        string headerText = hasInput
            ? (isCentered
                ? $"LIVE INPUT: {rawPercent:+0;-0;0}%  >>  OUTPUT: {outPercent:+0;-0;0}%"
                : $"LIVE INPUT: {rawPercent:0}%  >>  OUTPUT: {outPercent:0}%")
            : "LIVE INPUT: (no signal)";

        var headerColor = hasInput ? FUIColors.Active : FUIColors.TextDim.WithAlpha(150);
        FUIRenderer.DrawText(canvas, headerText, new SKPoint(leftMargin, y), headerColor, 12f);
        y += 16f;

        if (hasInput)
        {
            // Draw a visual bar indicator for the processed output
            float barHeight = 8f;
            var barBounds = new SKRect(leftMargin, y, rightMargin, y + barHeight);

            // Background
            using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background0);
            canvas.DrawRect(barBounds, bgPaint);

            // Convert output value to bar position (0..1)
            float normalizedValue = (processedOutput + 1f) / 2f;
            float barX = barBounds.Left + normalizedValue * barBounds.Width;

            if (isCentered)
            {
                // Center line for centered axes
                using var centerPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
                canvas.DrawLine(barBounds.MidX, barBounds.Top, barBounds.MidX, barBounds.Bottom, centerPaint);

                // Fill from center to current position
                var fillBounds = processedOutput >= 0
                    ? new SKRect(barBounds.MidX, barBounds.Top, barX, barBounds.Bottom)
                    : new SKRect(barX, barBounds.Top, barBounds.MidX, barBounds.Bottom);

                using var fillPaint = FUIRenderer.CreateFillPaint(FUIColors.ActiveStrong);
                canvas.DrawRect(fillBounds, fillPaint);
            }
            else
            {
                // Fill from left edge to current position (for sliders/throttles)
                var fillBounds = new SKRect(barBounds.Left, barBounds.Top, barX, barBounds.Bottom);
                using var fillPaint = FUIRenderer.CreateFillPaint(FUIColors.ActiveStrong);
                canvas.DrawRect(fillBounds, fillPaint);
            }

            // Position indicator (vertical line)
            using var indicatorPaint = FUIRenderer.CreateStrokePaint(FUIColors.Active, 2f);
            canvas.DrawLine(barX, barBounds.Top, barX, barBounds.Bottom, indicatorPaint);

            // Frame
            using var framePaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
            canvas.DrawRect(barBounds, framePaint);

            y += barHeight + 14f;  // baseline needs +14 so text top (baseline-10) clears bar bottom

            // Labels below bar - different for centered vs end-only
            if (isCentered)
            {
                FUIRenderer.DrawText(canvas, "-100%", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "0%", new SKPoint(barBounds.MidX - 8, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "+100%", new SKPoint(rightMargin - 28, y), FUIColors.TextDim, 12f);
            }
            else
            {
                FUIRenderer.DrawText(canvas, "0%", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "50%", new SKPoint(barBounds.MidX - 8, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "100%", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 12f);
            }
            y += 12f;
        }

        return y - startY;
    }

    private void DrawCurvePath(SKCanvas canvas, SKRect bounds)
    {
        using var path = new SKPath();
        bool first = true;

        // Sample the curve at many points
        for (float t = 0; t <= 1.001f; t += 0.01f)
        {
            float input = Math.Min(t, 1f);
            float output = ComputeCurveValue(input);

            float x = bounds.Left + input * bounds.Width;
            float y = bounds.Bottom - output * bounds.Height;

            if (first)
            {
                path.MoveTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        // Glow
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(50),
            StrokeWidth = 5f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawPath(path, glowPaint);

        // Main line
        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active,
            StrokeWidth = 2f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        canvas.DrawPath(path, linePaint);
    }

    private float ComputeCurveValue(float input)
    {
        // Apply curve type only - deadzone is handled separately
        float output = _curve.SelectedType switch
        {
            CurveType.Linear => input,
            CurveType.SCurve => ApplySCurve(input),
            CurveType.Exponential => ApplyExponential(input),
            CurveType.Custom => InterpolateControlPoints(input),
            _ => input
        };

        output = Math.Clamp(output, 0f, 1f);

        // Apply inversion
        if (_deadzone.AxisInverted)
            output = 1f - output;

        return output;
    }

    private static float ApplySCurve(float x)
    {
        // S-curve using smoothstep-like function
        return x * x * (3f - 2f * x);
    }

    private static float ApplyExponential(float x)
    {
        // Exponential curve (steeper at the end)
        return x * x;
    }

    private float InterpolateControlPoints(float x)
    {
        if (_curve.ControlPoints.Count < 2) return x;

        // Find segment containing x
        for (int i = 0; i < _curve.ControlPoints.Count - 1; i++)
        {
            var p1 = _curve.ControlPoints[i];
            var p2 = _curve.ControlPoints[i + 1];

            if (x >= p1.X && x <= p2.X)
            {
                if (Math.Abs(p2.X - p1.X) < 0.001f) return p1.Y;
                float t = (x - p1.X) / (p2.X - p1.X);

                // Use Catmull-Rom spline interpolation for smooth curves
                // Need 4 points: p0, p1, p2, p3
                var p0 = i > 0 ? _curve.ControlPoints[i - 1] : new SKPoint(p1.X - (p2.X - p1.X), p1.Y - (p2.Y - p1.Y));
                var p3 = i < _curve.ControlPoints.Count - 2 ? _curve.ControlPoints[i + 2] : new SKPoint(p2.X + (p2.X - p1.X), p2.Y + (p2.Y - p1.Y));

                return CatmullRomInterpolate(p0.Y, p1.Y, p2.Y, p3.Y, t);
            }
        }

        // Extrapolate
        return x < _curve.ControlPoints[0].X ? _curve.ControlPoints[0].Y : _curve.ControlPoints[^1].Y;
    }

    /// <summary>
    /// Catmull-Rom spline interpolation for smooth curves through control points.
    /// t ranges from 0 to 1, output is between p1 and p2.
    /// </summary>
    private static float CatmullRomInterpolate(float p0, float p1, float p2, float p3, float t)
    {
        // Catmull-Rom spline formula with tension = 0.5 (centripetal)
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private void DrawCurveControlPoints(SKCanvas canvas, SKRect bounds)
    {
        const float PointRadius = 7f;
        const float CenterPointRadius = 3.5f; // Half size for center point

        for (int i = 0; i < _curve.ControlPoints.Count; i++)
        {
            var pt = _curve.ControlPoints[i];
            float x = bounds.Left + pt.X * bounds.Width;

            // Apply inversion to display Y position to match the curve
            float displayY = _deadzone.AxisInverted ? (1f - pt.Y) : pt.Y;
            float y = bounds.Bottom - displayY * bounds.Height;

            bool isHovered = i == _curve.HoveredPoint;
            bool isDragging = i == _curve.DraggingPoint;
            bool isEndpoint = i == 0 || i == _curve.ControlPoints.Count - 1;
            bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;

            // Center point is smaller and not interactive
            float baseRadius = isCenterPoint ? CenterPointRadius : PointRadius;
            float radius = (isHovered || isDragging) && !isCenterPoint ? baseRadius + 2 : baseRadius;
            var color = isDragging ? FUIColors.Warning : (isHovered && !isCenterPoint ? FUIColors.TextBright : FUIColors.Active);

            // Glow (skip for center point)
            if (!isCenterPoint)
            {
                using var glowPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = color.WithAlpha(40),
                    IsAntialias = true,
                    ImageFilter = SKImageFilter.CreateBlur(5f, 5f)
                };
                canvas.DrawCircle(x, y, radius + 4, glowPaint);
            }

            // Fill
            using var fillPaint = FUIRenderer.CreateFillPaint(isEndpoint || isCenterPoint ? FUIColors.Background1 : color.WithAlpha(60));
            canvas.DrawCircle(x, y, radius, fillPaint);

            // Stroke
            using var strokePaint = FUIRenderer.CreateStrokePaint(isCenterPoint ? FUIColors.Frame : color, isEndpoint ? 2f : (isCenterPoint ? 1f : 1.5f));
            canvas.DrawCircle(x, y, radius, strokePaint);

            // Value label when hovered/dragged (not for center point)
            if ((isHovered || isDragging) && !isCenterPoint)
            {
                string label = $"({pt.X:F2}, {pt.Y:F2})";
                float labelY = y - radius - 10;
                if (labelY < bounds.Top + 10)
                    labelY = y + radius + 14;

                FUIRenderer.DrawText(canvas, label, new SKPoint(x - 22, labelY), FUIColors.TextBright, 12f);
            }
        }
    }

    private SKPoint CurveScreenToGraph(SKPoint screenPt, SKRect bounds)
    {
        float x = (screenPt.X - bounds.Left) / bounds.Width;
        float y = (bounds.Bottom - screenPt.Y) / bounds.Height;

        // If inverted, convert screen Y back to graph Y (uninvert)
        if (_deadzone.AxisInverted)
            y = 1f - y;

        return new SKPoint(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

}