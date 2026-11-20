# Lossless Dynamic (LDM) → Basic GPT Disk Converter (Windows 10/11)

> 🇬🇧 **English** | [🇷🇺 **Русская версия ниже**](#-русская-версия--перевод-динамического-диска-в-базовый-gpt-без-потери-данных)

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

## Usage (EN)

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

---

# 🇷🇺 Русская версия — Перевод Динамического диска в Базовый GPT без потери данных

**Перевод Динамического GPT-диска Windows (включая сложные 2-кусочные «вывернутые» тома, на которых сдаются платные утилиты!) обратно в стандартный Базовый GPT-диск — с `0` потерянных байт, полным побайтовым бэкапом секторов, 5 стоп-проверками и откатом в 1 клик.**

> **Проверено в реальном бою** на 2 ТБ GPT HDD с **1 388 957 файлами / 124 397 папками / 1 585 943 499 034 байтами (1.47 ТБ)**, включая виртуальный диск WSL2 `ext4.vhdx` на 30.66 ГБ — **100.000% побайтовое совпадение после конвертации (`0` байт потерь).**

---

## Почему платные программы (AOMEI, EaseUS, MiniTool) не справляются

Когда вы удаляете небольшой раздел *перед* (слева от) разделом NTFS и нажимаете **«Расширить том»** в Управлении дисками Windows, система переводит диск в **Динамический (LDM)** и приклеивает переднее пустое место к **самому концу** логического тома:

- **Кусок №1 (начало NTFS, `$Boot`, `$MFT`):** физически начинается *после* переднего отступа.
- **Кусок №2 (хвост NTFS):** физически лежит в *самом начале* диска (в обратном порядке!).

Стандартный Базовый раздел GPT обязан быть одним непрерывным отрезком секторов `[FirstLBA .. LastLBA]`, поэтому обычные конвертеры отказываются переводить диск без полного удаления всех данных.

## Как эта утилита решает проблему без потери единого файла

1. **Автоматический анализ экстентов Динамического GPT:** считывает точные физические границы `StartingAddress` / `EndingAddress` всех кусков LDM.
2. **Отцепление «хвостика» (если том из 2 кусков):** штатным сжатием Windows NTFS (`diskpart shrink`) сжимает том ровно на размер Куска №2 — сама Windows безопасно переносит задетые кластеры в свободное место внутри Куска №1 и полностью отцепляет Кусок №2.
3. **Проверка обоих загрузочных секторов NTFS (0 записей в область данных!):** проверяет, что в `StartLBA` лежит основной загрузочный сектор NTFS (`"NTFS    "`), а в `StartLBA + TotalSectors` — идентичный резервный загрузочный сектор NTFS, записанный драйвером `ntfs.sys`.
4. **Полный побайтовый бэкап и обновление таблицы GPT на месте:**
   - Сохраняет побайтовые дампы `Primary GPT (LBA 0..33)`, `1 МБ LDM Metadata`, `Backup GPT (последние 33 сектора)` и загрузочного сектора `NTFS` в папку `Backups\`.
   - Эксклюзивно блокирует и размонтирует том (`FSCTL_LOCK_VOLUME` + `FSCTL_DISMOUNT_VOLUME`).
   - Прописывает в `Primary + Backup GPT` стандартный раздел `Basic Data Partition` (`EBD0A0A2-B9E5-4433-87C0-68B6B72699C7`) с корректным `CRC32` через выровненный по странице `64 КБ` буфер `VirtualAlloc` (`FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH`), побайтово сверяет все `34 304 / 34 304 байта` GPT и очищает старую неразмеченную зону `LDM Metadata`.
5. **5 побайтовых стоп-проверок:** сверяет точное количество папок, файлов и байтов ДО сжатия, ПОСЛЕ сжатия (до изменения GPT) и ПОСЛЕ перевода в Базовый. Если не совпадёт хотя бы **1 байт**, скрипт сохраняет полный лог и встаёт на паузу с запросом подтверждения.

## Использование (RU)

1. Закройте все программы и окна Проводника, использующие целевой Динамический диск.
2. Запустите двойным кликом **`Convert_Dynamic_To_Basic.cmd`** (подтвердите запрос UAC от имени Администратора).
   - *Никаких подозрительных готовых `.exe`!* Скрипт сам компилирует `src\DynamicToBasicEngine.cs` и `src\FastDriveInventory.cs` локально встроенным компилятором Windows `.NET 4.x csc.exe`.
3. Выберите нужный Динамический GPT-том из найденного списка.
4. Дождитесь **`100%`** (в окне отображается шкала `0%..100%` и пульс каждые 5 секунд).
5. Если когда-либо понадобится вернуться обратно в Динамический: запустите двойным кликом **`Restore_Original_Dynamic.cmd`**.

### Требования и безопасность
- Windows 10 / 11 (x64), права Администратора.
- Целевой диск должен быть **несистемным Динамическим GPT-диском с NTFS** (из 1 или 2 кусков; системный диск `C:` заблокирован программно).
- Объём свободного места на целевом томе должен быть больше размера 2-го куска (если том состоит из 2 кусков).
- Сохраняйте папку `Backups\` до полной проверки переведённого Базового диска.
