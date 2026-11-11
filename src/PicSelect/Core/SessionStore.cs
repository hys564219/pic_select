using System.IO;
using System.Text;
using System.Text.Json;

namespace PicSelect.Core;

public sealed class SessionState
{
    public string Folder { get; set; } = "";
    public List<string> Files { get; set; } = new();
    public int Cursor { get; set; }
    public List<string> Kept { get; set; } = new();
    public List<string> Seen { get; set; } = new();
    public DateTime SavedAt { get; set; }
}

/// <summary>
/// 会话状态持久化。全部落在 exe 同目录，删掉文件夹即彻底清除。
/// </summary>
public static class SessionStore
{
    public static string DataDir => Path.Combine(AppContext.BaseDirectory, "picselect.data");
    private static string SessionPath => Path.Combine(DataDir, "session.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SessionState? Load()
    {
        try
        {
            if (!File.Exists(SessionPath)) return null;
            var json = File.ReadAllText(SessionPath, Encoding.UTF8);
            var s = JsonSerializer.Deserialize<SessionState>(json, Options);
            if (s is null || string.IsNullOrWhiteSpace(s.Folder) || s.Files.Count == 0) return null;
            return s;
        }
        catch { return null; }
    }

    /// <summary>原子写：先写 .tmp 再替换，断电不会留下半个文件。</summary>
    public static void Save(SessionState s)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            s.SavedAt = DateTime.Now;
            var json = JsonSerializer.Serialize(s, Options);
            var tmp = SessionPath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Move(tmp, SessionPath, overwrite: true);
        }
        catch { /* 状态写失败不应影响筛选 */ }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(SessionPath)) File.Delete(SessionPath);
        }
        catch { }
    }

    /// <summary>导出保留清单，作为删除前的额外保险。</summary>
    public static string? ExportKeepList(string folder, IEnumerable<string> kept)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var name = $"{Loc.T("export.filePrefix")}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var path = Path.Combine(DataDir, name);
            var sb = new StringBuilder();
            sb.AppendLine(Loc.T("export.header.folder") + folder);
            sb.AppendLine(Loc.T("export.header.time") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (var f in kept) sb.AppendLine(f);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }
        catch { return null; }
    }
}
