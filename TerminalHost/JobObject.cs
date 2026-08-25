using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VPetLLM.TerminalHost
{
    /// <summary>
    /// Win32 Job Object 包装，用来保证被 AI 拉起的整棵进程树一定会被回收。
    ///
    /// <c>Process.Kill(true)</c> 是尽力而为：它遍历当前已知的子进程，脱钩的孙进程、
    /// 或者在遍历期间新生成的进程会被漏掉；VPet 本身崩溃时更是什么都不会清理。
    /// Job Object 配 <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> 的语义是内核级的：
    /// 句柄一关，job 里的所有进程立刻被终止。做法取自 codex 的 utils/pty/src/win/job.rs。
    /// </summary>
    public sealed class JobObject : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed;

        private JobObject(IntPtr handle) => _handle = handle;

        /// <summary>创建一个 job；失败返回 null（调用方退回 Process.Kill(true)）。</summary>
        public static JobObject? Create()
        {
            var handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero) return null;

            var job = new JobObject(handle);
            if (!job.SetKillOnClose())
            {
                job.Dispose();
                return null;
            }
            return job;
        }

        private bool SetKillOnClose()
        {
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                return SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ptr, (uint)size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// 把进程收进 job。
        ///
        /// 注意存在一个无法消除的竞态：.NET 的 <c>Process.Start</c> 不支持
        /// CREATE_SUSPENDED，所以从进程启动到这里调用之间的极短窗口内，
        /// 子进程理论上可以抢先 fork 出逃逸的孙进程。shell 启动耗时在毫秒级，
        /// 实践中够用；codex 用 std::process 也有同样的限制。
        /// </summary>
        public bool Assign(Process process)
        {
            if (_disposed || _handle == IntPtr.Zero) return false;
            try
            {
                return AssignProcessToJobObject(_handle, process.Handle);
            }
            catch
            {
                // 进程可能已经退出，此时 Handle 会抛
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);   // KILL_ON_JOB_CLOSE：这一步会带走整棵树
                _handle = IntPtr.Zero;
            }
        }

        #region P/Invoke

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        #endregion
    }
}
