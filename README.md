## VDRIVE Quickstart

Looking to run **VDRIVE** without building from source?  
Grab the ready‑to‑run binaries from the [v1.0.0‑beta release](https://github.com/8bitflynn/VDRIVE/releases/tag/v1.0.0-beta).

---

## VDRIVE
<img src="https://8bitflynn.io/Resources/Images/VDRIVE.png" alt="VDRIVE Logo" width="120" align="right"/>

**Wirelessly SEARCH/MOUNT disk images and LOAD/SAVE data to/from your Commodore 64!**

- VDRIVE is a free, open‑source tool built for modern flexibility in retro workflows. It enables remote disk access, mount/unmount control, and HTTP support directly from your C64.
- Designed with developers in mind, VDRIVE allows programmers, artists, and musicians to share disk images in real time. Save work on the C64, inspect or modify it with modern tools, then load it back instantly — no SD card swaps or manual copying.
- VDRIVE supports safe concurrency: multiple C64s can LOAD and SAVE to the same disk image without corruption. File access is synchronized with read/write locks, following a Last‑Write‑Wins model for simplicity and performance.

---

## VDRIVE Hardware

### WiC64 (ESP32‑Based, Memory‑Mapped)

<img src="https://8bitflynn.io/Resources/Images/WiC64_ESP32_ESP8266.jpg" 
     alt="WiC64 C64 Hardware" 
     width="250" 
     align="right"/>

The [WiC64](https://wic64.net/web/) implementation of VDRIVE is fully functional!  
WiC64 is memory‑mapped directly to the C64, bypassing the limitations of serial communication and enabling near‑instant loading of large binaries.

---

## VDRIVE StorageAdapters

VDRIVE supports modular `StorageAdapters` to handle all `LOAD`, `SAVE`, and directory operations. These adapters interface with:

- [`c1541.exe`](https://vice-emu.sourceforge.io/) from **VICE** — command‑line utility for manipulating Commodore disk images.
- [`cbmdisk.pyd`](https://style64.org/cbmdisk) from **[DirMaster](https://style64.org/dirmaster)** — Python wrapper for advanced disk image access.

---

## VDRIVE FloppyResolvers

The `FloppyResolver` system abstracts disk image discovery across **local paths** and **remote archives**.  
Resolvers support:

- **Local Search** — scan user‑defined directories for `.D64`, `.PRG`, and other CBM files.
- **Remote Search** — query curated online archives such as [Commodore.Software](https://commodore.software/). 

VDRIVE also includes a [HvscPsidFloppyResolver](https://github.com/8bitflynn/VDRIVE/blob/master/VDRIVE/Floppy/Impl/HvscPsidFloppyResolver.cs) that integrates with the [High Voltage SID Collection](https://www.hvsc.c64.org/) and converts SID files to PRG via [PSID64](https://psid64.sourceforge.io/), turning your C64 into a SID jukebox.

---

## Install Steps (WiC64)

### 1. Use a WiC64 Relay
- Connect a real [WiC64](https://wic64.net/web/) device, or enable WiC64 emulation in **VICE 3.9+**.

### 2. Build the C64 Client
- Assemble [`vdrive_wic64.asm`](https://github.com/8bitflynn/VDRIVE/blob/master/vdrive_wic64.asm) using the [ACME cross assembler](https://github.com/meonwax/acme).  
- This version communicates via WiC64 through HTTP.

### 3. Configure the Server
Edit `appsettings.json` to define the search paths VDRIVE should scan.  
Optionally, configure a remote `IFloppyResolver` for distributed setups.

### 4. Run the VDRIVE Server/Client
Launch the C# .NET Core server (or client, if the firmware is in server mode).  
VDRIVE runs on [any OS with .NET Core runtime](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md).

### 5. Test on Real Hardware
From your Commodore 64:

```text
LOAD "vdrive_wic64.prg",8,1

SYS 49152   : Enable VDRIVE
SYS 49155   : Disable VDRIVE
SYS 49158   : Search for disk images
SYS 49161   : Mount a different floppy from previous search
```
- Search results include sequence numbers and filenames/descriptions.  
- Enter a number to mount a disk from the results.  
- You can switch between previously found floppies using their sequence number.  
- Use `LOAD` and `SAVE` as usual — now routed through VDRIVE.

---

### Known Limitations

- VDRIVE runs at `$C000`, so any `LOAD` that hits that region will crash it. Eventually, full image transfers (e.g., D64) to SDIEC or other devices will be supported. A 

> VDRIVE is functional, but still evolving. Expect rough edges — and feel free to contribute or fork.

---

