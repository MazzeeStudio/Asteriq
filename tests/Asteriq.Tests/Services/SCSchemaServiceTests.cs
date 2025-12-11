using System.Xml;
using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.Tests.Services;

public class SCSchemaServiceTests
{
    private readonly SCSchemaService _service = new();

    private static XmlDocument CreateXmlDoc(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    #region ParseActions Tests

    [Fact]
    public void ParseActions_WithEmptyDocument_ReturnsEmptyList()
    {
        var doc = CreateXmlDoc("<root></root>");

        var actions = _service.ParseActions(doc);

        Assert.Empty(actions);
    }

    [Fact]
    public void ParseActions_WithSingleActionMap_ParsesActions()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""spaceship_movement"">
                    <action name=""v_strafe_forward"" keyboard=""w""/>
                    <action name=""v_strafe_back"" keyboard=""s""/>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.Equal(2, actions.Count);
        Assert.All(actions, a => Assert.Equal("spaceship_movement", a.ActionMap));
        Assert.Contains(actions, a => a.ActionName == "v_strafe_forward");
        Assert.Contains(actions, a => a.ActionName == "v_strafe_back");
    }

    [Fact]
    public void ParseActions_WithMultipleActionMaps_ParsesAllActions()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""spaceship_movement"">
                    <action name=""v_strafe_forward""/>
                </actionmap>
                <actionmap name=""spaceship_weapons"">
                    <action name=""v_attack1""/>
                    <action name=""v_attack2""/>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.Equal(3, actions.Count);
        Assert.Single(actions, a => a.ActionMap == "spaceship_movement");
        Assert.Equal(2, actions.Count(a => a.ActionMap == "spaceship_weapons"));
    }

    [Fact]
    public void ParseActions_SetsCategory_FromActionMapName()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""spaceship_movement"">
                    <action name=""v_strafe_forward""/>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.Single(actions);
        Assert.Equal("Flight Control", actions[0].Category); // Uses SCCategoryMapper for user-friendly names
    }

    [Fact]
    public void ParseActions_InfersAxisType_FromActionName()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""spaceship_movement"">
                    <action name=""v_strafe_forward""/>
                    <action name=""v_pitch""/>
                    <action name=""v_roll""/>
                    <action name=""v_throttle_abs""/>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.Equal(4, actions.Count);
        Assert.Equal(SCInputType.Axis, actions.First(a => a.ActionName == "v_strafe_forward").InputType);
        Assert.Equal(SCInputType.Axis, actions.First(a => a.ActionName == "v_pitch").InputType);
        Assert.Equal(SCInputType.Axis, actions.First(a => a.ActionName == "v_roll").InputType);
        Assert.Equal(SCInputType.Axis, actions.First(a => a.ActionName == "v_throttle_abs").InputType);
    }

    [Fact]
    public void ParseActions_InfersButtonType_FromActionName()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""spaceship_weapons"">
                    <action name=""v_attack1""/>
                    <action name=""v_toggle_mining_mode""/>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.All(actions, a => Assert.Equal(SCInputType.Button, a.InputType));
    }

    [Fact]
    public void ParseActions_ParsesKeyboardBindings_FromAttribute()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""spaceship_movement"">
                    <action name=""v_strafe_forward"" keyboard=""w""/>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.Single(actions);
        Assert.Single(actions[0].DefaultBindings);
        Assert.Equal("kb1", actions[0].DefaultBindings[0].DevicePrefix);
        Assert.Equal("w", actions[0].DefaultBindings[0].Input);
    }

    [Fact]
    public void ParseActions_ParsesJoystickBindings_FromChildElement()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""spaceship_movement"">
                    <action name=""v_strafe_forward"">
                        <joystick input=""y""/>
                    </action>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.Single(actions);
        Assert.Single(actions[0].DefaultBindings);
        Assert.Equal("js1", actions[0].DefaultBindings[0].DevicePrefix);
        Assert.Equal("y", actions[0].DefaultBindings[0].Input);
    }

    [Fact]
    public void ParseActions_ParsesInvertAttribute()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""spaceship_movement"">
                    <action name=""v_pitch"">
                        <joystick input=""y"" invert=""1""/>
                    </action>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.Single(actions[0].DefaultBindings);
        Assert.True(actions[0].DefaultBindings[0].Inverted);
    }

    [Fact]
    public void ParseActions_ParsesActivationMode()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""spaceship_targeting"">
                    <action name=""v_target_cycle"">
                        <joystick input=""button5"" activationMode=""double_tap""/>
                    </action>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.Single(actions[0].DefaultBindings);
        Assert.Equal(SCActivationMode.DoubleTap, actions[0].DefaultBindings[0].ActivationMode);
    }

    [Fact]
    public void ParseActions_ParsesRebindElements()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""spaceship_movement"">
                    <action name=""v_strafe_forward"">
                        <rebind input=""js1_y""/>
                    </action>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.Single(actions[0].DefaultBindings);
        Assert.Equal("js1", actions[0].DefaultBindings[0].DevicePrefix);
        Assert.Equal("y", actions[0].DefaultBindings[0].Input);
    }

    [Fact]
    public void ParseActions_ParsesModifiers_FromInputString()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""default"">
                    <action name=""toggle_something"" keyboard=""lshift+w""/>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.Single(actions[0].DefaultBindings);
        Assert.Equal("w", actions[0].DefaultBindings[0].Input);
        Assert.Contains("lshift", actions[0].DefaultBindings[0].Modifiers);
    }

    [Fact]
    public void ParseActions_GeneratesUniqueKey()
    {
        var doc = CreateXmlDoc(@"
            <profile>
                <actionmap name=""spaceship_movement"">
                    <action name=""v_strafe_forward""/>
                </actionmap>
            </profile>");

        var actions = _service.ParseActions(doc);

        Assert.Single(actions);
        Assert.Equal("spaceship_movement.v_strafe_forward", actions[0].Key);
    }

    #endregion

    #region CompareSchemas Tests

    [Fact]
    public void CompareSchemas_WithIdenticalSchemas_ReportsNoChanges()
    {
        var actions = new List<SCAction>
        {
            new() { ActionMap = "test", ActionName = "action1" },
            new() { ActionMap = "test", ActionName = "action2" }
        };

        var report = _service.CompareSchemas(actions, actions);

        Assert.False(report.HasChanges);
        Assert.Empty(report.AddedActions);
        Assert.Empty(report.RemovedActions);
    }

    [Fact]
    public void CompareSchemas_WithAddedActions_ReportsAdditions()
    {
        var oldActions = new List<SCAction>
        {
            new() { ActionMap = "test", ActionName = "action1" }
        };
        var newActions = new List<SCAction>
        {
            new() { ActionMap = "test", ActionName = "action1" },
            new() { ActionMap = "test", ActionName = "action2" }
        };

        var report = _service.CompareSchemas(oldActions, newActions);

        Assert.True(report.HasChanges);
        Assert.Single(report.AddedActions);
        Assert.Equal("action2", report.AddedActions[0].ActionName);
        Assert.Empty(report.RemovedActions);
    }

    [Fact]
    public void CompareSchemas_WithRemovedActions_ReportsRemovals()
    {
        var oldActions = new List<SCAction>
        {
            new() { ActionMap = "test", ActionName = "action1" },
            new() { ActionMap = "test", ActionName = "action2" }
        };
        var newActions = new List<SCAction>
        {
            new() { ActionMap = "test", ActionName = "action1" }
        };

        var report = _service.CompareSchemas(oldActions, newActions);

        Assert.True(report.HasChanges);
        Assert.Empty(report.AddedActions);
        Assert.Single(report.RemovedActions);
        Assert.Equal("action2", report.RemovedActions[0].ActionName);
    }

    [Fact]
    public void CompareSchemas_DetectsPossibleRenames()
    {
        var oldActions = new List<SCAction>
        {
            new() { ActionMap = "test", ActionName = "old_action" }
        };
        var newActions = new List<SCAction>
        {
            new() { ActionMap = "test", ActionName = "new_action" }
        };

        var report = _service.CompareSchemas(oldActions, newActions);

        Assert.True(report.HasChanges);
        Assert.Single(report.PossibleRenames);
        Assert.Equal("old_action", report.PossibleRenames[0].Old.ActionName);
        Assert.Equal("new_action", report.PossibleRenames[0].New.ActionName);
    }

    [Fact]
    public void CompareSchemas_Summary_FormatsCorrectly()
    {
        var oldActions = new List<SCAction>
        {
            new() { ActionMap = "test", ActionName = "removed" }
        };
        var newActions = new List<SCAction>
        {
            new() { ActionMap = "test", ActionName = "added1" },
            new() { ActionMap = "test", ActionName = "added2" }
        };

        var report = _service.CompareSchemas(oldActions, newActions);

        Assert.Contains("2 added", report.Summary);
        Assert.Contains("1 removed", report.Summary);
    }

    #endregion

    #region GroupBy Tests

    [Fact]
    public void GroupByActionMap_GroupsCorrectly()
    {
        var actions = new List<SCAction>
        {
            new() { ActionMap = "map1", ActionName = "action1" },
            new() { ActionMap = "map1", ActionName = "action2" },
            new() { ActionMap = "map2", ActionName = "action3" }
        };

        var grouped = _service.GroupByActionMap(actions);

        Assert.Equal(2, grouped.Count);
        Assert.Equal(2, grouped["map1"].Count);
        Assert.Single(grouped["map2"]);
    }

    [Fact]
    public void GroupByCategory_GroupsCorrectly()
    {
        var actions = new List<SCAction>
        {
            new() { ActionMap = "map1", ActionName = "action1", Category = "Category A" },
            new() { ActionMap = "map2", ActionName = "action2", Category = "Category A" },
            new() { ActionMap = "map3", ActionName = "action3", Category = "Category B" }
        };

        var grouped = _service.GroupByCategory(actions);

        Assert.Equal(2, grouped.Count);
        Assert.Equal(2, grouped["Category A"].Count);
        Assert.Single(grouped["Category B"]);
    }

    #endregion

    #region FilterJoystickActions Tests

    [Fact]
    public void FilterJoystickActions_IncludesActionsWithJoystickBindings()
    {
        var actions = new List<SCAction>
        {
            new()
            {
                ActionMap = "test", ActionName = "js_action",
                DefaultBindings = new List<SCDefaultBinding>
                {
                    new() { DevicePrefix = "js1", Input = "button1" }
                }
            },
            new()
            {
                ActionMap = "test", ActionName = "kb_action",
                DefaultBindings = new List<SCDefaultBinding>
                {
                    new() { DevicePrefix = "kb1", Input = "w" }
                }
            }
        };

        var filtered = _service.FilterJoystickActions(actions);

        Assert.Single(filtered);
        Assert.Equal("js_action", filtered[0].ActionName);
    }

    [Fact]
    public void FilterJoystickActions_IncludesAxisActions()
    {
        var actions = new List<SCAction>
        {
            new()
            {
                ActionMap = "test", ActionName = "axis_action",
                InputType = SCInputType.Axis,
                DefaultBindings = new List<SCDefaultBinding>()
            },
            new()
            {
                ActionMap = "test", ActionName = "button_action",
                InputType = SCInputType.Button,
                DefaultBindings = new List<SCDefaultBinding>()
            }
        };

        var filtered = _service.FilterJoystickActions(actions);

        Assert.Single(filtered);
        Assert.Equal("axis_action", filtered[0].ActionName);
    }

    #endregion
}
