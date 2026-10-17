using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PlaylistFlac
{
    // A Windows job object with "kill on close" ties the download engine's lifetime to the app.
    // Without it, a crash or End Task leaves the engine running: it keeps downloading, keeps the
    // Soulseek login busy, and still holds the sign-in it read at startup. The job handle is held for
    // the life of the process, so Windows closes it, and ends every process in the job, when the app
    // exits for any reason.
    //
    // This is containment, not a requirement: on any failure the engine still runs, uncontained.
    internal sealed class ChildProcessJob : IDisposable
    {
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private static readonly object sharedGate = new object();
        private static ChildProcessJob shared;
        private IntPtr handle;
        private readonly object handleGate = new object();

        // One job for the whole app. It is never disposed, so it closes with the process.
        internal static ChildProcessJob Shared
        {
            get
            {
                lock (sharedGate)
                {
                    if (shared == null) shared = new ChildProcessJob();
                    return shared;
                }
            }
        }

        internal ChildProcessJob() { handle = TryCreate(); }

        internal bool IsActive { get { lock (handleGate) return handle != IntPtr.Zero; } }

        // True when the process now belongs to the job.
        internal bool TryAssign(Process process)
        {
            lock (handleGate)
            {
            IntPtr job = handle;
            if (job == IntPtr.Zero || process == null) return false;
            try { return NativeMethods.AssignProcessToJobObject(job, process.Handle); }
            catch (InvalidOperationException) { return false; }
            catch (Win32Exception) { return false; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            }
        }

        // Closing the last handle ends every process still in the job.
        public void Dispose()
        {
            lock (handleGate)
            {
            IntPtr job = Interlocked.Exchange(ref handle, IntPtr.Zero);
            if (job != IntPtr.Zero) NativeMethods.CloseHandle(job);
            }
        }

        private static IntPtr TryCreate()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return IntPtr.Zero;
            IntPtr job = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) return IntPtr.Zero;
                var limits = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
                int length = Marshal.SizeOf(typeof(NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                buffer = Marshal.AllocHGlobal(length);
                Marshal.StructureToPtr(limits, buffer, false);
                // On failure the finally block closes the job handle; closing it here too would
                // risk closing an unrelated handle that reused the same value.
                if (!NativeMethods.SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)length))
                    return IntPtr.Zero;
                IntPtr ready = job;
                job = IntPtr.Zero;
                return ready;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (MarshalDirectiveException) { }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (job != IntPtr.Zero) NativeMethods.CloseHandle(job);
            }
            return IntPtr.Zero;
        }

        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
            internal struct IO_COUNTERS
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint informationLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CloseHandle(IntPtr handle);
        }
    }
}
