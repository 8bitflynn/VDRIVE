## Configuration Reference

This section explains each field in `appsettings.json` for VDRIVE.

### Authentication
- **AllowedAuthTokens** — Optional list of tokens permitted to access the server.
  - Tokens are 8 bytes and null‑terminated. They are embedded in the PRG binary and must currently be patched manually. Tokens are typically used over public networks **but are not necessary** when running VDRIVE locally.
  - Default is to allow all vdrive clients.

### Storage
- **StorageAdapter** — Backend tool for disk image operations.  
  Options: `DirMaster`, `Vice`. Default: Vice.
- **StorageAdapterSettings**:
  - **ReadOnly** — Prevent modifications to disk images when true. 
  - **NewFloppyPath** — Directory for new floppy images (e.g. `C:\Programming\Commodore\VirtualFloppies\NewFloppyImages`).
  - **DirMaster** — Python + CBMdisk integration.
  - **Vice** — Uses `c1541.exe`, mostly tested with version 2.4 but also works with 3.9.

### Floppy Resolver
- **FloppyResolver** — Defines how disk images are located.  
  Options: `Local`, `CommodoreSoftware`, `C64`, `HvscPsid`.
- **Local** — Scans directories (recursive if enabled). Supports `.d64`, `.prg`, etc.
- **CommodoreSoftware** — Remote search via [Commodore.Software](https://commodore.software). 
- **HvscPsid** — SID music resolver using `psid64.exe` with flags (`-v`, `-c`, `-r`, `-q`).

### Logging
- **LoggingLevel** — Controls verbosity.  
  Options: Verbose, Info, Warning, Error, Critical. Default: Verbose.

### Session & Search
- **SessionTimeoutMinutes** — Default: 90.
- **MaxSearchResults** — Default: 65535.
- **SearchPageSize** — Default: 12.
- **SearchIntroMessage** — Optional banner text.

### Temporary Storage
- **TempPath** — Custom temp directory (empty = system default).
- **TempFolder** — Scratch folder name. Default: `VDRIVE_Scratch`.
- **Chunksize** — Data chunk size in bytes. Default: 1024.
  
### Networking
- **ServerOrClientMode** — Always `Server` in WiC64. (Client mode was legacy only.)
- **ServerType** — Always `Http` in WiC64. (Other protocols were legacy only.)
- **ServerListenAddress** — Bind address. Default: **http://*:**
- **ServerPort** — Default: **80**
- **SendTimeoutSeconds** — Default: **15 seconds**
- **ReceiveTimeoutSeconds** — Default: **15 seconds**


