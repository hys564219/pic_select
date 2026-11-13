using System.IO;
using System.Text.Json;

namespace PicSelect.Core;

public enum Lang { En = 0, Zh = 1, Ja = 2 }

/// <summary>
/// 界面文案表。三种语言：英文（默认）、中文、日文。
/// 缺键时回落到英文，再缺就原样返回 key，方便发现漏翻。
/// </summary>
public static class Loc
{
    public static Lang Current { get; private set; } = Lang.En;

    /// <summary>语言切换后触发，界面据此重刷所有文案。</summary>
    public static event Action? Changed;

    public static string[] DisplayNames { get; } = { "English", "中文", "日本語" };

    public static void Init()
    {
        Current = AppSettings.LoadLanguage();
    }

    public static void Set(Lang lang)
    {
        if (lang == Current) return;
        Current = lang;
        AppSettings.SaveLanguage(lang);
        Changed?.Invoke();
    }

    /// <summary>测试用：只改当前语言，不写配置文件。</summary>
    public static void ForceLang(Lang lang) => Current = lang;

    public static string T(string key, params object[] args)
    {
        string s =
            Tables[Current].TryGetValue(key, out var v) ? v :
            Tables[Lang.En].TryGetValue(key, out var e) ? e : key;
        return args.Length == 0 ? s : string.Format(s, args);
    }

    // ────────────────────────────── 文案表 ──────────────────────────────

    private static readonly Dictionary<Lang, Dictionary<string, string>> Tables = new()
    {
        [Lang.En] = new()
        {
            ["app.title"] = "PicSelect",
            ["app.titleFolder"] = "{0} — {1}",

            ["top.open"] = "Open folder",
            ["top.jump"] = "Go to",
            ["top.restart"] = "Restart",
            ["top.help"] = "Shortcuts",
            ["top.export"] = "Export kept list",
            ["top.delete"] = "Delete unmarked",

            ["btn.keep"] = "Keep",
            ["btn.skip"] = "Next",
            ["btn.prev"] = "Previous",
            ["btn.mark"] = "Mark",
            ["btn.unmark"] = "Unmark",
            ["btn.prevMarked"] = "Prev marked",
            ["btn.nextMarked"] = "Next marked",
            ["btn.undo"] = "Undo",

            ["hint.keep"] = "Space / K",
            ["hint.skip"] = "D / →",
            ["hint.prev"] = "A / ←",
            ["hint.mark"] = "S",
            ["hint.prevMarked"] = "[ / Ctrl+←",
            ["hint.nextMarked"] = "] / Ctrl+→",
            ["hint.undo"] = "Ctrl+Z",

            ["badge.marked"] = "✓ Kept",

            ["hint.drop"] = "Drop a folder here, or click \u201cOpen folder\u201d",
            ["hint.done.title"] = "All done",
            ["hint.done.sub"] = "{0} images moved to the Recycle Bin · you can open the next folder",

            ["busy.loading"] = "Loading…",
            ["busy.decode"] = "This image can't be decoded",
            ["busy.gone"] = "File no longer exists",

            ["status.processed"] = "Processed {0} / {1}",
            ["status.kept"] = "Kept {0} · Unmarked {1}",
            ["status.cache"] = "Cache {0} · {1} MB",
            ["status.lastImage"] = "Last image · you can delete the unmarked ones now",
            ["meta.marked"] = "{0} · kept",

            ["nav.noPrev"] = "No marked image before this one — went to the first image",
            ["nav.noNext"] = "No marked image after this one — went to the last processed image",
            ["nav.noNextNone"] = "No marked image after this one, and nothing processed yet",
            ["nav.atFirst"] = "Already at the first image",
            ["nav.atLast"] = "Already at the last processed position",

            ["dlg.restore.title"] = "Resume",
            ["dlg.restore.body"] = "Last folder:\n{0}\n\nProgress {1} / {2}, {3} kept.\n\nResume where you left off?",
            ["dlg.restart.title"] = "Restart",
            ["dlg.restart.body"] = "Restarting clears the marks for this round. No files are deleted.\n\nContinue?",
            ["dlg.openFolder.title"] = "Select the folder to review",

            ["err.scan"] = "Could not read the folder: {0}",
            ["err.noImages"] = "No supported images found in this folder.",

            ["dlg.delete.title"] = "Confirm delete",
            ["dlg.delete.body"] = "{0} unmarked images will be moved to the Recycle Bin.\n{1} kept.\n\nFiles go to the Recycle Bin and can be restored. Continue?",
            ["dlg.delete.none"] = "Everything is marked as kept. Nothing to delete.",
            ["del.progress"] = "Moving to Recycle Bin {0} / {1} …",
            ["del.partial.title"] = "Some files could not be deleted",
            ["del.partial.body"] = "{0} moved to the Recycle Bin, {1} failed:\n\n{2}\n\nFailed files may be in use by another program. Close it and try again.",
            ["del.more"] = "… and {0} more",

            ["export.title"] = "Export kept list",
            ["export.fail"] = "Export failed. Check that the tool folder is writable.",
            ["export.ok"] = "Exported to:\n{0}",
            ["export.header.folder"] = "# Folder: ",
            ["export.header.time"] = "# Exported: ",
            ["export.filePrefix"] = "kept",

            ["jump.title"] = "Go to image",
            ["jump.hint"] = "Enter a number (1 – {0}), or part of a file name, e.g. IMG_2045 or *2045*",
            ["jump.cancel"] = "Cancel",
            ["jump.go"] = "Go",
            ["jump.notFound"] = "No image matched \u201c{0}\u201d.",

            ["help.title"] = "Shortcuts",
            ["help.body"] =
                "Space / K     Keep and go next\n" +
                "D / →         Next without marking\n" +
                "A / ←         Previous image\n" +
                "S             Toggle mark (stays on this image)\n" +
                "[ / ]         Previous / next marked image\n" +
                "Ctrl+← / →    Same as above\n" +
                "Ctrl+Z        Undo last step\n" +
                "Ctrl+G        Go to image\n" +
                "Ctrl+O        Open folder\n" +
                "Ctrl+Delete   Delete all unmarked images\n" +
                "Home / End    First / last image\n\n" +
                "Wheel         Zoom around the cursor\n" +
                "Drag          Pan\n" +
                "Double-click  Fit window ↔ 100%\n" +
                "Enter / F     Same as double-click\n" +
                "0             100%\n\n" +
                "You can also drag a folder into the window.",
        },

        [Lang.Zh] = new()
        {
            ["app.title"] = "图片筛选",
            ["app.titleFolder"] = "{0} — {1}",

            ["top.open"] = "打开文件夹",
            ["top.jump"] = "跳转",
            ["top.restart"] = "重新开始",
            ["top.help"] = "快捷键",
            ["top.export"] = "导出保留清单",
            ["top.delete"] = "删除未标记",

            ["btn.keep"] = "保留",
            ["btn.skip"] = "下一张",
            ["btn.prev"] = "上一张",
            ["btn.mark"] = "标记保留",
            ["btn.unmark"] = "取消标记",
            ["btn.prevMarked"] = "上一个标记",
            ["btn.nextMarked"] = "下一个标记",
            ["btn.undo"] = "撤销",

            ["hint.keep"] = "空格 / K",
            ["hint.skip"] = "D / →",
            ["hint.prev"] = "A / ←",
            ["hint.mark"] = "S",
            ["hint.prevMarked"] = "[ / Ctrl+←",
            ["hint.nextMarked"] = "] / Ctrl+→",
            ["hint.undo"] = "Ctrl+Z",

            ["badge.marked"] = "✓ 已标记保留",

            ["hint.drop"] = "把文件夹拖进来，或点左上角「打开文件夹」",
            ["hint.done.title"] = "筛选完成",
            ["hint.done.sub"] = "{0} 张已移入回收站 · 可以打开下一个文件夹了",

            ["busy.loading"] = "载入中…",
            ["busy.decode"] = "这张图片无法解码",
            ["busy.gone"] = "文件已不存在",

            ["status.processed"] = "已处理 {0} / {1}",
            ["status.kept"] = "保留 {0} · 未标记 {1}",
            ["status.cache"] = "缓存 {0} 张 · {1} MB",
            ["status.lastImage"] = "已到最后一张 · 现在可以删除未标记的图片了",
            ["meta.marked"] = "{0} · 已标记保留",

            ["nav.noPrev"] = "前面没有已标记的图片，回到第一张",
            ["nav.noNext"] = "后面没有已标记的图片，跳到最后一个处理过的位置",
            ["nav.noNextNone"] = "后面没有已标记的图片，也还没有处理过任何图片",
            ["nav.atFirst"] = "已经是最前面一张了",
            ["nav.atLast"] = "已经在最后处理的位置了",

            ["dlg.restore.title"] = "继续筛选",
            ["dlg.restore.body"] = "上次在筛选：\n{0}\n\n进度 {1} / {2} 张，已标记保留 {3} 张。\n\n继续上次的进度？",
            ["dlg.restart.title"] = "重新开始",
            ["dlg.restart.body"] = "重新开始会清除本轮的标记记录，不会删除任何文件。\n\n确定吗？",
            ["dlg.openFolder.title"] = "选择要筛选的图片文件夹",

            ["err.scan"] = "读取文件夹失败：{0}",
            ["err.noImages"] = "这个文件夹里没有找到支持的图片。",

            ["dlg.delete.title"] = "确认删除",
            ["dlg.delete.body"] = "将把 {0} 张未标记的图片移入回收站。\n保留 {1} 张。\n\n文件会进入回收站，可以还原。是否继续？",
            ["dlg.delete.none"] = "所有图片都已标记保留，没有需要删除的。",
            ["del.progress"] = "正在移入回收站 {0} / {1} …",
            ["del.partial.title"] = "部分文件删除失败",
            ["del.partial.body"] = "已移入回收站 {0} 张，{1} 张失败：\n\n{2}\n\n失败的文件可能正被其他程序占用，关闭后可以再试一次。",
            ["del.more"] = "… 另有 {0} 个",

            ["export.title"] = "导出保留清单",
            ["export.fail"] = "导出失败，请检查工具目录是否可写。",
            ["export.ok"] = "已导出到：\n{0}",
            ["export.header.folder"] = "# 文件夹: ",
            ["export.header.time"] = "# 导出时间: ",
            ["export.filePrefix"] = "保留清单",

            ["jump.title"] = "跳转到图片",
            ["jump.hint"] = "输入序号（1 – {0}），或文件名片段，例如 IMG_2045、*2045*",
            ["jump.cancel"] = "取消",
            ["jump.go"] = "跳转",
            ["jump.notFound"] = "没有找到匹配「{0}」的图片。",

            ["help.title"] = "快捷键",
            ["help.body"] =
                "空格 / K     保留并进入下一张\n" +
                "D / →        下一张（不标记）\n" +
                "A / ←        上一张\n" +
                "S            标记 / 取消标记（不移动）\n" +
                "[ / ]        上一个 / 下一个已标记的图片\n" +
                "Ctrl+← / →   同上\n" +
                "Ctrl+Z       撤销上一步\n" +
                "Ctrl+G       跳转到指定图片\n" +
                "Ctrl+O       打开文件夹\n" +
                "Ctrl+Delete  删除所有未标记图片\n" +
                "Home / End   第一张 / 最后一张\n\n" +
                "滚轮          缩放（以鼠标位置为中心）\n" +
                "拖拽          平移\n" +
                "双击 / Enter  适应窗口 ↔ 100% 切换\n" +
                "F             同上\n" +
                "0             100%\n\n" +
                "也可以直接把文件夹拖进窗口。",
        },

        [Lang.Ja] = new()
        {
            ["app.title"] = "画像選別",
            ["app.titleFolder"] = "{0} — {1}",

            ["top.open"] = "フォルダを開く",
            ["top.jump"] = "移動",
            ["top.restart"] = "最初から",
            ["top.help"] = "ショートカット",
            ["top.export"] = "残すリストを出力",
            ["top.delete"] = "未選択を削除",

            ["btn.keep"] = "残す",
            ["btn.skip"] = "次へ",
            ["btn.prev"] = "前へ",
            ["btn.mark"] = "残す印",
            ["btn.unmark"] = "印を消す",
            ["btn.prevMarked"] = "前の印",
            ["btn.nextMarked"] = "次の印",
            ["btn.undo"] = "元に戻す",

            ["hint.keep"] = "スペース / K",
            ["hint.skip"] = "D / →",
            ["hint.prev"] = "A / ←",
            ["hint.mark"] = "S",
            ["hint.prevMarked"] = "[ / Ctrl+←",
            ["hint.nextMarked"] = "] / Ctrl+→",
            ["hint.undo"] = "Ctrl+Z",

            ["badge.marked"] = "✓ 残す印あり",

            ["hint.drop"] = "フォルダをここにドロップ、または左上の「フォルダを開く」",
            ["hint.done.title"] = "選別完了",
            ["hint.done.sub"] = "{0} 枚をごみ箱に移動しました · 次のフォルダを開けます",

            ["busy.loading"] = "読み込み中…",
            ["busy.decode"] = "この画像は読み込めません",
            ["busy.gone"] = "ファイルが存在しません",

            ["status.processed"] = "処理済み {0} / {1}",
            ["status.kept"] = "残す {0} · 未選択 {1}",
            ["status.cache"] = "キャッシュ {0} 枚 · {1} MB",
            ["status.lastImage"] = "最後の画像です · 未選択の画像を削除できます",
            ["meta.marked"] = "{0} · 残す印あり",

            ["nav.noPrev"] = "前方に印のある画像がないため、先頭に移動しました",
            ["nav.noNext"] = "後方に印のある画像がないため、最後に処理した位置へ移動しました",
            ["nav.noNextNone"] = "後方に印がなく、まだ処理した画像もありません",
            ["nav.atFirst"] = "すでに先頭です",
            ["nav.atLast"] = "すでに最後に処理した位置です",

            ["dlg.restore.title"] = "続きから",
            ["dlg.restore.body"] = "前回のフォルダ:\n{0}\n\n進捗 {1} / {2} 枚、残す印 {3} 枚。\n\n前回の続きから再開しますか？",
            ["dlg.restart.title"] = "最初から",
            ["dlg.restart.body"] = "最初からやり直すと、今回の印はすべて消えます。ファイルは削除されません。\n\nよろしいですか？",
            ["dlg.openFolder.title"] = "選別するフォルダを選択",

            ["err.scan"] = "フォルダを読み込めませんでした: {0}",
            ["err.noImages"] = "このフォルダには対応形式の画像がありません。",

            ["dlg.delete.title"] = "削除の確認",
            ["dlg.delete.body"] = "未選択の {0} 枚をごみ箱に移動します。\n残す {1} 枚。\n\nファイルはごみ箱に入り、復元できます。続行しますか？",
            ["dlg.delete.none"] = "すべての画像に残す印が付いています。削除する画像はありません。",
            ["del.progress"] = "ごみ箱に移動中 {0} / {1} …",
            ["del.partial.title"] = "一部のファイルを削除できませんでした",
            ["del.partial.body"] = "{0} 枚をごみ箱に移動し、{1} 枚失敗しました:\n\n{2}\n\n失敗したファイルは他のプログラムが使用中の可能性があります。閉じてから再試行してください。",
            ["del.more"] = "… 他 {0} 件",

            ["export.title"] = "残すリストを出力",
            ["export.fail"] = "出力に失敗しました。ツールのフォルダに書き込めるか確認してください。",
            ["export.ok"] = "出力しました:\n{0}",
            ["export.header.folder"] = "# フォルダ: ",
            ["export.header.time"] = "# 出力日時: ",
            ["export.filePrefix"] = "残すリスト",

            ["jump.title"] = "画像へ移動",
            ["jump.hint"] = "番号（1 – {0}）またはファイル名の一部を入力（例: IMG_2045、*2045*）",
            ["jump.cancel"] = "キャンセル",
            ["jump.go"] = "移動",
            ["jump.notFound"] = "「{0}」に一致する画像が見つかりません。",

            ["help.title"] = "ショートカット",
            ["help.body"] =
                "スペース / K   残して次へ\n" +
                "D / →          印を付けずに次へ\n" +
                "A / ←          前の画像\n" +
                "S              印を付ける / 外す（移動しない）\n" +
                "[ / ]          前 / 次の印へ\n" +
                "Ctrl+← / →     上と同じ\n" +
                "Ctrl+Z         一つ戻す\n" +
                "Ctrl+G         画像へ移動\n" +
                "Ctrl+O         フォルダを開く\n" +
                "Ctrl+Delete    未選択をすべて削除\n" +
                "Home / End     先頭 / 最後\n\n" +
                "ホイール        カーソル位置を中心に拡大縮小\n" +
                "ドラッグ        移動\n" +
                "ダブルクリック  ウィンドウに合わせる ↔ 100%\n" +
                "Enter / F       上と同じ\n" +
                "0               100%\n\n" +
                "フォルダをウィンドウにドラッグしても開けます。",
        },
    };
}

/// <summary>
/// 便携设置。和会话数据放在一起，删掉 picselect.data 即可彻底清除。
/// </summary>
public static class AppSettings
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "picselect.data");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    private sealed class Model
    {
        public string Language { get; set; } = "en";
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Lang LoadLanguage()
    {
        try
        {
            if (!File.Exists(FilePath)) return Lang.En;
            var m = JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath), Options);
            return m?.Language?.ToLowerInvariant() switch
            {
                "zh" => Lang.Zh,
                "ja" => Lang.Ja,
                _ => Lang.En,
            };
        }
        catch { return Lang.En; }
    }

    public static void SaveLanguage(Lang lang)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(new Model { Language = lang switch
            {
                Lang.Zh => "zh",
                Lang.Ja => "ja",
                _ => "en",
            } }, Options);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json, new System.Text.UTF8Encoding(false));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* 设置写失败不该影响使用 */ }
    }
}
