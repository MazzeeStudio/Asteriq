using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Asteriq.UI.Controllers;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Svg.Skia;

namespace Asteriq.UI;

public partial class MainForm
{
    private void RefreshProfileList()
    {
        _profiles = _profileRepository.ListProfiles();
    }

    private void OpenDriverSetupDialog()
    {
        using var setupForm = new DriverSetupForm(_driverSetupManager, _themeService, _appSettings, settingsMode: true);
        setupForm.ShowDialog(this);
        _canvas.Invalidate();
    }

    private void CreateNewProfilePrompt()
    {
        string defaultName = $"Profile {_profiles.Count + 1}";
        var name = FUIInputDialog.Show(this, "New Profile", "Profile Name:", defaultName, "Create");
        if (name is not null)
        {
            _profileManager.CreateAndActivateProfile(name);
            UpdateMappingsPrimaryDeviceMap();
            RefreshProfileList();
        }
    }

    private void ImportProfilePrompt()
    {
        using var openDialog = new OpenFileDialog
        {
            Title = "Import Profile",
            Filter = "Asteriq Profile (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            CheckFileExists = true
        };

        if (openDialog.ShowDialog(this) == DialogResult.OK)
        {
            var imported = _profileRepository.ImportProfile(openDialog.FileName);
            if (imported is not null)
            {
                _profileManager.ActivateProfile(imported.Id);
                // Initialize primary devices for imported profile
                _profileManager.ActiveProfile?.UpdateAllPrimaryDevices();
                UpdateMappingsPrimaryDeviceMap();
                RefreshProfileList();
            }
            else
            {
                FUIMessageBox.ShowError(this,
                    "Failed to import profile. The file may be corrupted or in an invalid format.",
                    "Import Failed");
            }
        }
    }

    private void ExportActiveProfile()
    {
        if (_profileManager.ActiveProfile is null)
        {
            FUIMessageBox.ShowInfo(this,
                "No profile is currently active. Please select a profile first.",
                "Export");
            return;
        }

        var profile = _profileManager.ActiveProfile;
        string suggestedName = $"{profile.Name.Replace(" ", "_")}.json";

        using var saveDialog = new SaveFileDialog
        {
            Title = "Export Profile",
            Filter = "Asteriq Profile (*.json)|*.json",
            DefaultExt = "json",
            FileName = suggestedName,
            OverwritePrompt = true
        };

        if (saveDialog.ShowDialog(this) == DialogResult.OK)
        {
            bool success = _profileRepository.ExportProfile(profile.Id, saveDialog.FileName);
            if (success)
            {
                FUIMessageBox.ShowInfo(this,
                    $"Profile '{profile.Name}' exported successfully.",
                    "Export Complete");
            }
            else
            {
                FUIMessageBox.ShowError(this,
                    "Failed to export profile.",
                    "Export Failed");
            }
        }
    }

    private void DuplicateActiveProfile()
    {
        var profile = _profileManager.ActiveProfile;
        if (profile is null) return;

        string newName = $"{profile.Name} (copy)";
        var duplicated = _profileRepository.DuplicateProfile(profile.Id, newName);
        if (duplicated is not null)
        {
            _profileManager.ActivateProfile(duplicated.Id);
            _profileManager.ActiveProfile?.UpdateAllPrimaryDevices();
            UpdateMappingsPrimaryDeviceMap();
            RefreshProfileList();
        }
    }

    private void DeleteActiveProfile()
    {
        var profile = _profileManager.ActiveProfile;
        if (profile is null) return;

        int result = FUIMessageBox.Show(this,
            $"Delete profile '{profile.Name}'?\n\nThis cannot be undone.",
            "Delete Profile", FUIMessageBox.MessageBoxType.Question, "Delete", "Cancel");
        if (result != 0) return;

        var profileId = profile.Id;
        _profileManager.DeactivateProfile();
        _profileRepository.DeleteProfile(profileId);
        RefreshProfileList();

        // Activate first remaining profile if any
        if (_profiles.Count > 0)
            _profileManager.ActivateProfile(_profiles[0].Id);

        UpdateMappingsPrimaryDeviceMap();
    }

}