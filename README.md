## 🧠 VDRIVE
<img src="https://8bitflynn.io/Resources/Images/VDRIVE.png" alt="VDRIVE Logo" width="120" align="right"/>

**Wirelessly LOAD/SAVE data to your Commodore 64!**  

---

### ⚙️ Notes

- VDRIVE currently uses `c1541.exe` from **VICE 2.4** for all `LOAD`, `SAVE`, and directory requests.
- VICE 3.9’s version of `c1541.exe` appears incompatible — needs investigation.
- Eventually, VDRIVE will implement its own `ILoad` / `ISave` interface to eliminate reliance on VICE — but using `c1541.exe` saved a lot of time and allowed faster prototyping.

---

### 🧪 Install Steps

1. **Flash the ESP8266**  
   Burn `ESP8266_Firmware.ino` to your WiFi modem.  
   ⚠️ This will overwrite the modem firmware — but it can be re-flashed later as needed.

2. **Assemble the C64 Client**  
   Compile `vdrive.asm` using **CBM Studio** (6510 assembly flavor).

3. **Configure the Server**  
   Edit `appsettings.json` to point to your disk image.  
   🔁 Restart required to change disks (until this is fixed).

4. **Run the VDRIVE Server**  
   Launch the C# .NET Core server.

5. **Test on Real Hardware**  
   Try `LOAD` / `SAVE` from your Commodore 64!


---

### 🚧 Known Limitations

- VDRIVE runs @ $C000 so any LOAD that hits that limit will crash VDRIVE. Eventually there will be an option to transfer directly to disk (even full images like D64) to modern SDIEC or other devices.
- VDRIVE currently has to be re-started to change disk image but only because I have not finished the assembly to request floppy changes and it will soon work as expected.

> 🧠 VDRIVE is functional, but still evolving. Expect rough edges — and feel free to contribute or fork.

