# Lossless Dynamic (LDM) → Basic GPT Disk Converter (Windows 10/11)

**Language:** [🇬🇧 English](#-english-version) | [🇷🇺 Русский](#-русская-версия)

---

## 🇬🇧 English Version

**Convert a Windows Dynamic (LDM) GPT disk — including complex non-contiguous ("reverse-wrapped") 2-extent volumes where commercial tools fail — back into a standard Basic GPT disk without data loss, backed by raw sector dumps, 5 byte-level verification checkpoints, and a 1-click rollback.**

> **Verified in production** on a 2 TB GPT HDD containing **1,388,957 files / 124,397 folders / 1,585,943,499,034 bytes (1.47 TB)** including a 30.66 GB WSL2 `ext4.vhdx` virtual disk — **100.000% byte-for-byte identical after conversion (`0` bytes lost).**

### Why Commercial Partition Tools (AOMEI, EaseUS, MiniTool) Fail

When you delete a small partition *to the left* (at lower LBAs) of an NTFS volume and click **Extend Volume** in Windows Disk Management, Windows converts the disk to **Dynamic (LDM)** and appends that leading free space to the **logical end** of the volume:

- **Extent #1 (NTFS start, `$Boot`, `$MFT`):** physically starts *after* the leading gap.
- **Extent #2 (NTFS tail):** physically resides at the *beginning* of the disk (out of linear order).

Because a standard Basic GPT partition must occupy a single contiguous LBA range `[FirstLBA .. LastLBA]`, third-party partition managers refuse to convert such volumes without wiping and recreating the entire partition.

### How This Tool Solves It Without Losing a Single Byte

1. **Automatic Extent Mapping:** Queries exact physical `StartingAddress` and `EndingAddress` offsets for every LDM extent backing the target volume.
2. **Safe Tail Detachment (for 2-Extent Volumes):** Shrinks the NTFS volume via native Windows `diskpart shrink` by the exact size of `Extent #2`. Windows `ntfs.sys` transparently relocates any files occupying the tail into free space inside `Extent #1` and drops `Extent #2`, leaving one contiguous NTFS volume.
3. **Dual Boot-Sector Boundary Proof (Zero Writes to NTFS Data):** Verifies that `StartLBA` contains the primary NTFS boot sector (`"NTFS    "`) and `StartLBA + TotalSectors` contains the matching backup NTFS boot sector updated by `ntfs.sys`.
4. **Raw Sector Backups & In-Place GPT Rewrite:**
   - Saves exact binary dumps of `Primary GPT (LBA 0..33)`, `1 MB LDM Metadata`, `Backup GPT (last 33 sectors)`, and `NTFS Boot Sector` inside `Backups\`.
   - Exclusively locks and dismounts the volume (`FSCTL_LOCK_VOLUME` + `FSCTL_DISMOUNT_VOLUME`) to flush all filesystem caches.
   - Rewrites both `Primary` and `Backup` GPT tables with a standard `Basic Data Partition` (`EBD0A0A2-B9E5-4433-87C0-68B6B72699C7`) and valid IEEE `CRC32` checksums using page-aligned 64 KB unbuffered I/O (`VirtualAlloc`, `FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH`), reads back and verifies all `34,304 / 34,304` GPT bytes, and wipes the now-unallocated `LDM Metadata` sectors.
5. **5 Byte-Level Checkpoints:** Computes exact folder, file, and byte totals before shrinking, after shrinking (prior to any GPT modification), and after Basic conversion. Any discrepancy immediately flushes the diagnostic log and pauses for confirmation.

### Usage (EN)

1. Close all applications and Explorer windows accessing the target Dynamic drive.
2. Double-click **`Convert_Dynamic_To_Basic.cmd`** and accept the UAC Administrator prompt.
   - *No precompiled third-party binaries:* the launcher compiles `src\DynamicToBasicEngine.cs` and `src\FastDriveInventory.cs` locally using Windows' built-in `.NET 4.x csc.exe`.
3. Select the target Dynamic GPT volume from the detected list.
4. Wait for **`100%`** completion (tracked via a live progress bar and a 5-second heartbeat).
5. To revert back to Dynamic LDM at any time, double-click **`Restore_Original_Dynamic.cmd`**.

### Requirements & Safety Notes
- Windows 10 / 11 (x64) with Administrator privileges.
- Target must be a **non-system NTFS Dynamic GPT disk** (1-extent or 2-extent; the OS boot drive `C:` is strictly excluded by design).
- Free space on the target volume must exceed the size of the 2nd extent (on 2-extent volumes).
- Keep the generated `Backups\` directory until you have verified full access to your converted Basic disk.

---

## 🇷🇺 Русская версия

**Конвертер для перевода динамического диска GPT (Windows LDM) обратно в стандартный базовый диск GPT без потери данных — работает даже со сложными «разорванными» томами из двух экстентов, на которых пасуют платные программы. Включает посекторный бэкап, 5 побайтовых проверок целостности и откат в один клик.**

> **Проверено на реальном диске** — 2 ТБ GPT HDD (**1 388 957 файлов / 124 397 папок / 1 585 943 499 034 байт / 1.47 ТБ**), включая виртуальный диск WSL2 `ext4.vhdx` объёмом 30.66 ГБ: **100.000% побайтовое совпадение после конвертации (`0` байт потерь).**

### Почему обычные программы (AOMEI, EaseUS, MiniTool) не могут перевести такой диск

Если удалить небольшой раздел *слева* (ближе к началу диска) от основного тома NTFS и нажать **«Расширить том»** в «Управлении дисками» Windows, система переведёт диск в **динамический (LDM)** и присоединит освободившееся пространство в начало диска как *логический хвост* тома:

- **Экстент #1 (начало тома NTFS, загрузочный сектор, `$MFT`):** физически начинается *после* пустого участка в начале диска.
- **Экстент #2 (хвост тома NTFS):** физически расположен *в самом начале* диска (в обратном порядке адресов LBA).

По стандарту обычный базовый раздел GPT обязан представлять собой единый непрерывный диапазон секторов `[FirstLBA .. LastLBA]`, поэтому сторонние утилиты отказываются конвертировать такой диск без полного форматирования и удаления всех данных.

### Как эта утилита решает проблему без потери единого байта

1. **Автоматическое определение геометрии экстентов:** считывает точные физические границы (`StartingAddress` и `EndingAddress`) всех фрагментов LDM, образующих целевой том.
2. **Безопасное отсоединение второго экстента (если том состоит из 2 частей):** запускает штатное сжатие тома Windows (`diskpart shrink`) ровно на размер `Экстента #2`. Драйвер `ntfs.sys` сам переносит все файлы из отрезаемой области в свободное пространство `Экстента #1`, после чего `Экстент #2` исчезает и том становится единым непрерывным участком.
3. **Проверка границ NTFS без записи в область пользовательских данных:** убеждается, что в секторе `StartLBA` находится основной загрузочный сектор NTFS (`"NTFS    "`), а в секторе `StartLBA + TotalSectors` — его точная резервная копия, обновлённая драйвером `ntfs.sys`.
4. **Посекторное резервное копирование и обновление таблицы разделов GPT:**
   - Перед любыми изменениями сохраняет в папку `Backups\` точные бинарные дампы: `Primary GPT (LBA 0..33)`, `1 МБ метаданных LDM`, `Backup GPT (последние 33 сектора диска)` и загрузочный сектор `NTFS`.
   - Эксклюзивно блокирует и размонтирует том (`FSCTL_LOCK_VOLUME` + `FSCTL_DISMOUNT_VOLUME`) со сбросом всех кэшей на диск.
   - Перезаписывает основную и резервную таблицы GPT, задавая стандартный тип раздела `Basic Data Partition` (`EBD0A0A2-B9E5-4433-87C0-68B6B72699C7`) и пересчитанные контрольные суммы `CRC32` (прямая небуферизированная запись блоками по 64 КБ через `VirtualAlloc`, `FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH`), выполняет побайтовую сверку всех `34 304 / 34 304` байт таблиц GPT и очищает уже неиспользуемую область старых метаданных LDM.
5. **5 побайтовых контрольных точек:** подсчитывает точное число папок, файлов и байтов до сжатия, после сжатия (до изменения GPT) и после перевода диска в базовый. При любом расхождении программа сохраняет подробный отчёт в лог и останавливается для подтверждения пользователем.

### Как пользоваться (RU)

1. Закройте все программы и окна Проводника, обращающиеся к целевому динамическому диску.
2. Запустите двойным кликом **`Convert_Dynamic_To_Basic.cmd`** и подтвердите запуск от имени Администратора (UAC).
   - *Никаких готовых неизвестных `.exe` в архиве нет:* скрипт компилирует `src\DynamicToBasicEngine.cs` и `src\FastDriveInventory.cs` прямо у вас на ПК с помощью встроенного в Windows компилятора `.NET 4.x csc.exe`.
3. Выберите нужный динамический том GPT из предложенного списка (введя его номер).
4. Дождитесь завершения до **`100%`** (в консоли отображаются шкала прогресса и статус каждые 5 секунд).
5. При необходимости вернуть диск обратно в исходное динамическое состояние в любой момент запустите **`Restore_Original_Dynamic.cmd`**.

### Требования и меры предосторожности
- Windows 10 / 11 (x64), права Администратора.
- Целевой том должен находиться на **несистемном динамическом GPT-диске с файловой системой NTFS** (из 1 или 2 экстентов; системный диск с Windows заблокирован программно).
- На томе должно быть достаточно свободного места — больше размера второго (отсоединяемого) экстента.
- Не удаляйте папку `Backups\`, пока полностью не убедитесь в корректной работе базового диска.
