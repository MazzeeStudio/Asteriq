namespace Asteriq.UI;

/// <summary>
/// Base class for all FUI borderless dialog windows.
/// Adds CS_DROPSHADOW so dialogs have a native OS shadow for depth.
/// </summary>
public class FUIBaseDialog : Form
{
    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x00020000;
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }
}
