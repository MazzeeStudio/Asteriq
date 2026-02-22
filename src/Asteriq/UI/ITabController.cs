using SkiaSharp;

namespace Asteriq.UI;

public interface ITabController
{
    void Draw(SKCanvas canvas, SKRect bounds, float padLeft, float contentTop, float contentBottom);
    void OnMouseDown(MouseEventArgs e);
    void OnMouseMove(MouseEventArgs e);
    void OnMouseUp(MouseEventArgs e);
    void OnMouseWheel(MouseEventArgs e);
    bool ProcessCmdKey(ref Message msg, Keys keyData);
    void OnMouseLeave();
    void OnTick();
    void OnActivated();
    void OnDeactivated();
}
