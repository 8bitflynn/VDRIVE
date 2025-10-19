## 🧠 VDRIVE
<img src="https://8bitflynn.io/Resources/Images/VDRIVE.png" alt="VDRIVE Logo" width="120" align="right"/>

**Wirelessly SEARCH/MOUNT disk images and LOAD/SAVE data to/from your Commodore 64!**  

- VDRIVE is a free, open-source tool built for those who want modern flexibility in retro workflows. It reflects months of design, testing, and iteration — not a plug-and-play gimmick. If you prefer original hardware, that’s valid. If you want remote disk access, mount/unmount control, and HTTP support, VDRIVE is here for you.
- VDRIVE includes IFloppyResolver implementations for both local disks and several remote repositories. For remote sources, I’ve been actively requesting permission from repository owners. If I don’t hear back, I treat access as equivalent to browsing their site from a modern machine — respectful, read-only, and non-invasive. That said, I fully respect the wishes of content owners: if any repository owner prefers their site not be included, I’ll remove it immediately.
  

## 🕹️ VDRIVE Signal

Docs will land at [8bitflynn.io](https://8bitflynn.io) when the dust settles.

---

### ⚙️ Notes

- VDRIVE currently uses `c1541.exe` from **VICE 2.4** for all `LOAD`, `SAVE`, and directory requests.
- VICE 3.9’s version of `c1541.exe` appears incompatible — needs investigation.
- Eventually, VDRIVE will implement its own `ILoad` / `ISave` interface to eliminate reliance on VICE — but using `c1541.exe` saved a lot of time and allowed faster prototyping.
- This release is intended for developers and technically inclined users. Setup requires compiling and assembling. Prebuilt binaries will be provided once the project nears completion.
- The simple PRG support (no disk) will be fixed soon by wrapping PRG in new D64 from c1541.exe but for now it can be loaded by just mounting it.
- Latest changes allow for multiple C64s to connect to a single VDRIVE server and share disk images so paired programming is now possible on C64 and can be done without ever leaving it.

---

### 🧪 Install Steps

1. **Flash the ESP8266**  
   Burn `ESP8266_Firmware.ino` to your WiFi modem. For now configuration has to be hard coded but included WifiSetup.BAS will be fixed soon so that configuration can be done on C64 directly.
   
   ⚠️ This will overwrite the modem firmware — but it can be re-flashed later as needed.

3. **Assemble the C64 Client**  
   Compile `vdrive.asm` using **CBM Studio**.

4. **Configure the Server**  
   Edit `appsettings.json` to point to your disk images.

5. **Run the VDRIVE Server**  
   Launch the C# .NET Core server.   
     `Should run on any OS with .NET Core runtime installed.` 

7. **Test on Real Hardware**  
   From your Commodore 64:

   - **a.** `LOAD "vdrive.prg"` from regular disk  
   - **b.** `SYS 49152` to enable VDRIVE  (49155 is disable)
   - **c.** `SYS 49158` to search for disk images  
     Results include sequence numbers (1, 2, 3, 4) and descriptions or filenames.  
     Enter the number to mount  
   - **d.** `SYS 49161` to mount a different floppy disk from the results (valid until next search)  
     Enter the sequence number from the search.  
     You can switch to any previously found floppy by entering its number again without re-searching  
   - **e.** `LOAD`/`SAVE` from your VDRIVE

---

### 🚧 Known Limitations

- VDRIVE runs at `$C000`, so any `LOAD` that hits that limit will crash VDRIVE. Eventually there will be an option to transfer directly to disk (even full images like D64) to modern SDIEC or other devices.
- The `setupwifi` is out of date — for now, Wi-Fi setup requires manual configuration in firmware.

> 🧠 VDRIVE is functional, but still evolving. Expect rough edges — and feel free to contribute or fork.
