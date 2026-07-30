using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;

namespace EiTRVO.ProEngine.ViewModels;

public partial class DownloadViewModel : BaseViewModel
{
    private readonly IDownloadService _downloadService;
    private readonly INotificationService _notificationService;
    private readonly IGameFolderService _gameFolder;
    private readonly InstanceManager _instanceManager;
    private readonly HttpClient _httpClient;

    private List<ManifestVersion> _allVersions = new();

    // === Observable Properties ===

    [ObservableProperty]
    private VersionEntry? _selectedVersion;

    // === Collections ===
    public ObservableCollection<VersionEntry> FilteredVersions { get; } = new();
    public HashSet<string> ActiveFilters { get; } = new() { "release" };

    // === Events for View interaction ===
    public event Action? NavigateToModpackDownload;
    public event Action<VersionEntry>? NavigateToInstallation;
    public event Action? NavigateToResourceDownload;
    public Func<LauncherSettings>? SettingsProvider;

    public DownloadViewModel(
        IDownloadService downloadService,
        INotificationService notificationService,
        IGameFolderService gameFolder,
        InstanceManager instanceManager,
        HttpClient httpClient)
    {
        _downloadService = downloadService;
        _notificationService = notificationService;
        _gameFolder = gameFolder;
        _instanceManager = instanceManager;
        _httpClient = httpClient;
    }

    public void SetAllVersions(List<ManifestVersion> versions)
    {
        _allVersions = versions;
        RefreshFilteredList();
    }

    /// <summary>Load version manifest from cache/online and populate the filtered list.</summary>
    public async Task InitializeVersionListAsync()
    {
        _allVersions = await LoadVersionManifestCachedAsync();
        RefreshFilteredList();
    }

    public bool HasVersion(string versionId)
        => _allVersions.Any(v => v.Id == versionId);

    public void RefreshFilteredList()
    {
        var filtered = ActiveFilters.Count == 0
            ? _allVersions
            : _allVersions.Where(v => ActiveFilters.Contains(v.Type));

        var items = filtered.Select(v => new VersionEntry
        {
            Id = v.Id,
            Type = v.Type,
            Url = v.Url
        }).ToList();

        FilteredVersions.Clear();
        foreach (var item in items)
            FilteredVersions.Add(item);
    }

    // === Commands ===

    [RelayCommand]
    private void OpenModpackDownload()
        => NavigateToModpackDownload?.Invoke();

    [RelayCommand]
    private void OpenResourceDownload()
        => NavigateToResourceDownload?.Invoke();

    [RelayCommand]
    private void ToggleFilter(string type)
    {
        if (ActiveFilters.Contains(type))
            ActiveFilters.Remove(type);
        else
            ActiveFilters.Add(type);
        RefreshFilteredList();
    }

    [RelayCommand]
    private async Task RefreshVersionsAsync()
    {
        _allVersions = await LoadVersionManifestCachedAsync();
        RefreshFilteredList();
    }

    [RelayCommand]
    private void GoToInstallation(string versionId)
    {
        var manifestVer = _allVersions.FirstOrDefault(v => v.Id == versionId);
        if (manifestVer == null) return;
        NavigateToInstallation?.Invoke(new VersionEntry
        {
            Id = manifestVer.Id,
            Type = manifestVer.Type,
            Url = manifestVer.Url
        });
    }

    // === Version Manifest Cache ===

    private async Task<List<ManifestVersion>> LoadVersionManifestCachedAsync()
    {
        string cacheFile = Path.Combine(_gameFolder.GameDir, "version_manifest.json");
        if (File.Exists(cacheFile))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile);
            if (age.TotalMinutes < 5)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(cacheFile);
                    var manifest = JsonSerializer.Deserialize<VersionManifest>(json);
                    if (manifest?.Versions != null && manifest.Versions.Count > 0)
                        return manifest.Versions;
                }
                catch { /* cache corrupt */ }
            }
        }

        var versions = await _downloadService.LoadOnlineVersionsAsync(_httpClient);
        try
        {
            var manifest = new VersionManifest { Versions = versions };
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(manifest));
        }
        catch { /* best-effort */ }
        return versions;
    }
}
