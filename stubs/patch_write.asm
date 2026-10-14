; ---------------------------------------------------------------------------
; VDRIVE patch stub ",W": write the mounted D64 to a real drive (SD2IEC), then mount it
;
; LOAD"*,W",8  (or ,W9 for drive 9) -> the server answers with this stub, load status $FE
; (run after load): VDRIVE loads it to $0801 and jumps to it. VDRIVE stays on: the stub
; LOADs the D64 from it in slices of up to 32K into a buffer at $1000, and writes each
; slice to the drive. A drive that answers JiffyDOS (SD2IEC) gets the slice with the
; JiffyDOS protocol, about 6x faster (jiffy module at the end); any other drive gets it with
; the KERNAL's CHKOUT/CHROUT (VDRIVE only takes over LOAD and SAVE, so these reach the real
; drive).
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
;   powershell -ExecutionPolicy Bypass -File stubs\build_stubs.ps1   (from the solution folder: assembles
;   both stubs and updates their arrays; -Check only compares. Needs only ACME.)
; by hand: acme -f plain -o patch_write.bin patch_write.asm  (-f plain: this ACME's -f cbm writes Intel HEX)
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

; jiffy module: copied up at start (its bytes sit where the first slice lands)
PORT    = $dd00                ; serial bus: bit3 ATN, bit4 CLK, bit5 DATA out (1 = pull low),
                               ; bit6 CLK, bit7 DATA in (1 = released)
jtables = $9000                ; 4 bit-pair tables, page aligned ($9000-$93FF)
jrun    = $9400                ; the module runs here
base    = $02                  ; $DD00 bits 0-2 (VIC bank, RS232) with every line released
to0     = $f7                  ; ready-wait timeout (3 bytes)
to1     = $f8
to2     = $f9
peoi    = $fa                  ; written at +60: CLK low = more follows, released = last byte
pend    = $fd                  ; written the moment a byte is acknowledged
ysc     = $fe                  ; $D011 bits 0-2: which raster lines are bad lines

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
s_run:      !fill 17, 0        ; $0AC9 the program to start: the first PRG in the directory ("*" when none;
                               ; LOAD"*" would take a USR/SEQ separator line that comes before it)
p_prg:      !byte 0            ; $0ADA 1 = a single program, not a disk image: copied, nothing to mount or start

start:
    ldx #0                     ; the jiffy module up to $9400 (before a slice lands on it)
-   lda jiffy_src,x
    sta jrun,x
    lda jiffy_src+$100,x
    sta jrun+$100,x
    lda jiffy_src+$200,x
    sta jrun+$200,x
    inx
    bne -
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
    ldx #15                    ; a drive there? (CHKOUT is the first time the bus is used: without
    jsr chkout                 ; this check the commands went to the screen and the status read
    php                        ; waited on the keyboard)
    jsr clrchn
    plp
    bcs no_drive

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
    jsr print_head             ; VDRIVE: DISK 1/2 ALREADY ON #8
    lda #13
    jsr chrout
    lda p_prg                  ; a program: that's all
    beq +
    jmp done
+   jmp finish

write:
    ldx #<t_writing
    ldy #>t_writing
    jsr print_head             ; VDRIVE: DISK 1/2 WRITING TO #8, then a dot per slice (fits 40 columns)
    lda #' '
    jsr chrout
    jmp slice_loop

not_mounted:
    lda #0                     ; no "DISK 3/3 " here: one line of 40 columns (VDRIVE: SAVED ON #12, NOT MOUNTED)
    sta s_label+1
    ldx #<t_written
    ldy #>t_written
    jsr print_head             ; VDRIVE: SAVED ON #12
    ldx #<t_nomount
    ldy #>t_nomount
    jsr print                  ; ", NOT MOUNTED"
    jmp done

no_drive:
    lda #15
    jsr close
    lda #$80
    sta msgflag
    ldx #5                     ; ?DEVICE NOT PRESENT ERROR
    jmp ($0300)

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

    ; write the slice: JiffyDOS when the drive answers it, else CHKOUT/CHROUT
+   jsr jwrite
    cmp #1
    beq kernal_write
    cmp #0
    beq slice_sent
    lda #2                     ; the drive stopped answering mid-slice
    jsr close
    ldx #<t_jiffy
    ldy #>t_jiffy
    jsr print
    jmp done
kernal_write:
    ldx #2
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
slice_sent:
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
    lda p_prg
    beq +
    lda #0                     ; a program: VDRIVE: SAVED ON #12
    sta s_label+1
    ldx #<t_written
    ldy #>t_written
    jsr print_head
    lda #13
    jsr chrout
    jmp done
+   lda p_listlen
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
    jmp not_mounted            ; a drive that stores files but can't mount images (IDE64): written, that's all
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
    ldx s_run                  ; the name, into the copy
    stx r_len+1
-   lda s_run,x
    sta r_name-1,x
    dex
    bne -
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
r_len:
    lda #1                     ; patched: the name's length
    ldx #<r_name
    ldy #>r_name
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
    ldx #3                     ; RUN typed into the keyboard buffer: READY. runs it, the way a
-   lda r_keys,x               ; user would. An autostart loader (one that took over $0302 as
    sta $0277,x                ; it loaded) gets control at READY. first, as on a real drive
    dex
    bpl -
    lda #4
    sta $c6
r_ready:
    lda #$76                   ; then end the way BASIC's own LOAD does in direct mode ($E1AB):
    ldy #$a3                   ; READY., CLR, relink, and into the main loop through $0302. Not
    jsr $ab1e                  ; the warm start ($A002): that goes through $0300 first, and an
    jmp $a52a                  ; autostart loader may have loaded junk there (California Games)
r_error:
    tax                        ; KERNAL error number
    jmp $a437                  ; BASIC: ?ERROR, READY. (not through $0300, see above)
r_keys:
    !byte $52, $55, $4e, $0d   ; R U N return
r_name:
    !fill 16, 0                ; patched: the name
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
t_writing: !pet "writing to #", 0
t_cached: !pet "already on #", 0
t_written: !pet "saved on #", 0
t_nomount: !pet ", not mounted", 13, 0
t_mounted: !pet "mounted", 13, 0
t_drive:  !pet 13, "?drive: ", 0
t_load:   !pet 13, "?vdrive load error", 13, 0
t_chkout: !pet 13, "?drive: file not open", 13, 0
t_jiffy:  !pet 13, "?drive: jiffydos transfer stopped", 13, 0
t_ready:  !pet "mounted. load", 34, "*", 34, ",8,1 to start", 13, 0

vdev:        !byte 0
count:       !word 0
status_text: !fill 42, 0

resident_end:
!if resident_end > buffer { !error "stub runs into the slice buffer" }

; --- jiffy module ----------------------------------------------------------------------
; Copied to $9400 at start: these bytes sit at $1000+, where the first slice lands.
;
; JiffyDOS write, as the SD2IEC receives it (sd2iec iec.c, llfl-jiffydos.c):
;  - Detection: during LISTEN under ATN the C64 holds CLK low for a while before the last
;    bit. A JiffyDOS drive answers by pulling DATA low for about 101 us; it then takes the
;    data bytes of this LISTEN with the JiffyDOS protocol (until the next ATN).
;  - A byte: the drive releases DATA (ready). The C64 releases CLK at time 0, then puts two
;    bits at a time on CLK and DATA (pulled low = 1):
;       +10 us bit4 CLK, bit5 DATA    +23 us bit6 CLK, bit7 DATA
;       +34 us bit3 CLK, bit1 DATA    +47 us bit2 CLK, bit0 DATA
;       +60 us CLK low = more follows, CLK released = last byte (EOI)
;    The drive samples each pair in the middle of its window and pulls DATA low at +73 us.
;  - No handshake per bit: the loop below is cycle exact, with interrupts off and no byte
;    started where the screen will steal cycles (a bad line).
; The whole LISTEN/SECOND/UNLISTEN is done here, on the serial bus itself, not through the
; KERNAL (the IDE64 hooks those routines). A drive that doesn't answer - a 1541 without
; JiffyDOS, the IDE64's own drive number, nobody at all - gets the KERNAL path instead.

jiffy_src:
!pseudopc jrun {

; write count bytes from buffer to file 2 (open, secondary 2) on p_dev
; A = 0 sent, 1 = no JiffyDOS here (use CHKOUT/CHROUT), 2 = the drive stopped answering
jwrite:
    lda jmode
    cmp #2
    bne +
    lda #1                     ; found out on an earlier slice
    rts
+   cmp #0
    bne +
    jsr jtables_build          ; first slice
+   lda #0
    sta jflag
    sei
    lda PORT
    ora #$08                   ; ATN low
    sta PORT
    jsr bus_pre
    lda p_dev
    ora #$20                   ; LISTEN
    sec                        ; ...watching for the JiffyDOS answer
    jsr bus_tx
    bcs jw_none
    jsr bus_pre
    lda #$62                   ; SECOND: data, secondary 2
    clc
    jsr bus_tx
    bcs jw_none
    lda PORT
    and #$f7                   ; ATN released: data follows
    sta PORT
    lda jflag
    beq jw_none                ; LISTEN wasn't answered: not JiffyDOS
    lda #1
    sta jmode
    jsr jsend
    pha
    jsr bus_unlisten
    cli
    pla
    rts
jw_none:
    jsr bus_unlisten           ; (harmless when nobody listened)
    cli
    lda #2
    sta jmode
    lda #1
    rts

jmode: !byte 0                 ; 0 = not tried yet, 1 = JiffyDOS, 2 = KERNAL
jflag: !byte 0
jdet:  !byte 0
jbyte: !byte 0
bitcnt: !byte 0
endlo: !byte 0                 ; address of the last byte
endhi: !byte 0
save_d015: !byte 0

; the slice, buffer .. buffer+count-1, JiffyDOS. A = 0 sent, 2 = no answer
jsend:
    lda #<buffer
    sta ptr
    clc
    adc count
    sta endlo
    lda #>buffer
    sta ptr+1
    adc count+1
    sta endhi
    lda endlo                  ; minus one: the last byte
    bne +
    dec endhi
+   dec endlo
    lda $d011
    and #7
    sta ysc
    lda $d015
    sta save_d015
    lda #0
    sta $d015                  ; sprites steal cycles too

js_byte:
    ldy #0
    lda base
    ora #$10
    sta peoi                   ; CLK low at +60: more follows
    sta pend
    lda ptr+1
    cmp endhi
    bne +
    lda ptr
    cmp endlo
    bne +
    lda base
    sta peoi                   ; last byte: CLK released at +60 = EOI
    ora #$18
    sta pend                   ; ...and at its ack CLK and ATN low at once: the drive loops
                               ; straight back for another byte, and a released CLK would
                               ; give it one more (it did: one byte too many)
+   lda (ptr),y
    tax
    sty to0
    sty to1
    lda #$f0
    sta to2                    ; about 15 s for the drive to be ready

js_rdy:                        ; drive ready: DATA released
    bit PORT
    bmi js_line
    inc to0
    bne js_rdy
    inc to1
    bne js_rdy
    inc to2
    bne js_rdy
    jmp js_fail

js_line:                       ; the byte needs about 1.3 raster lines free of bad lines
    lda $d011
    bmi js_safe                ; lines 256+: border
    lda $d012
    cmp #$f8
    bcs js_safe                ; below the last bad line
    cmp #$2d
    bcc js_safe                ; above the first
    sbc ysc                    ; (C = 1)
    and #7
    beq js_line                ; this line is a bad line
    cmp #5
    bcs js_line                ; one is coming within the next two
js_safe:
    lda base                   ; 3

    ; ---- cycle exact: writes at 0, +10, +23, +34, +47, +60 cycles (~us)
    sta PORT                   ; 4  time 0: CLK released
    lda jtables,x              ; 4
    nop                        ; 2
    sta PORT                   ; 4  +10
    lda jtables+$100,x         ; 4
    nop                        ; 2
    bit $ff                    ; 3
    sta PORT                   ; 4  +23
    lda jtables+$200,x         ; 4
    bit $ff                    ; 3
    sta PORT                   ; 4  +34
    lda jtables+$300,x         ; 4
    nop                        ; 2
    bit $ff                    ; 3
    sta PORT                   ; 4  +47
    lda peoi                   ; 3
    nop                        ; 2
    nop                        ; 2
    nop                        ; 2
    sta PORT                   ; 4  +60
    ; ---- the drive pulls DATA low at about +73
    lda pend                   ; ready in A: answered the moment it comes
    ldx #0
-   bit PORT
    bpl js_ack
    dex
    bne -
    jmp js_fail
js_ack:
    sta PORT                   ; CLK low (and ATN low after the last byte)
    lda ptr
    cmp endlo
    bne +
    lda ptr+1
    cmp endhi
    beq js_done
+   inc ptr
    bne +
    inc ptr+1
+   jmp js_byte
js_done:
    lda save_d015
    sta $d015
    lda #0
    rts
js_fail:
    lda base
    ora #$10
    sta PORT                   ; ATN released, CLK low
    lda save_d015
    sta $d015
    lda #2
    rts

; bit-pair tables: what to write to $DD00 for each byte value, one table per step
jtables_build:
    lda PORT
    and #$07
    sta base
    ldx #0
-   txa
    and #$10                   ; bit4 -> CLK
    beq +
    lda #$10
+   sta jbyte
    txa
    and #$20                   ; bit5 -> DATA
    ora jbyte
    ora base
    sta jtables,x
    txa
    lsr
    lsr
    and #$30                   ; bit6 -> CLK, bit7 -> DATA
    ora base
    sta jtables+$100,x
    txa
    and #$08                   ; bit3 -> CLK
    asl
    sta jbyte
    txa
    and #$02                   ; bit1 -> DATA
    asl
    asl
    asl
    asl
    ora jbyte
    ora base
    sta jtables+$200,x
    txa
    and #$04                   ; bit2 -> CLK
    asl
    asl
    sta jbyte
    txa
    and #$01                   ; bit0 -> DATA
    asl
    asl
    asl
    asl
    asl
    ora jbyte
    ora base
    sta jtables+$300,x
    inx
    bne -
    rts

; --- the serial bus by hand (as KERNAL $ED36-$EDAC, $EDFE) ---
bus_pre:                       ; CLK low, DATA released, 1 ms
    jsr clk_lo
    jsr dat_hi
    txa
    ldx #$b8
-   dex
    bne -
    tax
    rts

; send A under ATN. C = 1: hold CLK low before the last bit and watch for the JiffyDOS
; answer (jflag). C = 1 on return: nobody there, or no acknowledge
bus_tx:
    sta jbyte
    lda #0
    ror
    sta jdet
    jsr dat_hi
    jsr dat_in
    bcs bt_fail                ; DATA high: no device on the bus
    jsr clk_hi
-   jsr dat_in
    bcc -                      ; every listener ready
    jsr clk_lo
    lda #8
    sta bitcnt
bt_bit:
    lda bitcnt
    cmp #1
    bne bt_send
    bit jdet
    bpl bt_send
    ldx #64                    ; about 700 us; the answer comes after 218 us, for 101 us
-   lda PORT
    bpl bt_seen
    dex
    bne -
    beq bt_send
bt_seen:
    inc jflag
-   lda PORT
    bpl -                      ; until the drive lets go again
bt_send:
    jsr dat_in
    bcc bt_fail
    ror jbyte
    bcs +
    jsr dat_lo
    jmp ++
+   jsr dat_hi
++  jsr clk_hi
    nop
    nop
    nop
    nop
    lda PORT
    and #$df                   ; DATA released
    ora #$10                   ; CLK low
    sta PORT
    dec bitcnt
    bne bt_bit
    ldx #100                   ; about 1 ms for the acknowledge
-   lda PORT
    bpl +
    dex
    bne -
    beq bt_fail
+   clc
    rts
bt_fail:
    sec
    rts

bus_unlisten:                  ; ATN, UNLISTEN, then every line released
    lda PORT
    ora #$08
    sta PORT
    jsr bus_pre
    lda #$3f
    clc
    jsr bus_tx
    lda PORT
    and #$f7                   ; ATN released
    sta PORT
    ldx #10
-   dex
    bne -
    lda PORT
    and #$cf                   ; CLK and DATA released
    sta PORT
    rts

clk_hi: lda PORT
        and #$ef
        sta PORT
        rts
clk_lo: lda PORT
        ora #$10
        sta PORT
        rts
dat_hi: lda PORT
        and #$df
        sta PORT
        rts
dat_lo: lda PORT
        ora #$20
        sta PORT
        rts
dat_in: lda PORT               ; C = DATA (1 = released)
        cmp PORT
        bne dat_in
        asl
        rts
}
jiffy_end:
!if jiffy_end - jiffy_src > $300 { !error "jiffy module is more than the 768 bytes start copies" }
!if jtables + $400 > jrun { !error "tables run into the module" }

stub_end:
