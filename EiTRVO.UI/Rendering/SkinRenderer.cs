using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EiTRVO.UI.Rendering;

public static class SkinRenderer
{
    public static BitmapSource RenderAvatar(byte[] skinPng)
    {
        var skin = LoadSkin(skinPng);
        bool hasHat = skin.PixelHeight >= 64;

        var drawing = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(drawing, BitmapScalingMode.NearestNeighbor);

        using (var dc = drawing.RenderOpen())
        {
            dc.DrawImage(
                new CroppedBitmap(skin, new Int32Rect(8, 8, 8, 8)),
                new Rect(0, 0, 8, 8));

            if (hasHat)
            {
                dc.DrawImage(
                    new CroppedBitmap(skin, new Int32Rect(40, 8, 8, 8)),
                    new Rect(0, 0, 8, 8));
            }
        }

        var result = new RenderTargetBitmap(8, 8, 96, 96, PixelFormats.Pbgra32);
        result.Render(drawing);
        result.Freeze();
        return result;
    }

    private static BitmapSource LoadSkin(byte[] pngData)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(pngData);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
