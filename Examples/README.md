## Demo Video
[BASIC using API to SEARCH/MOUNT/LOAD](https://8bitflynn.io/Resources/Videos/VDRIVE_BASIC_ILOAD.m4v)

Typical API usage: graphics, sound, or executable code.

---

## Example Programs

### Load Workflow
- **`testload.bas`** — minimal BASIC program showing search → mount → load.
- **`testload-ml.asm`** — ACME assembler version, integrates VDRIVE LOAD calls.

### Save Workflow
- **`testsave.bas`** — minimal BASIC program showing search → mount → save.  

  Edit lines 200/210 of this PRG to change the SAVE memory range.

  Example: saving screen memory $0400–$0800:

```BASIC
      200 poke 193,0:poke 194,4
      210 poke 174,0:poke 175,8
      220 DELETE THIS LINE
```

- **`testsave-ml.asm`** — ACME assembler version, integrates VDRIVE SAVE calls.

---

## VDRIVE API Reference

### Jump Table (HEX / Decimal)

```asm
    ; enable / disable vdrive
    jmp enable_vdrive              ; $C000 / 49152
    jmp disable_vdrive             ; $C003 / 49155

    ; interactive mode
    jmp vdrive_search_floppies     ; $C006 / 49158
    jmp vdrive_mount_floppy        ; $C009 / 49161

    ; direct mode
    jmp vdrive_search_direct       ; $C00C / 49164
    jmp vdrive_mount_direct        ; $C00F / 49167
    jmp vdrive_iload_direct        ; $C012 / 49170 (or call ILOAD vector $0330–$0331)
    jmp vdrive_isave_direct        ; $C015 / 49173 (or call ISAVE vector $0332–$0333)

    ; reboot WiC64
    jmp reboot_wic64               ; $C018 / 49176
```

### Programmatic Interface Pointers

```asm
    api_user_input_ptr:        !word user_input          ; $C01B/$C01C (49179/49180)
    api_user_input_len_ptr:    !word user_input_length   ; $C01D/$C01E (49181/49182)
    api_http_url_ptr:          !word http_url            ; $C01F/$C020 (49183/49184)
    api_response_buffer_ptr:   !word response_buffer     ; $C021/$C022 (49185/49186)
    api_vdrive_devnum_ptr:     !word vdrive_devnum       ; $C023/$C024 (49187/49188)
    api_vdrive_retcode_ptr:    !word vdrive_retcode      ; $C025/$C026 (49189/49190)
    api_search_result_count_ptr: !word search_result_count ; $C027/$C028 (49191/49192)
```
