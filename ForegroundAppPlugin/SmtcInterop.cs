using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ForegroundAppPlugin
{
    internal enum SmtcPlaybackStatus
    {
        Closed = 0,
        Opened = 1,
        Changing = 2,
        Stopped = 3,
        Playing = 4,
        Paused = 5
    }

    /// <summary>SMTC 当前会话的一次快照（纯托管数据，可跨线程读）。</summary>
    internal sealed class MediaSnapshot
    {
        public string AppId = string.Empty;
        public string AppName = string.Empty;
        public string Title = string.Empty;
        public string Artist = string.Empty;
        public string Album = string.Empty;
        public SmtcPlaybackStatus Status;
        public TimeSpan Position;
        public TimeSpan Duration;

        public bool HasMedia => !string.IsNullOrWhiteSpace(Title);

        /// <summary>曲目标识：只含「是哪首歌」，不含播放状态，避免暂停/继续被当成切歌。</summary>
        public string TrackKey => AppId + "\u0001" + Title + "\u0001" + Artist + "\u0001" + Album;
    }

    /// <summary>
    /// Windows 系统媒体传输控件（SMTC）的裸 COM 访问。
    ///
    /// 为什么不用 CsWinRT 投影：VPetLLM 的 PluginManager 只把插件的 .dll/.pdb 影子拷贝到临时目录，
    /// 再用独立 AssemblyLoadContext 加载，不解析任何旁挂依赖。所以插件必须是单文件 DLL，
    /// 不能带上 WinRT.Runtime.dll / Microsoft.Windows.SDK.NET.dll。这里直接按 vtable 调。
    ///
    /// IID 与槽位序号取自 C:\Windows\System32\WinMetadata\Windows.Media.winmd，不是猜的。
    ///
    /// 线程约束：每个使用者线程都要先调 Initialize，用完调 Shutdown。原始接口指针没有做
    /// 跨单元封送，所以缓存的管理器是 ThreadStatic 的，绝不在线程之间传递。
    /// </summary>
    internal static unsafe class SmtcInterop
    {
        // Windows.Media.Control.IGlobalSystemMediaTransportControlsSessionManagerStatics
        private static Guid IID_ManagerStatics = new Guid("2050c4ee-11a0-57de-aed7-c97c70338245");
        // Windows.Foundation.IAsyncInfo
        private static Guid IID_AsyncInfo = new Guid("00000036-0000-0000-c000-000000000046");

        private const string ManagerClassName = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";

        // IGlobalSystemMediaTransportControlsSessionManagerStatics
        private const int Slot_Statics_RequestAsync = 6;

        // IGlobalSystemMediaTransportControlsSessionManager
        private const int Slot_Manager_GetCurrentSession = 6;

        // IGlobalSystemMediaTransportControlsSession
        private const int Slot_Session_SourceAppUserModelId = 6;
        private const int Slot_Session_TryGetMediaPropertiesAsync = 7;
        private const int Slot_Session_GetTimelineProperties = 8;
        private const int Slot_Session_GetPlaybackInfo = 9;

        // IGlobalSystemMediaTransportControlsSessionMediaProperties
        private const int Slot_Props_Title = 6;
        private const int Slot_Props_Artist = 9;      // 注意：8 是 AlbumArtist，9 才是 Artist
        private const int Slot_Props_AlbumTitle = 10;

        // IGlobalSystemMediaTransportControlsSessionPlaybackInfo
        private const int Slot_Playback_Status = 7;

        // IGlobalSystemMediaTransportControlsSessionTimelineProperties
        private const int Slot_Timeline_StartTime = 6;
        private const int Slot_Timeline_EndTime = 7;
        private const int Slot_Timeline_Position = 10;

        // IAsyncInfo / IAsyncOperation<T>
        private const int Slot_AsyncInfo_Status = 7;
        private const int Slot_AsyncOperation_GetResults = 8;

        private const int AsyncStatusStarted = 0;
        private const int AsyncStatusCompleted = 1;

        private const int RO_INIT_MULTITHREADED = 1;

        /// <summary>
        /// 每线程各持一份会话管理器。宿主允许停用后再启用插件（同一实例走 Unload -> Initialize），
        /// 新的监视线程可能在旧线程退出前就起来了；做成 ThreadStatic 后两者各管各的指针，
        /// 旧线程收尾时的 Shutdown 不会把新线程正在用的管理器释放掉。
        /// </summary>
        [ThreadStatic]
        private static IntPtr _manager;

        [DllImport("combase.dll")]
        private static extern int RoInitialize(int initType);

        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

        /// <summary>在拥有本类的线程上把 COM 单元初始化好。重复调用无害。</summary>
        public static void Initialize()
        {
            // S_FALSE（已初始化）和 RPC_E_CHANGED_MODE（线程已是 STA）都不影响后续调用，忽略返回值。
            RoInitialize(RO_INIT_MULTITHREADED);
        }

        /// <summary>释放缓存的会话管理器。必须在持有它的那个线程上调用。</summary>
        public static void Shutdown()
        {
            if (_manager != IntPtr.Zero)
            {
                Release(_manager);
                _manager = IntPtr.Zero;
            }
        }

        /// <summary>读取当前 SMTC 会话。没有会话、SMTC 不可用或调用失败时返回 null。</summary>
        public static MediaSnapshot? TryGetSnapshot()
        {
            var manager = GetManager();
            if (manager == IntPtr.Zero) return null;

            if (GetPointer(manager, Slot_Manager_GetCurrentSession, out IntPtr session) < 0)
            {
                // 管理器指针失效（例如 RPC 断开），丢掉重建，下一轮再试。
                Shutdown();
                return null;
            }
            if (session == IntPtr.Zero) return null;

            try
            {
                var snapshot = new MediaSnapshot();

                if (GetPointer(session, Slot_Session_SourceAppUserModelId, out IntPtr appId) >= 0)
                    snapshot.AppId = ConsumeHString(appId);
                snapshot.AppName = FriendlyAppName(snapshot.AppId);

                ReadPlaybackInfo(session, snapshot);
                ReadTimeline(session, snapshot);
                ReadMediaProperties(session, snapshot);

                return snapshot;
            }
            finally
            {
                Release(session);
            }
        }

        private static void ReadPlaybackInfo(IntPtr session, MediaSnapshot snapshot)
        {
            if (GetPointer(session, Slot_Session_GetPlaybackInfo, out IntPtr info) < 0 || info == IntPtr.Zero) return;
            try
            {
                if (GetInt32(info, Slot_Playback_Status, out int status) >= 0)
                    snapshot.Status = (SmtcPlaybackStatus)status;
            }
            finally
            {
                Release(info);
            }
        }

        private static void ReadTimeline(IntPtr session, MediaSnapshot snapshot)
        {
            if (GetPointer(session, Slot_Session_GetTimelineProperties, out IntPtr timeline) < 0 || timeline == IntPtr.Zero) return;
            try
            {
                // WinRT TimeSpan 就是一个 Int64，单位与 .NET Ticks 相同（100ns）。
                long start = 0;
                long end = 0;
                if (GetInt64(timeline, Slot_Timeline_StartTime, out long s) >= 0) start = s;
                if (GetInt64(timeline, Slot_Timeline_EndTime, out long e) >= 0) end = e;
                if (GetInt64(timeline, Slot_Timeline_Position, out long p) >= 0) snapshot.Position = TimeSpan.FromTicks(p);
                if (end > start) snapshot.Duration = TimeSpan.FromTicks(end - start);
            }
            finally
            {
                Release(timeline);
            }
        }

        private static void ReadMediaProperties(IntPtr session, MediaSnapshot snapshot)
        {
            if (GetPointer(session, Slot_Session_TryGetMediaPropertiesAsync, out IntPtr operation) < 0 || operation == IntPtr.Zero) return;
            try
            {
                IntPtr props = AwaitOperation(operation, 2000);
                if (props == IntPtr.Zero) return;
                try
                {
                    if (GetPointer(props, Slot_Props_Title, out IntPtr title) >= 0)
                        snapshot.Title = ConsumeHString(title);
                    if (GetPointer(props, Slot_Props_Artist, out IntPtr artist) >= 0)
                        snapshot.Artist = ConsumeHString(artist);
                    if (GetPointer(props, Slot_Props_AlbumTitle, out IntPtr album) >= 0)
                        snapshot.Album = ConsumeHString(album);
                }
                finally
                {
                    Release(props);
                }
            }
            finally
            {
                Release(operation);
            }
        }

        private static IntPtr GetManager()
        {
            if (_manager != IntPtr.Zero) return _manager;

            if (WindowsCreateString(ManagerClassName, ManagerClassName.Length, out IntPtr classId) < 0) return IntPtr.Zero;
            try
            {
                if (RoGetActivationFactory(classId, ref IID_ManagerStatics, out IntPtr factory) < 0 || factory == IntPtr.Zero)
                    return IntPtr.Zero;
                try
                {
                    if (GetPointer(factory, Slot_Statics_RequestAsync, out IntPtr operation) < 0 || operation == IntPtr.Zero)
                        return IntPtr.Zero;
                    try
                    {
                        _manager = AwaitOperation(operation, 5000);
                    }
                    finally
                    {
                        Release(operation);
                    }
                }
                finally
                {
                    Release(factory);
                }
            }
            finally
            {
                WindowsDeleteString(classId);
            }
            return _manager;
        }

        /// <summary>
        /// 同步等一个 IAsyncOperation&lt;T&gt; 完成并取出结果指针。调用方负责 Release 结果与 operation 本身。
        /// 用轮询而不是 Completed 回调：回调要现造一个 COM vtable，而这些操作都是毫秒级返回的。
        /// </summary>
        private static IntPtr AwaitOperation(IntPtr operation, int timeoutMs)
        {
            if (QueryInterface(operation, ref IID_AsyncInfo, out IntPtr asyncInfo) < 0 || asyncInfo == IntPtr.Zero)
                return IntPtr.Zero;
            try
            {
                var stopwatch = Stopwatch.StartNew();
                while (true)
                {
                    if (GetInt32(asyncInfo, Slot_AsyncInfo_Status, out int status) < 0) return IntPtr.Zero;
                    if (status == AsyncStatusCompleted) break;
                    if (status != AsyncStatusStarted) return IntPtr.Zero;   // Canceled / Error
                    if (stopwatch.ElapsedMilliseconds > timeoutMs) return IntPtr.Zero;
                    Thread.Sleep(5);
                }
            }
            finally
            {
                Release(asyncInfo);
            }

            if (GetPointer(operation, Slot_AsyncOperation_GetResults, out IntPtr result) < 0) return IntPtr.Zero;
            return result;
        }

        /// <summary>把 AUMID 收拾成人能读的名字，顺带认一批常见播放器。</summary>
        private static string FriendlyAppName(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return string.Empty;

            var name = appId;

            // 打包应用形如 Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic
            int bang = name.IndexOf('!');
            if (bang >= 0) name = name.Substring(0, bang);
            int underscore = name.IndexOf('_');
            if (underscore > 0) name = name.Substring(0, underscore);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);

            int lastDot = name.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < name.Length - 1) name = name.Substring(lastDot + 1);

            switch (name.ToLowerInvariant())
            {
                case "zunemusic": return "Windows Media Player";
                case "zunevideo": return "Films & TV";
                case "spotify": return "Spotify";
                case "chrome": return "Google Chrome";
                case "msedge": return "Microsoft Edge";
                case "firefox": return "Firefox";
                case "vlc": return "VLC";
                case "foobar2000": return "foobar2000";
                case "qqmusic": return "QQ Music";
                case "cloudmusic": return "NetEase Cloud Music";
                case "kugou": return "KuGou Music";
                case "aimp": return "AIMP";
                case "musicbee": return "MusicBee";
                case "potplayermini64":
                case "potplayermini": return "PotPlayer";
                default: return name;
            }
        }

        // ---- vtable 调用辅助 ----
        // IUnknown: 0=QueryInterface 1=AddRef 2=Release；WinRT 接口的自有方法从槽位 6 开始
        // （3..5 是 IInspectable 的 GetIids / GetRuntimeClassName / GetTrustLevel）。

        private static int QueryInterface(IntPtr instance, ref Guid iid, out IntPtr result)
        {
            IntPtr* vtable = *(IntPtr**)instance;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, out IntPtr, int>)vtable[0];
            fixed (Guid* pIid = &iid)
            {
                return fn(instance, pIid, out result);
            }
        }

        private static uint Release(IntPtr instance)
        {
            if (instance == IntPtr.Zero) return 0;
            IntPtr* vtable = *(IntPtr**)instance;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint>)vtable[2];
            return fn(instance);
        }

        private static int GetPointer(IntPtr instance, int slot, out IntPtr value)
        {
            IntPtr* vtable = *(IntPtr**)instance;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, out IntPtr, int>)vtable[slot];
            return fn(instance, out value);
        }

        private static int GetInt32(IntPtr instance, int slot, out int value)
        {
            IntPtr* vtable = *(IntPtr**)instance;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, out int, int>)vtable[slot];
            return fn(instance, out value);
        }

        private static int GetInt64(IntPtr instance, int slot, out long value)
        {
            IntPtr* vtable = *(IntPtr**)instance;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, out long, int>)vtable[slot];
            return fn(instance, out value);
        }

        /// <summary>读出 HSTRING 内容并释放它。</summary>
        private static string ConsumeHString(IntPtr hstring)
        {
            if (hstring == IntPtr.Zero) return string.Empty;
            try
            {
                IntPtr buffer = WindowsGetStringRawBuffer(hstring, out uint length);
                if (buffer == IntPtr.Zero || length == 0) return string.Empty;
                return new string((char*)buffer, 0, (int)length);
            }
            finally
            {
                WindowsDeleteString(hstring);
            }
        }
    }
}
