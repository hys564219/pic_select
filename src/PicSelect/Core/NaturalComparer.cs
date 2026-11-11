using System.Runtime.InteropServices;

namespace PicSelect.Core;

/// <summary>按资源管理器的习惯排序：img2 排在 img10 前面。</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        try { return StrCmpLogicalW(x, y); }
        catch { return string.CompareOrdinal(x, y); }
    }
}
