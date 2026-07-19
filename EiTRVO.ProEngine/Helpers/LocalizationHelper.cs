using System.Globalization;
using System.Resources;

namespace EiTRVO.ProEngine.Helpers;

/// <summary>Provides localized strings from the .resx resource files.</summary>
public static class LocalizationHelper
{
    private static readonly ResourceManager _resourceManager =
        new("EiTRVO.ProEngine.Properties.Resources", typeof(LocalizationHelper).Assembly);

    public static string Get(string key)
        => _resourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object[] args)
        => string.Format(Get(key), args);
}
