# Dual Deck Commander

[Magyar](README.hu.md) | **English**

**A free, dual-panel file manager for Windows – built for emulator fans and disc-image collectors.**

Classic two-panel, keyboard-driven file management in the spirit of Total Commander, with
native support for the formats retro and console enthusiasts actually use: Xbox / Xbox 360
ISOs (including **ISO → Games on Demand conversion**), Amiga ADF / DMS / HDF disk images and
LHA / LZX archives, plus ZIP, RAR and 7-Zip.

![Dual Deck Commander – main window, dark theme](docs/screenshots/main-dark.png)

**[⬇ Download the latest version](https://github.com/derapoka/DualDeckCommander/releases/latest)**
– or install with winget:

```
winget install BaranyiLaszlo.DualDeckCommander
```

Windows 10 / 11, 64-bit. No .NET installation needed. Freeware.

> The user interface is currently in **Hungarian**. An English UI is planned.

---

## Highlights

### Xbox / Xbox 360 disc images
- **Browse ISOs like folders** – XGD1, XGD2, XGD3 and XISO (trimmed) images. View files (F3),
  extract anything (F5), see Title ID, Media ID and layout (Alt+Enter).
- **Convert ISO → Games on Demand (GoD)** (Ctrl+G) – batch conversion, game title pre-filled
  from a built-in database of ~4,900 titles, optional trimming of unused space, free-space
  check, clean-up on cancel. Works for original Xbox titles too. Output is byte-identical to
  [iso2god-rs](https://github.com/iliazeus/iso2god-rs).
- **Mount / unmount** any ISO as a drive with one keypress (Windows' built-in mounting).

![Xbox 360 ISO browsing](docs/screenshots/xbox-iso.png)
![GoD conversion dialog](docs/screenshots/god-convert.png)

### Amiga
- **ADF** (DD/HD), **ADZ**, **DMS** (all compression modes: NONE, SIMPLE, QUICK, MEDIUM, DEEP,
  HEAVY1, HEAVY2 – DMS → ADF with a single F5), **HDF** hard-disk images with RDB partitions.
- OFS, FFS, INTL and DIRCACHE file systems.
- **LHA / LZH** (-lh0- … -lh7-, header levels 0–2) and **Amiga LZX** (including merged groups)
  with their own decoders, CRC-checked.

![Amiga ADF browsing](docs/screenshots/amiga-adf.png)

### Archives
ZIP (including old DOS code-page names), RAR 1.5 – 5.x, 7-Zip, LHA, LZX – browse inside like a
folder, view, extract selected files or whole archives (several at once, each into its own folder).

### Built-in image viewer
Just the picture, no clutter: page through all images of a folder (or archive) with the arrow
keys or mouse wheel, zoom, rotate, full screen. JPEG/JFIF, PNG, BMP, GIF (animated), TIFF, ICO,
TGA, plus WebP, HEIC and AVIF via Windows codecs. Respects EXIF orientation.

![Image viewer](docs/screenshots/image-viewer.png)

### Everyday file management
- Two panels with **tabs** (Ctrl+T / Ctrl+W / Ctrl+Tab) and **favourite folders** (Ctrl+D, 1–9 hotkeys)
- **Dark and light themes** (Ctrl+Shift+T), drive bar with usage shown under the selected drive
- Familiar keys: F3 view, F4 edit, F5 copy, F6 move, F7 new folder, F8 delete (Recycle Bin),
  Shift+Del permanent delete
- **Right-click selection** like Total Commander (long press for the context menu);
  marked files shown in red
- Overwrite / error handling (Retry, Skip, All…), progress with cancel
- Search by name and content (Alt+F7), command line under the panels
- Clipboard and drag & drop with Explorer, auto refresh, hidden files (Ctrl+H)
- Settings dialog (Ctrl+O), full keyboard reference in the app (F1)

![Settings](docs/screenshots/settings.png)

---

## Installation

1. Download `DualDeckCommander-x.y.z-telepito.exe` from
   [Releases](https://github.com/derapoka/DualDeckCommander/releases/latest)
   (*telepítő* = installer).
2. Run it. You can choose the install folder and drive, a desktop icon and Start-menu folder, and
   optionally associate Amiga formats (.adf .adz .dms .hdf .lha .lzh .lzx).
3. It appears under *Settings → Apps* and uninstalls cleanly.

Silent install: `DualDeckCommander-x.y.z-telepito.exe /S /D=D:\Apps\Dual Deck Commander`

> **"Windows protected your PC"?** The installer is not code-signed yet, so SmartScreen may
> warn about an unknown publisher. Click *More info → Run anyway*. Each release's
> VirusTotal report is linked in the release notes.

## Building from source

Requirements: Windows, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

- **Program only:** run `build.bat` → `publish\DualDeckCommander.exe`
- **Installer:** run `Telepito-keszitese.bat` – installs the .NET 8 SDK and NSIS with winget if
  missing, builds a self-contained single-file exe and the installer into `dist\`.
- **CI:** every push builds the installer via GitHub Actions. To publish a release, bump
  `<Version>` in the csproj and `VERSION` in the .nsi, then run *Actions → release → Run workflow*.

```bat
cd src
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ..\publish
```

| Source | Contents |
|---|---|
| `MainForm.cs`, `FilePanel.cs`, `TabBar.cs` | main window, panels, tabs |
| `VirtualFs.cs` | common layer for disc images and archives |
| `XboxIso.cs`, `GodConverter.cs`, `GodForm.cs` | Xbox XDVDFS/GDF reader, GoD conversion |
| `AmigaDisk.cs`, `Lha.cs`, `Lzx.cs` | ADF/ADZ/HDF/DMS, LHA and LZX decoders |
| `Archives.cs`, `SharpArchive.cs` | ZIP; RAR and 7-Zip (SharpCompress) |
| `ImageViewerForm.cs`, `ImageLoader.cs` | image viewer (GDI+, WIC, own TGA decoder) |
| `installer\` | NSIS installer script |

## Legal

Dual Deck Commander does not contain, download or circumvent protection on any game or
copyrighted content. You are solely responsible for having the rights to the files and disc
images you work with, and for complying with applicable copyright law.

## Licence and credits

**Freeware** – © László Baranyi. See [LICENSE.txt](LICENSE.txt).

- [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT) – RAR and 7-Zip
- [iso2god-rs](https://github.com/iliazeus/iso2god-rs) by Ilia Pozdnyakov (MIT) – GoD header template and title database
- DMS decoder based on Teemu Suutari's [ancient](https://github.com/temisu/ancient) (BSD)
- LHA decoder follows Haruhiko Okumura's ar002 / LZHUF; LZX decoder follows unlzx.c (Erik Meusel, Dan Fraser)

## Contact

Bug reports and ideas: [Issues](https://github.com/derapoka/DualDeckCommander/issues).
