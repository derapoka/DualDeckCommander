# Dual Deck Commander

**Magyar** | [English](README.md)

Ingyenes, kétpaneles fájlkezelő Windowsra, a Total Commander működése és kinézete alapján.
C# + WinForms (.NET 8). (Korábbi neve: TwinCommander – a beállításokat magától átveszi.)

## Telepítő készítése (egy dupla kattintás)

Kattints duplán a **`Telepito-keszitese.bat`** fájlra. Mindent elvégez:

1. ha hiányzik, telepíti a szükséges ingyenes eszközöket (.NET 8 SDK, NSIS) a Windows `winget` programjával,
2. lefordítja a programot (önálló exe – a célgépen nem kell hozzá .NET),
3. elkészíti a telepítőt: **`dist\DualDeckCommander-0.7.0-telepito.exe`**, és megnyitja a mappát.

Az első futás a letöltések miatt néhány percig tart, utána kb. egy perc.

### A telepítő

Klasszikus, magyar nyelvű telepítővarázsló:

1. **Üdvözlés**
2. **Licencfeltételek** – a jogi nyilatkozattal, el kell fogadni
3. **Összetevők** – program (kötelező), **asztali ikon** (alapból bekapcsolva),
   fájltársítás az Amiga formátumokhoz (.adf .adz .dms .hdf .lha .lzh .lzx, alapból kikapcsolva)
4. **Telepítés helye** – szabadon választható mappa és meghajtó (Tallózás), a szükséges és a szabad hellyel
5. **Start menü mappa** – átnevezhető, vagy kikapcsolható
6. **Telepítés** – folyamatjelzővel
7. **Befejezés** – „A Dual Deck Commander indítása most” és „Licencfeltételek megjelenítése”

Ezen kívül:
- megjelenik a **Gépház → Alkalmazások** (Programok és szolgáltatások) listában ikonnal, verzióval, kiadóval, mérettel,
- a Start menüben eltávolító parancsikon is van,
- frissítéskor a korábbi telepítési mappát ajánlja fel,
- az eltávolító mindent visszatesz (parancsikonok, fájltársítások, registry), a beállítások törlését megkérdezi,
- csendes telepítés rendszergazdáknak: `DualDeckCommander-0.7.0-telepito.exe /S /D=D:\Programok\Dual Deck Commander`.

GitHubon a `.github/workflows/build.yml` minden feltöltés után elkészíti a telepítőt (Actions fül → artifact).

## Újdonságok a 0.5-ös verzióban

- **Beállítások ablak** (Beállítások menü vagy Ctrl+O) három csoportban:
  - *Megjelenés:* téma, betűméret, sortávolság, fájlméretek formája, eszköztár /
    funkcióbillentyű-sáv / parancssor ki-be, rejtett fájlok
  - *Működés:* jobb egérgomb (kijelölés vagy helyi menü), mi történjen Enterre .iso fájlon
    (kérdezzen / böngészés / csatolás) és archívumon (belenézés / társított program),
    Lomtárba törlés, megerősítés, fülek megjegyzése, indítóképernyő
  - *Programok:* szerkesztő (tallózással), a beállítások mappája
- A legtöbb beállítás közvetlenül a **Nézet** és a **Beállítások** menüből is állítható
- **Új névjegy** a teljes leírással és jogi nyilatkozattal (Súgó → Névjegy)
- **Saját programikon** és **indítóképernyő** (kikapcsolható; parancssori kapcsoló: `/nosplash`)
- Parancssori indítás mappával vagy fájllal: `DualDeckCommander.exe "D:\Amiga\játék.adf"`
  (a fájltársítás is ezt használja)
- **Telepítő** (lásd fent)
- **Beépített képnézegető** – Enter vagy F3 egy képen. Csak a kép, sötét háttéren:
  - lapozás a mappa (vagy archívum) képei között: ← → / PgUp PgDn / szóköz / görgő / kattintás a kép szélén
  - nagyítás: Ctrl+görgő vagy + −, `1` eredeti méret, `0` ablakhoz igazítás, húzással mozgatás
  - `R` forgatás, F11 vagy dupla kattintás teljes képernyő, Esc bezárás
  - formátumok: JPEG / JFIF, PNG, BMP, GIF (animált is), TIFF, ICO, TGA, valamint WebP, HEIC, AVIF
    (ez utóbbiakhoz a Windows képkodekjei kellenek – Windows 11-en a WebP alapból megvan)
  - a telefonos fotók tájolását (EXIF) magától figyelembe veszi
  - kikapcsolható: Beállítások → Képek megnyitása → A Windows társított programja

## Újdonságok a 0.4-es verzióban

- **Fülek** mindkét panelen: Ctrl+T új fül, Ctrl+W bezárás, Ctrl+Tab / Ctrl+Shift+Tab váltás,
  Ctrl+↑ a kurzor alatti mappa új fülön. Középső gomb a fülön: bezárás, dupla kattintás az üres
  részen: új fül. A fülek kilépéskor megmaradnak.
- **Kedvenc mappák** (Ctrl+D vagy a „Kedvencek” menü/gomb): 1–9 gyorsbillentyűvel,
  hozzáadás egy lépésben, szerkesztő ablak (átnevezés, sorrend, törlés). Archívum belseje is lehet kedvenc.
- **Amiga lemezképek böngészése:**
  - **ADF** (floppy, DD és HD), **ADZ** (gzip-elt ADF)
  - **DMS** (DiskMasher) – minden tömörítési mód: NONE, SIMPLE, QUICK, MEDIUM, DEEP, HEAVY1, HEAVY2.
    A gyökérben a kibontott **.adf** is ott van, így egy F5-tel DMS → ADF átalakítás.
  - **HDF** merevlemezképek, RDB-partíciókkal is (DH0, DH1… külön mappaként)
  - Fájlrendszer: OFS, FFS, INTL, DIRCACHE. Saját formátumú (nem AmigaDOS) játéklemezek nem
    böngészhetők, PFS3/SFS partíciók sem; jelszavas DMS nem támogatott.

## Újdonságok a 0.3-as verzióban

- **Új név:** Dual Deck Commander (`DualDeckCommander.exe`)
- **Archívumok böngészése és kibontása:** ZIP, RAR (1.5–5.x), 7-Zip, LHA/LZH, Amiga LZX
- **ISO-n Enter:** megkérdezi, hogy böngészni (Xbox / Xbox 360) vagy meghajtóként csatolni szeretnéd
- **Jobb egérgombos kijelölés** (Total Commander-stílusban), hosszan nyomva helyi menü
- A kijelölés csak a fájlnevet színezi pirosra
- A meghajtó foglaltsága a kiválasztott meghajtó betűjele alatt látszik
- Javítva: a kék kijelölések és a vízszintes vonalak hibásan jelentek meg; a „Név” oszlop kitölti a panelt

## Archívumok

| Művelet | Billentyű |
|---|---|
| Belenézés (mint egy mappába) | Enter / dupla kattintás az archívumon |
| Fájl megtekintése | F3 |
| Kijelölt fájlok / mappák kibontása a másik panelre | F5 |
| Fájl megnyitása (ideiglenes kibontással) | Enter |
| Teljes archívum kibontása (több archívum: mindegyik külön mappába) | Alt+F9 |
| Archívum adatai (fájlok, méretek, tömörítési arány) | Alt+Enter |

| Formátum | Kiterjesztés | Megjegyzés |
|---|---|---|
| ZIP | .zip | beépített; régi DOS-os (852-es kódlapú) fájlnevek is |
| RAR | .rar | RAR 1.5 – 5.x, tömör (solid) is; SharpCompress könyvtárral |
| 7-Zip | .7z | LZMA, LZMA2, PPMd, BZip2 stb., tömör is; SharpCompress könyvtárral |
| LHA / LZH | .lha, .lzh | Amiga LhA, LHarc, LHa for UNIX: -lh0- -lh1- -lh4- -lh5- -lh6- -lh7-, fejlécszint 0–2 |
| Amiga LZX | .lzx | normál és „merged” (összevont) tárolás, CRC-ellenőrzéssel |

Az archívumok csak olvashatók (archívumba írás még nincs). Jelszavas archívumot még nem bont ki.

## Lemezképek (.iso)

Enter egy `.iso` fájlon – a program megkérdezi:

- **Böngészés** – Xbox / Xbox 360 lemezképeknél (XGD1, XGD2, XGD3, XISO). F3 nézőke, F5 kibontás,
  Alt+Enter: Title ID, Media ID, formátum.
- **Csatolás meghajtóként** – a Windows beépített csatolásával; a panel rögtön az új meghajtóra lép.
- **GoD átalakítás** – lásd lent.
- **Leválasztás** – a csatolt lemezkép leválasztása.

Kérdés nélküli böngészés: **Ctrl+PgDn**.

### Átalakítás Games on Demand (GoD) formátumra

Xbox 360 (és eredeti Xbox) játéklemezek ISO-ját a konzol merevlemezéről / USB-ről indítható
GoD formára alakítja – választható művelet, az eredeti ISO nem változik.

- Indítás: **Ctrl+G**, *Parancsok → Xbox ISO átalakítása GoD formátumra*, a helyi menü
  vagy az ISO-n Enter után a **GoD átalakítás** gomb. Több kijelölt ISO egyszerre is mehet.
- A párbeszédablakban: **célmappa** (tallózással), **játék neve** (a beépített, ~4900 címes
  adatbázisból előre kitöltve), **üres terület levágása**, és hogy a kész kimenetet megnyissa-e
  a másik panelen. A legutóbbi választásokat megjegyzi.
- Kimenet: `<cél>\<Title ID>\00007000\<Media ID>` + `<Media ID>.data\Data0000…`
  (eredeti Xbox játéknál `00005000`). A Title ID mappát a konzolon a
  `Content\0000000000000000\` mappába kell másolni.
- Előtte ellenőrzi a szabad helyet és rákérdez a meglévő kimenet felülírására; megszakításkor
  a félkész fájlokat törli.
- A kimenet bájtra azonos az iso2god-rs programéval (tesztelve XISO és XGD2 lemezképpel).

## Egér

- **Jobb gomb:** kijelöl / megszüntet; lenyomva tartva és húzva több fájlt jelöl egyszerre
- **Jobb gomb hosszan nyomva:** helyi menü (billentyűzetről: Menü gomb vagy Shift+F10)
- **Ctrl / Shift + bal gomb:** kijelölés, **dupla kattintás:** megnyitás

## További funkciók

- Két panel, sötét / világos téma (Ctrl+Shift+T), meghajtóikonok, színezett mappák
- F3 nézőke, F4 szerkesztő, F5 másolás, F6 áthelyezés, F7 új mappa, F8 Lomtárba, Shift+Del végleges törlés
- **Több mappa egyszerre** (F7 → „Több mappa…”, vagy Shift+F7): nagy ablakba beillesztett szövegből
  soronként egy mappa; élő előnézet, almappák (`Szülő\Gyerek`), felsorolásjelek levágása, tiltott
  karakterek cseréje (`Halo 3: ODST` → `Halo 3 - ODST`), opcionális sorszámozás, betöltés .txt fájlból
- Felülírás- és hibakezelés (Újra / Kihagyás / Mindet …), haladásjelző, megszakítás
- Alt+F7 keresés név és tartalom alapján, parancssor a panelek alatt
- Vágólap és fogd és vidd az Intézővel is, automatikus frissítés, rejtett fájlok (Ctrl+H)

A teljes billentyűlista a programban: **F1**.

## Fordítás

1. .NET 8 SDK: `winget install Microsoft.DotNet.SDK.8`
2. Dupla kattintás a `build.bat`-ra → `publish\DualDeckCommander.exe`

Az első fordításnál a gép letölti a SharpCompress csomagot a NuGetről (internet kell hozzá).
Runtime nélkül is futó, önálló exe:

```bat
cd src
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o ..\publish
```

## Fájlok

| Fájl | Tartalom |
|---|---|
| `MainForm.cs` | főablak, menü, billentyűk, műveletek, csatolás |
| `FilePanel.cs` | egy panel: lista, rajzolás, kijelölés (jobb gomb is), navigáció |
| `VirtualFs.cs` | közös réteg a lemezképekhez és archívumokhoz |
| `XboxIso.cs` | Xbox / Xbox 360 lemezkép (XDVDFS / GDF), játékcím-adatbázis |
| `GodConverter.cs`, `GodForm.cs` | ISO → Games on Demand átalakítás és párbeszédablaka |
| `Archives.cs`, `SharpArchive.cs` | ZIP; RAR és 7-Zip (SharpCompress) |
| `Lha.cs`, `Lzx.cs` | saját LHA és Amiga LZX kitömörítő |
| `AmigaDisk.cs` | Amiga ADF / ADZ / HDF (RDB) olvasó és DMS kitömörítő |
| `TabBar.cs`, `FavoritesForm.cs` | fülsáv, kedvenc mappák szerkesztője |
| `SettingsForm.cs`, `AboutForm.cs`, `SplashForm.cs` | beállítások, névjegy, indítóképernyő |
| `ImageViewerForm.cs`, `ImageLoader.cs` | képnézegető, képbetöltés (GDI+, WIC, saját TGA) |
| `Assets\` | ikon, logó, indítóképernyő (a `tools\make_assets.py` készíti) |
| `installer\` | NSIS telepítő szkript, licenc (TXT + RTF), varázsló képek |
| `FileOperations.cs`, `ProgressForm.cs` | másolás / áthelyezés / törlés / kibontás |
| `Theme.cs` | színsémák, sötét mód, ikonok |
| `ViewerForm.cs`, `SearchForm.cs`, `Dialogs.cs` | nézőke, keresés, párbeszédablakok |

## Licenc

Freeware (ingyenes) – Baranyi László. Részletek: `LICENSE.txt`.
Külső komponens: SharpCompress (MIT). Az LHA dekóder Haruhiko Okumura ar002 / LZHUF
algoritmusait, az LZX dekóder az unlzx.c (Erik Meusel, Dan Fraser) működését követi.
A DMS dekóder Teemu Suutari „ancient” könyvtárán (BSD licenc) alapul.
A GoD átalakítás az iso2god-rs (Ilia Pozdnyakov, MIT licenc) működését követi, annak
fejlécsablonját és címadatbázisát használja.
