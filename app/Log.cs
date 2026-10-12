using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace LiveBackground
{
    internal static class Log
    {
        public static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveBackground");

        private static readonly string File = Path.Combine(Folder, "log.txt");
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Folder);
                    var info = new FileInfo(File);
                    if (info.Exists && info.Length > 512 * 1024) info.Delete();
                    System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void Win32(string what) =>
            Write($"{what}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
    }
}
