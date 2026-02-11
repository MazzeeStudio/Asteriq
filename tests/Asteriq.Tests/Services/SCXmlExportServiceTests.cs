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
        Assert.Contains(options!, o => o.Attribute("instance")?.Value == "1");
        Assert.Contains(options!, o => o.Attribute("instance")?.Value == "2");
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
        Assert.Contains(actionMaps!, m => m.Attribute("name")?.Value == "spaceship_movement");
        Assert.Contains(actionMaps!, m => m.Attribute("name")?.Value == "spaceship_weapons");
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

    [Fact]
    public void Export_CreatesKeyboardBindings_WithKbPrefix()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_movement",
            ActionName = "v_strafe_forward",
            DeviceType = SCDeviceType.Keyboard,
            InputName = "w",
            InputType = SCInputType.Button
        });

        var doc = _service.Export(profile);

        var rebind = doc.Descendants("rebind").First();
        Assert.Equal("kb1_w", rebind.Attribute("input")?.Value);
    }

    [Fact]
    public void Export_CreatesMouseBindings_WithMoPrefix()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_weapons",
            ActionName = "v_attack1",
            DeviceType = SCDeviceType.Mouse,
            InputName = "mouse1",
            InputType = SCInputType.Button
        });

        var doc = _service.Export(profile);

        var rebind = doc.Descendants("rebind").First();
        Assert.Equal("mo1_mouse1", rebind.Attribute("input")?.Value);
    }

    [Fact]
    public void Export_CreatesMultipleBindingsPerAction()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_movement",
            ActionName = "v_strafe_forward",
            DeviceType = SCDeviceType.Keyboard,
            InputName = "w"
        });
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_movement",
            ActionName = "v_strafe_forward",
            DeviceType = SCDeviceType.Joystick,
            VJoyDevice = 1,
            InputName = "y"
        });

        var doc = _service.Export(profile);

        var action = doc.Descendants("action").First(a => a.Attribute("name")?.Value == "v_strafe_forward");
        var rebinds = action.Elements("rebind").ToList();
        Assert.Equal(2, rebinds.Count);
        Assert.Contains(rebinds, r => r.Attribute("input")?.Value == "kb1_w");
        Assert.Contains(rebinds, r => r.Attribute("input")?.Value == "js1_y");
    }

    [Fact]
    public void Export_HandlesModifiersCorrectly_ForKeyboard()
    {
        var profile = CreateTestProfile();
        profile.Bindings.Add(new SCActionBinding
        {
            ActionMap = "spaceship_targeting",
            ActionName = "v_target_hostile",
            DeviceType = SCDeviceType.Keyboard,
            InputName = "t",
            Modifiers = new List<string> { "lalt" }
        });

        var doc = _service.Export(profile);

        var rebind = doc.Descendants("rebind").First();
        Assert.Equal("kb1_lalt+t", rebind.Attribute("input")?.Value);
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
    public void SCActionBinding_Key_IncludesDeviceType()
    {
        var binding = new SCActionBinding
        {
            ActionMap = "spaceship_movement",
            ActionName = "v_strafe_forward",
            DeviceType = SCDeviceType.Joystick
        };

        // Key now includes device type to allow multiple bindings per action
        Assert.Equal("spaceship_movement.v_strafe_forward.Joystick", binding.Key);
        // ActionKey is the old format without device type
        Assert.Equal("spaceship_movement.v_strafe_forward", binding.ActionKey);
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

    #region Import Tests

    [Fact]
    public void ImportFromFile_SkipsEmptyInputNames()
    {
        // Create a test XML with empty input names (just whitespace after prefix)
        var xml = @"<ActionMaps version=""1"" optionsVersion=""2"" rebindVersion=""2"" profileName=""test"">
            <actionmap name=""test_map"">
                <action name=""valid_action"">
                    <rebind input=""js1_button5""/>
                </action>
                <action name=""empty_action"">
                    <rebind input=""js1_ ""/>
                </action>
            </actionmap>
        </ActionMaps>";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, xml);

            var result = _service.ImportFromFile(tempFile);

            Assert.True(result.Success);
            Assert.Single(result.Bindings);
            Assert.Equal("valid_action", result.Bindings[0].ActionName);
            Assert.Equal("button5", result.Bindings[0].InputName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportFromFile_ParsesJoystickBindings()
    {
        var xml = @"<ActionMaps version=""1"" profileName=""test"">
            <actionmap name=""spaceship_movement"">
                <action name=""v_strafe_forward"">
                    <rebind input=""js1_y""/>
                </action>
            </actionmap>
        </ActionMaps>";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, xml);

            var result = _service.ImportFromFile(tempFile);

            Assert.True(result.Success);
            Assert.Single(result.Bindings);
            var binding = result.Bindings[0];
            Assert.Equal("spaceship_movement", binding.ActionMap);
            Assert.Equal("v_strafe_forward", binding.ActionName);
            Assert.Equal(SCDeviceType.Joystick, binding.DeviceType);
            Assert.Equal(1u, binding.VJoyDevice);
            Assert.Equal("y", binding.InputName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportFromFile_ParsesMouseBindings()
    {
        var xml = @"<ActionMaps version=""1"" profileName=""test"">
            <actionmap name=""spaceship_weapons"">
                <action name=""v_attack1"">
                    <rebind input=""mo1_mouse1""/>
                </action>
            </actionmap>
        </ActionMaps>";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, xml);

            var result = _service.ImportFromFile(tempFile);

            Assert.True(result.Success);
            Assert.Single(result.Bindings);
            var binding = result.Bindings[0];
            Assert.Equal(SCDeviceType.Mouse, binding.DeviceType);
            Assert.Equal("mouse1", binding.InputName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportFromFile_ParsesKeyboardBindings()
    {
        var xml = @"<ActionMaps version=""1"" profileName=""test"">
            <actionmap name=""spaceship_movement"">
                <action name=""v_strafe_forward"">
                    <rebind input=""kb1_w""/>
                </action>
            </actionmap>
        </ActionMaps>";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, xml);

            var result = _service.ImportFromFile(tempFile);

            Assert.True(result.Success);
            Assert.Single(result.Bindings);
            var binding = result.Bindings[0];
            Assert.Equal(SCDeviceType.Keyboard, binding.DeviceType);
            Assert.Equal("w", binding.InputName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportFromFile_ParsesModifiers()
    {
        var xml = @"<ActionMaps version=""1"" profileName=""test"">
            <actionmap name=""spaceship_targeting"">
                <action name=""v_target_hostile"">
                    <rebind input=""js1_rctrl+button5""/>
                </action>
            </actionmap>
        </ActionMaps>";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, xml);

            var result = _service.ImportFromFile(tempFile);

            Assert.True(result.Success);
            Assert.Single(result.Bindings);
            var binding = result.Bindings[0];
            Assert.Equal("button5", binding.InputName);
            Assert.Single(binding.Modifiers);
            Assert.Equal("rctrl", binding.Modifiers[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportFromFile_ParsesActivationMode()
    {
        var xml = @"<ActionMaps version=""1"" profileName=""test"">
            <actionmap name=""seat_general"">
                <action name=""v_eject"">
                    <rebind input=""js1_button5"" activationMode=""double_tap""/>
                </action>
            </actionmap>
        </ActionMaps>";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, xml);

            var result = _service.ImportFromFile(tempFile);

            Assert.True(result.Success);
            Assert.Single(result.Bindings);
            Assert.Equal(SCActivationMode.DoubleTap, result.Bindings[0].ActivationMode);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportFromFile_ParsesInvertedAxes()
    {
        var xml = @"<ActionMaps version=""1"" profileName=""test"">
            <actionmap name=""spaceship_movement"">
                <action name=""v_pitch"">
                    <rebind input=""js1_y"" invert=""1""/>
                </action>
            </actionmap>
        </ActionMaps>";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, xml);

            var result = _service.ImportFromFile(tempFile);

            Assert.True(result.Success);
            Assert.Single(result.Bindings);
            Assert.True(result.Bindings[0].Inverted);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportFromFile_ParsesMultipleJoystickInstances()
    {
        var xml = @"<ActionMaps version=""1"" profileName=""test"">
            <actionmap name=""spaceship_movement"">
                <action name=""v_pitch"">
                    <rebind input=""js1_y""/>
                </action>
                <action name=""v_roll"">
                    <rebind input=""js2_x""/>
                </action>
            </actionmap>
        </ActionMaps>";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, xml);

            var result = _service.ImportFromFile(tempFile);

            Assert.True(result.Success);
            Assert.Equal(2, result.Bindings.Count);
            Assert.Equal(1u, result.Bindings[0].VJoyDevice);
            Assert.Equal(2u, result.Bindings[1].VJoyDevice);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportFromFile_ParsesModifiersWithJoystick()
    {
        // Test that js1_rctrl+button7 parses correctly
        var xml = @"<ActionMaps version=""1"" profileName=""test"">
            <actionmap name=""seat_general"">
                <action name=""v_eject"">
                    <rebind input=""js2_rctrl+button7"" activationMode=""double_tap""/>
                </action>
            </actionmap>
        </ActionMaps>";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, xml);

            var result = _service.ImportFromFile(tempFile);

            Assert.True(result.Success);
            Assert.Single(result.Bindings);
            var binding = result.Bindings[0];
            Assert.Equal(SCDeviceType.Joystick, binding.DeviceType);
            Assert.Equal(2u, binding.VJoyDevice);
            Assert.Equal("button7", binding.InputName);
            Assert.Single(binding.Modifiers);
            Assert.Equal("rctrl", binding.Modifiers[0]);
            Assert.Equal(SCActivationMode.DoubleTap, binding.ActivationMode);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ImportFromFile_RealWorldProfile_CountsBindingsCorrectly()
    {
        // Test with a snippet similar to the user's real profile
        var xml = @"<ActionMaps version=""1"" profileName=""test"">
            <actionmap name=""seat_general"">
                <action name=""v_eject"">
                    <rebind input=""js2_rctrl+button7"" activationMode=""double_tap""/>
                </action>
                <action name=""v_enter_remote_turret_1"">
                    <rebind input=""js2_button24""/>
                </action>
                <action name=""v_toggle_mining_mode"">
                    <rebind input=""js1_ "" activationMode=""double_tap""/>
                </action>
            </actionmap>
            <actionmap name=""spaceship_movement"">
                <action name=""v_roll"">
                    <rebind input=""js1_z""/>
                </action>
                <action name=""v_strafe_lateral"">
                    <rebind input=""js2_x""/>
                </action>
            </actionmap>
        </ActionMaps>";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, xml);

            var result = _service.ImportFromFile(tempFile);

            Assert.True(result.Success);
            // Should have 4 bindings (the js1_ with space should be skipped)
            Assert.Equal(4, result.Bindings.Count);
            Assert.All(result.Bindings, b => Assert.Equal(SCDeviceType.Joystick, b.DeviceType));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region SetBinding Multiple VJoy Instances Tests

    [Fact]
    public void SetBinding_PreservesBindingsForDifferentVJoyDevices()
    {
        var profile = new SCExportProfile();

        var binding1 = new SCActionBinding
        {
            DeviceType = SCDeviceType.Joystick,
            VJoyDevice = 1,
            InputName = "button5"
        };
        var binding2 = new SCActionBinding
        {
            DeviceType = SCDeviceType.Joystick,
            VJoyDevice = 2,
            InputName = "button3"
        };

        profile.SetBinding("test", "action1", binding1);
        profile.SetBinding("test", "action1", binding2);

        Assert.Equal(2, profile.Bindings.Count);
        Assert.Contains(profile.Bindings, b => b.VJoyDevice == 1 && b.InputName == "button5");
        Assert.Contains(profile.Bindings, b => b.VJoyDevice == 2 && b.InputName == "button3");
    }

    [Fact]
    public void SetBinding_ReplacesBindingForSameVJoyDevice()
    {
        var profile = new SCExportProfile();

        var binding1 = new SCActionBinding
        {
            DeviceType = SCDeviceType.Joystick,
            VJoyDevice = 1,
            InputName = "button5"
        };
        var binding2 = new SCActionBinding
        {
            DeviceType = SCDeviceType.Joystick,
            VJoyDevice = 1,
            InputName = "button10"
        };

        profile.SetBinding("test", "action1", binding1);
        profile.SetBinding("test", "action1", binding2);

        Assert.Single(profile.Bindings);
        Assert.Equal("button10", profile.Bindings[0].InputName);
    }

    [Fact]
    public void SetBinding_AllowsKeyboardAndJoystickForSameAction()
    {
        var profile = new SCExportProfile();

        var kbBinding = new SCActionBinding
        {
            DeviceType = SCDeviceType.Keyboard,
            InputName = "w"
        };
        var jsBinding = new SCActionBinding
        {
            DeviceType = SCDeviceType.Joystick,
            VJoyDevice = 1,
            InputName = "y"
        };

        profile.SetBinding("test", "action1", kbBinding);
        profile.SetBinding("test", "action1", jsBinding);

        Assert.Equal(2, profile.Bindings.Count);
        Assert.Contains(profile.Bindings, b => b.DeviceType == SCDeviceType.Keyboard);
        Assert.Contains(profile.Bindings, b => b.DeviceType == SCDeviceType.Joystick);
    }

    #endregion
}
