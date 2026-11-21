# Közzététel lépésről lépésre: GitHub → winget

GitHub-felhasználó: **derapoka** – a linkek mindenhol erre mutatnak
(https://github.com/derapoka/DualDeckCommander), ezért a tárolót pontosan
`DualDeckCommander` néven kell létrehozni.

## 1. GitHub tároló (egyszer)

1. Regisztrálj a github.com oldalon, majd **New repository** → név: `DualDeckCommander`, **Public**.
2. Töltsd fel a projektet. A legegyszerűbb a GitHub Desktop programmal:
   *File → Add local repository* → a projekt mappája → *Publish repository*.
   (Parancssorból: `git init`, `git add .`, `git commit -m "Dual Deck Commander 0.6.0"`,
   `git remote add origin https://github.com/derapoka/DualDeckCommander.git`, `git push -u origin main`.)
3. Készítsd el a képernyőképeket a `docs\screenshots\README.md` szerint, és töltsd fel őket is.
4. A tároló oldalán jobb oldalt az **About** fogaskerékkel add meg a rövid leírást és a
   témacímkéket: `file-manager`, `xbox-360`, `amiga`, `emulation`, `games-on-demand`, `windows`.

## 2. Kiadás (minden új verziónál)

1. Írd át a verziót két helyen: `src\DualDeckCommander.csproj` (`<Version>`) és
   `installer\DualDeckCommander.nsi` (`!define VERSION`).
2. Töltsd fel a változást, majd a tároló oldalán: **Actions** fül → bal oldalt **release** →
   jobb oldalt **Run workflow** → zöld **Run workflow** gomb. (A verziót a csproj fájlból veszi,
   a `v0.7.0` címkét magától létrehozza.)
3. Pár perc múlva a **Releases** oldalon
   megjelenik a kiadás a telepítővel és a `SHA256SUMS.txt` fájllal.
4. Töltsd fel a telepítőt a **virustotal.com** oldalra, és a jelentés linkjét írd be a kiadás
   leírásába (*Edit release*).

## 3. winget (minden új verziónál)

### A) Legegyszerűbb: wingetcreate (ajánlott)

```bat
winget install Microsoft.WingetCreate
```

**Első alkalommal** a kész leírófájlokkal:

1. A 0.6.0-s fájlok már teljesen ki vannak töltve (az `InstallerSha256` is), nincs mit átírni.
   Későbbi verzióknál a hash a kiadás `SHA256SUMS.txt` fájljában van.
2. Ellenőrzés és helyi próba:
   ```bat
   winget validate --manifest winget\manifests\b\BaranyiLaszlo\DualDeckCommander\0.6.0
   winget settings --enable LocalManifestFiles
   winget install --manifest winget\manifests\b\BaranyiLaszlo\DualDeckCommander\0.6.0
   ```
3. Beküldés (megnyitja a GitHub-bejelentkezést, és magától nyit egy pull requestet):
   ```bat
   wingetcreate submit winget\manifests\b\BaranyiLaszlo\DualDeckCommander\0.6.0
   ```

**Későbbi verzióknál** egyetlen parancs elég, a hash-t magától kiszámolja:

```bat
wingetcreate update BaranyiLaszlo.DualDeckCommander --version 0.7.0 --urls https://github.com/derapoka/DualDeckCommander/releases/download/v0.7.0/DualDeckCommander-0.7.0-telepito.exe --submit
```

### B) Kézzel

Forkold a `microsoft/winget-pkgs` tárolót, másold be a mappát ugyanerre az útvonalra
(`manifests/b/BaranyiLaszlo/DualDeckCommander/0.6.0/`), és nyiss pull requestet.

### Mi történik utána?

Egy automatikus ellenőrzés telepíti a programot egy tiszta Windowson, és víruskeresővel is
átnézi. Ha valami nem tetszik neki, a pull requestben megírja, mit kell javítani. Elfogadás után
(általában néhány nap) bárki telepítheti:

```
winget install BaranyiLaszlo.DualDeckCommander
winget install dualdeck
```

Megjegyzés: az aláíratlan telepítőket a víruskeresők néha tévesen jelzik. Ilyenkor a
pull requestben jelzik; a hamis riasztást a víruskereső gyártójánál lehet bejelenteni
(pl. Microsoft Defender: „Submit a file for malware analysis”).
