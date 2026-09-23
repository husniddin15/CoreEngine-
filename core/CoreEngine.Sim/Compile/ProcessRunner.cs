using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CoreEngine.Sim.Compile
{
    /// <summary>The exit code and the combined standard output and error of a finished program.</summary>
    public sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string output, TimeSpan duration)
        {
            ExitCode = exitCode;
            Output = output;
            Duration = duration;
        }

        public int ExitCode { get; }
        public string Output { get; }
        public TimeSpan Duration { get; }
    }

    /// <summary>Runs a program to completion and captures what it prints (ADR-0003: the toolchain runs as separate processes).</summary>
    public interface IProcessRunner
    {
        ProcessResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout);
    }

    public static class ProcessRunner
    {
        /// <summary>
        /// The Win32 runner on Windows, so .NET, Mono and IL2CPP builds all use the same code path;
        /// IL2CPP does not implement System.Diagnostics.Process (it fails with "Native error= Success").
        /// </summary>
        public static IProcessRunner Default { get; } =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new Win32ProcessRunner() : (IProcessRunner)new ManagedProcessRunner();
    }

    /// <summary>System.Diagnostics.Process: fine under .NET and Mono, not implemented under IL2CPP.</summary>
    public sealed class ManagedProcessRunner : IProcessRunner
    {
        public ProcessResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);

            var output = new StringBuilder();
            var watch = Stopwatch.StartNew();
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                throw new TimeoutException($"{Path.GetFileName(executable)} did not finish within {timeout.TotalSeconds:F0} s.");
            }
            process.WaitForExit(); // flush the asynchronous output readers
            lock (output) return new ProcessResult(process.ExitCode, output.ToString(), watch.Elapsed);
        }
    }

    /// <summary>
    /// CreateProcessW with one anonymous pipe for standard output and error, through P/Invoke to kernel32,
    /// which works under IL2CPP too. No console window appears. Reads with PeekNamedPipe, so a timeout
    /// can end the wait.
    /// </summary>
    public sealed class Win32ProcessRunner : IProcessRunner
    {
        public ProcessResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            var commandLine = new StringBuilder(QuoteArgument(executable));
            foreach (string argument in arguments) commandLine.Append(' ').Append(QuoteArgument(argument));

            var security = new SecurityAttributes { nLength = Marshal.SizeOf<SecurityAttributes>(), bInheritHandle = 1 };
            if (!CreatePipe(out IntPtr readPipe, out IntPtr writePipe, ref security, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var process = new ProcessInformation();
            try
            {
                // Only the child's end of the pipe may be inherited.
                SetHandleInformation(readPipe, HandleFlagInherit, 0);
                var startup = new StartupInfo
                {
                    cb = Marshal.SizeOf<StartupInfo>(),
                    dwFlags = StartfUseStdHandles,
                    hStdOutput = writePipe,
                    hStdError = writePipe,
                };
                var watch = Stopwatch.StartNew();
                if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, true, CreateNoWindow, IntPtr.Zero, null, ref startup, out process))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not start {executable}");
                CloseHandle(writePipe); // the child holds its own copy; ReadFile ends when every writer is gone
                writePipe = IntPtr.Zero;

                var output = new MemoryStream();
                var buffer = new byte[8192];
                while (true)
                {
                    bool drained = !Drain(readPipe, buffer, output);
                    if (WaitForSingleObject(process.hProcess, drained ? 5u : 0u) == WaitObject0)
                    {
                        while (Drain(readPipe, buffer, output)) { }
                        break;
                    }
                    if (watch.Elapsed > timeout)
                    {
                        TerminateProcess(process.hProcess, 1);
                        throw new TimeoutException($"{Path.GetFileName(executable)} did not finish within {timeout.TotalSeconds:F0} s.");
                    }
                }
                GetExitCodeProcess(process.hProcess, out uint exitCode);
                return new ProcessResult((int)exitCode, Encoding.UTF8.GetString(output.ToArray()), watch.Elapsed);
            }
            finally
            {
                if (writePipe != IntPtr.Zero) CloseHandle(writePipe);
                CloseHandle(readPipe);
                if (process.hProcess != IntPtr.Zero) CloseHandle(process.hProcess);
                if (process.hThread != IntPtr.Zero) CloseHandle(process.hThread);
            }
        }

        /// <summary>Reads whatever is waiting in the pipe; false when nothing was there.</summary>
        static bool Drain(IntPtr pipe, byte[] buffer, MemoryStream output)
        {
            if (!PeekNamedPipe(pipe, IntPtr.Zero, 0, IntPtr.Zero, out int available, IntPtr.Zero) || available == 0) return false;
            if (!ReadFile(pipe, buffer, Math.Min(available, buffer.Length), out int read, IntPtr.Zero) || read == 0) return false;
            output.Write(buffer, 0, read);
            return true;
        }

        /// <summary>
        /// Quotes one argument so CommandLineToArgvW (and Go's os.Args, used by arduino-cli) gives it back
        /// unchanged: backslashes are literal except before a quote.
        /// </summary>
        internal static string QuoteArgument(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return argument;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"') sb.Append('\\', backslashes * 2 + 1);
                else sb.Append('\\', backslashes);
                sb.Append(c);
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            return sb.Append('"').ToString();
        }

        const int HandleFlagInherit = 1;
        const int StartfUseStdHandles = 0x100;
        const uint CreateNoWindow = 0x08000000;
        const uint WaitObject0 = 0;

        [StructLayout(LayoutKind.Sequential)]
        struct SecurityAttributes
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct StartupInfo
        {
            public int cb;
            public IntPtr lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessInformation
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CreatePipe(out IntPtr readPipe, out IntPtr writePipe, ref SecurityAttributes attributes, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr handle, int mask, int flags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessW(string? applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool PeekNamedPipe(IntPtr pipe, IntPtr buffer, int bufferSize, IntPtr bytesRead, out int totalBytesAvailable, IntPtr bytesLeftThisMessage);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(IntPtr file, byte[] buffer, int bytesToRead, out int bytesRead, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);
    }
}
