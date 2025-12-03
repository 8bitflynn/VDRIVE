## Video Example
<a href="https://8bitflynn.io/Resources/Videos/VDRIVE_Jukebox.m4v" target="_blank">BASIC using VDRIVE API to LOAD PORTAL</a>. 

Typically API usage would be for assets like graphics and sound or executable code.

## Example Programs

This directory contains two minimal examples showing how to use **VDRIVE** programmatically to:

1. Search for a known floppy image by name (`DATA4.D64`)
2. Mount it by name (number works too, but names are more reliable)
3. LOAD a known file (portal) from that mounted disk into RAM, optionally using the secondary address to control the load location

### `testapimin-bas.bas`
A short, plain‑text BASIC program demonstrating the search → mount → load sequence with minimal code.

### `testapimin-ml.prg`
A Machine Language version of the same workflow, written for the **ACME assembler**, useful for integrating VDRIVE calls into assembly projects.

---

### VDRIVE API Jump Table & Pointers Reference

The following example shows the VDRIVE API entry points and programmatic interface pointers.  
These are the addresses you can `jmp` to or dereference when interacting with VDRIVE in either interactive or direct mode.

```asm
; interactive mode jmps
jmp enable_vdrive              ; $C000 / 49152
jmp disable_vdrive             ; $C003 / 49155
jmp vdrive_search_floppies     ; $C006 / 49158
jmp vdrive_mount_floppy        ; $C009 / 49161

; direct mode jmps (programmatic)
jmp vdrive_search_direct       ; $C00C / 49164
jmp vdrive_mount_direct        ; $C00F / 49167
jmp vdrive_iload_direct        ; $C012 / 49170
jmp vdrive_isave_direct        ; $C015 / 49173

; reboot WiC64 to original state
jmp reboot_wic64               ; $C018 / 49176

; programmatic interface pointers
api_user_input_ptr:
    !word user_input          ; $C01B/$C01C (49179/49180): Pointer to 64-byte input buffer
api_user_input_len_ptr:
    !word user_input_length   ; $C01D/$C01E (49181/49182): Pointer to input buffer length byte
api_http_url_ptr:
    !word http_url            ; $C01F/$C020 (49183/49184): Pointer to 80-byte URL buffer
api_response_buffer_ptr:
    !word response_buffer     ; $C021/$C022 (49185/49186): Pointer to 512-byte response buffer
api_vdrive_devnum_ptr:
    !word vdrive_devnum       ; $C023/$C024 (49187/49188): Pointer to device number byte
api_vdrive_retcode_ptr:
    !word vdrive_retcode      ; $C025/$C026 (49189/49190): Pointer to return code byte
api_search_result_count_ptr:
    !word search_result_count ; $C027/$C028 (49191/49192): Pointer to 16-bit search result count


---
