using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.ViewModels;

public partial class SchematicManagementViewModel : BaseViewModel
{
    private readonly INotificationService _notification;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string _instanceName = "";

    [ObservableProperty]
    private string _schematicsFolder = "";

    [ObservableProperty]
    private bool _isEmpty = true;

    public ObservableCollection<SchematicEntry> Schematics { get; } = new();

    /// <summary>Triggers MainWindow to navigate back to instance detail.</summary>
    public event Action? BackRequested;

    public SchematicManagementViewModel(INotificationService notification, IDialogService dialogService)
    {
        _notification = notification;
        _dialogService = dialogService;
    }

    /// <summary>Load instance context and scan schematic files.</summary>
    public void LoadSchematics(string instanceName, string schematicsFolder)
    {
        InstanceName = instanceName;
        SchematicsFolder = schematicsFolder;
        Schematics.Clear();

        if (!Directory.Exists(schematicsFolder))
        {
            IsEmpty = true;
            return;
        }

        try
        {
            var files = Directory.GetFiles(schematicsFolder)
                .Where(f =>
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext is ".litematic" or ".schematic" or ".schem";
                })
                .OrderBy(f => Path.GetFileName(f))
                .ToList();

            foreach (var file in files)
                Schematics.Add(SchematicEntry.FromFile(file));

            IsEmpty = Schematics.Count == 0;
        }
        catch
        {
            IsEmpty = true;
        }
    }

    [RelayCommand]
    private async Task ImportSchematicAsync()
    {
        var filePath = await _dialogService.ShowOpenFileDialogAsync(
            "选择投影文件", "投影文件|*.litematic;*.schematic;*.schem|所有文件|*.*");
        if (filePath == null) return;

        try
        {
            string fileName = Path.GetFileName(filePath);
            string destPath = Path.Combine(SchematicsFolder, fileName);

            // 同名文件自动去重
            if (File.Exists(destPath))
            {
                string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                int counter = 1;
                while (File.Exists(Path.Combine(SchematicsFolder, $"{nameNoExt}_{counter}{ext}")))
                    counter++;
                destPath = Path.Combine(SchematicsFolder, $"{nameNoExt}_{counter}{ext}");
            }

            Directory.CreateDirectory(SchematicsFolder);
            await Task.Run(() => File.Copy(filePath, destPath));

            _notification.Show("投影导入成功！", NotificationType.Success);
            LoadSchematics(InstanceName, SchematicsFolder);
        }
        catch (Exception ex)
        {
            _notification.Show($"导入失败：{ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteSchematicAsync(SchematicEntry? entry)
    {
        if (entry == null) return;

        if (!await _dialogService.ShowConfirmAsync(
            $"确定要删除投影「{entry.Name}」吗？\n该操作不可恢复！",
            "确认删除"))
            return;

        try
        {
            if (File.Exists(entry.FullPath))
                File.Delete(entry.FullPath);

            Schematics.Remove(entry);
            IsEmpty = Schematics.Count == 0;
            _notification.Show($"投影「{entry.Name}」已删除。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notification.Show($"删除失败：{ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke();
}
