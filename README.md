## VDRIVE
<img src="https://8bitflynn.io/Resources/Images/VDRIVE.png" alt="VDRIVE Logo" width="120" align="right"/>

**Wirelessly SEARCH/MOUNT disk images and LOAD/SAVE data to/from your Commodore 64!**

- VDRIVE is a free, open-source tool built for those who want modern flexibility in retro workflows. It reflects months of design, testing, and iteration — not a plug-and-play gimmick. If you prefer original hardware, that’s valid. If you want remote disk access, mount/unmount control, and HTTP support, VDRIVE is here for you.
  
- VDRIVE was built with developers in mind and disk images can be shared right from your C64 in real time. Programmers, graphics artists, and sound designers can use the same VDRIVE server and access the latest changes right from your C64 from anywhere it can connect in real time! Local modern tools can be used to build binaries and assets and then saved to the local drive. VDRIVE searches any specified paths so saved resources can be loaded directly from the C64 without any copying making it easier to test those many iterations! The same is true from the C64 side, and any data saved to the disk image over VDRIVE can then be accessed on your modern machine making those tough to find bugs a lot easier to track down with tools like <a href="https://sourceforge.net/projects/c64-debugger/" target="_blank">C64Debugger</a>.

- VDRIVE includes `IFloppyResolver` <a href="https://github.com/8bitflynn/VDRIVE/tree/master/VDRIVE/Floppy" target="_blank">implementation's</a> for searching and mounting both local disks and several remote repositories. For remote sources, I’ve been actively requesting permission from repository owners. If I don’t hear back, I treat access as equivalent to browsing their site from a modern machine — respectful, read-only, and non-invasive. That said, I fully respect the wishes of content owners: if any repository owner prefers their site not be included, I’ll remove it immediately.

## VDRIVE Hardware

<img src="https://8bitflynn.io/Resources/Images/ESP8266_C64_SerialHardware.jpg" alt="ESP8266 C64 Serial Hardware" width="250" align="right"/>

- **ESP8266 WiFi modem**  
  Acts as the wireless transport layer. Flashed with custom firmware to handle all requests.

- **ESP8266 WiFi**  
  The firmware can be reused in other projects needing TCP-to-Serial bridging. This design makes the hardware "invisible" to the C64 and other connected devices. Thanks to this abstraction, partial VDRIVE functionality works in VICE 3.9 via its RS232-to-IP bridge.

- **ESP8266 / C64 BREAKOUT BOARD / DIY**  
  These devices can be built using an ESP8266 chip and a Commodore 64 userport breakout board. I plan to providing build instructions when I get a chance, though similar guides already exist. There are also pre-built units from Retro Vendors or on eBay. 

## VDRIVE Signal

Docs will land at <a href="https://8bitflynn.io/Projects" target="_blank">https://8bitflynn.io/Projects</a> when the dust settles.

---

### Notes

- This release is intended for developers and technically inclined users. Setup requires compiling and assembling. Pre-built binaries will be provided once the project nears completion.
- VDRIVE currently uses `c1541.exe` from **VICE 2.4** for all `LOAD`, `SAVE`, and directory requests.
- VICE 3.9’s version of `c1541.exe` is incompatible — but will likely have a implementation soon.
- PRG (no disk) files can be loaded by selecting them in search and issuing `LOAD "*",8,1` to inject directly into memory.
- Multiple C64s can connect to a single VDRIVE server and share disk images — enabling lots of possibilities locally or remotely.
- VDRIVE is optimized for PRG workflows and single-file compatibility. Multi-disk games, fastloaders, and custom loaders may not function as expected. Celebrate what works, and understand the limits.
- Future versions may expand compatibility by staging full disk images locally, unlocking broader support without compromising simplicity.

---

### Install Steps

1. **Flash the ESP8266**  
   Burn `ESP8266_Firmware.ino` to your WiFi modem. 
   - To build the firmware in [Arduino Sketch](https://www.arduino.cc/en/software/), add this URL to the "Additional Board Manager URLs" in the "Preferences" dialog:  
     `http://arduino.esp8266.com/stable/package_esp8266com_index.json`  
   ⚠️ This will overwrite the modem firmware — but it can be re-flashed later as needed.

2. **Build the C64 Client**  
   Build [`vdrive.asm`](https://github.com/8bitflynn/VDRIVE/blob/master/vdrive.asm) using [CBM Studio](https://www.ajordison.co.uk/download.html).  
   Build [`UP9600.asm`](https://github.com/bozimmerman/Zimodem/blob/master/cbm8bit/src/up9600.asm) from Bo Zimmerman's repository.

3. **Configure the Server**  
   Edit `appsettings.json` with the paths VDRIVE should search. (Optionally use a remote IFloppyResolver).

4. **Run the VDRIVE Server/Client**  
   Launch the C# .NET Core server. (or client if firmware is in server mode)
     - VDRIVE should run on <a href="https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md" target="_blank">any OS with .NET Core runtime</a> installed.
     - The VDRIVE dependency on Vice 2.4 "c1541.exe" to LOAD/SAVE/DIR means VDRIVE must use one of the operating sytems that Vice 2.4 supports.
     - VDRIVE implementation of <a href="https://style64.org/dirmaster" target="_blank">DirMaster</a> if functional! VDRIVE can now LOAD/SAVE/DIR using DirMaster through <a href="https://style64.org/cbmdisk" target="_blank">CBMdisk</a> which is the library portion if DirMaster.
     - VDRIVE implementation of <a href="https://vice-emu.sourceforge.io/index.html#download" target="_blank">Vice 3.9</a> is on the short list.
     
5. **Test on Real Hardware**  
   From your Commodore 64:  
   - `LOAD "UP9600.prg"` from regular disk
   - `LOAD "setupwifi.prg"` from regular disk, RUN PRG and enter WiFi information    
     → Wifi information will be stored in flash memory on ESP8266 so setup only needs to be done once   
   - `LOAD "vdrive.prg"` from regular disk  
   - `SYS 49152` to enable VDRIVE (`SYS 49155` disables it)
   - `SYS 49158` to search for disk images  
     → Results include sequence numbers (1, 2, 3…) and descriptions or filenames  
     → Enter the number to mount a disk from the search results  
   - `SYS 49161` to mount a different floppy from the results (valid until next search)  
     → You can switch to any previously found floppy by entering its number again  
   - Use `LOAD`/`SAVE` as usual — now routed through VDRIVE

---

### Known Limitations

- VDRIVE runs at `$C000`, so any `LOAD` that hits that region will crash it. Eventually, full image transfers (e.g., D64) to SDIEC or other devices will be supported.

> VDRIVE is functional, but still evolving. Expect rough edges — and feel free to contribute or fork.
