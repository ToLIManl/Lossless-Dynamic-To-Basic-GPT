using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace FastDriveInventory
{
    internal static class Program
    {
        private static readonly HashSet<string> SkipRootDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System Volume Information",
            "$RECYCLE.BIN",
            "Config.Msi"
        };

        private static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            string root = (args.Length > 0) ? args[0] : @"D:\";
            var sw = Stopwatch.StartNew();

            long totalDirs = 0;
            long totalFiles = 0;
            long totalBytes = 0;

            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(root));

            while (stack.Count > 0)
            {
                DirectoryInfo cur = stack.Pop();
                totalDirs++;

                DirectoryInfo[] subDirs = null;
                try { subDirs = cur.GetDirectories(); } catch { }
                if (subDirs != null)
                {
                    bool isRoot = (cur.Parent == null);
                    for (int i = 0; i < subDirs.Length; i++)
                    {
                        DirectoryInfo sd = subDirs[i];
                        if (isRoot && SkipRootDirs.Contains(sd.Name)) continue;
                        try
                        {
                            if ((sd.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            stack.Push(sd);
                        }
                        catch { }
                    }
                }

                FileInfo[] files = null;
                try { files = cur.GetFiles(); } catch { }
                if (files != null)
                {
                    for (int i = 0; i < files.Length; i++)
                    {
                        totalFiles++;
                        try { totalBytes += files[i].Length; } catch { }
                    }
                }
            }
            sw.Stop();

            Console.WriteLine(string.Format("DIRS={0}|FILES={1}|BYTES={2}|SEC={3:N1}", totalDirs, totalFiles, totalBytes, sw.Elapsed.TotalSeconds));
        }
    }
}
