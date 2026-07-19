using EiTRVO.ProEngine.Models;
using EiTRVO.ProEngine.Orchestrators;

namespace EiTRVO.ProEngine.Services;

public class AppSettingsService : ISettingsService
{
    private readonly IGameFolderService _gameFolder;

    public AppSettingsService(IGameFolderService gameFolder)
    {
        _gameFolder = gameFolder;
    }

    public LauncherSettings Load() => SettingsService.Load(_gameFolder.GameDir);

    public void Save(LauncherSettings settings) => SettingsService.Save(_gameFolder.GameDir, settings);
}
