using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void MakeCurveSymmetrical()
    {
        if (_curve.ControlPoints.Count < 2) return;

        // Create a new symmetrical set of points
        var newPoints = new List<SKPoint>();

        // Always include start point
        newPoints.Add(new SKPoint(0, 0));

        // For each point in the left half (X < 0.5), create its mirror
        var leftHalf = _curve.ControlPoints
            .Where(p => p.X > 0 && p.X < 0.5f)
            .OrderBy(p => p.X)
            .ToList();

        foreach (var pt in leftHalf)
        {
            newPoints.Add(pt);
        }

        // Add center point if there's one
        var centerPoint = _curve.ControlPoints.FirstOrDefault(p => Math.Abs(p.X - 0.5f) < 0.02f);
        if (centerPoint.X > 0.4f && centerPoint.X < 0.6f)
        {
            newPoints.Add(new SKPoint(0.5f, 0.5f)); // Center is always (0.5, 0.5) for perfect symmetry
        }
        else if (leftHalf.Count > 0)
        {
            // Add a center point if we have left half points
            newPoints.Add(new SKPoint(0.5f, 0.5f));
        }

        // Add mirrored points from left half (in reverse order for right half)
        for (int i = leftHalf.Count - 1; i >= 0; i--)
        {
            var pt = leftHalf[i];
            newPoints.Add(new SKPoint(1f - pt.X, 1f - pt.Y));
        }

        // Always include end point
        newPoints.Add(new SKPoint(1, 1));

        _curve.ControlPoints = newPoints;
    }

    private void AddCurveControlPoint(SKPoint graphPt)
    {
        // Don't add points at exact endpoints
        if (graphPt.X <= 0.01f || graphPt.X >= 0.99f) return;

        // Find insertion position (maintain sorted order by X)
        int insertIndex = 0;
        for (int i = 0; i < _curve.ControlPoints.Count; i++)
        {
            if (_curve.ControlPoints[i].X < graphPt.X)
                insertIndex = i + 1;
        }

        _curve.ControlPoints.Insert(insertIndex, graphPt);

        // If symmetrical mode is enabled, also add the mirror point
        if (_curve.Symmetrical)
        {
            float mirrorX = 1f - graphPt.X;
            float mirrorY = 1f - graphPt.Y;

            // Don't add if mirror point would be too close to existing point
            bool tooClose = _curve.ControlPoints.Any(p => Math.Abs(p.X - mirrorX) < 0.04f);
            if (!tooClose && mirrorX > 0.01f && mirrorX < 0.99f)
            {
                // Find insertion position for mirror point
                int mirrorInsertIndex = 0;
                for (int i = 0; i < _curve.ControlPoints.Count; i++)
                {
                    if (_curve.ControlPoints[i].X < mirrorX)
                        mirrorInsertIndex = i + 1;
                }

                _curve.ControlPoints.Insert(mirrorInsertIndex, new SKPoint(mirrorX, mirrorY));
            }
        }

        _curve.SelectedType = CurveType.Custom;
        _ctx.InvalidateCanvas();
    }

    private void RemoveCurveControlPoint(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= _curve.ControlPoints.Count)
            return;

        var pt = _curve.ControlPoints[pointIndex];

        // Don't remove endpoints (0,0) or (1,1)
        bool isEndpoint = pointIndex == 0 || pointIndex == _curve.ControlPoints.Count - 1;
        if (isEndpoint)
            return;

        // Don't remove center point (0.5, 0.5)
        bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;
        if (isCenterPoint)
            return;

        // Remove the point
        _curve.ControlPoints.RemoveAt(pointIndex);

        // If symmetrical mode is enabled, also remove the mirror point
        if (_curve.Symmetrical)
        {
            float mirrorX = 1f - pt.X;

            // Find and remove the mirror point
            for (int i = _curve.ControlPoints.Count - 1; i >= 0; i--)
            {
                var mirrorPt = _curve.ControlPoints[i];
                // Skip endpoints and center
                if (i == 0 || i == _curve.ControlPoints.Count - 1)
                    continue;
                if (Math.Abs(mirrorPt.X - 0.5f) < 0.01f && Math.Abs(mirrorPt.Y - 0.5f) < 0.01f)
                    continue;

                if (Math.Abs(mirrorPt.X - mirrorX) < 0.02f)
                {
                    _curve.ControlPoints.RemoveAt(i);
                    break;
                }
            }
        }

        _ctx.InvalidateCanvas();
    }

}