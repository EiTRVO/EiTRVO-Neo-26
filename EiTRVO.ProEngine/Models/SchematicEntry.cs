using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EiTRVO.ProEngine.Models;

public partial class SchematicEntry : ObservableObject
{
    /// <summary>Display name (without extension, e.g. "my_build").</summary>
    public string Name { get; set; } = "";

    /// <summary>Full file name (e.g. "my_build.litematic").</summary>
    public string FileName { get; set; } = "";

    /// <summary>Absolute file path on disk.</summary>
    public string FullPath { get; set; } = "";

    /// <summary>Create a SchematicEntry from a file path.</summary>
    public static SchematicEntry FromFile(string filePath)
    {
        return new SchematicEntry
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            FileName = Path.GetFileName(filePath),
            FullPath = filePath
        };
    }
}
