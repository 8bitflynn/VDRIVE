## 🧠 VDRIVE  
<img src="https://8bitflynn.io/Resources/Images/VDRIVE.png" alt="VDRIVE Logo" width="120" align="right"/>

**Wirelessly LOAD/SAVE data to your Commodore 64!**  


A work-in-progress tool for bridging retro hardware with modern workflows — built for clarity, control, and zero guesswork.

---

### ⚙️ Notes

- VDRIVE currently uses `c1541.exe` from **VICE 2.4** for all `LOAD`, `SAVE`, and directory requests.
- VICE 3.9’s version of `c1541.exe` appears incompatible — needs investigation.
- For now, just add the path to `c1541.exe` in your `appsettings.json`.
- Eventually, VDRIVE will implement its own `ILoad` / `ISave` interface to eliminate reliance on VICE — but using `c1541.exe` saved time and allowed faster prototyping.

---

### 🧪 Install Steps

1. **Flash the ESP8266**  
   Burn `ESP8266_Firmware.ino` to your WiFi modem.  
   ⚠️ This will overwrite the modem firmware — but it can be re-flashed later if needed.

2. **Assemble the C64 Client**  
   Compile `vdrive.asm` using **CBM Studio** (6510 assembly flavor).

3. **Configure the Server**  
   Edit `appsettings.json` to point to your disk image.  
   🔁 Restart required to change disks (until this is fixed).

4. **Run the VDRIVE Server**  
   Launch the C# .NET Core server.

5. **Test on Real Hardware**  
   Try `LOAD` / `SAVE` from your Commodore 64!
