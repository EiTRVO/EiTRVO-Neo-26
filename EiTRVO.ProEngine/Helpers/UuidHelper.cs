using System;
using System.Security.Cryptography;
using System.Text;

namespace EiTRVO.ProEngine.Helpers;

public static class UuidHelper
{
    public static string OfflineUuid(string name)
    {
        string input = "OfflinePlayer:" + name;
        using var md5 = MD5.Create();
        byte[] data = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        data[6] = (byte)((data[6] & 0x0F) | 0x30);
        data[8] = (byte)((data[8] & 0x3F) | 0x80);
        return new Guid(data).ToString("D");
    }

    public static string FormatUuid(string uuid)
    {
        if (uuid.Length == 32)
            return $"{uuid.Substring(0, 8)}-{uuid.Substring(8, 4)}-{uuid.Substring(12, 4)}-{uuid.Substring(16, 4)}-{uuid.Substring(20, 12)}";
        return uuid;
    }
}
