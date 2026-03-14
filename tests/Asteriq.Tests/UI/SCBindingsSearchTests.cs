using Asteriq.Models;
using Asteriq.UI.Controllers;

namespace Asteriq.Tests.UI;

/// <summary>
/// Tests for <see cref="SCBindingsSearch"/> — both free-text and button-capture search modes.
/// </summary>
public class SCBindingsSearchTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static SCAction Action(string map, string name) =>
        new() { ActionMap = map, ActionName = name };

    private static SCActionBinding JoyBinding(SCAction a, uint vjoy, string input, params string[] modifiers) =>
        new()
        {
            ActionMap = a.ActionMap,
            ActionName = a.ActionName,
            DeviceType = SCDeviceType.Joystick,
            VJoyDevice = vjoy,
            InputName = input,
            Modifiers = new List<string>(modifiers)
        };

    private static SCActionBinding PhysBinding(SCAction a, string hidPath, string input, params string[] modifiers) =>
        new()
        {
            ActionMap = a.ActionMap,
            ActionName = a.ActionName,
            DeviceType = SCDeviceType.Joystick,
            PhysicalDeviceId = hidPath,
            InputName = input,
            Modifiers = new List<string>(modifiers)
        };

    private static SCActionBinding KbBinding(SCAction a, string input) =>
        new()
        {
            ActionMap = a.ActionMap,
            ActionName = a.ActionName,
            DeviceType = SCDeviceType.Keyboard,
            InputName = input
        };

    // ─────────────────────────────────────────────────────────────────────────
    // MatchesTextSearch
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MatchesTextSearch_ActionNameSubstring_ReturnsTrue()
    {
        var action = Action("spaceship_movement", "v_strafe_forward");
        Assert.True(SCBindingsSearch.MatchesTextSearch(action, [], "strafe"));
    }

    [Fact]
    public void MatchesTextSearch_ActionNameNoMatch_ReturnsFalse()
    {
        var action = Action("spaceship_movement", "v_strafe_forward");
        Assert.False(SCBindingsSearch.MatchesTextSearch(action, [], "landing"));
    }

    [Fact]
    public void MatchesTextSearch_BindingInputSubstring_ReturnsTrue()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { JoyBinding(action, 1, "button3") };
        // "button3" is a substring of "button3" — matches
        Assert.True(SCBindingsSearch.MatchesTextSearch(action, bindings, "button3"));
    }

    [Fact]
    public void MatchesTextSearch_Button3MatchesButton30_ReturnsTrue()
    {
        // Text search is intentionally broad — "button3" is a substring of "button30"
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { JoyBinding(action, 1, "button30") };
        Assert.True(SCBindingsSearch.MatchesTextSearch(action, bindings, "button3"));
    }

    [Fact]
    public void MatchesTextSearch_ModifierSubstring_ReturnsTrue()
    {
        var action = Action("spaceship_movement", "v_ifcs_toggle");
        var bindings = new[] { JoyBinding(action, 1, "button5", "rctrl") };
        Assert.True(SCBindingsSearch.MatchesTextSearch(action, bindings, "rctrl"));
    }

    [Fact]
    public void MatchesTextSearch_ComposedModifierInput_ReturnsTrue()
    {
        var action = Action("spaceship_movement", "v_ifcs_toggle");
        var bindings = new[] { JoyBinding(action, 1, "button5", "rctrl") };
        // Searching the composed "rctrl+button5" form should match
        Assert.True(SCBindingsSearch.MatchesTextSearch(action, bindings, "rctrl+button5"));
    }

    [Fact]
    public void MatchesTextSearch_UnrelatedBindingIgnored_ReturnsFalse()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        var otherAction = Action("spaceship_movement", "v_strafe_forward");
        // Binding belongs to a different action
        var bindings = new[] { JoyBinding(otherAction, 1, "button3") };
        Assert.False(SCBindingsSearch.MatchesTextSearch(action, bindings, "button3"));
    }

    [Fact]
    public void MatchesTextSearch_DefaultBindingInput_ReturnsTrue()
    {
        var action = Action("spaceship_movement", "v_pitch_up");
        action.DefaultBindings.Add(new SCDefaultBinding { DevicePrefix = "js1", Input = "x" });
        Assert.True(SCBindingsSearch.MatchesTextSearch(action, [], "x"));
    }

    [Fact]
    public void MatchesTextSearch_CaseInsensitive_ReturnsTrue()
    {
        var action = Action("spaceship_movement", "v_strafe_forward");
        Assert.True(SCBindingsSearch.MatchesTextSearch(action, [], "STRAFE"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MatchesButtonCapture — exact match, column-scoped
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MatchesButtonCapture_ExactMatch_ReturnsTrue()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { JoyBinding(action, vjoy: 1, input: "button3") };
        Assert.True(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_Button3DoesNotMatchButton30_ReturnsFalse()
    {
        // THIS is the core fix — "button3" must not match "button30"
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { JoyBinding(action, vjoy: 1, input: "button30") };
        Assert.False(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_Button3DoesNotMatchButton31_ReturnsFalse()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { JoyBinding(action, vjoy: 1, input: "button31") };
        Assert.False(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_WrongVJoyColumn_ReturnsFalse()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { JoyBinding(action, vjoy: 2, input: "button3") };
        // Highlight is on vJoy 1 — binding is on vJoy 2
        Assert.False(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_CorrectVJoyColumn_OtherButtonIgnored_ReturnsFalse()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { JoyBinding(action, vjoy: 1, input: "button5") };
        Assert.False(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_WithModifier_ExactMatch_ReturnsTrue()
    {
        var action = Action("spaceship_movement", "v_ifcs_toggle");
        var bindings = new[] { JoyBinding(action, vjoy: 1, input: "button3", "rctrl") };
        Assert.True(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: "rctrl", vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_NoModifierCaptured_BindingHasModifier_ReturnsFalse()
    {
        // User pressed plain button3 — should NOT match an rctrl+button3 binding
        var action = Action("spaceship_movement", "v_ifcs_toggle");
        var bindings = new[] { JoyBinding(action, vjoy: 1, input: "button3", "rctrl") };
        Assert.False(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_ModifierCaptured_BindingHasNoModifier_ReturnsFalse()
    {
        // User pressed rctrl+button3 — should NOT match a plain button3 binding
        var action = Action("spaceship_movement", "v_ifcs_toggle");
        var bindings = new[] { JoyBinding(action, vjoy: 1, input: "button3") };
        Assert.False(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: "rctrl", vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_PhysicalDevice_ExactMatch_ReturnsTrue()
    {
        const string hidPath = @"\\?\HID#VID_3344&PID_0001#1234";
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { PhysBinding(action, hidPath, "button3") };
        Assert.True(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: null, physicalDeviceId: hidPath));
    }

    [Fact]
    public void MatchesButtonCapture_PhysicalDevice_WrongHidPath_ReturnsFalse()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { PhysBinding(action, @"\\?\HID#OTHER", "button3") };
        Assert.False(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: null,
            physicalDeviceId: @"\\?\HID#VID_3344&PID_0001#1234"));
    }

    [Fact]
    public void MatchesButtonCapture_NoColumnConstraint_AnyJoystick_ReturnsTrue()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { JoyBinding(action, vjoy: 2, input: "button3") };
        // No column specified — accept any joystick binding with exact input
        Assert.True(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: null, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_NoColumnConstraint_KeyboardBinding_ReturnsFalse()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { KbBinding(action, "button3") };
        // No column but keyboard binding — should not match joystick capture
        Assert.False(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: null, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_CaseInsensitiveInput_ReturnsTrue()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new[] { JoyBinding(action, vjoy: 1, input: "BUTTON3") };
        Assert.True(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_HatInput_ExactMatch_ReturnsTrue()
    {
        var action = Action("spaceship_movement", "v_view_up");
        var bindings = new[] { JoyBinding(action, vjoy: 1, input: "hat1_up") };
        Assert.True(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "hat1_up", capturedModifier: null, vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_HatInputPartial_ReturnsFalse()
    {
        // "hat1" should not match "hat1_up"
        var action = Action("spaceship_movement", "v_view_up");
        var bindings = new[] { JoyBinding(action, vjoy: 1, input: "hat1_up") };
        Assert.False(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "hat1", capturedModifier: null, vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_EmptyBindingsList_ReturnsFalse()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        Assert.False(SCBindingsSearch.MatchesButtonCapture(action, [],
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: 1, physicalDeviceId: null));
    }

    [Fact]
    public void MatchesButtonCapture_MultipleBindings_OneMatches_ReturnsTrue()
    {
        var action = Action("spaceship_weapons", "v_attack1");
        var bindings = new SCActionBinding[]
        {
            JoyBinding(action, vjoy: 1, input: "button5"),   // wrong button
            JoyBinding(action, vjoy: 1, input: "button3"),   // correct
            JoyBinding(action, vjoy: 2, input: "button3"),   // correct button, wrong vJoy
        };
        Assert.True(SCBindingsSearch.MatchesButtonCapture(action, bindings,
            capturedInput: "button3", capturedModifier: null, vjoyDeviceId: 1, physicalDeviceId: null));
    }
}
