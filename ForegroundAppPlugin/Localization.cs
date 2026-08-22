using System;
using System.Collections.Generic;

namespace ForegroundAppPlugin
{
    /// <summary>
    /// 插件内的多语言文本表。
    /// 宿主把语言代码存在 <c>Settings.Language</c> 里，取值为 Language.json 的
    /// <c>Language.Select</c> 键：en / zh-hans / zh-hant / jp。
    /// </summary>
    internal static class Lang
    {
        public const string En = "en";
        public const string Hans = "zh-hans";
        public const string Hant = "zh-hant";
        public const string Jp = "jp";

        /// <summary>
        /// 把宿主给的语言代码归一化成表里的四个键之一。
        /// 宿主写入的日文代码是 "jp"，但历史上插件普遍写成 "ja"，两者都接受。
        /// </summary>
        public static string Normalize(string? language)
        {
            if (string.IsNullOrWhiteSpace(language)) return Hans;
            switch (language.Trim().ToLowerInvariant())
            {
                case "jp":
                case "ja":
                case "ja-jp":
                    return Jp;
                case "zh-hant":
                case "zh-tw":
                case "zh-hk":
                case "zh-mo":
                    return Hant;
                case "zh-hans":
                case "zh":
                case "zh-cn":
                case "zh-sg":
                    return Hans;
                case "en":
                    return En;
                default:
                    return language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? En : Hans;
            }
        }

        /// <summary>取翻译。key 不存在时返回 key 本身，方便一眼看出漏翻。</summary>
        public static string T(string? language, string key)
        {
            if (!Table.TryGetValue(key, out var row)) return key;
            switch (Normalize(language))
            {
                case En: return row.En;
                case Hant: return row.Hant;
                case Jp: return row.Jp;
                default: return row.Hans;
            }
        }

        private static readonly Dictionary<string, (string En, string Hans, string Hant, string Jp)> Table = new()
        {
            ["plugin_desc"] = (
                "Monitors the foreground application (including window title) and the currently playing media reported by Windows SMTC, and provides them to the AI. Keyword and allow/deny list filters keep private apps out of the AI's context.",
                "监视前台应用程序（包括窗口标题）以及 Windows SMTC 报告的当前播放媒体，并将其提供给 AI。可用关键词和黑白名单过滤，让隐私应用不进入 AI 上下文。",
                "監視前臺應用程式（包括視窗標題）以及 Windows SMTC 回報的當前播放媒體，並將其提供給 AI。可用關鍵詞和黑白名單過濾，讓隱私應用不進入 AI 上下文。",
                "フォアグラウンドアプリケーション（ウィンドウタイトルを含む）と Windows SMTC が報告する再生中のメディアを監視し、AI に提供します。キーワードと許可/拒否リストでプライベートなアプリを AI のコンテキストから除外できます。"),

            ["tab_title"] = ("Foreground App", "前台应用", "前臺應用", "フォアグラウンド"),
            ["panel_title"] = ("Foreground App Settings", "前台应用设置", "前臺應用設定", "フォアグラウンドアプリ設定"),

            ["section_app"] = ("Foreground Application", "前台应用", "前臺應用", "フォアグラウンドアプリ"),
            ["lbl_jitter"] = ("Poll interval (s)", "轮询间隔（秒）", "輪詢間隔（秒）", "ポーリング間隔（秒）"),
            ["hint_jitter"] = (
                "How often the foreground window is checked. Larger values reduce chatter.",
                "检查前台窗口的频率。数值越大，打扰越少。",
                "檢查前臺視窗的頻率。數值越大，打擾越少。",
                "フォアグラウンドウィンドウを確認する頻度。値が大きいほど通知が減ります。"),

            ["section_media"] = ("Media Playback (SMTC)", "媒体播放（SMTC）", "媒體播放（SMTC）", "メディア再生（SMTC）"),
            ["chk_media_enable"] = (
                "Detect currently playing media",
                "识别当前播放的媒体",
                "識別當前播放的媒體",
                "再生中のメディアを検出する"),
            ["hint_media_enable"] = (
                "Reads the track from Windows System Media Transport Controls, so any player that shows up in the volume flyout works.",
                "从 Windows 系统媒体传输控件读取曲目信息，凡是能出现在音量浮出控件里的播放器都支持。",
                "從 Windows 系統媒體傳輸控制項讀取曲目資訊，凡是能出現在音量浮出控制項裡的播放器都支援。",
                "Windows システム メディア トランスポート コントロールから曲情報を読み取ります。音量フライアウトに表示されるプレーヤーはすべて対応します。"),
            ["lbl_media_interval"] = ("Media poll interval (s)", "媒体轮询间隔（秒）", "媒體輪詢間隔（秒）", "メディアポーリング間隔（秒）"),
            ["chk_media_dynamic"] = (
                "Include now playing in the AI's context",
                "把当前播放内容加入 AI 上下文",
                "把當前播放內容加入 AI 上下文",
                "再生中の曲を AI のコンテキストに含める"),
            ["chk_media_notify"] = (
                "Notify the AI when the track changes",
                "切歌时主动通知 AI",
                "切歌時主動通知 AI",
                "曲が変わったら AI に通知する"),
            ["hint_media_notify"] = (
                "Off by default: on a short playlist this can interrupt often.",
                "默认关闭：歌单很短时会频繁打断。",
                "預設關閉：歌單很短時會頻繁打斷。",
                "既定はオフ：曲が短いと頻繁に割り込みます。"),

            ["section_privacy"] = ("Privacy Filter", "隐私过滤", "隱私過濾", "プライバシーフィルター"),
            ["lbl_keywords"] = ("Blocked keywords", "屏蔽关键词", "封鎖關鍵詞", "ブロックキーワード"),
            ["hint_keywords"] = (
                "One per line. If the window title or process name contains any of these, nothing is sent to the AI at all. Case-insensitive.",
                "一行一个。窗口标题或进程名含有其中任意一个时，完全不会告知 AI。不区分大小写。",
                "一行一個。視窗標題或行程名含有其中任一個時，完全不會告知 AI。不區分大小寫。",
                "1 行に 1 つ。ウィンドウタイトルまたはプロセス名にいずれかが含まれる場合、AI には一切通知しません。大文字小文字は区別しません。"),
            ["chk_keywords_media"] = (
                "Also apply keywords to media titles",
                "关键词同时作用于媒体信息",
                "關鍵詞同時作用於媒體資訊",
                "キーワードをメディア情報にも適用する"),

            ["lbl_filter_mode"] = ("App list mode", "应用名单模式", "應用名單模式", "アプリリストのモード"),
            ["filter_mode_off"] = ("Off - report every app", "关闭 - 上报所有应用", "關閉 - 回報所有應用", "オフ - すべてのアプリを通知"),
            ["filter_mode_whitelist"] = ("Whitelist - only these apps", "白名单 - 仅上报名单内应用", "白名單 - 僅回報名單內應用", "ホワイトリスト - リスト内のみ通知"),
            ["filter_mode_blacklist"] = ("Blacklist - never these apps", "黑名单 - 名单内应用不上报", "黑名單 - 名單內應用不回報", "ブラックリスト - リスト内は通知しない"),
            ["lbl_filter_list"] = ("App list (process names)", "应用名单（进程名）", "應用名單（行程名）", "アプリリスト（プロセス名）"),
            ["hint_filter_list"] = (
                "One process name per line, with or without .exe. Use Browse to pick from running and installed apps.",
                "一行一个进程名，带不带 .exe 都行。可用「浏览应用」从运行中和已安装的应用里挑。",
                "一行一個行程名，帶不帶 .exe 都行。可用「瀏覽應用」從執行中和已安裝的應用裡挑。",
                "1 行に 1 つのプロセス名。.exe の有無は問いません。「アプリを参照」から実行中・インストール済みのアプリを選べます。"),
            ["btn_browse_apps"] = ("Browse apps...", "浏览应用...", "瀏覽應用...", "アプリを参照..."),
            ["hint_whitelist_empty"] = (
                "Whitelist mode with an empty list blocks every app.",
                "白名单模式下名单为空 = 拦下所有应用。",
                "白名單模式下名單為空 = 攔下所有應用。",
                "ホワイトリストが空の場合、すべてのアプリがブロックされます。"),

            ["preview_blocked_keyword"] = (
                "Current app is hidden by a keyword.",
                "当前应用已被关键词屏蔽。",
                "當前應用已被關鍵詞封鎖。",
                "現在のアプリはキーワードによって非表示です。"),
            ["preview_blocked_list"] = (
                "Current app is hidden by the app list.",
                "当前应用已被名单屏蔽。",
                "當前應用已被名單封鎖。",
                "現在のアプリはリストによって非表示です。"),

            ["picker_title"] = ("Choose applications", "选择应用", "選擇應用", "アプリケーションを選択"),
            ["picker_search"] = ("Search...", "搜索...", "搜尋...", "検索..."),
            ["picker_running"] = ("running", "运行中", "執行中", "実行中"),
            ["picker_installed"] = ("installed", "已安装", "已安裝", "インストール済み"),
            ["picker_hint"] = (
                "Pick one or more apps to add to the list.",
                "勾选一个或多个应用加入名单。",
                "勾選一個或多個應用加入名單。",
                "リストに追加するアプリを 1 つ以上選んでください。"),
            ["picker_add"] = ("Add selected", "添加所选", "加入所選", "選択を追加"),
            ["picker_cancel"] = ("Cancel", "取消", "取消", "キャンセル"),
            ["picker_empty"] = ("No applications found.", "没有找到应用。", "沒有找到應用。", "アプリが見つかりません。"),

            ["section_preview"] = ("Currently Detected", "当前识别结果", "當前識別結果", "現在の検出結果"),
            ["btn_refresh"] = ("Refresh", "刷新", "重新整理", "更新"),
            ["preview_no_media"] = ("No media is playing.", "没有正在播放的媒体。", "沒有正在播放的媒體。", "再生中のメディアはありません。"),
            ["preview_media_off"] = ("Media detection is off.", "媒体识别已关闭。", "媒體識別已關閉。", "メディア検出はオフです。"),
            ["preview_no_app"] = ("No foreground app detected yet.", "尚未识别到前台应用。", "尚未識別到前臺應用。", "フォアグラウンドアプリはまだ検出されていません。"),

            ["btn_save"] = ("Save", "保存", "儲存", "保存"),

            ["err_invalid_number_title"] = ("Invalid Input", "输入无效", "輸入無效", "入力エラー"),
            ["err_invalid_jitter"] = (
                "Poll interval must be a whole number of at least 1 second.",
                "轮询间隔必须是不小于 1 的整数秒。",
                "輪詢間隔必須是不小於 1 的整數秒。",
                "ポーリング間隔は 1 秒以上の整数で指定してください。"),
            ["err_invalid_media_interval"] = (
                "Media poll interval must be a whole number of at least 1 second.",
                "媒体轮询间隔必须是不小于 1 的整数秒。",
                "媒體輪詢間隔必須是不小於 1 的整數秒。",
                "メディアポーリング間隔は 1 秒以上の整数で指定してください。"),

            ["msg_settings_opened"] = ("Setting window opened.", "设置窗口已打开。", "設定視窗已開啟。", "設定ウィンドウを開きました。"),
            ["msg_invalid_action"] = ("Invalid action.", "无效的操作。", "無效的操作。", "無効な操作です。"),

            ["status_playing"] = ("playing", "正在播放", "正在播放", "再生中"),
            ["status_paused"] = ("paused", "已暂停", "已暫停", "一時停止中"),
            ["status_stopped"] = ("stopped", "已停止", "已停止", "停止中"),
        };
    }
}
