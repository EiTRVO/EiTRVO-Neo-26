using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EiTRVO.ProEngine.Models;

public partial class ModEntry : ObservableObject
{
    /// <summary>Display name (without extension, e.g. "jei-1.12.2-4.15.0").</summary>
    public string Name { get; set; } = "";

    /// <summary>Full file name (e.g. "jei-1.12.2-4.15.0.jar" or ".modtemp").</summary>
    public string FileName { get; set; } = "";

    /// <summary>Absolute file path on disk.</summary>
    public string FullPath { get; set; } = "";

    [ObservableProperty]
    private bool _isDisabled;

    /// <summary>Button text for the enable/disable toggle.</summary>
    public string DisableButtonText => IsDisabled ? "启用" : "禁用";

    partial void OnIsDisabledChanged(bool value)
        => OnPropertyChanged(nameof(DisableButtonText));

    /// <summary>Display-friendly file name shown below the mod name.</summary>
    public string DisplayFileName => FileName;

    /// <summary>Create a ModEntry from a file path. Determines IsDisabled from the extension.</summary>
    public static ModEntry FromFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        string name = Path.GetFileNameWithoutExtension(filePath);
        // For .modtemp files, strip the extra extension to get the real mod name
        if (fileName.EndsWith(".modtemp", System.StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(name);

        return new ModEntry
        {
            Name = name,
            FileName = fileName,
            FullPath = filePath,
            IsDisabled = fileName.EndsWith(".modtemp", System.StringComparison.OrdinalIgnoreCase)
        };
    }
}
