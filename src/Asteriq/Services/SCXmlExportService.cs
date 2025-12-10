using System.Text;
using System.Xml;
using System.Xml.Linq;
using Asteriq.Models;

namespace Asteriq.Services;

/// <summary>
/// Exports Asteriq bindings to Star Citizen's actionmaps XML format.
/// </summary>
public class SCXmlExportService
{
    /// <summary>
    /// Exports a profile to SC XML format
    /// </summary>
    /// <param name="profile">The export profile configuration</param>
    /// <returns>XML document ready to save</returns>
    public XDocument Export(SCExportProfile profile)
    {
        // Star Citizen does NOT want an XML declaration - files must start directly with <ActionMaps>
        var doc = new XDocument(
            new XElement("ActionMaps",
                new XAttribute("version", "1"),
                new XAttribute("optionsVersion", "2"),
                new XAttribute("rebindVersion", "2"),
                new XAttribute("profileName", profile.ProfileName),
                CreateCustomisationUIHeader(profile),
                CreateActionProfOptionsElement(),
                CreateModifiersElement(),
                CreateOptionElements(profile),
                CreateActionMapElements(profile)
            )
        );

        return doc;
    }

    /// <summary>
    /// Creates the CustomisationUIHeader element required by Star Citizen
    /// </summary>
    private static XElement CreateCustomisationUIHeader(SCExportProfile profile)
    {
        var devicesElement = new XElement("devices");

        // Always add keyboard and mouse
        devicesElement.Add(new XElement("keyboard", new XAttribute("instance", "1")));
        devicesElement.Add(new XElement("mouse", new XAttribute("instance", "1")));

        // Add joystick devices based on profile mapping
        var maxInstance = profile.JoystickCount;
        for (int i = 1; i <= maxInstance; i++)
        {
            devicesElement.Add(new XElement("joystick",
                new XAttribute("instance", i)));
        }

        return new XElement("CustomisationUIHeader",
            new XAttribute("label", profile.ProfileName),
            new XAttribute("description", $"Exported from Asteriq on {DateTime.Now:yyyy-MM-dd}"),
            new XAttribute("image", ""),
            devicesElement,
            new XElement("categories")
        );
    }

    /// <summary>
    /// Exports and saves a profile to file.
    /// Star Citizen requires UTF-8 without BOM and NO XML declaration.
    /// </summary>
    public void ExportToFile(SCExportProfile profile, string filePath)
    {
        var doc = Export(profile);

        // Star Citizen requires UTF-8 without BOM and NO XML declaration
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(false), // false = no BOM
            OmitXmlDeclaration = true  // SC rejects files with <?xml ... ?> declaration
        };

        using var writer = XmlWriter.Create(filePath, settings);
        doc.Save(writer);

        System.Diagnostics.Debug.WriteLine($"[SCXmlExportService] Exported profile to {filePath}");
    }

    /// <summary>
    /// Exports to SC's Mappings folder for a specific installation
    /// </summary>
    public string ExportToInstallation(SCExportProfile profile, SCInstallation installation)
    {
        // Ensure mappings directory exists
        SCInstallationService.EnsureMappingsDirectory(installation);

        var fileName = profile.GetExportFileName();
        var filePath = Path.Combine(installation.MappingsPath, fileName);

        ExportToFile(profile, filePath);

        return filePath;
    }

    private static XElement CreateActionProfOptionsElement()
    {
        return new XElement("actionProfOptions");
    }

    private static XElement CreateModifiersElement()
    {
        // Empty modifiers for now - could be expanded for custom modifier definitions
        return new XElement("modifiers");
    }

    /// <summary>
    /// Creates options elements for each joystick device
    /// </summary>
    private static IEnumerable<XElement> CreateOptionElements(SCExportProfile profile)
    {
        var options = new List<XElement>();

        // Create options for each mapped vJoy device
        foreach (var kvp in profile.VJoyToSCInstance.OrderBy(k => k.Value))
        {
            var vjoyId = kvp.Key;
            var scInstance = kvp.Value;

            // vJoy devices have a specific product GUID format
            // Using zeros since vJoy GUIDs are system-specific
            var optionElement = new XElement("options",
                new XAttribute("type", "joystick"),
                new XAttribute("instance", scInstance),
                new XAttribute("Product", $"vJoy Device {{00000000-0000-0000-0000-00000000000{vjoyId}}}")
            );
            options.Add(optionElement);
        }

        return options;
    }

    /// <summary>
    /// Creates actionmap elements with all bindings
    /// </summary>
    private static IEnumerable<XElement> CreateActionMapElements(SCExportProfile profile)
    {
        // Group bindings by action map
        var bindingsByMap = profile.Bindings
            .GroupBy(b => b.ActionMap)
            .OrderBy(g => g.Key);

        foreach (var mapGroup in bindingsByMap)
        {
            var actionMapElement = new XElement("actionmap",
                new XAttribute("name", mapGroup.Key));

            foreach (var binding in mapGroup.OrderBy(b => b.ActionName))
            {
                var actionElement = CreateActionElement(binding, profile);
                if (actionElement != null)
                {
                    actionMapElement.Add(actionElement);
                }
            }

            // Only yield action maps that have actions
            if (actionMapElement.HasElements)
            {
                yield return actionMapElement;
            }
        }
    }

    /// <summary>
    /// Creates an action element with rebind
    /// </summary>
    private static XElement? CreateActionElement(SCActionBinding binding, SCExportProfile profile)
    {
        var scInstance = profile.GetSCInstance(binding.VJoyDevice);
        var inputString = binding.GetSCInputString(scInstance);

        var actionElement = new XElement("action",
            new XAttribute("name", binding.ActionName));

        var rebindElement = new XElement("rebind",
            new XAttribute("input", inputString));

        // Add inversion attribute if applicable (axes only)
        if (binding.Inverted && binding.InputType == SCInputType.Axis)
        {
            rebindElement.Add(new XAttribute("invert", "1"));
        }

        // Add activationMode attribute if not default press
        var activationModeStr = binding.ActivationMode.ToXmlString();
        if (activationModeStr != null)
        {
            rebindElement.Add(new XAttribute("activationMode", activationModeStr));
        }

        actionElement.Add(rebindElement);
        return actionElement;
    }

    /// <summary>
    /// Validates an export profile before export
    /// </summary>
    public ExportValidationResult Validate(SCExportProfile profile)
    {
        var result = new ExportValidationResult();

        if (string.IsNullOrWhiteSpace(profile.ProfileName))
        {
            result.Errors.Add("Profile name is required");
        }

        if (profile.Bindings.Count == 0)
        {
            result.Warnings.Add("No bindings defined - exported profile will be empty");
        }

        // Check for duplicate bindings
        var duplicates = profile.Bindings
            .GroupBy(b => b.Key)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var dup in duplicates)
        {
            result.Warnings.Add($"Duplicate binding for {dup}");
        }

        // Check that all vJoy devices have SC instance mappings
        var usedVJoyDevices = profile.Bindings.Select(b => b.VJoyDevice).Distinct();
        foreach (var vjoy in usedVJoyDevices)
        {
            if (!profile.VJoyToSCInstance.ContainsKey(vjoy))
            {
                result.Warnings.Add($"vJoy device {vjoy} has no explicit SC instance mapping (will use default js{vjoy})");
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the export path preview for an installation
    /// </summary>
    public string GetExportPath(SCExportProfile profile, SCInstallation installation)
    {
        var fileName = profile.GetExportFileName();
        return Path.Combine(installation.MappingsPath, fileName);
    }
}

/// <summary>
/// Result of export validation
/// </summary>
public class ExportValidationResult
{
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public bool IsValid => Errors.Count == 0;
    public bool HasWarnings => Warnings.Count > 0;
}
