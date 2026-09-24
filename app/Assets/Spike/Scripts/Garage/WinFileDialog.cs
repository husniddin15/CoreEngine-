using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace CoreEngine.Spike.Garage
{
    /// <summary>
    /// Windows' own "Open" dialog (comdlg32), for the Body Studio's model upload. Unity has no file dialog in a
    /// player. The dialog is modal: the game waits until the player chooses a file or cancels. Every string goes
    /// in through unmanaged memory, so the structure is plain data for both Mono and IL2CPP.
    /// </summary>
    public static class WinFileDialog
    {
        [StructLayout(LayoutKind.Sequential)]
        struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public IntPtr lpstrFilter;
            public IntPtr lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public IntPtr lpstrFileTitle;
            public int nMaxFileTitle;
            public IntPtr lpstrInitialDir;
            public IntPtr lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public IntPtr lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        const int OfnNoChangeDir = 0x00000008, OfnPathMustExist = 0x00000800, OfnFileMustExist = 0x00001000, OfnExplorer = 0x00080000;
        const int MaxPath = 4096;

        [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", SetLastError = true)]
        static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowW(string? className, string windowName);

        [DllImport("user32.dll")]
        static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        const uint WmClose = 0x0010;

        public static bool Available =>
            Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>
        /// Asks for one file; null when the player cancels. The dialog lists files matching
        /// <paramref name="patterns"/> ("*.stl;*.obj") under <paramref name="description"/> ("3D models (*.stl, *.obj)").
        /// </summary>
        public static string? OpenFile(string title, string description, string patterns)
        {
            if (!Available) return null;
            IntPtr file = Marshal.AllocHGlobal(MaxPath * 2);
            IntPtr filter = Marshal.StringToHGlobalUni(description + "\0" + patterns + "\0\0");
            IntPtr caption = Marshal.StringToHGlobalUni(title);
            IntPtr folder = Marshal.StringToHGlobalUni(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            try
            {
                Marshal.WriteInt16(file, 0);
                var ofn = new OpenFileName
                {
                    lStructSize = Marshal.SizeOf<OpenFileName>(),
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = filter,
                    nFilterIndex = 1,
                    lpstrFile = file,
                    nMaxFile = MaxPath,
                    lpstrInitialDir = folder,
                    lpstrTitle = caption,
                    Flags = OfnExplorer | OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir,
                };
                return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(file) : null;
            }
            finally
            {
                Marshal.FreeHGlobal(file);
                Marshal.FreeHGlobal(filter);
                Marshal.FreeHGlobal(caption);
                Marshal.FreeHGlobal(folder);
            }
        }

        /// <summary>
        /// For the benchmark: a helper thread that waits for the dialog with this title and closes it, as the Cancel
        /// button would, so that a scripted run can open the real dialog. True once it found and closed it.
        /// </summary>
        public static Func<bool> CloseSoon(string title, int afterMs)
        {
            bool closed = false;
            var thread = new Thread(() =>
            {
                for (int waited = 0; waited < 10000; waited += 50)
                {
                    Thread.Sleep(50);
                    IntPtr window = FindWindowW("#32770", title); // the class of Windows' dialog boxes
                    if (window == IntPtr.Zero) continue;
                    Thread.Sleep(afterMs);
                    closed = PostMessageW(window, WmClose, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
            }) { IsBackground = true };
            thread.Start();
            return () =>
            {
                thread.Join();
                return closed;
            };
        }
    }
}
