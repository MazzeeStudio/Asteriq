using Asteriq.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// FUI-themed curve editor control for axis response curves.
/// Supports draggable control points, presets, and real-time preview.
/// </summary>
public class FUICurveEditor : UserControl
{
    private SKControl _canvas = null!;
    private AxisCurve _curve = new();

    // Control points (normalized 0-1 space)
    private List<SKPoint> _controlPoints = new();
    private int _draggedPointIndex = -1;
    private int _hoveredPointIndex = -1;

    // UI layout
    private SKRect _graphBounds;
    private SKRect _presetsBounds;
    private const float GraphPadding = 40f;
    private const float ControlPointRadius = 8f;
    private const float ControlPointHitRadius = 14f;

    // Preset buttons
    private readonly string[] _presetNames = { "LINEAR", "S-CURVE", "EXPO", "CUSTOM" };
    private int _hoveredPreset = -1;

    // Deadzone/saturation sliders
    private SKRect _deadzoneBounds;
    private SKRect _saturationBounds;
    private bool _draggingDeadzone;
    private bool _draggingSaturation;

    public event EventHandler<AxisCurve>? CurveChanged;

    public AxisCurve Curve
    {
        get => _curve;
        set
        {
            _curve = value;
            SyncControlPointsFromCurve();
            _canvas?.Invalidate();
        }
    }

    public FUICurveEditor()
    {
        InitializeComponent();
        InitializeDefaultControlPoints();
    }

    private void InitializeComponent()
    {
        Size = new Size(400, 350);
        BackColor = Color.FromArgb(10, 13, 16); // FUIColors.Background1

        _canvas = new SKControl { Dock = DockStyle.Fill };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseDown += OnMouseDown;
        _canvas.MouseUp += OnMouseUp;
        _canvas.MouseLeave += OnMouseLeave;
        Controls.Add(_canvas);
    }

    private void InitializeDefaultControlPoints()
    {
        // Default: linear curve with 2 points at corners
        _controlPoints = new List<SKPoint>
        {
            new(0f, 0f),
            new(1f, 1f)
        };
    }

    private void SyncControlPointsFromCurve()
    {
        if (_curve.Type == CurveType.Custom && _curve.ControlPoints is not null && _curve.ControlPoints.Count >= 2)
        {
            _controlPoints = _curve.ControlPoints.Select(p => new SKPoint(p.input, p.output)).ToList();
        }
        else
        {
            // Generate points from curve formula
            _controlPoints = new List<SKPoint> { new(0f, 0f) };

            // Sample the curve at several points
            for (float x = 0.25f; x <= 0.75f; x += 0.25f)
            {
                float y = _curve.Apply(x);
                _controlPoints.Add(new SKPoint(x, Math.Abs(y)));
            }

            _controlPoints.Add(new SKPoint(1f, 1f));
        }
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var width = e.Info.Width;
        var height = e.Info.Height;

        canvas.Clear(FUIColors.Background1);

        // Layout
        float presetsHeight = 36f;
        float slidersHeight = 60f;
        _presetsBounds = new SKRect(GraphPadding, 8, width - GraphPadding, presetsHeight);
        _graphBounds = new SKRect(GraphPadding, presetsHeight + 16, width - GraphPadding, height - slidersHeight - 16);

        float sliderY = height - slidersHeight + 8;
        float sliderWidth = (width - GraphPadding * 3) / 2;
        _deadzoneBounds = new SKRect(GraphPadding, sliderY, GraphPadding + sliderWidth, sliderY + 40);
        _saturationBounds = new SKRect(GraphPadding * 2 + sliderWidth, sliderY, width - GraphPadding, sliderY + 40);

        DrawPresets(canvas);
        DrawGraph(canvas);
        DrawSliders(canvas);
    }

    private void DrawPresets(SKCanvas canvas)
    {
        float buttonWidth = (_presetsBounds.Width - 12) / _presetNames.Length;
        float buttonHeight = _presetsBounds.Height - 4;

        for (int i = 0; i < _presetNames.Length; i++)
        {
            float x = _presetsBounds.Left + i * (buttonWidth + 4);
            var bounds = new SKRect(x, _presetsBounds.Top, x + buttonWidth, _presetsBounds.Top + buttonHeight);

            bool isActive = i switch
            {
                0 => _curve.Type == CurveType.Linear,
                1 => _curve.Type == CurveType.SCurve,
                2 => _curve.Type == CurveType.Exponential,
                3 => _curve.Type == CurveType.Custom,
                _ => false
            };

            bool isHovered = i == _hoveredPreset;

            var bgColor = isActive ? FUIColors.Active.WithAlpha(60) : (isHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
            var frameColor = isActive ? FUIColors.Active : (isHovered ? FUIColors.Primary : FUIColors.Frame);
            var textColor = isActive ? FUIColors.TextBright : (isHovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(bounds, bgPaint);

            using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = 1f };
            canvas.DrawRect(bounds, framePaint);

            FUIRenderer.DrawTextCentered(canvas, _presetNames[i], bounds, textColor, 10f);
        }
    }

    private void DrawGraph(SKCanvas canvas)
    {
        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(_graphBounds, framePaint);

        // Grid
        DrawGrid(canvas);

        // Reference diagonal (linear)
        var refStart = GraphToScreen(new SKPoint(0, 0));
        var refEnd = GraphToScreen(new SKPoint(1, 1));
        using var refPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame.WithAlpha(80),
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 4f }, 0)
        };
        canvas.DrawLine(refStart, refEnd, refPaint);

        // Deadzone and saturation regions
        DrawDeadzoneRegion(canvas);
        DrawSaturationRegion(canvas);

        // Curve
        DrawCurveLine(canvas);

        // Control points
        DrawControlPoints(canvas);

        // Axis labels
        FUIRenderer.DrawText(canvas, "INPUT", new SKPoint(_graphBounds.MidX - 15, _graphBounds.Bottom + 14), FUIColors.TextDim, 9f);

        // Vertical "OUTPUT" label
        canvas.Save();
        canvas.Translate(_graphBounds.Left - 28, _graphBounds.MidY + 20);
        canvas.RotateDegrees(-90);
        FUIRenderer.DrawText(canvas, "OUTPUT", new SKPoint(0, 0), FUIColors.TextDim, 9f);
        canvas.Restore();
    }

    private void DrawGrid(SKCanvas canvas)
    {
        using var gridPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Grid,
            StrokeWidth = 0.5f
        };

        // Vertical lines (every 25%)
        for (float x = 0.25f; x < 1f; x += 0.25f)
        {
            var pt = GraphToScreen(new SKPoint(x, 0));
            canvas.DrawLine(pt.X, _graphBounds.Top, pt.X, _graphBounds.Bottom, gridPaint);
        }

        // Horizontal lines (every 25%)
        for (float y = 0.25f; y < 1f; y += 0.25f)
        {
            var pt = GraphToScreen(new SKPoint(0, y));
            canvas.DrawLine(_graphBounds.Left, pt.Y, _graphBounds.Right, pt.Y, gridPaint);
        }

        // Center lines (brighter)
        using var centerPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.GridAccent,
            StrokeWidth = 0.75f
        };
        var center = GraphToScreen(new SKPoint(0.5f, 0.5f));
        canvas.DrawLine(center.X, _graphBounds.Top, center.X, _graphBounds.Bottom, centerPaint);
        canvas.DrawLine(_graphBounds.Left, center.Y, _graphBounds.Right, center.Y, centerPaint);
    }

    private void DrawDeadzoneRegion(SKCanvas canvas)
    {
        if (_curve.Deadzone <= 0) return;

        var dzEnd = GraphToScreen(new SKPoint(_curve.Deadzone, 0));
        var region = new SKRect(_graphBounds.Left, _graphBounds.Top, dzEnd.X, _graphBounds.Bottom);

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Warning.WithAlpha(20)
        };
        canvas.DrawRect(region, fillPaint);

        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Warning.WithAlpha(100),
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0)
        };
        canvas.DrawLine(dzEnd.X, _graphBounds.Top, dzEnd.X, _graphBounds.Bottom, linePaint);
    }

    private void DrawSaturationRegion(SKCanvas canvas)
    {
        if (_curve.Saturation >= 1) return;

        var satStart = GraphToScreen(new SKPoint(_curve.Saturation, 0));
        var region = new SKRect(satStart.X, _graphBounds.Top, _graphBounds.Right, _graphBounds.Bottom);

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Success.WithAlpha(20)
        };
        canvas.DrawRect(region, fillPaint);

        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Success.WithAlpha(100),
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0)
        };
        canvas.DrawLine(satStart.X, _graphBounds.Top, satStart.X, _graphBounds.Bottom, linePaint);
    }

    private void DrawCurveLine(SKCanvas canvas)
    {
        using var path = new SKPath();
        bool first = true;

        // Sample the curve at many points for smooth rendering
        for (float x = 0; x <= 1.001f; x += 0.01f)
        {
            float y = ComputeCurveOutput(Math.Min(x, 1f));
            var screenPt = GraphToScreen(new SKPoint(x, y));

            if (first)
            {
                path.MoveTo(screenPt);
                first = false;
            }
            else
            {
                path.LineTo(screenPt);
            }
        }

        // Glow
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(60),
            StrokeWidth = 4f,
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

    private void DrawControlPoints(SKCanvas canvas)
    {
        for (int i = 0; i < _controlPoints.Count; i++)
        {
            var pt = GraphToScreen(_controlPoints[i]);
            bool isHovered = i == _hoveredPointIndex;
            bool isDragged = i == _draggedPointIndex;
            bool isEndpoint = i == 0 || i == _controlPoints.Count - 1;

            float radius = isHovered || isDragged ? ControlPointRadius + 2 : ControlPointRadius;
            var color = isDragged ? FUIColors.Warning : (isHovered ? FUIColors.TextBright : FUIColors.Active);

            // Glow
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = color.WithAlpha(50),
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(6f, 6f)
            };
            canvas.DrawCircle(pt, radius + 4, glowPaint);

            // Fill
            using var fillPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = isEndpoint ? FUIColors.Background1 : color.WithAlpha(60),
                IsAntialias = true
            };
            canvas.DrawCircle(pt, radius, fillPaint);

            // Stroke
            using var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = color,
                StrokeWidth = isEndpoint ? 2f : 1.5f,
                IsAntialias = true
            };
            canvas.DrawCircle(pt, radius, strokePaint);

            // Value label when hovered/dragged
            if (isHovered || isDragged)
            {
                var p = _controlPoints[i];
                string label = $"({p.X:F2}, {p.Y:F2})";
                float labelY = pt.Y - radius - 12;
                if (labelY < _graphBounds.Top + 10)
                    labelY = pt.Y + radius + 16;

                FUIRenderer.DrawText(canvas, label, new SKPoint(pt.X - 25, labelY), FUIColors.TextBright, 9f);
            }
        }
    }

    private void DrawSliders(SKCanvas canvas)
    {
        DrawSlider(canvas, _deadzoneBounds, "DEADZONE", _curve.Deadzone, FUIColors.Warning, _draggingDeadzone);
        DrawSlider(canvas, _saturationBounds, "SATURATION", _curve.Saturation, FUIColors.Success, _draggingSaturation);
    }

    private void DrawSlider(SKCanvas canvas, SKRect bounds, string label, float value, SKColor color, bool dragging)
    {
        // Label
        FUIRenderer.DrawText(canvas, $"{label}: {value:P0}", new SKPoint(bounds.Left, bounds.Top), FUIColors.TextDim, 9f);

        // Track
        float trackY = bounds.Top + 18;
        float trackHeight = 8f;
        var trackBounds = new SKRect(bounds.Left, trackY, bounds.Right, trackY + trackHeight);

        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRect(trackBounds, trackPaint);

        using var trackFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRect(trackBounds, trackFramePaint);

        // Fill
        float fillWidth = (bounds.Width) * value;
        var fillBounds = new SKRect(bounds.Left + 1, trackY + 1, bounds.Left + 1 + fillWidth, trackY + trackHeight - 1);
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color.WithAlpha(100) };
        canvas.DrawRect(fillBounds, fillPaint);

        // Handle
        float handleX = bounds.Left + (bounds.Width) * value;
        float handleRadius = dragging ? 7f : 5f;

        using var handlePaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = dragging ? color : FUIColors.TextPrimary,
            IsAntialias = true
        };
        canvas.DrawCircle(handleX, trackY + trackHeight / 2, handleRadius, handlePaint);

        using var handleStroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        canvas.DrawCircle(handleX, trackY + trackHeight / 2, handleRadius, handleStroke);
    }

    private float ComputeCurveOutput(float input)
    {
        // If custom curve, interpolate between control points
        if (_curve.Type == CurveType.Custom || _controlPoints.Count > 2)
        {
            return InterpolateControlPoints(input);
        }

        // Otherwise use the curve's built-in Apply function
        return Math.Abs(_curve.Apply(input));
    }

    private float InterpolateControlPoints(float x)
    {
        if (_controlPoints.Count < 2) return x;

        // Find the segment containing x
        for (int i = 0; i < _controlPoints.Count - 1; i++)
        {
            var p1 = _controlPoints[i];
            var p2 = _controlPoints[i + 1];

            if (x >= p1.X && x <= p2.X)
            {
                if (Math.Abs(p2.X - p1.X) < 0.001f) return p1.Y;
                float t = (x - p1.X) / (p2.X - p1.X);
                return p1.Y + t * (p2.Y - p1.Y);
            }
        }

        // Extrapolate
        if (x < _controlPoints[0].X) return _controlPoints[0].Y;
        return _controlPoints[^1].Y;
    }

    private SKPoint GraphToScreen(SKPoint graphPt)
    {
        float x = _graphBounds.Left + graphPt.X * _graphBounds.Width;
        float y = _graphBounds.Bottom - graphPt.Y * _graphBounds.Height;
        return new SKPoint(x, y);
    }

    private SKPoint ScreenToGraph(SKPoint screenPt)
    {
        float x = (screenPt.X - _graphBounds.Left) / _graphBounds.Width;
        float y = (_graphBounds.Bottom - screenPt.Y) / _graphBounds.Height;
        return new SKPoint(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        var pt = new SKPoint(e.X, e.Y);
        bool needsInvalidate = false;

        // Check preset hover
        int newHoveredPreset = -1;
        if (_presetsBounds.Contains(pt))
        {
            float buttonWidth = (_presetsBounds.Width - 12) / _presetNames.Length;
            newHoveredPreset = (int)((pt.X - _presetsBounds.Left) / (buttonWidth + 4));
            if (newHoveredPreset >= _presetNames.Length) newHoveredPreset = -1;
        }
        if (newHoveredPreset != _hoveredPreset)
        {
            _hoveredPreset = newHoveredPreset;
            needsInvalidate = true;
        }

        // Dragging control point
        if (_draggedPointIndex >= 0)
        {
            var graphPt = ScreenToGraph(pt);

            // Constrain endpoints to X edges
            if (_draggedPointIndex == 0)
                graphPt.X = 0;
            else if (_draggedPointIndex == _controlPoints.Count - 1)
                graphPt.X = 1;
            else
            {
                // Interior points: constrain X between neighbors
                float minX = _controlPoints[_draggedPointIndex - 1].X + 0.02f;
                float maxX = _controlPoints[_draggedPointIndex + 1].X - 0.02f;
                graphPt.X = Math.Clamp(graphPt.X, minX, maxX);
            }

            _controlPoints[_draggedPointIndex] = graphPt;
            SyncCurveFromControlPoints();
            needsInvalidate = true;
        }
        else if (_draggingDeadzone)
        {
            float value = (pt.X - _deadzoneBounds.Left) / _deadzoneBounds.Width;
            _curve.Deadzone = Math.Clamp(value, 0f, Math.Min(0.5f, _curve.Saturation - 0.1f));
            CurveChanged?.Invoke(this, _curve);
            needsInvalidate = true;
        }
        else if (_draggingSaturation)
        {
            float value = (pt.X - _saturationBounds.Left) / _saturationBounds.Width;
            _curve.Saturation = Math.Clamp(value, Math.Max(0.5f, _curve.Deadzone + 0.1f), 1f);
            CurveChanged?.Invoke(this, _curve);
            needsInvalidate = true;
        }
        else
        {
            // Check control point hover
            int newHovered = FindControlPointAt(pt);
            if (newHovered != _hoveredPointIndex)
            {
                _hoveredPointIndex = newHovered;
                needsInvalidate = true;
            }
        }

        if (needsInvalidate)
            _canvas.Invalidate();
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var pt = new SKPoint(e.X, e.Y);

        // Check presets
        if (_presetsBounds.Contains(pt))
        {
            float buttonWidth = (_presetsBounds.Width - 12) / _presetNames.Length;
            int presetIndex = (int)((pt.X - _presetsBounds.Left) / (buttonWidth + 4));
            if (presetIndex >= 0 && presetIndex < _presetNames.Length)
            {
                ApplyPreset(presetIndex);
                return;
            }
        }

        // Check sliders
        float sliderTrackY = _deadzoneBounds.Top + 18;
        if (pt.Y >= sliderTrackY && pt.Y <= sliderTrackY + 20)
        {
            if (pt.X >= _deadzoneBounds.Left && pt.X <= _deadzoneBounds.Right)
            {
                _draggingDeadzone = true;
                OnMouseMove(sender, e);
                return;
            }
            if (pt.X >= _saturationBounds.Left && pt.X <= _saturationBounds.Right)
            {
                _draggingSaturation = true;
                OnMouseMove(sender, e);
                return;
            }
        }

        // Check control points
        int pointIndex = FindControlPointAt(pt);
        if (pointIndex >= 0)
        {
            _draggedPointIndex = pointIndex;
            _canvas.Invalidate();
            return;
        }

        // Double-click to add point
        if (_graphBounds.Contains(pt))
        {
            // Right-click to remove point
            if (e.Button == MouseButtons.Right && _hoveredPointIndex > 0 && _hoveredPointIndex < _controlPoints.Count - 1)
            {
                _controlPoints.RemoveAt(_hoveredPointIndex);
                _hoveredPointIndex = -1;
                SyncCurveFromControlPoints();
                _canvas.Invalidate();
                return;
            }

            // Add a new control point
            var graphPt = ScreenToGraph(pt);

            // Find insertion position (maintain sorted order by X)
            int insertIndex = 0;
            for (int i = 0; i < _controlPoints.Count; i++)
            {
                if (_controlPoints[i].X < graphPt.X)
                    insertIndex = i + 1;
            }

            _controlPoints.Insert(insertIndex, graphPt);
            _curve.Type = CurveType.Custom;
            SyncCurveFromControlPoints();
            _canvas.Invalidate();
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (_draggedPointIndex >= 0 || _draggingDeadzone || _draggingSaturation)
        {
            _draggedPointIndex = -1;
            _draggingDeadzone = false;
            _draggingSaturation = false;
            CurveChanged?.Invoke(this, _curve);
            _canvas.Invalidate();
        }
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        if (_hoveredPointIndex >= 0 || _hoveredPreset >= 0)
        {
            _hoveredPointIndex = -1;
            _hoveredPreset = -1;
            _canvas.Invalidate();
        }
    }

    private int FindControlPointAt(SKPoint screenPt)
    {
        for (int i = 0; i < _controlPoints.Count; i++)
        {
            var ptScreen = GraphToScreen(_controlPoints[i]);
            float dist = MathF.Sqrt(MathF.Pow(screenPt.X - ptScreen.X, 2) + MathF.Pow(screenPt.Y - ptScreen.Y, 2));
            if (dist <= ControlPointHitRadius)
                return i;
        }
        return -1;
    }

    private void ApplyPreset(int presetIndex)
    {
        _curve.Type = presetIndex switch
        {
            0 => CurveType.Linear,
            1 => CurveType.SCurve,
            2 => CurveType.Exponential,
            3 => CurveType.Custom,
            _ => CurveType.Linear
        };

        if (_curve.Type != CurveType.Custom)
        {
            _curve.Curvature = _curve.Type == CurveType.SCurve ? 0.5f : 0.3f;
        }

        SyncControlPointsFromCurve();
        CurveChanged?.Invoke(this, _curve);
        _canvas.Invalidate();
    }

    private void SyncCurveFromControlPoints()
    {
        _curve.Type = CurveType.Custom;
        _curve.ControlPoints = _controlPoints.Select(p => (p.X, p.Y)).ToList();
        CurveChanged?.Invoke(this, _curve);
    }
}
