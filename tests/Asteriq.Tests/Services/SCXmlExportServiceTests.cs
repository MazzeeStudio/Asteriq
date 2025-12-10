using System.Xml.Linq;
using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.Tests.Services;

public class SCXmlExportServiceTests
{
    private readonly SCXmlExportService _service = new();

    private static SCExportProfile CreateTestProfile()
    {
        var profile = new SCExportProfile
        {
            ProfileName = "test_profile"
        };
        profile.SetSCInstance(1, 1);
        return profile;
    }

    #region Export Tests

    [Fact]
    public void Export_CreatesActionMapsElement()
    {
        var profile = CreateTestProfile();

        var doc = _service.Export(profile);

        Assert.NotNull(doc.Root);
        Assert.Equal("ActionMaps", doc.Root.Name.LocalName);
    }

    [Fact]
    public void Export_SetsRequiredAttributes()
    {
        var profile = CreateTestProfile();

        var doc = _service.Export(profile);

        Assert.Equal("1", doc.Root?.Attribute("version")?.Value);
        Assert.Equal("2", doc.Root?.Attribute("optionsVersion")?.Value);
        Assert.Equal("2", doc.Root?.Attribute("rebindVersion")?.Value);
        Assert.Equal("test_profile", doc.Root?.Attribute("profileName")?.Value);
    }

    [Fact]
    public void Export_IncludesCustomisationUIHeader()
    {
        var profile = CreateTestProfile();

        var doc = _service.Export(profile);

        var header = doc.Root?.Element("CustomisationUIHeader");
        Assert.NotNull(header);
        Assert.Equal("test_profile", header.Attribute("label")?.Value);
    }

    [Fact]
    public void Export_IncludesDevicesInHeader()
    {
        var profile = CreateTestProfile();
        profile.SetSCInstance(1, 1);
        profile.SetSCInstance(2, 2);

        var doc = _service.Export(profile);

        var devices = doc.Root?.Element("CustomisationUIHeader")?.Element("devices");
        Assert.NotNull(devices);
        Assert.NotNull(devices.Element("keyboard"));
        Assert.NotNull(devices.Element("mouse"));
        Assert.Equal(2, devices.Elements("joystick").Count());
    }

    [Fact]
    public void Export_CreatesOptionsElements_ForEachVJoyDevice()
    {
        var profile = CreateTestProfile();
        profile.SetSCInstance(1, 1);
        profile.SetSCInstance(2, 2);

        var doc = _service.Export(profile);

        var options = doc.Root?.Elements("options").ToList();
        Assert.Equal(2, options?.Count);
        Assert.Contains(options, o => o.Attribute("instance")?.Value == "1");
        Assert.Contains(options, o => o.Attribute("instance")?.Value == "2");
    }

    [Fact]
    public void Export_CreatesActionMapElements_GroupedByMap()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_movement",
            ActionName = "v_strafe_forward",
            VJoyDevice = 1,
            InputName = "y",
            InputType = SCInputType.Axis
        });
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_weapons",
            ActionName = "v_attack1",
            VJoyDevice = 1,
            InputName = "button1",
            InputType = SCInputType.Button
        });

        var doc = _service.Export(profile);

        var actionMaps = doc.Root?.Elements("actionmap").ToList();
        Assert.Equal(2, actionMaps?.Count);
        Assert.Contains(actionMaps, m => m.Attribute("name")?.Value == "spaceship_movement");
        Assert.Contains(actionMaps, m => m.Attribute("name")?.Value == "spaceship_weapons");
    }

    [Fact]
    public void Export_CreatesRebindElements_WithCorrectInput()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_movement",
            ActionName = "v_strafe_forward",
            VJoyDevice = 1,
            InputName = "y",
            InputType = SCInputType.Axis
        });

        var doc = _service.Export(profile);

        var rebind = doc.Descendants("rebind").First();
        Assert.Equal("js1_y", rebind.Attribute("input")?.Value);
    }

    [Fact]
    public void Export_AddsInvertAttribute_ForInvertedAxes()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_movement",
            ActionName = "v_pitch",
            VJoyDevice = 1,
            InputName = "y",
            InputType = SCInputType.Axis,
            Inverted = true
        });

        var doc = _service.Export(profile);

        var rebind = doc.Descendants("rebind").First();
        Assert.Equal("1", rebind.Attribute("invert")?.Value);
    }

    [Fact]
    public void Export_DoesNotAddInvertAttribute_ForButtons()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_weapons",
            ActionName = "v_attack1",
            VJoyDevice = 1,
            InputName = "button1",
            InputType = SCInputType.Button,
            Inverted = true // Should be ignored for buttons
        });

        var doc = _service.Export(profile);

        var rebind = doc.Descendants("rebind").First();
        Assert.Null(rebind.Attribute("invert"));
    }

    [Fact]
    public void Export_AddsActivationModeAttribute_ForNonPress()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_targeting",
            ActionName = "v_target_cycle",
            VJoyDevice = 1,
            InputName = "button5",
            InputType = SCInputType.Button,
            ActivationMode = SCActivationMode.DoubleTap
        });

        var doc = _service.Export(profile);

        var rebind = doc.Descendants("rebind").First();
        Assert.Equal("double_tap", rebind.Attribute("activationMode")?.Value);
    }

    [Fact]
    public void Export_DoesNotAddActivationModeAttribute_ForPress()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_weapons",
            ActionName = "v_attack1",
            VJoyDevice = 1,
            InputName = "button1",
            InputType = SCInputType.Button,
            ActivationMode = SCActivationMode.Press
        });

        var doc = _service.Export(profile);

        var rebind = doc.Descendants("rebind").First();
        Assert.Null(rebind.Attribute("activationMode"));
    }

    [Fact]
    public void Export_MapsVJoyToSCInstance_Correctly()
    {
        var profile = CreateTestProfile();
        profile.SetSCInstance(1, 3); // vJoy 1 = js3
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_movement",
            ActionName = "v_strafe_forward",
            VJoyDevice = 1,
            InputName = "y"
        });

        var doc = _service.Export(profile);

        var rebind = doc.Descendants("rebind").First();
        Assert.Equal("js3_y", rebind.Attribute("input")?.Value);
    }

    [Fact]
    public void Export_EmptyActionMaps_AreNotIncluded()
    {
        var profile = CreateTestProfile();
        // No bindings

        var doc = _service.Export(profile);

        var actionMaps = doc.Root?.Elements("actionmap").ToList();
        Assert.Empty(actionMaps ?? new List<XElement>());
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void Validate_WithEmptyProfileName_ReturnsError()
    {
        var profile = new SCExportProfile { ProfileName = "" };

        var result = _service.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("name"));
    }

    [Fact]
    public void Validate_WithNoBindings_ReturnsWarning()
    {
        var profile = CreateTestProfile();
        // No bindings

        var result = _service.Validate(profile);

        Assert.True(result.IsValid);
        Assert.True(result.HasWarnings);
        Assert.Contains(result.Warnings, w => w.Contains("empty"));
    }

    [Fact]
    public void Validate_WithDuplicateBindings_ReturnsWarning()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "test",
            ActionName = "action1",
            VJoyDevice = 1,
            InputName = "button1"
        });
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "test",
            ActionName = "action1",
            VJoyDevice = 1,
            InputName = "button2"
        });

        var result = _service.Validate(profile);

        Assert.True(result.HasWarnings);
        Assert.Contains(result.Warnings, w => w.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_WithMissingVJoyMapping_ReturnsWarning()
    {
        var profile = new SCExportProfile { ProfileName = "test" };
        // Don't set VJoyToSCInstance for device 2
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "test",
            ActionName = "action1",
            VJoyDevice = 2,
            InputName = "button1"
        });

        var result = _service.Validate(profile);

        Assert.True(result.HasWarnings);
        Assert.Contains(result.Warnings, w => w.Contains("vJoy device 2"));
    }

    [Fact]
    public void Validate_ValidProfile_ReturnsNoErrors()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "test",
            ActionName = "action1",
            VJoyDevice = 1,
            InputName = "button1"
        });

        var result = _service.Validate(profile);

        Assert.True(result.IsValid);
    }

    #endregion

    #region SCExportProfile Tests

    [Fact]
    public void SCExportProfile_GetSCInstance_ReturnsConfiguredValue()
    {
        var profile = new SCExportProfile();
        profile.SetSCInstance(1, 5);

        var result = profile.GetSCInstance(1);

        Assert.Equal(5, result);
    }

    [Fact]
    public void SCExportProfile_GetSCInstance_ReturnsDefault_WhenNotConfigured()
    {
        var profile = new SCExportProfile();

        var result = profile.GetSCInstance(3);

        Assert.Equal(3, result); // Default: vJoy ID = SC instance
    }

    [Fact]
    public void SCExportProfile_JoystickCount_ReturnsMaxInstance()
    {
        var profile = new SCExportProfile();
        profile.SetSCInstance(1, 2);
        profile.SetSCInstance(2, 4);

        Assert.Equal(4, profile.JoystickCount);
    }

    [Fact]
    public void SCExportProfile_SetBinding_ReplacesExisting()
    {
        var profile = new SCExportProfile();
        var binding1 = new SCActionBinding { InputName = "button1" };
        var binding2 = new SCActionBinding { InputName = "button2" };

        profile.SetBinding("test", "action1", binding1);
        profile.SetBinding("test", "action1", binding2);

        Assert.Single(profile.Bindings);
        Assert.Equal("button2", profile.Bindings[0].InputName);
    }

    [Fact]
    public void SCExportProfile_GetBinding_ReturnsCorrectBinding()
    {
        var profile = new SCExportProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "test",
            ActionName = "action1",
            InputName = "button1"
        });

        var binding = profile.GetBinding("test", "action1");

        Assert.NotNull(binding);
        Assert.Equal("button1", binding.InputName);
    }

    [Fact]
    public void SCExportProfile_RemoveBinding_RemovesExisting()
    {
        var profile = new SCExportProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "test",
            ActionName = "action1"
        });

        var result = profile.RemoveBinding("test", "action1");

        Assert.True(result);
        Assert.Empty(profile.Bindings);
    }

    [Fact]
    public void SCExportProfile_GetExportFileName_SanitizesName()
    {
        var profile = new SCExportProfile { ProfileName = "My Profile <test>" };

        var filename = profile.GetExportFileName();

        Assert.Equal("layout_My Profile _test__exported.xml", filename);
    }

    #endregion

    #region SCActionBinding Tests

    [Fact]
    public void SCActionBinding_GetSCInputString_FormatsCorrectly()
    {
        var binding = new SCActionBinding
        {
            VJoyDevice = 1,
            InputName = "button5"
        };

        var result = binding.GetSCInputString(2);

        Assert.Equal("js2_button5", result);
    }

    [Fact]
    public void SCActionBinding_Key_CombinesMapAndName()
    {
        var binding = new SCActionBinding
        {
            ActionMap = "spaceship_movement",
            ActionName = "v_strafe_forward"
        };

        Assert.Equal("spaceship_movement.v_strafe_forward", binding.Key);
    }

    #endregion

    #region SCActivationMode Tests

    [Fact]
    public void SCActivationMode_ToXmlString_Press_ReturnsNull()
    {
        Assert.Null(SCActivationMode.Press.ToXmlString());
    }

    [Fact]
    public void SCActivationMode_ToXmlString_DoubleTap_ReturnsCorrectValue()
    {
        Assert.Equal("double_tap", SCActivationMode.DoubleTap.ToXmlString());
    }

    [Fact]
    public void SCActivationMode_ToXmlString_TripleTap_ReturnsCorrectValue()
    {
        Assert.Equal("triple_tap", SCActivationMode.TripleTap.ToXmlString());
    }

    [Fact]
    public void SCActivationMode_ToXmlString_Hold_ReturnsCorrectValue()
    {
        Assert.Equal("hold", SCActivationMode.Hold.ToXmlString());
    }

    [Fact]
    public void SCActivationMode_ParseFromXml_Empty_ReturnsPress()
    {
        Assert.Equal(SCActivationMode.Press, SCActivationModeExtensions.ParseFromXml(""));
        Assert.Equal(SCActivationMode.Press, SCActivationModeExtensions.ParseFromXml(null));
    }

    [Fact]
    public void SCActivationMode_ParseFromXml_DoubleTap_Variants()
    {
        Assert.Equal(SCActivationMode.DoubleTap, SCActivationModeExtensions.ParseFromXml("double_tap"));
        Assert.Equal(SCActivationMode.DoubleTap, SCActivationModeExtensions.ParseFromXml("doubletap"));
    }

    [Fact]
    public void SCActivationMode_ParseFromXml_Hold_Variants()
    {
        Assert.Equal(SCActivationMode.Hold, SCActivationModeExtensions.ParseFromXml("hold"));
        Assert.Equal(SCActivationMode.Hold, SCActivationModeExtensions.ParseFromXml("delayed_hold"));
    }

    #endregion
}
