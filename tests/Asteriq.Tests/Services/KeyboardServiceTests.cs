using Asteriq.Services;

namespace Asteriq.Tests.Services;

public class KeyboardServiceTests
{
    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("a", 0x41)]
    [InlineData("Z", 0x5A)]
    [InlineData("0", 0x30)]
    [InlineData("9", 0x39)]
    public void GetKeyCode_Letters_ReturnsCorrectCode(string keyName, int expectedCode)
    {
        var result = KeyboardService.GetKeyCode(keyName);

        Assert.NotNull(result);
        Assert.Equal(expectedCode, result.Value);
    }

    [Theory]
    [InlineData("F1", KeyboardService.VK_F1)]
    [InlineData("F12", KeyboardService.VK_F12)]
    [InlineData("F5", KeyboardService.VK_F5)]
    public void GetKeyCode_FunctionKeys_ReturnsCorrectCode(string keyName, int expectedCode)
    {
        var result = KeyboardService.GetKeyCode(keyName);

        Assert.NotNull(result);
        Assert.Equal(expectedCode, result.Value);
    }

    [Theory]
    [InlineData("CTRL", KeyboardService.VK_LCONTROL)]
    [InlineData("CONTROL", KeyboardService.VK_LCONTROL)]
    [InlineData("LCTRL", KeyboardService.VK_LCONTROL)]
    [InlineData("RCTRL", KeyboardService.VK_RCONTROL)]
    [InlineData("RCONTROL", KeyboardService.VK_RCONTROL)]
    public void GetKeyCode_CtrlKeys_ReturnsCorrectCode(string keyName, int expectedCode)
    {
        var result = KeyboardService.GetKeyCode(keyName);

        Assert.NotNull(result);
        Assert.Equal(expectedCode, result.Value);
    }

    [Theory]
    [InlineData("SHIFT", KeyboardService.VK_LSHIFT)]
    [InlineData("LSHIFT", KeyboardService.VK_LSHIFT)]
    [InlineData("RSHIFT", KeyboardService.VK_RSHIFT)]
    public void GetKeyCode_ShiftKeys_ReturnsCorrectCode(string keyName, int expectedCode)
    {
        var result = KeyboardService.GetKeyCode(keyName);

        Assert.NotNull(result);
        Assert.Equal(expectedCode, result.Value);
    }

    [Theory]
    [InlineData("ALT", KeyboardService.VK_LMENU)]
    [InlineData("LALT", KeyboardService.VK_LMENU)]
    [InlineData("RALT", KeyboardService.VK_RMENU)]
    [InlineData("MENU", KeyboardService.VK_LMENU)]
    public void GetKeyCode_AltKeys_ReturnsCorrectCode(string keyName, int expectedCode)
    {
        var result = KeyboardService.GetKeyCode(keyName);

        Assert.NotNull(result);
        Assert.Equal(expectedCode, result.Value);
    }

    [Theory]
    [InlineData("ENTER", KeyboardService.VK_RETURN)]
    [InlineData("RETURN", KeyboardService.VK_RETURN)]
    [InlineData("ESC", KeyboardService.VK_ESCAPE)]
    [InlineData("ESCAPE", KeyboardService.VK_ESCAPE)]
    [InlineData("SPACE", KeyboardService.VK_SPACE)]
    [InlineData("SPACEBAR", KeyboardService.VK_SPACE)]
    [InlineData("TAB", KeyboardService.VK_TAB)]
    [InlineData("BACKSPACE", KeyboardService.VK_BACK)]
    [InlineData("DELETE", KeyboardService.VK_DELETE)]
    [InlineData("INSERT", KeyboardService.VK_INSERT)]
    public void GetKeyCode_CommonKeys_ReturnsCorrectCode(string keyName, int expectedCode)
    {
        var result = KeyboardService.GetKeyCode(keyName);

        Assert.NotNull(result);
        Assert.Equal(expectedCode, result.Value);
    }

    [Theory]
    [InlineData("UP", KeyboardService.VK_UP)]
    [InlineData("DOWN", KeyboardService.VK_DOWN)]
    [InlineData("LEFT", KeyboardService.VK_LEFT)]
    [InlineData("RIGHT", KeyboardService.VK_RIGHT)]
    [InlineData("HOME", KeyboardService.VK_HOME)]
    [InlineData("END", KeyboardService.VK_END)]
    [InlineData("PAGEUP", KeyboardService.VK_PRIOR)]
    [InlineData("PGUP", KeyboardService.VK_PRIOR)]
    [InlineData("PAGEDOWN", KeyboardService.VK_NEXT)]
    [InlineData("PGDN", KeyboardService.VK_NEXT)]
    public void GetKeyCode_NavigationKeys_ReturnsCorrectCode(string keyName, int expectedCode)
    {
        var result = KeyboardService.GetKeyCode(keyName);

        Assert.NotNull(result);
        Assert.Equal(expectedCode, result.Value);
    }

    [Theory]
    [InlineData("NUM0", KeyboardService.VK_NUMPAD0)]
    [InlineData("NUMPAD0", KeyboardService.VK_NUMPAD0)]
    [InlineData("NUM9", KeyboardService.VK_NUMPAD9)]
    [InlineData("NUMPAD9", KeyboardService.VK_NUMPAD9)]
    [InlineData("NUMLOCK", KeyboardService.VK_NUMLOCK)]
    public void GetKeyCode_NumpadKeys_ReturnsCorrectCode(string keyName, int expectedCode)
    {
        var result = KeyboardService.GetKeyCode(keyName);

        Assert.NotNull(result);
        Assert.Equal(expectedCode, result.Value);
    }

    [Theory]
    [InlineData("UNKNOWNKEY")]
    [InlineData("NOTAKEY")]
    [InlineData("")]
    [InlineData("F13")]  // Only F1-F12 supported
    public void GetKeyCode_InvalidKey_ReturnsNull(string keyName)
    {
        var result = KeyboardService.GetKeyCode(keyName);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(KeyboardService.VK_LCONTROL, "LCtrl")]
    [InlineData(KeyboardService.VK_RCONTROL, "RCtrl")]
    [InlineData(KeyboardService.VK_LSHIFT, "LShift")]
    [InlineData(KeyboardService.VK_RSHIFT, "RShift")]
    [InlineData(KeyboardService.VK_LMENU, "LAlt")]
    [InlineData(KeyboardService.VK_RMENU, "RAlt")]
    [InlineData(KeyboardService.VK_RETURN, "Enter")]
    [InlineData(KeyboardService.VK_ESCAPE, "Esc")]
    [InlineData(KeyboardService.VK_SPACE, "Space")]
    public void GetKeyName_Modifiers_ReturnsCorrectName(int keyCode, string expectedName)
    {
        var result = KeyboardService.GetKeyName(keyCode);

        Assert.Equal(expectedName, result);
    }

    [Theory]
    [InlineData(KeyboardService.VK_F1, "F1")]
    [InlineData(KeyboardService.VK_F12, "F12")]
    public void GetKeyName_FunctionKeys_ReturnsCorrectName(int keyCode, string expectedName)
    {
        var result = KeyboardService.GetKeyName(keyCode);

        Assert.Equal(expectedName, result);
    }

    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x5A, "Z")]
    [InlineData(0x30, "0")]
    [InlineData(0x39, "9")]
    public void GetKeyName_LettersAndNumbers_ReturnsCorrectName(int keyCode, string expectedName)
    {
        var result = KeyboardService.GetKeyName(keyCode);

        Assert.Equal(expectedName, result);
    }

    [Fact]
    public void GetKeyName_UnknownKey_ReturnsHexCode()
    {
        var result = KeyboardService.GetKeyName(0xFF);

        Assert.Equal("0xFF", result);
    }

    [Fact]
    public void GetKeyCode_CaseInsensitive()
    {
        var lower = KeyboardService.GetKeyCode("rctrl");
        var upper = KeyboardService.GetKeyCode("RCTRL");
        var mixed = KeyboardService.GetKeyCode("RCtrl");

        Assert.Equal(lower, upper);
        Assert.Equal(upper, mixed);
    }

    [Fact]
    public void GetKeyCode_TrimsWhitespace()
    {
        var result = KeyboardService.GetKeyCode("  SPACE  ");

        Assert.NotNull(result);
        Assert.Equal(KeyboardService.VK_SPACE, result.Value);
    }

    // Test that KeyboardService can be created and disposed
    [Fact]
    public void KeyboardService_CreateAndDispose_NoException()
    {
        var service = new KeyboardService();
        service.Dispose();
    }

    // Test that ReleaseAll can be called without exception
    [Fact]
    public void KeyboardService_ReleaseAll_NoException()
    {
        var service = new KeyboardService();
        service.ReleaseAll();
        service.Dispose();
    }

    // Note: We can't easily test actual keyboard input without affecting the system,
    // but we can test the state tracking logic by calling SetKey multiple times
    // and verifying ReleaseAll doesn't throw
    [Fact]
    public void KeyboardService_SetKeyThenReleaseAll_NoException()
    {
        var service = new KeyboardService();

        // This will actually send keyboard input, so we use a safe key
        // and immediately release it
        service.SetKey(KeyboardService.VK_RCONTROL, true);
        service.SetKey(KeyboardService.VK_RCONTROL, false);
        service.ReleaseAll();
        service.Dispose();
    }
}
