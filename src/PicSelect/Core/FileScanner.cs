using System.IO;

namespace PicSelect.Core;

public static class FileScanner
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif",
        ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".webp"
    };

    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>扫描单个文件夹（不递归），按自然顺序排序。</summary>
    public static List<string> Scan(string folder)
    {
        var list = new List<string>();
        foreach (var f in Directory.EnumerateFiles(folder))
        {
            if (Extensions.Contains(Path.GetExtension(f)))
                list.Add(f);
        }
        list.Sort((a, b) => NaturalComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
        return list;
    }
}
