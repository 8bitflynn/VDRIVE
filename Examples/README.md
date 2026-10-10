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

  Edit lines 200/220 of this PRG to change the SAVE memory range.

  Example: saving screen memory $0400–$0800:

```BASIC
      200 poke 193,0:poke 194,4
      210 poke 174,0:poke 175,8

DELETE LINE 220
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

    ; patch stubs: starts a LOAD and hands it over once the status and address are in
    ; (C=0); the data waits on the WiC64 for the stub's own receive loop
    jmp vdrive_load_handoff        ; $C01B / 49179

    ; reserved for later entries (they return with C=1 for now)
    jmp vdrive_reserved            ; $C01E / 49182
    jmp vdrive_reserved            ; $C021 / 49185
```

### Programmatic Interface Pointers

```asm
    api_user_input_ptr:        !word user_input          ; $C024/$C025 (49188/49189)
    api_user_input_len_ptr:    !word user_input_length   ; $C026/$C027 (49190/49191)
    api_http_url_ptr:          !word http_url            ; $C028/$C029 (49192/49193)
    api_response_buffer_ptr:   !word response_buffer     ; $C02A/$C02B (49194/49195)
    api_vdrive_devnum_ptr:     !word vdrive_devnum       ; $C02C/$C02D (49196/49197)
    api_vdrive_retcode_ptr:    !word vdrive_retcode      ; $C02E/$C02F (49198/49199)
    api_search_result_count_ptr: !word search_result_count ; $C030/$C031 (49200/49201)
    api_token_ptr:             !word token               ; $C032/$C033 (49202/49203)
    api_session_id_ptr:        !word session_id          ; $C034/$C035 (49204/49205)
```

### Load Status Codes

- `$FF` — loaded.
- `$FE` — loaded, then run: VDRIVE jumps to the load address. The server uses it for patch stubs.
- anything else — error (`$04` = file not found).
