using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EiTRVO.ProEngine.Models;

public partial class ResourcePackEntry : ObservableObject
{
    /// <summary>Display name (without extension for files, folder name for folders).</summary>
    public string Name { get; set; } = "";

    /// <summary>File or folder name on disk (e.g. "Faithful.zip" or "Faithful").</summary>
    public string FileName { get; set; } = "";

    /// <summary>Absolute path on disk.</summary>
    public string FullPath { get; set; } = "";

    /// <summary>Whether this entry is a folder (unzipped pack) rather than a .zip file.</summary>
    public bool IsFolder { get; set; }

    [ObservableProperty]
    private bool _isDisabled;

    /// <summary>Button text for the enable/disable toggle.</summary>
    public string DisableButtonText => IsDisabled ? "启用" : "禁用";

    partial void OnIsDisabledChanged(bool value)
        => OnPropertyChanged(nameof(DisableButtonText));

    /// <summary>Create a ResourcePackEntry from a file or folder path.</summary>
    public static ResourcePackEntry FromPath(string path)
    {
        bool isFolder = Directory.Exists(path);
        string fileName = Path.GetFileName(path);
        string name = fileName;

        if (fileName.EndsWith(".restemp", System.StringComparison.OrdinalIgnoreCase))
        {
            // Strip .restemp to get the real name
            name = fileName[..^8]; // ".restemp" is 8 chars
            return new ResourcePackEntry
            {
                Name = name,
                FileName = fileName,
                FullPath = path,
                IsFolder = isFolder,
                IsDisabled = true
            };
        }

        if (!isFolder)
        {
            // It's a .zip file — name without extension
            name = Path.GetFileNameWithoutExtension(fileName);
        }

        return new ResourcePackEntry
        {
            Name = name,
            FileName = fileName,
            FullPath = path,
            IsFolder = isFolder,
            IsDisabled = false
        };
    }
}
