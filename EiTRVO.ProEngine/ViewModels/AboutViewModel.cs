using EiTRVO.ProEngine.Models;

namespace EiTRVO.ProEngine.ViewModels;

public partial class AboutViewModel : BaseViewModel
{
    public string Version => AppInfo.Version;
    public string ProEngineVersion => AppInfo.ProEngineVersion;
}
