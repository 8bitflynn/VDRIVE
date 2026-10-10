## VDRIVE
<img src="https://8bitflynn.io/Resources/Images/VDRIVE.png" alt="VDRIVE Logo" width="120" align="right"/>

**Wirelessly SEARCH/MOUNT disk images and LOAD/SAVE data to/from your Commodore 64.**

- VDRIVE is a free, open‑source tool built for modern flexibility in retro workflows. It enables remote disk access, search/mount control, and HTTP support directly from your C64.
- Designed with developers in mind, VDRIVE allows programmers, artists, and musicians to share disk images in real time. Save work on the C64, inspect or modify it with modern tools, then load it back instantly — no SD card swaps or manual copying.
- VDRIVE supports safe concurrency: multiple C64s can LOAD and SAVE to the same disk image without corruption. File access is synchronized with read/write locks, following a Last‑Write‑Wins model for simplicity and performance.
- Programs that fill all of memory load too, including over VDRIVE itself at `$C000`: the server sends a small patch stub that takes the program and runs it. See [Programs That Fill All of Memory](#programs-that-fill-all-of-memory-p).

---

## Quickstart

Looking to run **VDRIVE** without building from source?  
Grab the ready‑to‑run binaries from the [v1.0.0‑beta release](https://github.com/8bitflynn/VDRIVE/releases/tag/v1.0.0-beta).

Don't have a real Commodore 64 or a [WiC64](https://wic64.net/web/)? Use [VICE 3.10](https://vice-emu.sourceforge.io/index.html#download) with WiC64 emulation turned on. It can be found in Settings → Userport devices.

<img width="469" height="318" alt="VICE userport device settings with WiC64 selected" src="https://github.com/user-attachments/assets/b17e5ac9-3506-4b88-9b66-6ddf2bf629ab" />

No other settings need to be changed: the `vdrivewic64.prg` C64 binary embeds the server URL, which can be patched with any [HEX editor](https://mh-nexus.de/en/hxd/).

Open `vdrivewic64.prg` in a HEX editor and point the URL to your VDRIVE server's address, for example `http://192.168.1.222/` (80 characters max). The trailing slash is needed, or the request URLs will not be formed correctly. An optional token (7 characters max) follows the URL; see `AllowedAuthTokens` below.

<img width="849" height="190" alt="The server URL inside vdrivewic64.prg in a HEX editor" src="https://github.com/user-attachments/assets/a8b5bd47-fd80-40fd-9284-f97acadd5531" />

---

## Using VDRIVE

From your Commodore 64 (real hardware or VICE with WiC64 emulation):

```text
LOAD "vdrive_wic64.prg",8,1

SYS 49152   : Enable VDRIVE
SYS 49155   : Disable VDRIVE
SYS 49158   : Search for disk images
SYS 49161   : Mount a different floppy from the previous search
```

- Search results include sequence numbers and filenames/descriptions.
- Enter a number to mount a disk from the results; `+` / `-` page through them.
- You can switch between previously found floppies using their sequence number.
- Use `LOAD` and `SAVE` as usual — they are routed through VDRIVE.

### Programs That Fill All of Memory: `,P`

VDRIVE runs at `$C000`. A program that loads over it is sent through a patch stub instead:

```text
LOAD "NAME,P",8
```

The server answers with a small stub (load status `$FE`: run after load). VDRIVE loads it to `$0100` and runs it. The stub asks for the program and receives it itself, so it can write anywhere from `$0200` to `$FFFF`: over VDRIVE, and into the RAM under BASIC, the KERNAL and I/O. Then it RUNs a program that loads at `$0801`, or goes to `READY.` for anything else.

A plain `LOAD` of a program that would land on `$C000-$CFFF` uses the stub automatically, so `LOAD "NAME",8` just works. Everything else is an ordinary load. The stub source is [`stubs/patch_run.asm`](stubs/patch_run.asm).

### Known Limitations

- After a program has loaded over `$C000` (with `,P` or automatically), VDRIVE is gone until it is loaded again, so a game that loads more files later can't get them.
- With `,P`, bytes for `$D000-$DFFF` go into the RAM under I/O; a real drive's LOAD would write the I/O chips.

### Building On VDRIVE

VDRIVE has a jump table and API pointers at `$C000` for BASIC and machine language programs: search, mount, load and save directly. See the [Examples](https://github.com/8bitflynn/VDRIVE/tree/master/Examples) directory for sample programs and the full API reference.

---

## Videos

- [PRG LOAD](https://8bitflynn.io/Resources/Videos/VDRIVE_COMMANDO.m4v) - Loading Commando PRG from search results.
- [Paging](https://8bitflynn.io/Resources/Videos/VDRIVE_PAGING.m4v) - Search supports up to 65,535 results.
- [Remote](https://8bitflynn.io/Resources/Videos/VDRIVE_WIC64_Test.m4v) - Searching [Commodore.Software](https://commodore.software/) from the internet and mounting disks.
- [BASIC ILOAD](https://8bitflynn.io/Resources/Videos/VDRIVE_BASIC_ILOAD.m4v) - Using C64 BASIC to programmatically search / mount / load.
- [Jukebox](https://8bitflynn.io/Resources/Videos/VDRIVE_HVSCPSID.m4v) - Using the HvscPsid floppy resolver to search the [High Voltage SID Collection](https://www.hvsc.c64.org/) and then mount, calling [PSID64](https://psid64.sourceforge.io/) to convert raw SID files to PRG so the C64 can play them instantly.

---

## Install Steps (WiC64)

### 1. Use a WiC64
- Connect a real [WiC64](https://wic64.net/web/) device, or enable WiC64 emulation in **VICE 3.9+**.

### 2. Build the C64 Client
- Assemble [`vdrive_wic64.asm`](vdrive_wic64.asm) with the [ACME cross assembler](https://github.com/meonwax/acme): `acme -f cbm -o vdrive_wic64.prg vdrive_wic64.asm`
- It needs `wic64.h` and `wic64.asm` from the [WiC64 library](https://github.com/WiC64-Team/wic64-library) next to it.
- This version communicates via WiC64 through HTTP.

### 3. Configure the Server
Edit `appsettings.json` to define the search paths VDRIVE should scan.  
Optionally, configure a remote `IFloppyResolver` for distributed setups.

See the [Server Configuration Reference](#server-configuration-reference) below and the [configuration docs](docs/README.md) for detailed options.

### 4. Run the VDRIVE Server
Launch the C# .NET server (or client, if the firmware is in server mode).  
VDRIVE runs on [any OS with the .NET runtime](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md).

---

## Server Configuration Reference

Each field in `appsettings.json`:

### Authentication

- **AllowedAuthTokens** — Optional list of tokens permitted to access the service. Empty = all clients allowed. The token is embedded in the C64 client after the URL and sent with every request.

### Storage

- **StorageAdapter** — Backend tool for disk image operations. Options: `DirMaster`, `Vice`. Default: Vice.
- **StorageAdapterSettings**:
  - **ReadOnly** — Prevent modifications if true (SAVE answers `WRITE PROTECT ON`).
  - **NewFloppyPath** — Directory for new floppy images.
  - **DirMaster** — Python + CBMdisk integration.
  - **Vice** — Uses `c1541.exe`, tested with version 2.4.

### Floppy Resolver

- **FloppyResolver** — Defines how disk images are located. Options: `Local`, `CommodoreSoftware`, `HvscPsid`.
- **Local** — Scans directories (recursive if enabled). Supports `.d64`, `.prg`, etc. `GroupMultiDiskImages` groups the disks of a multi-disk set.
- **CommodoreSoftware** — Remote search via [Commodore.Software](https://commodore.software/).
- **HvscPsid** — SID music resolver using `psid64.exe`.

### Logging

- **LoggingLevel** — Controls verbosity. Options: Verbose, Info, Warning, Error, Critical. Default: Verbose.
- **LogPath** — Folder for the daily log file `vdrive-yyyyMMdd.log` (the same lines as the console). Empty = a `logs` folder next to the executable.

### Session & Search

- **SessionTimeoutMinutes** — Default 90.
- **MaxSearchResults** — Default 65535.
- **SearchPageSize** — Results per page on the C64 (fewer when long names would overflow its buffer). Default 15.
- **WebSearchPageSize** — Results per page for web clients. Default 20.
- **SearchIntroMessage** — Optional banner text.

### Temporary Storage

- **TempPath** — Custom temp directory (empty = system default).
- **TempFolder** — Scratch folder name. Default: VDRIVE_Scratch.
- **Chunksize** — Data chunk size in bytes. Default: 1024.

### Networking (WiC64)

- **ServerOrClientMode** — Always `Server` with the WiC64.
- **ServerType** — Always `Http`.
- **ServerListenAddress** — Bind address. Default: `http://*:`
- **ServerPort** — Default: `80`.
- **SendTimeoutSeconds** / **ReceiveTimeoutSeconds** — Default: 15 each.

---

## How It Works

### WiC64 (ESP32‑Based, Memory‑Mapped)

<img src="https://8bitflynn.io/Resources/Images/WiC64_ESP32_ESP8266.jpg" 
     alt="WiC64 C64 Hardware" 
     width="250" 
     align="right"/>

VDRIVE runs on the [WiC64](https://wic64.net/web/). The WiC64 is memory‑mapped directly to the C64 through the user port, bypassing the limits of serial (IEC) communication and loading large binaries in seconds.

### StorageAdapters

VDRIVE uses modular `StorageAdapters` for all `LOAD`, `SAVE`, and directory operations. These adapters interface with:

- [`c1541.exe`](https://vice-emu.sourceforge.io/) from **VICE** — command‑line utility for manipulating Commodore disk images.
- [`cbmdisk.pyd`](https://style64.org/cbmdisk) from **[DirMaster](https://style64.org/dirmaster)** — Python wrapper for advanced disk image access.

### FloppyResolvers

The `FloppyResolver` system abstracts disk image discovery across **local paths** and **remote archives**:

- **Local Search** — scan user‑defined directories for `.D64`, `.PRG`, and other CBM files.
- **Remote Search** — query curated online archives such as [Commodore.Software](https://commodore.software/).

VDRIVE also includes a [HvscPsidFloppyResolver](https://github.com/8bitflynn/VDRIVE/blob/master/VDRIVE/Floppy/Impl/HvscPsidFloppyResolver.cs) that integrates with the [High Voltage SID Collection](https://www.hvsc.c64.org/) and converts SID files to PRG via [PSID64](https://psid64.sourceforge.io/), turning your C64 into a SID jukebox.
