using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.Services;

public interface ISettingsService
{
    LauncherSettings Load();
    void Save(LauncherSettings settings);
}
