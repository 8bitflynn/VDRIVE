<p align="center">
  <img src="https://8bitflynn.io/Resources/Images/VDRIVE.png" alt="VDRIVE logo" width="180"/>
</p>

<h1 align="center">VDRIVE</h1>

<p align="center">
  <b>Wirelessly SEARCH/MOUNT disk images and LOAD/SAVE data to/from your Commodore 64.</b><br/>
  A C64 client over the <a href="https://wic64.net/web/">WiC64</a> and a C# .NET server.
</p>

<p align="center">
  <a href="#quickstart">Quickstart</a> ·
  <a href="#using-vdrive">Using VDRIVE</a> ·
  <a href="#programs-that-fill-all-of-memory-p">,P</a> ·
  <a href="#videos">Videos</a> ·
  <a href="#install-steps-wic64">Install</a> ·
  <a href="#server-configuration-reference">Configuration</a> ·
  <a href="Examples">Examples &amp; API</a>
</p>

---

## Highlights

- **Remote disk access from the C64.** Search, mount, LOAD and SAVE over HTTP, directly from the C64.
- **Built for sharing work.** Programmers, artists and musicians can share disk images in real time: save on the C64, inspect or change the file with modern tools, then load it back. No SD card swaps or manual copying.
- **Safe concurrency.** Several C64s can LOAD and SAVE to the same disk image without corruption. File access uses read/write locks with a Last‑Write‑Wins model.
- **Programs that fill all of memory load too,** including over VDRIVE itself at `$C000`: the server sends a small patch stub that takes the program and runs it. See [`,P`](#programs-that-fill-all-of-memory-p).
- **Local and remote collections.** Local folders, [Commodore.Software](https://commodore.software/), and the [High Voltage SID Collection](https://www.hvsc.c64.org/) as a SID jukebox.

---

## Quickstart

Ready‑to‑run binaries are in the [v1.0.0‑beta release](https://github.com/8bitflynn/VDRIVE/releases/tag/v1.0.0-beta).

> [!TIP]
> No real Commodore 64 or WiC64? Use [VICE 3.10](https://vice-emu.sourceforge.io/index.html#download) with WiC64 emulation turned on, under **Settings → Userport devices**.

<img width="469" height="318" alt="VICE userport device settings with WiC64 selected" src="https://github.com/user-attachments/assets/b17e5ac9-3506-4b88-9b66-6ddf2bf629ab" />

No other settings need to be changed: `vdrivewic64.prg` has the server URL inside it. Open it in a [HEX editor](https://mh-nexus.de/en/hxd/) and point the URL to your VDRIVE server, for example `http://192.168.1.222/` (80 characters max). An optional token (7 characters max) follows the URL; see `AllowedAuthTokens` below.

> [!NOTE]
> The trailing slash is needed, or the request URLs will not be formed correctly.

<img width="849" height="190" alt="The server URL inside vdrivewic64.prg in a HEX editor" src="https://github.com/user-attachments/assets/a8b5bd47-fd80-40fd-9284-f97acadd5531" />

---

## Using VDRIVE

From your Commodore 64 (real hardware or VICE with WiC64 emulation):

```text
LOAD "vdrive_wic64.prg",8,1
```

| Command | Does |
|---|---|
| `SYS 49152` | Enable VDRIVE |
| `SYS 49155` | Disable VDRIVE |
| `SYS 49158` | Search for disk images |
| `SYS 49161` | Mount a different floppy from the previous search |

- Search results show sequence numbers and filenames/descriptions.
- Enter a number to mount a disk from the results; `+` / `-` page through them.
- Previously found floppies can be mounted again by their sequence number.
- `LOAD` and `SAVE` work as usual and are routed through VDRIVE.

### Programs That Fill All of Memory: `,P`

VDRIVE runs at `$C000`. A program that loads over it is sent through a patch stub instead:

```text
LOAD "NAME,P",8
```

1. The server answers with a small stub (load status `$FE`: run after load). VDRIVE loads it to `$0100` and runs it.
2. The stub asks for the program and receives it itself, so it can write anywhere from `$0200` to `$FFFF`: over VDRIVE, and into the RAM under BASIC, the KERNAL and I/O.
3. Then it RUNs a program that loads at `$0801`, or goes to `READY.` for anything else.

A plain `LOAD` of a program that would land on `$C000-$CFFF` uses the stub automatically, so `LOAD "NAME",8` just works. Everything else is an ordinary load. The stub source is [`stubs/patch_run.asm`](stubs/patch_run.asm).

### Known Limitations

- After a program has loaded over `$C000` (with `,P` or automatically), VDRIVE is gone until it is loaded again, so a game that loads more files later can't get them.
- With `,P`, bytes for `$D000-$DFFF` go into the RAM under I/O; a real drive's LOAD would write the I/O chips.

### Building On VDRIVE

VDRIVE has a jump table and API pointers at `$C000` for BASIC and machine language programs to search, mount, load and save directly. The [Examples](Examples) directory has sample programs and the full API reference.

---

## Videos

| Video | Shows |
|---|---|
| [PRG LOAD](https://8bitflynn.io/Resources/Videos/VDRIVE_COMMANDO.m4v) | Loading the Commando PRG from search results. |
| [Paging](https://8bitflynn.io/Resources/Videos/VDRIVE_PAGING.m4v) | Search supports up to 65,535 results. |
| [Remote](https://8bitflynn.io/Resources/Videos/VDRIVE_WIC64_Test.m4v) | Searching [Commodore.Software](https://commodore.software/) from the internet and mounting disks. |
| [BASIC ILOAD](https://8bitflynn.io/Resources/Videos/VDRIVE_BASIC_ILOAD.m4v) | C64 BASIC searching, mounting and loading through the API. |
| [Jukebox](https://8bitflynn.io/Resources/Videos/VDRIVE_HVSCPSID.m4v) | The HvscPsid floppy resolver searching the [High Voltage SID Collection](https://www.hvsc.c64.org/); [PSID64](https://psid64.sourceforge.io/) converts the SID files to PRG so the C64 plays them instantly. |

---

## Install Steps (WiC64)

**1. Use a WiC64.** Connect a real [WiC64](https://wic64.net/web/), or enable WiC64 emulation in **VICE 3.9+**.

**2. Build the C64 client.** Assemble [`vdrive_wic64.asm`](vdrive_wic64.asm) with the [ACME cross assembler](https://github.com/meonwax/acme):

```text
acme -f cbm -o vdrive_wic64.prg vdrive_wic64.asm
```

It needs `wic64.h`, `wic64.asm` and `examples/macros.asm` from the [WiC64 library](https://github.com/WiC64-Team/wic64-library) next to it. The client talks to the server over HTTP through the WiC64.

**3. Configure the server.** Edit `appsettings.json` to set the search paths VDRIVE should scan. A remote `IFloppyResolver` can be configured for distributed setups. See the [configuration reference](#server-configuration-reference) below and the [configuration docs](docs/README.md).

**4. Run the server.** Launch the C# .NET server (or client, if the firmware is in server mode). VDRIVE runs on [any OS with the .NET runtime](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md).

---

## Server Configuration Reference

<details>
<summary>Every field in <code>appsettings.json</code></summary>

#### Authentication

| Setting | Meaning |
|---|---|
| `AllowedAuthTokens` | Optional list of tokens allowed to use the server. Empty = all clients. The token is embedded in the C64 client after the URL and sent with every request. |

#### Storage

| Setting | Meaning |
|---|---|
| `StorageAdapter` | Backend for disk image operations: `DirMaster` or `Vice`. Default: `Vice`. |
| `StorageAdapterSettings.ReadOnly` | No changes to disk images when true (SAVE answers `WRITE PROTECT ON`). |
| `StorageAdapterSettings.NewFloppyPath` | Folder for new floppy images. |
| `StorageAdapterSettings.DirMaster` | Python + CBMdisk integration. |
| `StorageAdapterSettings.Vice` | Uses `c1541.exe`, tested with version 2.4. |

#### Floppy Resolver

| Setting | Meaning |
|---|---|
| `FloppyResolver` | How disk images are found: `Local`, `CommodoreSoftware` or `HvscPsid`. |
| `Local` | Scans folders (recursive if enabled) for `.d64`, `.prg` and more. `GroupMultiDiskImages` groups the disks of a multi-disk set. |
| `CommodoreSoftware` | Remote search on [Commodore.Software](https://commodore.software/). |
| `HvscPsid` | SID music resolver using `psid64.exe`. |

#### Logging

| Setting | Meaning |
|---|---|
| `LoggingLevel` | `Verbose`, `Info`, `Warning`, `Error` or `Critical`. Default: `Verbose`. |
| `LogPath` | Folder for the daily log file `vdrive-yyyyMMdd.log` (the same lines as the console). Empty = a `logs` folder next to the executable. |

#### Session & Search

| Setting | Meaning |
|---|---|
| `SessionTimeoutMinutes` | Default: 90. |
| `MaxSearchResults` | Default: 65535. |
| `SearchPageSize` | Results per page on the C64 (fewer when long names would overflow its buffer). Default: 15. |
| `WebSearchPageSize` | Results per page for web clients. Default: 20. |
| `SearchIntroMessage` | Optional banner text. |

#### Temporary Storage

| Setting | Meaning |
|---|---|
| `TempPath` | Custom temp folder (empty = system default). |
| `TempFolder` | Scratch folder name. Default: `VDRIVE_Scratch`. |
| `Chunksize` | Data chunk size in bytes. Default: 1024. |

#### Networking (WiC64)

| Setting | Meaning |
|---|---|
| `ServerOrClientMode` | Always `Server` with the WiC64. |
| `ServerType` | Always `Http`. |
| `ServerListenAddress` | Bind address. Default: `http://*:` |
| `ServerPort` | Default: `80`. |
| `SendTimeoutSeconds` / `ReceiveTimeoutSeconds` | Default: 15 each. |

</details>

---

## How It Works

<img src="https://8bitflynn.io/Resources/Images/WiC64_ESP32_ESP8266.jpg" alt="WiC64 C64 hardware" width="250" align="right"/>

**WiC64 (ESP32‑based, memory‑mapped).** VDRIVE runs on the [WiC64](https://wic64.net/web/). The WiC64 is memory‑mapped directly to the C64 through the user port, bypassing the limits of serial (IEC) communication and loading large binaries in seconds.

**StorageAdapters.** Modular `StorageAdapters` handle every `LOAD`, `SAVE` and directory operation. They use:

- [`c1541.exe`](https://vice-emu.sourceforge.io/) from **VICE**, the command‑line tool for Commodore disk images.
- [`cbmdisk.pyd`](https://style64.org/cbmdisk) from **[DirMaster](https://style64.org/dirmaster)**, a Python wrapper for disk image access.

**FloppyResolvers.** The `FloppyResolver` system finds disk images in **local folders** (`.D64`, `.PRG` and other CBM files) and **remote archives** such as [Commodore.Software](https://commodore.software/). The [HvscPsidFloppyResolver](VDRIVE/Floppy/Impl/HvscPsidFloppyResolver.cs) searches the [High Voltage SID Collection](https://www.hvsc.c64.org/) and converts SID files to PRG with [PSID64](https://psid64.sourceforge.io/), turning the C64 into a SID jukebox.
