## Configuration Reference

This section explains each field in `appsettings.json` for VDRIVE.

Your own values (tokens, folders, tool paths) can go in `appsettings.private.json` next to it instead: same layout, only the settings you change. It is in `.gitignore`, so it never goes to GitHub, and its values and lists replace the ones in `appsettings.json`.

### Authentication
- **AllowedAuthTokens** — Optional list of tokens permitted to access the server.
  - Tokens are 8 bytes and null‑terminated. They are embedded in the PRG binary and must currently be patched manually. Tokens are typically used over public networks **but are not necessary** when running VDRIVE locally.
  - Default is to allow all vdrive clients.

### Storage Adapter
- **StorageAdapter** — Backend tool for disk image operations.  
  Options: `DirMaster`, `Vice`. Default: Vice.
- **StorageAdapterSettings**:
  - **ReadOnly** — Prevent modifications to disk images when true. 
  - **NewFloppyPath** — Directory for new floppy images (e.g. `C:\C64\NewFloppyImages`).
  - **DirMaster** — Python + CBMdisk integration.
  - **Vice** — Uses `c1541.exe`, mostly tested with version 2.4 but also works with 3.9.

### Floppy Resolver
- **FloppyResolver** — Defines how disk images are located.    
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

### Disk Writer (`,W`)
- **WriteFolder** — The folder on the C64's drive (SD2IEC, IDE64) that `,W` writes disks and programs into. It is created in the drive's root if it isn't there yet. Letters, digits, `_`, `-` and `.`, up to 16 characters. Default: `VDRIVE`.
  
### Networking
- **ServerOrClientMode** — Always `Server` with the WiC64.
- **ServerType** — Always `Http` with the WiC64.
- **ServerListenAddress** — Bind address. Default: **http://*:**
- **ServerPort** — Default: **80**
- **SendTimeoutSeconds** — Default: **15 seconds**
- **ReceiveTimeoutSeconds** — Default: **15 seconds**


