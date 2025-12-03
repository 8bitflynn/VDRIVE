## VDRIVE
<img src="https://8bitflynn.io/Resources/Images/VDRIVE.png" alt="VDRIVE Logo" width="120" align="right"/>

**Wirelessly SEARCH/MOUNT disk images and LOAD/SAVE data to/from your Commodore 64!**

- VDRIVE is a free, open-source tool built for those who want modern flexibility in retro workflows. It reflects months of design, testing, and iteration — not a plug-and-play gimmick. If you prefer original hardware, that’s valid. If you want remote disk access, mount/unmount control, and HTTP support, VDRIVE is here for you.
  
- VDRIVE is designed with developers in mind — enabling real-time disk image sharing directly from your Commodore 64. VDRIVE allows programmers, graphic artists, and sourd designers to work seemlessly in the same environment without ever leaving the C64. Modern tools that save to any configured search paths for VDRIVE will be avaiable as soon as its saved to disk. Have that hard to bug on C64? Save it to disk on C64 and take a closer look with modern tools. Save that fix and then load it on the C64 to test. No SD card swaps, no copying. Just development.

- VDRIVE is designed for safe concurrency, enabling multiple C64's to LOAD and SAVE to the same disk image—even the same file—without corruption. File access is synchronized using read/write locks scoped to the full file path. While this ensures atomic operations and prevents data races, it follows a Last-Write-Wins model: the most recent SAVE will overwrite prior versions, regardless of their relative freshness. This tradeoff favors performance and simplicity over version enforcement.

## VDRIVE Signal

Docs will land at <a href="https://8bitflynn.io/Projects" target="_blank">https://8bitflynn.io/Projects</a> when the dust settles.

<h2>VDRIVE Hardware</h2>

<p>VDRIVE supports two hardware configurations for connecting to the Commodore 64:</p>
<ul>
  <li><strong>WiC64 Option (Future-Focused):</strong> WiC64 (ESP32-Based, Memory-Mapped)</li>
  <li><strong>WiFi modem Option (Serial-Based):</strong> ESP8266 WiFi Modem</li>
</ul>

<h3>WiC64 (ESP32-Based, Memory-Mapped)</h3>

<img src="https://8bitflynn.io/Resources/Images/WiC64_ESP32_ESP8266.jpg" 
     alt="WiC64 C64 Hardware on left and ESP8266 on right" 
     width="250" 
     align="right"/>

<div style="border: 2px solid #0077cc; padding: 10px; background-color: #f0f8ff; margin-bottom: 15px;">
  The <a href="https://wic64.net/web/" target="_blank">WiC64</a> implementation of VDRIVE is fully functional! WiC64 is memory-mapped directly to the C64, so it bypasses the problems with serial communicaions altogether and can load large (for a C64) binaries instantly.
</div>

<h3>ESP8266 WiFi Modem (Serial-Based)</h3>

<img src="https://8bitflynn.io/Resources/Images/ESP8266_C64_SerialHardware.jpg" 
     alt="ESP8266 C64 Serial Hardware" 
     width="250" 
     align="right"/>

<p>The ESP8266 acts as a wireless transport layer, communicating with the C64 via serial. This setup uses custom firmware to handle TCP-to-Serial bridging that can be reused for other relay or bridge projects.</p>

<div style="border: 2px solid #999; padding: 10px; background-color: #f9f9f9; margin-bottom: 15px;">
  <strong>Note:</strong> While the ESP8266 option remains supported, future development and optimization of VDRIVE will primarily focus on the <a href="https://wic64.net/web/" target="_blank">WiC64</a> memory‑mapped approach.
</div>


## VDRIVE StorageAdapters

VDRIVE supports modular `StorageAdapters` to handle all `LOAD`, `SAVE`, and directory operations. These adapters interface with:

- [`c1541.exe`](https://vice-emu.sourceforge.io/) from **VICE** — a command-line utility for manipulating Commodore disk images.
- [`cbmdisk.pyd`](https://style64.org/cbmdisk) from **[DirMaster](https://style64.org/dirmaster)** — a Python wrapper for advanced disk image access and manipulation.

Adapters can be swapped or extended to support additional workflows, ensuring compatibility across platforms and tooling preferences.

## VDRIVE FloppyResolvers

VDRIVE's `FloppyResolver` system abstracts disk image discovery across **local paths** and **remote archives**, without directly manipulating disk contents. Instead, it delegates search execution to the host OS, which returns candidate results for mounting.

Resolvers support:

- **Local Search** — the host OS scans user-defined directories for matching `.D64`, `.T64`, `.PRG`, and other CBM-compatible files.
- **Remote Search** — the host OS queries curated online archives such as [Commodore.Software](https://commodore.software/) and returns matching entries based on filename, metadata, or contextual hints.

Users search for disks using floppy resolvers and all search reseults return a **sequence number** and a description or filename that can be selected to mount. 

VDRIVE now has a <a href="https://github.com/8bitflynn/VDRIVE/blob/master/VDRIVE/Floppy/Impl/HvscPsidFloppyResolver.cs" target="_blank">HvscPsidFloppyResolver</a> that joins the search and download from <a href="https://www.hvsc.c64.org/" target="_blank">High Voltage Sid Collection</a> with the conversion to PRG from <a href="https://psid64.sourceforge.io/">PSID64</a> making the C64 a SID jukebox!

---

### Notes

- This release is intended for developers and technically inclined users. Setup requires compiling and assembling. Pre-built binaries will be provided once the project nears completion.
- PRG (no disk) files can be loaded by selecting them in search and issuing `LOAD "*",8,1` to inject directly into memory.
- Multiple C64s can connect to a single VDRIVE server and share disk images — enabling lots of possibilities locally or remotely.
- VDRIVE is optimized for PRG workflows and single-file compatibility. Multi-disk games, fastloaders, and custom loaders may not function as expected. Celebrate what works, and understand the limits.
- Future versions may expand compatibility by staging full disk images locally, unlocking broader support without compromising simplicity.

---

### Install Steps

### 1. Choose Your Connection Method

You can use VDRIVE with either:

#### 🔌 A Direct ESP8266 Modem (requires flashing)
- Burn `ESP8266_Firmware.ino` to your WiFi modem.
- To build the firmware using [Arduino IDE](https://www.arduino.cc/en/software), open **Preferences** and add this URL to **Additional Board Manager URLs**:  
  `http://arduino.esp8266.com/stable/package_esp8266com_index.json`

#### 📡 A WiC64 Relay (no flashing required)
- Use a real **<a href="https://wic64.net/web/" target="_blank">WiC64</a>** device *or* enable WiC64 emulation in **VICE 3.9+**

### 2. Build the C64 Client

Choose the appropriate client for your setup:

- **For direct ESP8266 modem use:**  
  - Assemble [`vdrive.asm`](https://github.com/8bitflynn/VDRIVE/blob/master/vdrive.asm) using [CBM Studio](https://www.ajordison.co.uk/download.html).  
  - Build [`UP9600.asm`](https://github.com/bozimmerman/Zimodem/blob/master/cbm8bit/src/up9600.asm) from Bo Zimmerman's repository.

- **For WiC64 wireless interface (real or emulated):**  
  - Assemble [`vdrive_wic64.asm`](https://github.com/8bitflynn/VDRIVE/blob/master/vdrive_wic64.asm) using [ACME cross assembler](https://github.com/meonwax/acme).  
  - This version communicates via WiC64 through HTTP.

### 3. Configure the Server
Edit `appsettings.json` to define the search paths VDRIVE should scan.  
Optionally, configure a remote `IFloppyResolver` for distributed setups.

### 4. Run the VDRIVE Server/Client
Launch the C# .NET Core server (or client, if the firmware is in server mode).

VDRIVE runs on [any OS with .NET Core runtime](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md), as long as the `IStorageAdapter` implementation is compatible.  
**DirMaster** is Windows-only, but **VICE** supports multiple platforms (though not all are tested).

### 5. Test on Real Hardware

#### If using ESP8266 Modem:

From your Commodore 64:

    LOAD "UP9600.prg",8,1
    LOAD "setupwifi.prg",8
    RUN

Enter your WiFi credentials — stored in ESP8266 flash memory (setup is one-time).

Then:

    LOAD "UP9600.prg",8,1   : not needed if loaded already above
    LOAD "vdrive.prg",8,1
    SYS 49152   : Enable VDRIVE  
    SYS 49155   : Disable VDRIVE  
    SYS 49158   : Search for disk images  
    SYS 49161   : Mount a different floppy from previous search

---

#### If using WiC64 Relay:

From your Commodore 64:

    LOAD "vdrive_wic64.prg",8,1

Then:

    SYS 49152   : Enable VDRIVE  
    SYS 49155   : Disable VDRIVE  
    SYS 49158   : Search for disk images  
    SYS 49161   : Mount a different floppy from previous search

> No need to load `UP9600.prg` or run `setupwifi.prg` — WiC64 handles communication directly.


- Search results include sequence numbers and filenames/descriptions.  
- Enter a number to mount a disk from the results.  
- You can switch between previously found floppies using their sequence number.  
- Use `LOAD` and `SAVE` as usual — now routed through VDRIVE.

---

### Known Limitations

- VDRIVE runs at `$C000`, so any `LOAD` that hits that region will crash it. Eventually, full image transfers (e.g., D64) to SDIEC or other devices will be supported. A 

> VDRIVE is functional, but still evolving. Expect rough edges — and feel free to contribute or fork.
