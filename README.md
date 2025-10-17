## 🧠 VDRIVE
<img src="https://8bitflynn.io/Resources/Images/VDRIVE.png" alt="VDRIVE Logo" width="120" align="right"/>

**Wirelessly SEARCH/MOUNT disk images and LOAD/SAVE data to/from your Commodore 64!**  

- VDRIVE is a free, open-source tool built for those who want modern flexibility in retro workflows. It reflects months of design, testing, and iteration — not a plug-and-play gimmick. If you prefer original hardware, that’s valid. If you want remote disk access, mount/unmount control, and HTTP support, VDRIVE is here for you.

## 🕹️ VDRIVE Signal

Docs will land at [8bitflynn.io](https://8bitflynn.io) when the dust settles.

---

### ⚙️ Notes

- VDRIVE currently uses `c1541.exe` from **VICE 2.4** for all `LOAD`, `SAVE`, and directory requests.
- VICE 3.9’s version of `c1541.exe` appears incompatible — needs investigation.
- Eventually, VDRIVE will implement its own `ILoad` / `ISave` interface to eliminate reliance on VICE — but using `c1541.exe` saved a lot of time and allowed faster prototyping.
- This release is intended for developers and technically inclined users. Setup requires compiling and assembling. Prebuilt binaries will be provided once the project nears completion.
- The simple PRG support (no disk) will be fixed soon but for now if PRG is included in results, mount it and any loaded after that will be the PRG. Future version will just wrap it in D64.

---

### 🧪 Install Steps

1. **Flash the ESP8266**  
   Burn `ESP8266_Firmware.ino` to your WiFi modem.  
   ⚠️ This will overwrite the modem firmware — but it can be re-flashed later as needed.

2. **Assemble the C64 Client**  
   Compile `vdrive.asm` using **CBM Studio**.

3. **Configure the Server**  
   Edit `appsettings.json` to point to your disk images.

4. **Run the VDRIVE Server**  
   Launch the C# .NET Core server.

5. **Test on Real Hardware**  
   Test VDRIVE from your Commdore 64!

   a. LOAD "vdrive.prg" from regular disk.  
   b. SYS 49152 enable VDRIVE .  
   c. SYS 49158 search for disk images (results include sequence 1,2,3,4 and the description or filename), enter number to mount.
   d. SYS 49161 mount floppy disk (enter the sequence number from search). Any floppy result from search can be changed to by using this and entering in number without searching again.
   e. LOAD/SAVE from VDRIVE.
   
---

### 🚧 Known Limitations

- VDRIVE runs @ $C000 so any LOAD that hits that limit will crash VDRIVE. Eventually there will be an option to transfer directly to disk (even full images like D64) to modern SDIEC or other devices.
- The `setupwifi` is out of date so for now Wi-Fi setup requires manual configuration in firmware.


> 🧠 VDRIVE is functional, but still evolving. Expect rough edges — and feel free to contribute or fork.

