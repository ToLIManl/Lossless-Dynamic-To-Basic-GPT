# Lossless Dynamic (LDM) → Basic GPT Disk Converter (Windows 10/11)

**Convert a Windows Dynamic GPT Disk (including 2-extent reverse-wrapped volumes where commercial tools fail!) back to a standard Basic GPT Disk — with 0 bytes lost, full raw sector backups, 5 byte-level verification checkpoints, and 1-click rollback.**

> **Tested in production** on a 2 TB GPT HDD containing **1,388,957 files / 124,397 folders / 1,585,943,499,034 bytes (1.47 TB)** including a 30.66 GB WSL2 `ext4.vhdx` virtual disk — **100.000% byte-for-byte identical after conversion (`0` bytes lost).**

---

## Why Commercial Tools (AOMEI, EaseUS, MiniTool) Fail

When you delete a small partition *before* (to the left of) an NTFS partition and click **Extend Volume** in Windows Disk Management, Windows converts the disk to **Dynamic (LDM)** and stitches the front free space onto the **tail end** of the logical volume:

- **Extent #1 (Start of NTFS, `$Boot`, `$MFT`):** physically starts *after* the front gap.
- **Extent #2 (Tail of NTFS):** physically sits at the *beginning* of the disk (reverse-wrapped!).

A Basic GPT partition must be a single contiguous LBA range `[FirstLBA .. LastLBA]`, so standard converters refuse to convert without wiping the entire disk.

## How This Tool Solves It Without Losing a Single File

1. **Auto-Detects Dynamic GPT Extents:** Reads exact physical `StartingAddress` / `EndingAddress` of all LDM extents.
2. **Detaches the Wrapped Tail Extent (if 2 extents):** Uses Windows' native NTFS `diskpart shrink` to shrink the volume by the exact size of `Extent #2` — Windows safely relocates any clusters from the tail into free space inside `Extent #1` and drops `Extent #2`.
3. **Verifies Both NTFS Boot Sectors (Zero Writes to NTFS Data!):** Verifies that `StartLBA` holds the primary NTFS Boot Sector (`"NTFS    "`) and `StartLBA + TotalSectors` holds the matching NTFS Backup Boot Sector written by `ntfs.sys`.
4. **Full Raw Sector Backup & In-Place GPT Update:**
   - Saves bit-for-bit backups of `Primary GPT (LBA 0..33)`, `1 MB LDM Metadata`, `Backup GPT (last 33 sectors)`, and `NTFS Boot Sector` in `Backups\`.
   - Locks & dismounts the volume (`FSCTL_LOCK_VOLUME` + `FSCTL_DISMOUNT_VOLUME`).
   - Rewrites `Primary + Backup GPT` with standard `Basic Data Partition` (`EBD0A0A2-B9E5-4433-87C0-68B6B72699C7`) and valid IEEE `CRC32` using page-aligned 64 KB `VirtualAlloc` unbuffered I/O (`FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH`), verifies all `34,304 / 34,304` GPT bytes read back identically, then clears the unallocated old `LDM Metadata` area.
5. **5 Byte-Level Checkpoints:** Counts exact folders, files, and bytes BEFORE shrink, AFTER shrink (before touching GPT), and AFTER Basic conversion. If even **1 byte** differs, it saves the full log and pauses before proceeding.

---

## Usage / Использование (RU)

1. Close any programs/Explorer windows using the target Dynamic drive.
2. Double-click **`Convert_Dynamic_To_Basic.cmd`** (confirm UAC Administrator prompt).
   - *No prebuilt untrusted `.exe` required!* The script compiles `src\DynamicToBasicEngine.cs` and `src\FastDriveInventory.cs` locally using built-in Windows `.NET 4.x csc.exe`.
3. Select the Dynamic GPT volume from the detected list.
4. Wait for **`100%`** (with live `0%..100%` progress bar and 5-second heartbeat).
5. If you ever want to revert to Dynamic: double-click **`Restore_Original_Dynamic.cmd`**.

### Requirements & Safety Notes
- Windows 10 / 11 (x64), Administrator privileges.
- Target must be a **non-system NTFS Dynamic GPT disk** (1-extent or 2-extent; OS boot drive `C:` is blocked by design).
- Free space on the target volume must exceed the size of the 2nd extent (if 2-extent).
- Always keep your `Backups\` folder until you have verified your converted Basic disk.
