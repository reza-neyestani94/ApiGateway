namespace ApiGateway.RateLimiting.Core.Helpers;

public static class RowVersionHelper
{
    public static string ToHex(byte[] rowVersion)
    {
        return Convert.ToHexString(rowVersion);
    }

    public static byte[] FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Array.Empty<byte>();

        return Convert.FromHexString(hex);
    }

    public static int Compare(byte[]? a, byte[]? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null || a.Length == 0) return -1;
        if (b is null || b.Length == 0) return 1;

        var len = Math.Min(a.Length, b.Length);

        for (var i = 0; i < len; i++)
        {
            if (a[i] < b[i]) return -1;
            if (a[i] > b[i]) return 1;
        }

        if (a.Length == b.Length) return 0;

        return a.Length < b.Length ? -1 : 1;
    }

    public static byte[] Max(byte[] a, byte[] b)
    {
        return Compare(a, b) >= 0 ? a : b;
    }
}
