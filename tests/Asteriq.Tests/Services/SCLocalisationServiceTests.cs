using System.Text;
using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.Tests.Services;

public class SCLocalisationServiceTests
{
    /// <summary>
    /// Writes a global.ini under a temp directory laid out as SC does on disk:
    /// &lt;root&gt;/data/Localization/&lt;locale&gt;/global.ini. Returns the &lt;root&gt; path
    /// to pass to Load(). UTF-16 LE with BOM to match SC's actual encoding.
    /// </summary>
    private static string WriteIni(string content, string locale = "english")
    {
        var root = Path.Combine(Path.GetTempPath(), "asteriq-loc-test-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "data", "Localization", locale);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "global.ini"), content, new UnicodeEncoding(false, true));
        return root;
    }

    [Fact]
    public void Load_MissingInstallPath_ReturnsFalseAndLeavesUnloaded()
    {
        var svc = new SCLocalisationService();

        Assert.False(svc.Load(""));
        Assert.False(svc.Loaded);
        Assert.Equal(0, svc.StringCount);
        Assert.Null(svc.LoadedLocale);
    }

    [Fact]
    public void Load_MissingFile_ReturnsFalseWithoutThrowing()
    {
        var svc = new SCLocalisationService();
        var nonExistent = Path.Combine(Path.GetTempPath(), "asteriq-loc-does-not-exist-" + Guid.NewGuid().ToString("N"));

        Assert.False(svc.Load(nonExistent));
        Assert.False(svc.Loaded);
    }

    [Fact]
    public void Load_SimpleKeyValuePairs_ParsesCorrectly()
    {
        var root = WriteIni("ui_v_lock_all_doors=Lock All Doors\r\nui_v_eject=Eject\r\n");
        try
        {
            var svc = new SCLocalisationService();
            Assert.True(svc.Load(root));
            Assert.True(svc.Loaded);
            Assert.Equal("Lock All Doors", svc.Get("ui_v_lock_all_doors"));
            Assert.Equal("Eject", svc.Get("ui_v_eject"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Load_StripsCommaSuffixFromKeys()
    {
        // Plural/format variant: ",P" suffix — the base key should win.
        var root = WriteIni("ui_v_master_mode_cycle,P=Cycle Master Mode\r\nui_v_master_mode_cycle=Cycle Master Mode Base\r\n");
        try
        {
            var svc = new SCLocalisationService();
            svc.Load(root);
            // First occurrence wins — the ",P" line lands under the stripped key first.
            Assert.Equal("Cycle Master Mode", svc.Get("ui_v_master_mode_cycle"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Load_SkipsCommentsAndSectionHeadersAndBlankLines()
    {
        var ini =
            "; this is a comment\r\n" +
            "\r\n" +
            "[Section]\r\n" +
            "ui_v_eject=Eject\r\n" +
            "; another comment\r\n" +
            "ui_v_strafe_forward=Strafe Forward\r\n";
        var root = WriteIni(ini);
        try
        {
            var svc = new SCLocalisationService();
            svc.Load(root);
            Assert.Equal(2, svc.StringCount);
            Assert.Equal("Eject", svc.Get("ui_v_eject"));
            Assert.Equal("Strafe Forward", svc.Get("ui_v_strafe_forward"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Load_LinesWithoutEqualsAreIgnored()
    {
        var root = WriteIni("not_a_keyvalue_line\r\nui_v_eject=Eject\r\n=emptykey\r\n");
        try
        {
            var svc = new SCLocalisationService();
            svc.Load(root);
            Assert.Equal(1, svc.StringCount);
            Assert.Equal("Eject", svc.Get("ui_v_eject"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Get_MissingKeyReturnsNull()
    {
        var root = WriteIni("ui_v_eject=Eject\r\n");
        try
        {
            var svc = new SCLocalisationService();
            svc.Load(root);
            Assert.Null(svc.Get("ui_v_unknown"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Get_BeforeLoadReturnsNull()
    {
        var svc = new SCLocalisationService();
        Assert.Null(svc.Get("ui_v_anything"));
    }

    [Fact]
    public void Load_ReloadReplacesPreviousStrings()
    {
        var root1 = WriteIni("ui_v_old=Old\r\n");
        var root2 = WriteIni("ui_v_new=New\r\n");
        try
        {
            var svc = new SCLocalisationService();
            svc.Load(root1);
            Assert.Equal("Old", svc.Get("ui_v_old"));

            svc.Load(root2);
            Assert.Null(svc.Get("ui_v_old"));
            Assert.Equal("New", svc.Get("ui_v_new"));
        }
        finally
        {
            Directory.Delete(root1, true);
            Directory.Delete(root2, true);
        }
    }

    [Fact]
    public void HydrateLocalisation_AppliesLabelAndDistinctDescription()
    {
        var ini =
            "ui_v_lock_all_doors=Lock All Doors\r\n" +
            "ui_v_lock_all_doors_desc=Seals all external doors on the ship.\r\n";
        var root = WriteIni(ini);
        try
        {
            var svc = new SCLocalisationService();
            svc.Load(root);

            var actions = new List<SCAction>
            {
                new() { ActionMap = "seat_general", ActionName = "v_lock_all_doors" }
            };
            SCSchemaService.HydrateLocalisation(actions, svc);

            Assert.Equal("Lock All Doors", actions[0].DisplayLabel);
            Assert.Equal("Seals all external doors on the ship.", actions[0].Description);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void HydrateLocalisation_DropsDescriptionWhenIdenticalToLabel()
    {
        var ini =
            "ui_v_eject=Eject\r\n" +
            "ui_v_eject_desc=Eject\r\n";
        var root = WriteIni(ini);
        try
        {
            var svc = new SCLocalisationService();
            svc.Load(root);

            var actions = new List<SCAction>
            {
                new() { ActionMap = "seat_general", ActionName = "v_eject" }
            };
            SCSchemaService.HydrateLocalisation(actions, svc);

            Assert.Equal("Eject", actions[0].DisplayLabel);
            Assert.Null(actions[0].Description);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void HydrateLocalisation_LeavesActionsUntouchedWhenServiceNotLoaded()
    {
        var svc = new SCLocalisationService();
        var actions = new List<SCAction>
        {
            new() { ActionMap = "spaceship_movement", ActionName = "v_strafe_forward" }
        };

        SCSchemaService.HydrateLocalisation(actions, svc);

        Assert.Null(actions[0].DisplayLabel);
        Assert.Null(actions[0].Description);
    }

    [Fact]
    public void HydrateLocalisation_MissingLabelLeavesActionUnhydrated()
    {
        var root = WriteIni("ui_v_other=Other Action\r\n");
        try
        {
            var svc = new SCLocalisationService();
            svc.Load(root);

            var actions = new List<SCAction>
            {
                new() { ActionMap = "spaceship_movement", ActionName = "v_strafe_forward" }
            };
            SCSchemaService.HydrateLocalisation(actions, svc);

            Assert.Null(actions[0].DisplayLabel);
            Assert.Null(actions[0].Description);
        }
        finally { Directory.Delete(root, true); }
    }
}
