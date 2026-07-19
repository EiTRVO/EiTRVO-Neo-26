using System.Globalization;
using System.Windows;
using EiTRVO.ProEngine.Models;
using EiTRVO.UI.Converters;

namespace EiTRVO.Tests.Converters;

[TestClass]
public class ConverterTests
{
    // ================================================================
    // BoolInvertConverter
    // ================================================================

    [TestMethod]
    public void BoolInvert_Convert_True_ReturnsFalse()
    {
        var converter = new BoolInvertConverter();
        var result = converter.Convert(true, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.IsFalse((bool)result);
    }

    [TestMethod]
    public void BoolInvert_Convert_False_ReturnsTrue()
    {
        var converter = new BoolInvertConverter();
        var result = converter.Convert(false, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.IsTrue((bool)result);
    }

    [TestMethod]
    public void BoolInvert_Convert_True_VisibilityTarget_ReturnsCollapsed()
    {
        var converter = new BoolInvertConverter();
        var result = converter.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        // !true = false → false maps to Collapsed
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void BoolInvert_ConvertBack_True_ReturnsFalse()
    {
        var converter = new BoolInvertConverter();
        var result = converter.ConvertBack(true, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.IsFalse((bool)result);
    }

    // ================================================================
    // BoolToVisibilityConverter
    // ================================================================

    [TestMethod]
    public void BoolToVisibility_Convert_True_ReturnsVisible()
    {
        var converter = new BoolToVisibilityConverter();
        var result = converter.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void BoolToVisibility_Convert_False_ReturnsCollapsed()
    {
        var converter = new BoolToVisibilityConverter();
        var result = converter.Convert(false, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void BoolToVisibility_Convert_Null_ReturnsCollapsed()
    {
        var converter = new BoolToVisibilityConverter();
        var result = converter.Convert(null!, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void BoolToVisibility_ConvertBack_Visible_ReturnsTrue()
    {
        var converter = new BoolToVisibilityConverter();
        var result = converter.ConvertBack(Visibility.Visible, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.IsTrue((bool)result);
    }

    // ================================================================
    // NotificationTypeToBrushConverter
    // ================================================================

    [TestMethod]
    public void NotificationTypeToBrush_Convert_Info_ReturnsBrush()
    {
        var converter = new NotificationTypeToBrushConverter();
        var result = converter.Convert(NotificationType.Info, typeof(object), null!, CultureInfo.InvariantCulture);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<System.Windows.Media.SolidColorBrush>(result);
    }

    [TestMethod]
    public void NotificationTypeToBrush_Convert_Success_ReturnsBrush()
    {
        var converter = new NotificationTypeToBrushConverter();
        var result = converter.Convert(NotificationType.Success, typeof(object), null!, CultureInfo.InvariantCulture);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<System.Windows.Media.SolidColorBrush>(result);
    }

    [TestMethod]
    public void NotificationTypeToBrush_Convert_Warning_ReturnsBrush()
    {
        var converter = new NotificationTypeToBrushConverter();
        var result = converter.Convert(NotificationType.Warning, typeof(object), null!, CultureInfo.InvariantCulture);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<System.Windows.Media.SolidColorBrush>(result);
    }

    [TestMethod]
    public void NotificationTypeToBrush_Convert_Error_ReturnsBrush()
    {
        var converter = new NotificationTypeToBrushConverter();
        var result = converter.Convert(NotificationType.Error, typeof(object), null!, CultureInfo.InvariantCulture);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<System.Windows.Media.SolidColorBrush>(result);
    }

    [TestMethod]
    public void NotificationTypeToBrush_Convert_InvalidValue_FallsBackToInfoBrush()
    {
        var converter = new NotificationTypeToBrushConverter();
        // Non-NotificationType value defaults to Info
        var result = converter.Convert("invalid_string", typeof(object), null!, CultureInfo.InvariantCulture);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<System.Windows.Media.SolidColorBrush>(result);
    }

    [TestMethod]
    public void NotificationTypeToBrush_ConvertBack_ThrowsNotSupported()
    {
        var converter = new NotificationTypeToBrushConverter();
        Assert.ThrowsException<NotSupportedException>(() =>
            converter.ConvertBack(null!, typeof(object), null!, CultureInfo.InvariantCulture));
    }

    // ================================================================
    // StringNotEmptyToVisibilityConverter
    // ================================================================

    [TestMethod]
    public void StringNotEmpty_Convert_NonEmptyString_ReturnsVisible()
    {
        var converter = new StringNotEmptyToVisibilityConverter();
        var result = converter.Convert("hello", typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void StringNotEmpty_Convert_EmptyString_ReturnsCollapsed()
    {
        var converter = new StringNotEmptyToVisibilityConverter();
        var result = converter.Convert("", typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void StringNotEmpty_Convert_Null_ReturnsCollapsed()
    {
        var converter = new StringNotEmptyToVisibilityConverter();
        var result = converter.Convert(null!, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void StringNotEmpty_ConvertBack_ThrowsNotSupported()
    {
        var converter = new StringNotEmptyToVisibilityConverter();
        Assert.ThrowsException<NotSupportedException>(() =>
            converter.ConvertBack(null!, typeof(Visibility), null!, CultureInfo.InvariantCulture));
    }

    // ================================================================
    // VersionTypeToBrushConverter
    // ================================================================

    [TestMethod]
    public void VersionType_Convert_Release_ReturnsBrush()
    {
        var converter = new VersionTypeToBrushConverter();
        var result = converter.Convert("release", typeof(object), null!, CultureInfo.InvariantCulture);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<System.Windows.Media.SolidColorBrush>(result);
    }

    [TestMethod]
    public void VersionType_Convert_Snapshot_ReturnsBrush()
    {
        var converter = new VersionTypeToBrushConverter();
        var result = converter.Convert("snapshot", typeof(object), null!, CultureInfo.InvariantCulture);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<System.Windows.Media.SolidColorBrush>(result);
    }

    [TestMethod]
    public void VersionType_Convert_OldBeta_ReturnsBrush()
    {
        var converter = new VersionTypeToBrushConverter();
        var result = converter.Convert("old_beta", typeof(object), null!, CultureInfo.InvariantCulture);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<System.Windows.Media.SolidColorBrush>(result);
    }

    [TestMethod]
    public void VersionType_Convert_Unknown_ReturnsDefaultBrush()
    {
        var converter = new VersionTypeToBrushConverter();
        var result = converter.Convert("unknown_type", typeof(object), null!, CultureInfo.InvariantCulture);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<System.Windows.Media.SolidColorBrush>(result);
    }

    [TestMethod]
    public void VersionType_ConvertBack_ThrowsNotSupported()
    {
        var converter = new VersionTypeToBrushConverter();
        Assert.ThrowsException<NotSupportedException>(() =>
            converter.ConvertBack(null!, typeof(object), null!, CultureInfo.InvariantCulture));
    }
}
