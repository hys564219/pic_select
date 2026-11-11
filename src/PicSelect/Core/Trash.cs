using System.Runtime.InteropServices;

namespace PicSelect.Core;

public static class Trash
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>把单个文件送进回收站（可还原）。</summary>
    public static bool SendToRecycleBin(string path, out string? error)
    {
        error = null;
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",
                pTo = null,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOF_NOCONFIRMMKDIR,
            };
            int r = SHFileOperationW(ref op);
            if (r != 0)
            {
                error = Describe(r);
                return false;
            }
            if (op.fAnyOperationsAborted)
            {
                error = "操作被中断";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string Describe(int code) => code switch
    {
        0x71 => "源文件与目标相同",
        0x74 => "路径过长",
        0x78 => "文件已不存在或路径无效",
        0x7C => "拒绝访问（只读或被占用）",
        0x7E => "文件名无效",
        0x7F => "磁盘不可用",
        0x81 => "无法删除（文件可能正被其他程序使用）",
        0x82 => "无法创建目标文件",
        0x83 => "无法读取源文件",
        0x84 => "磁盘已满",
        0x85 => "源文件被占用",
        0xB7 => "已存在同名文件",
        0x402 => "未知错误",
        0x10000 => "操作被用户取消",
        _ => $"系统错误 0x{code:X}",
    };
}
