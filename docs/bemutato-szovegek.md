# Bemutató szövegek fórumokra és letöltőoldalakra

A linkek a https://github.com/derapoka/DualDeckCommander tárolóra mutatnak. Minden poszthoz tegyél 2–3 képernyőképet
(főablak sötét témában, és a közösségnek megfelelő funkció: Xbox ISO / GoD ablak, illetve ADF böngészés).

Pár szabály, hogy ne töröljék a posztot:
- Redditen előbb olvasd el a subreddit szabályait (sok helyen csak bizonyos napokon vagy külön
  szálban lehet saját programot hirdetni). Írj alá egyértelműen: „I made this”.
- Fórumokon a megfelelő alfórumba posztolj (pl. EAB: *Amiga Software* vagy *Coders. General*,
  Prohardver/LOGOUT: a program saját témája vagy a „Saját fejlesztésű programok” jellegű topik).
- Válaszolj a kérdésekre az első napokban – ez többet hoz, mint maga a poszt.
- Soha ne linkelj vagy említs játék-lemezképet letöltésre.

---

## 1. Magyar – Prohardver / LOGOUT / Facebook-csoportok

**Cím:** Dual Deck Commander – ingyenes kétpaneles fájlkezelő emulátorosoknak (Xbox ISO, GoD, Amiga ADF/DMS)

Sziasztok!

Elkészült egy saját fejlesztésű, ingyenes fájlkezelőm Windowsra, a **Dual Deck Commander**.
A Total Commander kétpaneles, billentyűzetes kezelését követi, de azokra a formátumokra van
kihegyezve, amikkel emulátoros és retró gépes körökben nap mint nap dolgozunk:

- **Xbox / Xbox 360 ISO** böngészése mappaként (XGD1/2/3, XISO), fájlok kibontása, Title ID / Media ID
- **ISO → Games on Demand (GoD) átalakítás** egy gombnyomásra (Ctrl+G), akár több ISO egyszerre,
  a játék neve beépített adatbázisból (~4900 cím)
- **Amiga ADF, ADZ, DMS, HDF** lemezképek böngészése – DMS-ből egy F5-tel ADF
- **LHA, LZX, ZIP, RAR, 7-Zip** archívumok böngészése és kibontása
- Fülek, kedvenc mappák, sötét téma, jobb egérgombos kijelölés, beépített képnézegető
- Teljesen magyar felület, rendes telepítő, ingyenes

Letöltés: https://github.com/derapoka/DualDeckCommander/releases/latest
vagy: `winget install BaranyiLaszlo.DualDeckCommander`

A telepítő még nincs digitálisan aláírva, ezért a Windows SmartScreen figyelmeztethet –
„További információ → Futtatás mindenképp”. A VirusTotal-jelentés a letöltési oldalon van.

Hibajelentést, ötletet szívesen fogadok itt vagy a GitHubon!

---

## 2. English – Xbox scene (ConsoleMods forum, r/360hacks)

**Title:** I made a free file manager that browses Xbox 360 ISOs and converts them to GoD – Dual Deck Commander

Hi all,

I've built a free dual-panel file manager for Windows, **Dual Deck Commander**, aimed at people
who juggle disc images. The Xbox side:

- **Browse Xbox / Xbox 360 ISOs like folders** – XGD1, XGD2, XGD3 and XISO. View or extract any
  file, check Title ID / Media ID with Alt+Enter.
- **ISO → Games on Demand conversion** built in (Ctrl+G): batch mode, game name pre-filled from a
  ~4,900-title database, optional trimming, free-space check, cleans up if you cancel.
  Original Xbox titles work too. Output is byte-identical to iso2god-rs (credited, MIT).
- **Mount / unmount** an ISO as a drive with one key.

It's also a full Total Commander-style file manager (tabs, favourites, dark theme, F5/F6 copy/move…)
and handles ZIP/RAR/7z plus Amiga formats.

Download: https://github.com/derapoka/DualDeckCommander/releases/latest
or `winget install BaranyiLaszlo.DualDeckCommander`

Note: the UI is in Hungarian for now (English is planned – the keys are the usual F3/F5/F6/F8
and Ctrl+G for GoD). The installer isn't code-signed yet, so SmartScreen may warn; the VirusTotal
report is linked on the release page.

Feedback and bug reports very welcome!

---

## 3. English – Amiga (English Amiga Board, Lemon Amiga)

**Title:** Dual Deck Commander – free Windows file manager with native ADF/DMS/HDF and LHA/LZX support

Hi everyone,

I wrote a free dual-panel file manager for Windows that treats Amiga files as first-class citizens:

- **ADF** (DD/HD) and **ADZ** – browse OFS/FFS/INTL/DIRCACHE disks like folders, view and extract files
- **DMS** – all compression modes (NONE, SIMPLE, QUICK, MEDIUM, DEEP, HEAVY1, HEAVY2);
  the unpacked .adf appears in the root, so DMS → ADF is a single F5
- **HDF** hard-disk images, including RDB partitions (each partition shows as its own folder)
- **LHA / LZH** (-lh0- … -lh7-) and **LZX** (including merged groups), CRC-checked, own decoders
- optional file associations for .adf .adz .dms .hdf .lha .lzh .lzx

Plus the usual commander features (two panels, tabs, favourites, dark theme) and ZIP/RAR/7z.
The DMS decoder is based on Teemu Suutari's *ancient* – thanks!

Not supported (yet): PFS3/SFS partitions, non-DOS custom-format game disks, encrypted DMS.

Download: https://github.com/derapoka/DualDeckCommander/releases/latest

The UI is in Hungarian for now; English is planned. I'd love to hear about any ADF/DMS/LHA/LZX
file that doesn't open correctly – it helps me improve the decoders.

---

## 4. English – r/emulation (general)

**Title:** Dual Deck Commander: a free Total Commander-style file manager for disc images and retro archives

I've released a free dual-panel file manager for Windows focused on the formats emulation folks use:
Xbox/Xbox 360 ISOs (browse + Games on Demand conversion), Amiga ADF/ADZ/DMS/HDF, LHA/LZX,
ZIP/RAR/7z – all browsable like folders, with extract-to-other-panel on F5. Also tabs, favourites,
dark theme and a clutter-free image viewer for box art and screenshots.

Download and screenshots: https://github.com/derapoka/DualDeckCommander

UI is Hungarian for now, English coming. Freeware, feedback welcome!

---

## 5. Letöltőoldalak (Softpedia, MajorGeeks, FileHorse, Uptodown, magyar oldalak)

**Program name:** Dual Deck Commander
**Version:** 0.6.0  **License:** Freeware  **OS:** Windows 10 / 11 (64-bit)
**Category:** File Managers
**Download URL:** https://github.com/derapoka/DualDeckCommander/releases/latest
**Homepage:** https://github.com/derapoka/DualDeckCommander

**Short description (EN, ~150 characters):**
Free dual-panel file manager for disc images and retro archives: Xbox ISO browsing and GoD conversion, Amiga ADF/DMS/HDF, LHA/LZX, ZIP/RAR/7z.

**Long description (EN):**
Dual Deck Commander is a free, keyboard-driven two-panel file manager in the spirit of Total Commander,
built for emulator fans and disc-image collectors. It browses Xbox and Xbox 360 ISO images (XGD1/2/3,
XISO) like folders and converts them to Games on Demand format in batches. It opens Amiga ADF, ADZ,
DMS and HDF disk images, and browses and extracts ZIP, RAR, 7-Zip, LHA and Amiga LZX archives.
Tabs, favourite folders, dark and light themes, right-click selection, search, and a built-in image
viewer (JPEG, PNG, GIF, TIFF, TGA, WebP, HEIC) round it out. The interface is in Hungarian.

**Rövid leírás (HU):**
Ingyenes kétpaneles fájlkezelő lemezképekhez és retró archívumokhoz: Xbox ISO böngészés és GoD átalakítás, Amiga ADF/DMS/HDF, LHA/LZX, ZIP/RAR/7z.

**Hosszú leírás (HU):**
A Dual Deck Commander ingyenes, billentyűzetről kezelhető kétpaneles fájlkezelő a Total Commander
mintájára, emulátoros és retró rajongóknak. Mappaként böngészi az Xbox és Xbox 360 ISO lemezképeket,
és akár egyszerre többet is Games on Demand formátumra alakít. Megnyitja az Amiga ADF, ADZ, DMS és HDF
lemezképeket, valamint böngészi és kibontja a ZIP, RAR, 7-Zip, LHA és Amiga LZX archívumokat.
Fülek, kedvenc mappák, sötét és világos téma, jobb egérgombos kijelölés, keresés és beépített
képnézegető is van benne. Magyar nyelvű felület, rendes telepítővel.
