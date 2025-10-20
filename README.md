## VDRIVE
<img src="https://8bitflynn.io/Resources/Images/VDRIVE.png" alt="VDRIVE Logo" width="120" align="right"/>

**Wirelessly SEARCH/MOUNT disk images and LOAD/SAVE data to/from your Commodore 64!**

- VDRIVE is a free, open-source tool built for those who want modern flexibility in retro workflows. It reflects months of design, testing, and iteration — not a plug-and-play gimmick. If you prefer original hardware, that’s valid. If you want remote disk access, mount/unmount control, and HTTP support, VDRIVE is here for you.
- VDRIVE includes `IFloppyResolver` implementations for both local disks and several remote repositories. For remote sources, I’ve been actively requesting permission from repository owners. If I don’t hear back, I treat access as equivalent to browsing their site from a modern machine — respectful, read-only, and non-invasive. That said, I fully respect the wishes of content owners: if any repository owner prefers their site not be included, I’ll remove it immediately.

## VDRIVE Hardware

- **ESP8266 WiFi modem**  
  Acts as the wireless transport layer. Flashed with custom firmware to handle all requests.
- **ESP8266 WiFi**
  NOTE: The ESP8266 firmware can be used on other projects that just need to bridge TCP and Serial. This makes the hardware "invisible" to the C64 and any other devices using it. Becuase of this design, I was able to partially get the vdrive code working on Vice 3.9 because it has a RS232 to IP connection.
- These devices can be built with a ESP8266 and a Commodore 64 userport breakout board. I plan on providing some instructions on how to build them but there is likely information already out there. Also these can be bought from retro businesses and Ebay.

<div style="float: right; margin-left: 20px; margin-bottom: 10px;">
  <a href="https://8bitflynn.io/Resources/Images/ESP8266_C64_SerialHardware.jpg" target="_blank">
    <img src="https://8bitflynn.io/Resources/Images/ESP8266_C64_SerialHardware.jpg" alt="ESP8266 C64 Serial Hardware" width="250"/>
  </a>
</div>

## VDRIVE Signal

Docs will land at [8bitflynn.io](https://8bitflynn.io) when the dust settles.

---

### Notes

- This release is intended for developers and technically inclined users. Setup requires compiling and assembling. Pre-built binaries will be provided once the project nears completion.
- VDRIVE currently uses `c1541.exe` from **VICE 2.4** for all `LOAD`, `SAVE`, and directory requests.
- VICE 3.9’s version of `c1541.exe` appears incompatible — needs investigation.
- Eventually, VDRIVE will implement its own `ILoad` / `ISave` interface to eliminate reliance on VICE — but using `c1541.exe` saved a lot of time and allowed faster prototyping.
- PRG (no disk) files can be loaded by selecting them in search and issuing `LOAD "*",8,1` to inject directly into memory.
- Latest changes allow multiple C64s to connect to a single VDRIVE server and share disk images — enabling paired programming without leaving the machine.

---

### Install Steps

1. **Flash the ESP8266**  
   Burn `ESP8266_Firmware.ino` to your WiFi modem. For now, configuration must be hardcoded — but `WifiSetup.BAS` will soon allow setup directly from the C64.
   - To build the firmware in Sketch, add this URL to the "Additional Board Manager URLs" in the "Preferences" dialog:  
     `http://arduino.esp8266.com/stable/package_esp8266com_index.json`

   ⚠️ This will overwrite the modem firmware — but it can be re-flashed later as needed.

2. **Assemble the C64 Client**  
   Build `vdrive.asm` using **<a target="_blank" href="https://www.ajordison.co.uk/download.html">CBM Studio</a>**.

3. **Configure the Server**  
   Edit `appsettings.json` to point to your disk images.

4. **Run the VDRIVE Server**  
   Launch the C# .NET Core server.  
   Should run on any OS with .NET Core runtime installed.

5. **Test on Real Hardware**  
   From your Commodore 64:

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
- `setupwifi` is out of date — for now, Wi-Fi setup requires manual configuration in firmware.

> VDRIVE is functional, but still evolving. Expect rough edges — and feel free to contribute or fork.
