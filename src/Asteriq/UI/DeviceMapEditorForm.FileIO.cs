#if DEBUG
using System.Text.Json;
using System.Text.Json.Serialization;
using Asteriq.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Svg.Skia;

namespace Asteriq.UI;

public partial class DeviceMapEditorForm
{
    private void LoadAvailableSvgFiles()
    {
        _imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices");
        if (Directory.Exists(_imagesDir))
        {
            _availableSvgFiles = Directory.GetFiles(_imagesDir, "*.svg")
                .Select(Path.GetFileName)
                .Where(f => f is not null)
                .Cast<string>()
                .ToList();
        }

        // Load first SVG if available
        if (_availableSvgFiles.Count > 0)
        {
            LoadSvgFile(_availableSvgFiles[0]);
            _deviceMap.SvgFile = _availableSvgFiles[0];
        }
    }

    private void LoadSvgFile(string fileName)
    {
        var path = Path.Combine(_imagesDir, fileName);
        if (File.Exists(path))
        {
            _currentSvg = new SKSvg();
            _currentSvg.Load(path);
        }
    }

    private void LoadJsonFile(string path)
    {
        var map = DeviceMap.Load(path);
        if (map is not null)
        {
            _deviceMap = map;
            _currentJsonPath = path;
            _jsonFileName = Path.GetFileName(path);
            _hasUnsavedChanges = false;
            _selectedControlKey = null;

            // Load corresponding SVG
            if (!string.IsNullOrEmpty(map.SvgFile))
            {
                LoadSvgFile(map.SvgFile);
            }

            // Debug: Show where file was loaded from
            System.Diagnostics.Debug.WriteLine($"Loaded from: {path}");
        }
        else
        {
            FUIMessageBox.ShowError(this, $"Failed to load file:\n{path}", "Load Error");
        }
    }

    private void SaveJsonFile()
    {
        try
        {
            string savePath;

            // If we have a current path from loading, use that directory
            // Otherwise show save dialog
            if (!string.IsNullOrEmpty(_currentJsonPath) && File.Exists(_currentJsonPath))
            {
                savePath = _currentJsonPath;
            }
            else
            {
                // Try to find source directory for Maps
                var sourceDir = FindSourceMapsDirectory();

                using var sfd = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = _jsonFileName,
                    InitialDirectory = sourceDir ?? Path.Combine(_imagesDir, "Maps")
                };

                if (sfd.ShowDialog() != DialogResult.OK)
                    return;

                savePath = sfd.FileName;
                _jsonFileName = Path.GetFileName(savePath);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(_deviceMap, options);
            File.WriteAllText(savePath, json);
            _currentJsonPath = savePath;
            _hasUnsavedChanges = false;
            _lastSaveMessage = $"Saved to {Path.GetFileName(savePath)}";
            _lastSaveTime = DateTime.Now;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            FUIMessageBox.ShowError(this, $"Failed to save: {ex.Message}", "Save Error");
        }
    }

    private static string? FindSourceMapsDirectory()
    {
        // Try to find the source directory by walking up from bin folder
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;

            var srcPath = Path.Combine(dir, "src", "Asteriq", "Images", "Devices", "Maps");
            if (Directory.Exists(srcPath))
                return srcPath;
        }
        return null;
    }

}
#endif