; ---------------------------------------------------------------------------
; VDRIVE patch stub ",W": write the mounted D64 to a real drive (SD2IEC), then mount it
;
; LOAD"*,W",8  (or ,W9 for drive 9) -> the server answers with this stub, load status $FE
; (run after load): VDRIVE loads it to $0801 and jumps to it. VDRIVE stays on: the stub
; LOADs the D64 from it in slices of up to 32K into a buffer at $1000, and writes each
; slice to the drive with the KERNAL's OPEN/CHKOUT/CHROUT (VDRIVE only takes over LOAD
; and SAVE, so these reach the real drive).
;
; On the drive (all names and commands come from the server, patched in below):
;   MD:VDRIVE, CD:VDRIVE          its own folder
;   NAME.D64 already there?       then nothing to write (written once, reused after)
;   @:NAME.TMP,P,W                written slice by slice, then
;   R:NAME.D64=NAME.TMP           renamed, so a broken write never looks finished
;   CD:NAME.D64                   mounted
; If the drive is on VDRIVE's own number, VDRIVE is switched off at the end, so the next
; LOAD goes to the drive. Then READY.
;
; Built with ACME; the bytes go into VDRIVE PatchStubs.cs (WriteStub) - rebuild that array
; whenever this file changes:
;   acme -f cbm -o patch_write.prg -l patch_write.sym patch_write.asm
; ---------------------------------------------------------------------------

vdrive_disable = $c003         ; VDRIVE jump table: puts back the LOAD/SAVE vectors it saved

setnam = $ffbd
setlfs = $ffba
open   = $ffc0
close  = $ffc3
chkin  = $ffc6
chkout = $ffc9
clrchn = $ffcc
chrin  = $ffcf
chrout = $ffd2
load   = $ffd5

msgflag = $9d                  ; KERNAL message mode ($80 = direct: SEARCHING/LOADING)
devnum  = $ba                  ; device of the LOAD that brought us here (VDRIVE's)

ptr     = $fb                  ; slice: next byte to write
buffer  = $1000                ; slices land here (up to 32K, to $8FFF)

* = $0801

    jmp start

; --- filled in by the server (offsets fixed: PatchStubs.cs) -----------------------
p_dev:      !byte 8            ; $0804 the drive to write to
p_slices:   !byte 1            ; $0805 number of slices
p_lastlen:  !word 0            ; $0806 bytes in the last slice (the others are 32K)
; commands and names, each [length][bytes], 40 bytes apiece
s_md:       !fill 40, 0        ; $0808 MD:VDRIVE
s_cdv:      !fill 40, 0        ; $0830 CD:VDRIVE
s_check:    !fill 40, 0        ; $0858 NAME.D64,P,R
s_write:    !fill 40, 0        ; $0880 @:NAME.TMP,P,W
s_rename:   !fill 40, 0        ; $08A8 R:NAME.D64=NAME.TMP
s_cdimg:    !fill 40, 0        ; $08D0 CD:NAME.D64
s_append:   !fill 40, 0        ; $08F8 NAME.TMP,P,A
p_run:      !byte 0            ; $0920 after mounting: 0 = stop, 1 = LOAD"*",8,1 + RUN, 2 = LOAD"*",8,1
p_next:     !byte 0            ; $0921 multi-disk: 0 = last disk, else the stub for the next disk...
s_chain:    !fill 40, 0        ; $0922 ...is LOADed with this name ("*,W8D2"); VDRIVE runs it in our place
s_label:    !fill 40, 0        ; $094A "DISK 1/2 " on screen (empty for one disk)
s_listopen: !fill 40, 0        ; $0972 last disk of a set: @:NAME.LST,P,W - the swap list
p_listlen:  !byte 0            ; $099A its length (0 = one disk: CD into it instead)
p_list:     !fill 256, 0       ; $099B one image per line ("//VDRIVE/:NAME-1A2B.D64")
p_force:    !byte 0            ; $0A9B 1 = ",W@": write it again even when it is already there
s_scratch:  !fill 40, 0        ; $0A9C S:NAME.D64 - the old copy goes before the rename (R: won't overwrite)
; -----------------------------------------------------------------------------------

slice_name: !text "*,W1A"      ; $0AC4 slice request to VDRIVE: disk (patched), slice (A = first)

start:
    lda devnum
    sta vdev
    lda #0
    sta msgflag                ; no SEARCHING/LOADING for the slice loads

    ; the command channel stays open until the end: closing it would close file 2 on the drive too
    lda #0
    jsr setnam
    lda #15
    ldx p_dev
    ldy #15
    jsr setlfs
    jsr open

    ; no swap list left over from an earlier game (it stays on until XS clears it, and replacing
    ; the list file while it is on kept the second disk from mounting)
    ldx #<c_xs_off
    ldy #>c_xs_off
    jsr command
    jsr status_ok

    ; its own folder (MD fails harmlessly when it's already there)
    ldx #<s_md
    ldy #>s_md
    jsr command
    ldx #<s_cdv
    ldy #>s_cdv
    jsr command
    jsr status_ok
    bne fail_drive

    ; already written once? then just mount it (unless ,W@)
    lda p_force
    bne write
    ldx #<s_check
    ldy #>s_check
    jsr open_file
    jsr status_ok              ; 00 = there
    php
    lda #2
    jsr close
    plp
    bne write
    ldx #<t_cached
    ldy #>t_cached
    jsr print_head             ; VDRIVE: ALREADY ON DRIVE n
    lda #13
    jsr chrout
    jmp finish

write:
    ldx #<t_writing
    ldy #>t_writing
    jsr print_head             ; VDRIVE: WRITING TO DRIVE n, then a dot per slice
    lda #' '
    jsr chrout
    jmp slice_loop

fail_drive:
    ldx #<t_drive
    ldy #>t_drive
    jsr print
    ldx #<status_text
    ldy #>status_text
    jsr print_raw
    jmp done

slice_loop:
    ; LOAD the next slice from VDRIVE to the buffer
    lda #5
    ldx #<slice_name
    ldy #>slice_name
    jsr setnam
    lda #1
    ldx vdev
    ldy #1                     ; to the address in its header ($1000)
    jsr setlfs
    lda #0
    jsr load
    bcc +
    ldx #<t_load
    ldy #>t_load
    jsr print
    jmp done

    ; how many bytes: 32K, or p_lastlen for the last slice
+   lda #0
    sta count
    lda #$80
    sta count+1
    lda slice_name+4
    sec
    sbc #'A'-1
    cmp p_slices
    bne +
    lda p_lastlen
    sta count
    lda p_lastlen+1
    sta count+1

    ; open the file: new for the first slice, append for the rest (it stays closed while VDRIVE
    ; loads: the drive lost an open file across those loads)
+   ldx #<s_append
    ldy #>s_append
    lda slice_name+4
    cmp #'A'
    bne +
    ldx #<s_write
    ldy #>s_write
+   jsr open_file
    jsr status_ok
    beq +
    lda #2
    jsr close
    jmp fail_drive

    ; write the slice
+   ldx #2
    jsr chkout
    bcc +
    jsr clrchn                 ; the drive won't take the file: stop, don't print it on the screen
    lda #2
    jsr close
    ldx #<t_chkout
    ldy #>t_chkout
    jsr print
    jmp done
+   lda #<buffer
    sta ptr
    lda #>buffer
    sta ptr+1
-   ldy #0                     ; (Y again each time: don't count on CHROUT keeping it)
    lda (ptr),y
    jsr chrout
    inc ptr
    bne +
    inc ptr+1
+   lda count
    bne +
    dec count+1
+   dec count
    lda count
    ora count+1
    bne -
    jsr clrchn
    lda #2
    jsr close
    jsr status_ok
    beq +
    jmp fail_drive
+   lda #'.'                   ; progress: one dot per slice
    jsr chrout

    inc slice_name+4
    lda slice_name+4
    sec
    sbc #'A'
    cmp p_slices
    beq +
    jmp slice_loop
+
    lda #13
    jsr chrout
    ldx #<s_scratch            ; an old copy (,W@) goes first: R: won't overwrite
    ldy #>s_scratch
    jsr command
    jsr status_ok              ; (01 FILES SCRATCHED, or 01 with 0 files: fine either way)
    ldx #<s_rename             ; only now does the image get its real name
    ldy #>s_rename
    jsr command
    jsr status_ok
    beq finish
    jmp fail_drive

; this disk is on the drive: next disk's stub, or (last disk) the swap list, mount and start
finish:
    lda p_next
    beq last_disk
    jsr clrchn
    lda #15
    jsr close
    lda s_chain                ; LOAD the next stub: VDRIVE jumps to it, this one is gone
    ldx #<(s_chain+1)
    ldy #>(s_chain+1)
    jsr setnam
    lda #1
    ldx vdev
    ldy #1
    jsr setlfs
    lda #0
    jsr load
    ldx #<t_load               ; only back here when that failed
    ldy #>t_load
    jsr print
    jmp done

last_disk:
    lda p_listlen
    beq mount
    ldx #<s_listopen           ; a set: write the swap list (XS below mounts its first disk)
    ldy #>s_listopen
    jsr open_file
    jsr status_ok
    beq +
    lda #2
    jsr close
    jmp fail_drive
+   ldx #2
    jsr chkout
    lda #0
    sta count
-   ldy count
    lda p_list,y
    jsr chrout
    inc count
    lda count
    cmp p_listlen
    bne -
    jsr clrchn
    lda #2
    jsr close
    jsr status_ok
    beq mount
    jmp fail_drive

mount:
    ldx #<s_cdimg
    ldy #>s_cdimg
    jsr command
    jsr status_ok
    beq +
    jmp fail_drive
+
    lda p_dev
    cmp vdev
    bne +
    jsr vdrive_disable         ; same drive number as VDRIVE: LOAD goes to the drive from now on
+   lda p_run
    bne start_game
    ldx #<t_ready
    ldy #>t_ready
    jsr print

done:
    jsr clrchn
    lda #15
    jsr close
    lda #$80
    sta msgflag
    jmp ($a002)                ; BASIC warm start: READY.

; LOAD"*",8,1 (+ RUN) from the drive: from the stack page, since the game loads over this stub
start_game:
    ldx #<t_mounted
    ldy #>t_mounted
    jsr print
    jsr clrchn
    lda #15
    jsr close
    ldx #runner_end - runner_src - 1
-   lda runner_src,x
    sta $0100,x
    dex
    bpl -
    lda p_dev                  ; patched into the copy
    sta r_dev+1
    lda p_run
    sta r_mode+1
    ldx #$ff                   ; never comes back here
    txs
    lda #$80
    sta msgflag                ; the drive's SEARCHING/LOADING show again
    jmp $0100

runner_src:
!pseudopc $0100 {
    lda #1
    ldx #<r_star
    ldy #>r_star
    jsr setnam
    lda #1
r_dev:
    ldx #8                     ; patched: the drive
    ldy #1
    jsr setlfs
    lda #0
    jsr load
    bcs r_error
    stx $2d                    ; end of program, as BASIC's LOAD leaves it
    sty $2e
r_mode:
    lda #1                     ; patched: 1 = RUN, 2 = READY
    cmp #1
    bne r_ready
    jsr $a659                  ; CLR, start of program
    jmp $a7ae                  ; RUN
r_error:
    tax                        ; KERNAL error number
    jmp ($0300)
r_ready:
    jmp ($a002)
r_star:
    !text "*"
}
runner_end:

; --- drive helpers ------------------------------------------------------------------

; send the string at X/Y ([len][bytes]) on the open command channel; status_ok reads the answer
command:
    stx ptr
    sty ptr+1
    ldy #0
    lda (ptr),y
    sta count                  ; length
    ldx #15
    jsr chkout
    lda #0
    sta count+1                ; index, kept in memory (don't count on CHROUT keeping Y)
-   inc count+1
    ldy count+1
    lda (ptr),y
    jsr chrout
    lda count+1
    cmp count
    bne -
    lda #13
    jsr chrout
    jmp clrchn

; file 2, secondary 2, with the name at X/Y
open_file:
    jsr name_xy
    lda #2
    ldx p_dev
    ldy #2
    jsr setlfs
    jmp open

; SETNAM from a [len][bytes] string at X/Y
name_xy:
    stx ptr
    sty ptr+1
    ldy #0
    lda (ptr),y
    inc ptr
    bne +
    inc ptr+1
+   ldx ptr
    ldy ptr+1
    jmp setnam

; read the drive's status line into status_text; Z=1 when it starts with "00"
status_ok:
    ldx #15
    jsr chkin
    ldy #0
-   jsr chrin
    cmp #13
    beq +
    sta status_text,y
    iny
    cpy #39
    bne -
+   lda #13
    sta status_text,y
    lda #0
    sta status_text+1,y
    jsr clrchn
    lda status_text
    cmp #'0'
    bne +
    lda status_text+1
    cmp #'0'
+   rts

; --- screen ---------------------------------------------------------------------------

; the text at X/Y, then the drive number (8-30)
print_head:
    txa
    pha
    tya
    pha
    ldx #<t_vdrive
    ldy #>t_vdrive
    jsr print
    ldx #<(s_label+1)          ; "DISK 1/2 " (length-prefixed; zero after it)
    ldy #>(s_label+1)
    jsr print_raw
    pla
    tay
    pla
    tax
    jsr print_raw
    lda p_dev
    ldx #'0'-1
-   inx                        ; tens
    sec
    sbc #10
    bcs -
    adc #10+'0'
    pha
    txa
    cmp #'0'
    beq +
    jsr chrout
+   pla
    jmp chrout

print:                         ; zero-terminated text at X/Y, on the screen
    stx ptr
    sty ptr+1
    jsr clrchn                 ; (CLRCHN changes A and X: the address is kept in ptr first)
    jmp print_ptr
print_raw:                     ; the same, without CLRCHN
    stx ptr
    sty ptr+1
print_ptr:
    ldy #0
-   lda (ptr),y
    beq +
    jsr chrout
    iny
    bne -
+   rts

c_xs_off: !byte 2
          !text "XS"
t_vdrive: !pet "vdrive: ", 0
t_writing: !pet "writing to drive ", 0
t_cached: !pet "already on drive ", 0
t_mounted: !pet "mounted", 13, 0
t_drive:  !pet 13, "?drive: ", 0
t_load:   !pet 13, "?vdrive load error", 13, 0
t_chkout: !pet 13, "?drive: file not open", 13, 0
t_ready:  !pet "mounted. load", 34, "*", 34, ",8,1 to start", 13, 0

vdev:        !byte 0
count:       !word 0
status_text: !fill 42, 0

stub_end:
!if stub_end > buffer { !error "stub runs into the slice buffer" }
