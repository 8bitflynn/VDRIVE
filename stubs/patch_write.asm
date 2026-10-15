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

BLANK = 1                      ; 1: the screen is off while a slice is written (no bad lines: the jiffy
                               ; loop needn't wait for them); 0: it stays on, bytes wait for clear lines
TIMING = 0                     ; 1: a stopwatch prints where the time went after the last slice and waits
                               ; for a key (LOAD UNPK OPEN WRIT CLOS, 1/100 s); 0: none of it

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
; unpack (packed slices), in BASIC's zero page: BASIC isn't running, and nothing else runs while it unpacks
usrc    = $22                  ; next packed byte (2 bytes)
ubits   = $57                  ; the slice's bit table, copied here: the output runs over it (16 bytes, to $66)
ubyte   = $67                  ; the bits of the current 8 sectors, shifted out one at a time
usn     = $68                  ; sector number in the slice
usect   = $69                  ; sectors in the slice
uk      = $6a                  ; its empty sectors
buffer  = $1000                ; slices land here (up to 32K, to $8FFF)

; jiffy module: copied up at start (its bytes sit where the first slice lands)
PORT    = $dd00                ; serial bus: bit3 ATN, bit4 CLK, bit5 DATA out (1 = pull low),
                               ; bit6 CLK, bit7 DATA in (1 = released)
jtables = $9000                ; 4 bit-pair tables, page aligned ($9000-$93FF)
jrun    = $9400                ; the module runs here (up to 6 pages, to $99FF)
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
p_fills:    !fill 26, 0        ; $0ADB empty sectors in each slice: 0 = it comes as it is, else packed (unpack)

start:
    ldx #0                     ; the jiffy module (and unpack) up to $9400 (before a slice lands on it)
-   lda jiffy_src,x
    sta jrun,x
    lda jiffy_src+$100,x
    sta jrun+$100,x
    lda jiffy_src+$200,x
    sta jrun+$200,x
    lda jiffy_src+$300,x
    sta jrun+$300,x
    lda jiffy_src+$400,x
    sta jrun+$400,x
    lda jiffy_src+$500,x
    sta jrun+$500,x
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
    lda p_prg                  ; a program: loaded from the drive (prg_start)
    beq +
    jmp prg_start
+   jmp finish

write:
    ldx #<t_writing
    ldy #>t_writing
    jsr print_head             ; VDRIVE: DISK 1/2 WRITING TO #8, then a dot per slice (fits 40 columns)
    lda #' '
    jsr chrout
    jsr t_start                ; (stopwatch: from here)
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
    jmp new_error              ; (NEW first: see new_ready)

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

    ; a packed slice (its empty sectors came as one byte each): expanded to the buffer (stopwatch: LOAD, UNPK)
+   jsr t_slice

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
+   jsr t_open                 ; (stopwatch: OPEN)
    jsr jwrite
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
+   jsr kwrite                 ; the slice to file 2: IDEDOS's block WRITE, or byte by byte (in the module)
slice_sent:
    jsr t_write                ; (stopwatch: WRIT)
    jsr clrchn
    lda #2
    jsr close
    jsr status_ok
    beq +
    jmp fail_drive
+   jsr t_close                ; (stopwatch: CLOS)
    lda #'.'                   ; progress: one dot per slice
    jsr chrout

    inc slice_name+4
    lda slice_name+4
    sec
    sbc #'A'
    cmp p_slices
    beq +
    jmp slice_loop
+   jsr t_print                ; (stopwatch: the times, then a key)
    lda #13
    jsr chrout
    ldx #<s_scratch           ; an old copy (,W@) goes first: R: won't overwrite
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
    jmp prg_start              ; then loaded from the drive (and RUN if it's BASIC)
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
    jmp new_ready              ; NEW, then BASIC's warm start: READY.

; LOAD"*",8,1 (+ RUN) from the drive: from the stack page, since the game loads over this stub
start_game:
    ldx #<t_mounted
    ldy #>t_mounted
    jsr print
start_load:                    ; (a single program comes in here: nothing was mounted)
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
jm:     !word 0                ; bytes before the last one (count - 1)
jchunks: !byte 0               ; 256-byte chunks still to go (the first one shorter)
jy0:    !byte 0                ; Y at the start: the first chunk ends when Y wraps
save_d015: !byte 0
save_d011: !byte 0

; the slice, buffer .. buffer+count-1, JiffyDOS. A = 0 sent, 2 = no answer
; All bytes but the last go out in one tight loop: Y indexes 256-byte chunks, the first one the short one,
; so each chunk ends when Y wraps (nothing to compare per byte). Then the last byte goes through the same
; loop once more with EOI. The cycle-exact part is the same for every byte.
jsend:
    lda count                  ; jm = count - 1: the bytes before the last
    sec
    sbc #1
    sta jm
    lda count+1
    sbc #0
    sta jm+1
    lda jm                     ; chunks = (jm + 255) / 256
    clc
    adc #$ff
    lda jm+1
    adc #0
    sta jchunks
    lda #0                     ; Y starts at -jm (low byte), ptr = buffer - that: ptr + Y = buffer
    sec
    sbc jm
    sta jy0
    lda #<buffer
    sec
    sbc jy0
    sta ptr
    lda #>buffer
    sbc #0
    sta ptr+1
    lda $d011
    and #7
    sta ysc
    lda $d015
    sta save_d015
    lda #0
    sta $d015                  ; sprites steal cycles too
!if BLANK {
    lda $d011
    sta save_d011
    and #$6f                   ; screen off (bit 4)
    sta $d011
-   bit $d011                  ; bad lines only stop from the next frame on: wait for its start
    bpl -                      ; (down into lines 256+,
-   bit $d011
    bmi -                      ; then over the top: line 0, before the first bad line)
}

    lda base
    ora #$10
    sta peoi                   ; CLK low at +60: more follows
    sta pend
    ldy jy0
    lda jchunks
    beq js_last                ; a single byte: only the last one

js_next:
    lda (ptr),y
    tax
    bit PORT                   ; drive ready (DATA released)? Mostly yes, straight away
!if BLANK {
    bpl js_wait
} else {
    bmi js_go                  ; (the bad line check in between: js_wait too far for a branch)
    jmp js_wait
}
js_go:
!if BLANK = 0 {
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
}                              ; (BLANK: the screen is off, there are no bad lines)
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
    iny
    bne js_next
    inc ptr+1                  ; the next chunk
    dec jchunks
    bne js_next

js_last:                       ; all but the last sent - or the last one too
    lda peoi
    cmp base
    beq js_done                ; (EOI was set: the last byte is out)
    lda base
    sta peoi                   ; last byte: CLK released at +60 = EOI
    ora #$18
    sta pend                   ; ...and at its ack CLK and ATN low at once: the drive loops
                               ; straight back for another byte, and a released CLK would
                               ; give it one more (it did: one byte too many)
    inc ptr                    ; it's at ptr + Y (Y = 0 here): once more through the loop as Y = 255,
    bne +                      ; so ptr - 255 (then the loop ends right here again)
    inc ptr+1
+   dec ptr+1
    ldy #$ff
    lda #1
    sta jchunks
    jmp js_next

js_wait:                       ; the drive isn't ready yet: wait for it, about 15 s at most
    lda #0
    sta to0
    sta to1
    lda #$f0
    sta to2
jw_loop:
!if TIMING {
    inc jwait                  ; stopwatch: rounds spent waiting for the drive (23 cycles each)
    bne jw_count
    inc jwait+1
    bne jw_count
    inc jwait+2
jw_count:
}
    bit PORT
    bpl jw_more
    jmp js_go                  ; ready (too far back for a branch)
jw_more:
    inc to0
    bne jw_loop
    inc to1
    bne jw_loop
    inc to2
    bne jw_loop
    jmp js_fail
js_done:
    jsr js_screen
    lda #0
    rts
js_fail:
    lda base
    ora #$10
    sta PORT                   ; ATN released, CLK low
    jsr js_screen
    lda #2
    rts
js_screen:                     ; sprites (and the screen) back as they were
    lda save_d015
    sta $d015
!if BLANK {
    lda save_d011
    and #$7f
    sta $d011
}
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

; Expand a packed slice (A = its empty sectors, count = its length): the server sent each empty sector - 256
; bytes of one value - as that one byte (PatchStubs.BuildWriteSlice). In memory it ends where the slice ends:
; [16 bytes, a bit per sector, 1 = empty][the sectors in order] from $0FF0 + 255 x A. Expanded forward to $1000:
; every empty sector frees 255 bytes, so the output never reaches data that hasn't been read yet.
unpack:
    sta uk
    lda #$f0                   ; usrc = $0FF0 + 256 x A - A
    sec
    sbc uk
    sta usrc
    lda #$0f
    sbc #0
    clc
    adc uk
    sta usrc+1
    ldy #15                    ; the bit table first: the output runs over it
-   lda (usrc),y
    sta ubits,y
    dey
    bpl -
    lda usrc
    clc
    adc #16
    sta usrc
    bcc +
    inc usrc+1
+   lda #<buffer
    sta ptr
    lda #>buffer
    sta ptr+1
    lda count+1                ; sectors: count / 256, rounded up
    ldx count
    beq +
    clc
    adc #1
+   sta usect
    lda #0
    sta usn
u_sector:
    lda usn                    ; every 8 sectors: the next bit table byte
    and #7
    bne u_bit
    lda usn
    lsr
    lsr
    lsr
    tax
    lda ubits,x
    sta ubyte
u_bit:
    ldy #0
    lsr ubyte
    bcc u_copy
    lda (usrc),y               ; empty: its one byte, 256 times
    inc usrc
    bne u_fill
    inc usrc+1
u_fill:
    sta (ptr),y
    iny
    bne u_fill
    beq u_next                 ; (always)
u_copy:
    lda (usrc),y               ; the others: 256 bytes, forward (a short last sector: the bytes
    sta (ptr),y                ; after it are never written to the drive)
    iny
    bne u_copy
    inc usrc+1
u_next:
    inc ptr+1
    inc usn
    lda usn
    cmp usect
    bne u_sector
    rts

; The slice (buffer, count bytes) to file 2, CHKOUT already done (any drive without JiffyDOS). An IDE64 takes
; it in one call: IDEDOS's WRITE ($DEF1), "much faster than calling CHROUT a lot of times" (IDE64 user's guide
; 13.2.2: A = zero page address of a pointer to the data, X/Y = how many bytes). On a serial bus drive (a 1541,
; even with an IDE64 in the C64) WRITE gives an error without writing (C = 1): then byte by byte as before.
kwrite:
    lda #<buffer
    sta ptr
    lda #>buffer
    sta ptr+1
    jsr is_ide64
    bcs kw_bytes
    lda #ptr
    ldx count
    ldy count+1
    jsr $def1
    bcc kw_done                ; written
    lda #<buffer               ; not here: byte by byte (ptr from the start again)
    sta ptr
    lda #>buffer
    sta ptr+1
kw_bytes:
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
kw_done:
    rts

; C = 0: an IDE64 is in the C64 (IDE64 user's guide 13.2.1: "IDE" at $DE60-$DE62, each read twice; first make
; sure there's no old Action Replay there, which crashes when $DExx is read)
is_ide64:
    lda $df09
    cmp $df09
    bne +                      ; not steady: no Action Replay
    cmp #$78
    beq ide_no                 ; maybe one: leave $DExx alone
+   ldx #2
-   lda $de60,x
    cmp $de60,x
    bne ide_no
    cmp ide_sig,x
    bne ide_no
    dex
    bpl -
    clc
    rts
ide_no:
    sec
    rts
ide_sig: !byte $49, $44, $45   ; "IDE"

; A single program, written (or already on the drive): loaded back from the drive like a disk's game - the
; server put its name in s_run and p_run = 1 (RUN: a BASIC program) or 2 (LOAD,1 and READY.: its SYS is the
; user's). p_run 0 (an older server): just stop. On VDRIVE's own drive number VDRIVE goes off first, so the
; LOAD reaches the drive.
prg_start:
    lda p_run
    bne +
    jmp done
+   lda p_dev
    cmp vdev
    bne +
    jsr vdrive_disable
+   jmp start_load

; Back to BASIC without starting a game: this stub is still at $0801 and BASIC's pointers still take it
; for the program (LIST showed nonsense, RUN would run it). So first a NEW: an empty program at $0801,
; variables and strings gone. (Where the stub starts a game, that LOAD replaces it anyway.)
new_ready:
    jsr basic_new
    jmp ($a002)                ; BASIC warm start: READY.
new_error:                     ; X = the error number
    jsr basic_new
    jmp ($0300)                ; BASIC error: ?... ERROR, READY.
basic_new:
    lda #0
    sta $0801                  ; the end of the program right at the start
    sta $0802
    lda #<$0803
    sta $2d                    ; variables, arrays, end of arrays: just after it
    sta $2f
    sta $31
    lda #>$0803
    sta $2e
    sta $30
    sta $32
    lda $37                    ; strings: none (their bottom = the top of BASIC memory)
    sta $33
    lda $38
    sta $34
    rts

t_slice:                       ; right after a slice's LOAD: unpack it if packed (and stopwatch: LOAD, UNPK)
!if TIMING {
    ldx #0
    jsr tadd
}
    lda slice_name+4
    sec
    sbc #'A'
    tax
    lda p_fills,x
    beq +
    jsr unpack
!if TIMING {
    ldx #2
    jmp tadd
}
+   rts

; --- stopwatch (TIMING = 1): where a slice's time goes ----------------------------------------------
; CIA 2's two timers chained: A underflows every 1/100 s, B counts those down from $FFFF. They keep
; running with interrupts off (the jiffy write); nothing else here uses them (the WiC64 only reads $DD0D).
; Each mark adds the time since the previous mark to one of five totals, printed after the last slice.
!if TIMING = 0 {
t_start:
t_open:
t_write:
t_close:
t_print:
    rts
} else {
t_start:
    lda #0
    sta $dd0e                  ; both timers stopped
    sta $dd0f
    ldx #<10227                ; NTSC: 1.0227 MHz / 100
    ldy #>10227
    lda $02a6                  ; KERNAL: 1 = PAL
    beq +
    ldx #<9852                 ; PAL: 0.9852 MHz / 100
    ldy #>9852
+   stx $dd04
    sty $dd05
    lda #$ff
    sta $dd06
    sta $dd07
    sta tlast
    sta tlast+1
    lda #%01010001             ; B: counts A's underflows, loaded, running
    sta $dd0f
    lda #%00010001             ; A: continuous, loaded, running
    sta $dd0e
    lda #0
    ldx #9
-   sta tacc,x
    dex
    bpl -
    sta jwait
    sta jwait+1
    sta jwait+2
    rts

t_open:  ldx #4                ; the file opened (append), status read
         jmp tadd
t_write: ldx #6                ; the slice written
         jmp tadd
t_close: ldx #8                ; closed, status read
                               ; (falls through)
tadd:                          ; total X += time since the last mark
-   ldy $dd07                  ; B, read again if its high byte changed in between
    lda $dd06
    cpy $dd07
    bne -
    sta tnow
    sty tnow+1
    sec                        ; the timer counts down: elapsed = last - now
    lda tlast
    sbc tnow
    sta tdif
    lda tlast+1
    sbc tnow+1
    sta tdif+1
    clc
    lda tacc,x
    adc tdif
    sta tacc,x
    lda tacc+1,x
    adc tdif+1
    sta tacc+1,x
    lda tnow
    sta tlast
    lda tnow+1
    sta tlast+1
    rts

t_print:                       ; LOAD 1234 UNPK 56 OPEN 789 WRIT 1234 CLOS 567 (1/100 S)
    jsr clrchn
    lda #0                     ; BASIC's number work area back to zeros: unpack used it, and the print
    ldx #$70-$57               ; routine's normalising took unpack's count from $68 as the number's
-   sta $57,x                  ; fraction (LOAD came out as 1167.29099)
    dex
    bpl -
    sta tix
-   ldx tix
    lda t_labels,x
    tax
    ldy #>t_lab0
    jsr print_raw
    lda tix
    asl
    tax
    lda tacc,x
    pha
    lda tacc+1,x
    tay
    pla
    tax
    tya
    jsr $bdcd                  ; BASIC: print A (high) / X (low) as a number
    inc tix
    lda tix
    cmp #5
    bne -
    ldx #<t_lab6               ; WAIT: rounds spent waiting for the drive, / 256 (one = 5888 cycles, ~5.8 ms)
    ldy #>t_lab6
    jsr print_raw
    lda #0
    sta $68
    sta $70
    ldx jwait+1
    lda jwait+2
    jsr $bdcd
    ldx #<t_lab5
    ldy #>t_lab5
    jsr print_raw
    lda #0
    sta $c6                    ; empty keyboard buffer, then wait for a key
-   jsr $ffe4
    beq -
    rts

tlast:  !word 0
tnow:   !word 0
tdif:   !word 0
tacc:   !fill 10, 0            ; LOAD, UNPK, OPEN, WRIT, CLOS: 1/100 s each
tix:    !byte 0
t_labels: !byte <t_lab0, <t_lab1, <t_lab2, <t_lab3, <t_lab4
t_lab0: !pet 13, "load ", 0
t_lab1: !pet " unpk ", 0
t_lab2: !pet " open ", 0
t_lab3: !pet " writ ", 0
t_lab4: !pet " clos ", 0
t_lab5: !pet 13, "press a key", 13, 0
t_lab6: !pet " (1/100 s) wait ", 0
jwait:  !fill 3, 0             ; rounds of js_rdy (the drive not ready yet)
!if >t_lab0 != >t_lab4 { !error "stopwatch labels cross a page" }
}
}
jiffy_end:
!if jiffy_end - jiffy_src > $600 { !error "jiffy module is more than the 6 pages start copies" }
!if jtables + $400 > jrun { !error "tables run into the module" }

stub_end:
