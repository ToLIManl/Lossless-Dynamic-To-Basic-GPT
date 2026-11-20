using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LosslessDynamicToBasic
{
    internal static class Program
    {
        private const uint GENERIC_READ  = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ  = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_NO_BUFFERING  = 0x20000000;
        private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

        private const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140;
        private const uint FSCTL_LOCK_VOLUME            = 0x00090018;
        private const uint FSCTL_DISMOUNT_VOLUME        = 0x00090020;
        private const int  MAX_IO_CHUNK_BYTES           = 65536; // 64 KB (128 sectors)

        private static readonly Guid GuidEmpty     = Guid.Empty;
        private static readonly Guid GuidLdmMeta   = new Guid("5808C8AA-7E8F-42E0-85D2-E1E90434CFB3");
        private static readonly Guid GuidLdmData   = new Guid("AF9B60A0-1431-4F62-BC68-3311714A69AD");
        private static readonly Guid GuidBasicData = new Guid("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");
        private static readonly Guid GuidMsr       = new Guid("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");
        private static readonly Guid GuidEfiSystem = new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFilePointerEx(SafeFileHandle hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle hFile, IntPtr lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle hFile, IntPtr lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushFileBuffers(SafeFileHandle hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice, uint dwIoControlCode,
            IntPtr lpInBuffer, uint nInBufferSize,
            IntPtr lpOutBuffer, uint nOutBufferSize,
            out uint lpBytesReturned, IntPtr lpOverlapped);

        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++)
                    c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                table[i] = c;
            }
            return table;
        }

        private static uint ComputeCrc32(byte[] buffer, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++)
                crc = (crc >> 8) ^ Crc32Table[(byte)((crc ^ buffer[offset + i]) & 0xFF)];
            return crc ^ 0xFFFFFFFFu;
        }

        private static byte[] RawReadSectors(SafeFileHandle h, ulong startLba, int sectorCount)
        {
            int totalBytes = sectorCount * 512;
            byte[] result = new byte[totalBytes];
            IntPtr alignedBuf = VirtualAlloc(IntPtr.Zero, (UIntPtr)MAX_IO_CHUNK_BYTES, 0x3000, 0x04);
            if (alignedBuf == IntPtr.Zero) throw new OutOfMemoryException("VirtualAlloc failed.");
            try
            {
                int offset = 0;
                while (offset < totalBytes)
                {
                    int chunk = Math.Min(MAX_IO_CHUNK_BYTES, totalBytes - offset);
                    long newPos;
                    if (!SetFilePointerEx(h, (long)(startLba * 512UL) + offset, out newPos, 0))
                        throw new IOException("SetFilePointerEx failed: Win32=" + Marshal.GetLastWin32Error());
                    uint read;
                    if (!ReadFile(h, alignedBuf, (uint)chunk, out read, IntPtr.Zero) || read != (uint)chunk)
                        throw new IOException(string.Format("ReadFile(LBA {0}+off {1}) failed: Win32={2}", startLba, offset, Marshal.GetLastWin32Error()));
                    Marshal.Copy(alignedBuf, result, offset, chunk);
                    offset += chunk;
                }
                return result;
            }
            finally { VirtualFree(alignedBuf, UIntPtr.Zero, 0x8000); }
        }

        private static void RawWriteSectors(SafeFileHandle h, ulong startLba, byte[] data)
        {
            if (data.Length % 512 != 0) throw new ArgumentException("Buffer length must be a multiple of 512.");
            IntPtr alignedBuf = VirtualAlloc(IntPtr.Zero, (UIntPtr)MAX_IO_CHUNK_BYTES, 0x3000, 0x04);
            if (alignedBuf == IntPtr.Zero) throw new OutOfMemoryException("VirtualAlloc failed.");
            try
            {
                int offset = 0;
                while (offset < data.Length)
                {
                    int chunk = Math.Min(MAX_IO_CHUNK_BYTES, data.Length - offset);
                    long newPos;
                    if (!SetFilePointerEx(h, (long)(startLba * 512UL) + offset, out newPos, 0))
                        throw new IOException("SetFilePointerEx failed: Win32=" + Marshal.GetLastWin32Error());
                    Marshal.Copy(data, offset, alignedBuf, chunk);
                    uint written;
                    if (!WriteFile(h, alignedBuf, (uint)chunk, out written, IntPtr.Zero) || written != (uint)chunk)
                        throw new IOException(string.Format("WriteFile(LBA {0}+off {1}) failed: Win32={2}", startLba, offset, Marshal.GetLastWin32Error()));
                    offset += chunk;
                }
            }
            finally { VirtualFree(alignedBuf, UIntPtr.Zero, 0x8000); }
        }

        private static SafeFileHandle LockAndDismountVolume(string driveLetter)
        {
            string volPath = @"\\.\" + driveLetter.TrimEnd('\\', ':') + ":";
            SafeFileHandle hVol = CreateFile(volPath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (hVol.IsInvalid)
                throw new IOException("CreateFile(" + volPath + ") failed: Win32=" + Marshal.GetLastWin32Error());

            uint bytesRet;
            bool locked = DeviceIoControl(hVol, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesRet, IntPtr.Zero);
            Console.WriteLine(locked
                ? "  [OK] Locked volume " + volPath + " exclusively."
                : "  [WARN] FSCTL_LOCK_VOLUME returned Win32=" + Marshal.GetLastWin32Error() + ", proceeding to dismount...");

            if (!DeviceIoControl(hVol, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesRet, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                hVol.Dispose();
                throw new IOException("FSCTL_DISMOUNT_VOLUME(" + volPath + ") failed: Win32=" + err + " (Close all apps using " + volPath + "!)");
            }
            Console.WriteLine("  [OK] Dismounted volume " + volPath + " cleanly (NTFS cache flushed).");
            return hVol;
        }

        private static void BuildBasicGptTables(
            byte[] primaryGpt34, byte[] backupGpt33,
            ulong basicFirstLba, ulong basicLastLba,
            out byte[] newPrimaryGpt34, out byte[] newBackupGpt33,
            out ulong ldmMetaFirstLba, out ulong ldmMetaLastLba)
        {
            newPrimaryGpt34 = (byte[])primaryGpt34.Clone();
            newBackupGpt33  = (byte[])backupGpt33.Clone();
            ldmMetaFirstLba = 0; ldmMetaLastLba = 0;

            if (Encoding.ASCII.GetString(newPrimaryGpt34, 512, 8) != "EFI PART")
                throw new InvalidDataException("Invalid Primary GPT signature.");
            if (Encoding.ASCII.GetString(newBackupGpt33, 32 * 512, 8) != "EFI PART")
                throw new InvalidDataException("Invalid Backup GPT signature.");

            uint priHdrSize = BitConverter.ToUInt32(newPrimaryGpt34, 512 + 12);
            uint bakHdrSize = BitConverter.ToUInt32(newBackupGpt33, 32 * 512 + 12);
            byte[] newEntries = new byte[128 * 128];
            int dstIdx = 0;
            bool removedMeta = false, convertedData = false;

            for (int i = 0; i < 128; i++)
            {
                int srcOff = 1024 + (i * 128);
                byte[] tBytes = new byte[16];
                Array.Copy(primaryGpt34, srcOff, tBytes, 0, 16);
                Guid tg = new Guid(tBytes);
                if (tg == GuidEmpty) continue;

                if (tg == GuidLdmMeta)
                {
                    ldmMetaFirstLba = BitConverter.ToUInt64(primaryGpt34, srcOff + 32);
                    ldmMetaLastLba  = BitConverter.ToUInt64(primaryGpt34, srcOff + 40);
                    removedMeta = true;
                    continue;
                }

                int dstOff = dstIdx * 128;
                Array.Copy(primaryGpt34, srcOff, newEntries, dstOff, 128);

                if (tg == GuidLdmData)
                {
                    GuidBasicData.ToByteArray().CopyTo(newEntries, dstOff);
                    BitConverter.GetBytes(basicFirstLba).CopyTo(newEntries, dstOff + 32);
                    BitConverter.GetBytes(basicLastLba).CopyTo(newEntries, dstOff + 40);
                    BitConverter.GetBytes(0UL).CopyTo(newEntries, dstOff + 48);
                    byte[] nameBytes = new byte[72];
                    byte[] rawName = Encoding.Unicode.GetBytes("Basic data partition");
                    Array.Copy(rawName, 0, nameBytes, 0, Math.Min(rawName.Length, 70));
                    Array.Copy(nameBytes, 0, newEntries, dstOff + 56, 72);
                    convertedData = true;
                }
                dstIdx++;
            }

            if (!removedMeta || !convertedData)
                throw new InvalidOperationException(string.Format("Expected GPT LDM Metadata + LDM Data entries (removedMeta={0}, convertedData={1}).", removedMeta, convertedData));

            uint entriesCrc = ComputeCrc32(newEntries, 0, newEntries.Length);
            Array.Copy(newEntries, 0, newPrimaryGpt34, 1024, newEntries.Length);
            Array.Copy(newEntries, 0, newBackupGpt33, 0, newEntries.Length);

            BitConverter.GetBytes(entriesCrc).CopyTo(newPrimaryGpt34, 512 + 88);
            BitConverter.GetBytes(0u).CopyTo(newPrimaryGpt34, 512 + 16);
            BitConverter.GetBytes(ComputeCrc32(newPrimaryGpt34, 512, (int)priHdrSize)).CopyTo(newPrimaryGpt34, 512 + 16);

            int bakHdrOff = 32 * 512;
            BitConverter.GetBytes(entriesCrc).CopyTo(newBackupGpt33, bakHdrOff + 88);
            BitConverter.GetBytes(0u).CopyTo(newBackupGpt33, bakHdrOff + 16);
            BitConverter.GetBytes(ComputeCrc32(newBackupGpt33, bakHdrOff, (int)bakHdrSize)).CopyTo(newBackupGpt33, bakHdrOff + 16);
        }

        private static int Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            if (args.Length < 4)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("  DynamicToBasicEngine.exe --backup  <DiskNum> <StartLBA> <BackupDir>");
                Console.WriteLine("  DynamicToBasicEngine.exe --verify  <DiskNum> <StartLBA> <BackupDir>");
                Console.WriteLine("  DynamicToBasicEngine.exe --convert <DiskNum> <DriveLetter> <StartLBA>");
                Console.WriteLine("  DynamicToBasicEngine.exe --restore <DiskNum> <DriveLetter> <BackupDir>");
                return 1;
            }

            try
            {
                string mode = args[0].ToLowerInvariant();
                int diskNum = int.Parse(args[1]);
                string devPath = string.Format(@"\\.\PhysicalDrive{0}", diskNum);

                if (mode == "--backup" || mode == "--verify")
                {
                    ulong startLba = ulong.Parse(args[2]);
                    string bkDir   = args[3];
                    Directory.CreateDirectory(bkDir);

                    using (SafeFileHandle h = CreateFile(devPath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING, IntPtr.Zero))
                    {
                        if (h.IsInvalid) throw new IOException("CreateFile(" + devPath + ") failed: Win32=" + Marshal.GetLastWin32Error());

                        byte[] priGpt = RawReadSectors(h, 0, 34);
                        if (Encoding.ASCII.GetString(priGpt, 512, 8) != "EFI PART")
                            throw new InvalidDataException("Disk #" + diskNum + " is not a GPT disk!");

                        ulong altLba = BitConverter.ToUInt64(priGpt, 512 + 32);
                        byte[] bakGpt = RawReadSectors(h, altLba - 32UL, 33);
                        byte[] boot   = RawReadSectors(h, startLba, 1);

                        string oem = Encoding.ASCII.GetString(boot, 3, 8);
                        ushort bps = BitConverter.ToUInt16(boot, 0x0B);
                        ulong totalSec = BitConverter.ToUInt64(boot, 0x28);
                        ulong serial   = BitConverter.ToUInt64(boot, 0x48);

                        Console.WriteLine(string.Format("  [NTFS START @ LBA {0}] OEM='{1}' | BPS={2} | TotalSectors={3} | Serial=0x{4:X8}",
                            startLba, oem, bps, totalSec, (uint)(serial & 0xFFFFFFFFUL)));

                        if (oem != "NTFS    " || bps != 512)
                            throw new InvalidDataException("No valid 512-byte NTFS boot sector at LBA " + startLba);

                        if (mode == "--verify")
                        {
                            ulong endLba = startLba + totalSec;
                            byte[] bakBoot = RawReadSectors(h, endLba, 1);
                            string bOem = Encoding.ASCII.GetString(bakBoot, 3, 8);
                            ulong bTot  = BitConverter.ToUInt64(bakBoot, 0x28);
                            ulong bSer  = BitConverter.ToUInt64(bakBoot, 0x48);
                            Console.WriteLine(string.Format("  [NTFS END   @ LBA {0}] OEM='{1}' | TotalSectors={2} | SerialMatch={3}",
                                endLba, bOem, bTot, (bSer == serial)));
                            if (bOem != "NTFS    " || bTot != totalSec || bSer != serial)
                            {
                                Console.WriteLine("  [ABORT - SAFE] NTFS Backup Boot Sector mismatch at LBA " + endLba + " (volume may still span >1 extent).");
                                return 10;
                            }
                        }

                        // Find LDM Metadata partition bounds for backup
                        ulong mStart = 34, mEnd = 2081;
                        for (int i = 0; i < 128; i++)
                        {
                            int off = 1024 + (i * 128);
                            byte[] tb = new byte[16];
                            Array.Copy(priGpt, off, tb, 0, 16);
                            if (new Guid(tb) == GuidLdmMeta)
                            {
                                mStart = BitConverter.ToUInt64(priGpt, off + 32);
                                mEnd   = BitConverter.ToUInt64(priGpt, off + 40);
                                break;
                            }
                        }
                        byte[] ldmMeta = RawReadSectors(h, mStart, (int)(mEnd - mStart + 1UL));
                        string prefix = (mode == "--verify") ? "post_shrink_" : "orig_";
                        File.WriteAllBytes(Path.Combine(bkDir, prefix + "primary_gpt_lba0_33.bin"), priGpt);
                        File.WriteAllBytes(Path.Combine(bkDir, prefix + "ldm_metadata.bin"), ldmMeta);
                        File.WriteAllBytes(Path.Combine(bkDir, prefix + "backup_gpt_last33.bin"), bakGpt);
                        File.WriteAllBytes(Path.Combine(bkDir, prefix + "ntfs_boot.bin"), boot);
                        File.WriteAllText(Path.Combine(bkDir, prefix + "meta_lba.txt"), mStart + "," + mEnd + "," + altLba);
                        Console.WriteLine("  [OK] Saved " + prefix + "* raw binary sector backups to " + bkDir);
                        return 0;
                    }
                }

                if (mode == "--convert")
                {
                    string driveLetter = args[2];
                    ulong startLba = ulong.Parse(args[3]);

                    using (SafeFileHandle hVol = LockAndDismountVolume(driveLetter))
                    using (SafeFileHandle hDisk = CreateFile(devPath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH, IntPtr.Zero))
                    {
                        if (hDisk.IsInvalid) throw new IOException("CreateFile(" + devPath + ") failed: Win32=" + Marshal.GetLastWin32Error());

                        byte[] boot = RawReadSectors(hDisk, startLba, 1);
                        if (Encoding.ASCII.GetString(boot, 3, 8) != "NTFS    " || BitConverter.ToUInt16(boot, 0x0B) != 512)
                            throw new InvalidDataException("Invalid NTFS boot sector at LBA " + startLba);

                        ulong totalSec = BitConverter.ToUInt64(boot, 0x28);
                        ulong serial   = BitConverter.ToUInt64(boot, 0x48);
                        ulong endLba   = startLba + totalSec;

                        byte[] bakBoot = RawReadSectors(hDisk, endLba, 1);
                        if (Encoding.ASCII.GetString(bakBoot, 3, 8) != "NTFS    " ||
                            BitConverter.ToUInt64(bakBoot, 0x28) != totalSec ||
                            BitConverter.ToUInt64(bakBoot, 0x48) != serial)
                            throw new InvalidDataException("SAFETY ABORT: NTFS Backup Boot Sector at LBA " + endLba + " does not match StartLBA!");

                        Console.WriteLine(string.Format("  [1/4] NTFS Primary (LBA {0}) & Backup Boot (LBA {1}) verified 100% — zero NTFS writes needed!", startLba, endLba));

                        byte[] priGpt = RawReadSectors(hDisk, 0, 34);
                        ulong altLba  = BitConverter.ToUInt64(priGpt, 512 + 32);
                        byte[] bakGpt = RawReadSectors(hDisk, altLba - 32UL, 33);

                        byte[] newPri, newBak;
                        ulong metaStart, metaEnd;
                        BuildBasicGptTables(priGpt, bakGpt, startLba, endLba, out newPri, out newBak, out metaStart, out metaEnd);

                        Console.WriteLine("  [2/4] Writing Basic Primary GPT (LBA 0..33) & Backup GPT...");
                        RawWriteSectors(hDisk, 0, newPri);
                        RawWriteSectors(hDisk, altLba - 32UL, newBak);
                        FlushFileBuffers(hDisk);

                        Console.WriteLine("  [3/4] Byte-for-byte read-back check (34,304 / 34,304 GPT bytes)...");
                        byte[] vPri = RawReadSectors(hDisk, 0, 34);
                        byte[] vBak = RawReadSectors(hDisk, altLba - 32UL, 33);
                        for (int i = 0; i < newPri.Length; i++) if (vPri[i] != newPri[i]) throw new InvalidDataException("Primary GPT mismatch at byte " + i);
                        for (int i = 0; i < newBak.Length; i++) if (vBak[i] != newBak[i]) throw new InvalidDataException("Backup GPT mismatch at byte " + i);

                        uint bytesRet;
                        DeviceIoControl(hDisk, IOCTL_DISK_UPDATE_PROPERTIES, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesRet, IntPtr.Zero);
                        Thread.Sleep(250);

                        Console.WriteLine(string.Format("  [4/4] Clearing unallocated old LDM Metadata area LBA {0}..{1}...", metaStart, metaEnd));
                        try
                        {
                            byte[] zeroLdm = new byte[(int)(metaEnd - metaStart + 1UL) * 512];
                            RawWriteSectors(hDisk, metaStart, zeroLdm);
                            FlushFileBuffers(hDisk);
                            Console.WriteLine("  [OK] Old LDM Metadata area cleared cleanly.");
                        }
                        catch (Exception exWipe)
                        {
                            Console.WriteLine("  [NOTE] Unallocated LDM wipe skipped (" + exWipe.Message + ") — GPT is already 100% Basic!");
                        }

                        Console.WriteLine("  [SUCCESS] Converted Disk #" + diskNum + " to Basic GPT (34,304 / 34,304 bytes verified)!");
                        return 0;
                    }
                }

                if (mode == "--restore")
                {
                    string driveLetter = args[2];
                    string bkDir       = args[3];
                    string prefix = File.Exists(Path.Combine(bkDir, "post_shrink_primary_gpt_lba0_33.bin")) ? "post_shrink_" : "orig_";

                    byte[] pri = File.ReadAllBytes(Path.Combine(bkDir, prefix + "primary_gpt_lba0_33.bin"));
                    byte[] ldm = File.ReadAllBytes(Path.Combine(bkDir, prefix + "ldm_metadata.bin"));
                    byte[] bak = File.ReadAllBytes(Path.Combine(bkDir, prefix + "backup_gpt_last33.bin"));
                    string[] metaParts = File.ReadAllText(Path.Combine(bkDir, prefix + "meta_lba.txt")).Trim().Split(',');
                    ulong mStart = ulong.Parse(metaParts[0]);
                    ulong altLba = ulong.Parse(metaParts[2]);

                    SafeFileHandle hVol = null;
                    try
                    {
                        string root = driveLetter.TrimEnd('\\', ':') + @":\";
                        if (Directory.Exists(root)) hVol = LockAndDismountVolume(driveLetter);

                        using (SafeFileHandle hDisk = CreateFile(devPath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH, IntPtr.Zero))
                        {
                            if (hDisk.IsInvalid) throw new IOException("CreateFile(" + devPath + ") failed: Win32=" + Marshal.GetLastWin32Error());
                            RawWriteSectors(hDisk, mStart, ldm);
                            RawWriteSectors(hDisk, 0, pri);
                            RawWriteSectors(hDisk, altLba - 32UL, bak);
                            FlushFileBuffers(hDisk);
                            uint bytesRet;
                            DeviceIoControl(hDisk, IOCTL_DISK_UPDATE_PROPERTIES, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesRet, IntPtr.Zero);
                            Console.WriteLine("  [OK] Restored Dynamic LDM GPT & Metadata on Disk #" + diskNum);
                            return 0;
                        }
                    }
                    finally { if (hVol != null) hVol.Dispose(); }
                }

                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FATAL] " + ex.Message);
                return 2;
            }
        }
    }
}
