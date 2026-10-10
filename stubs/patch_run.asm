; ---------------------------------------------------------------------------
; VDRIVE patch stub ",P": write bytes anywhere in memory, then run
;
; LOAD"NAME,P",8  -> the server answers with this stub, load status $FE (run after
; load): VDRIVE loads it to $0100 and jumps to it. The server has put the program's
; name in it with ",P2" on the end. The stub asks VDRIVE for that (vdrive_load_handoff:
; request sent, status and address read, the data still waiting) and takes the data
; itself, so it can write over VDRIVE at $C000 too. The data is a list of segments:
;
;   [addr lo][addr hi][len lo][len hi][len bytes]  ...  [go lo][go hi][0][0]
;
; len is sent as 65536 - length (counts up to 0). Every byte is stored with all RAM
; banked in ($01 = $34), so $A000-$FFFF, the RAM under I/O included, takes it too.
; The last record (length 0) gives where to go: run_basic, READY ($A474), or a start
; address.
;
; Lives at the bottom of the stack page: the stack is reset to $01FF and stays above
; the end of this code. The server never writes $0100-$01FF or ptr/count.
;
; Built with ACME; the bytes go into VDRIVE PatchStubs.cs - rebuild that array
; whenever this file changes:
;   acme -f cbm -o patch_run.prg -l patch_run.sym patch_run.asm
; ---------------------------------------------------------------------------

vdrive_load_handoff = $c01b    ; VDRIVE jump table

setnam  = $ffbd
msgflag = $9d                  ; KERNAL message mode ($80 = direct: SEARCHING/LOADING)

ptr   = $fb                    ; where the bytes go
count = $fd                    ; 65536 - bytes left in this segment

* = $0100

start:
    ldx #$ff                   ; never returns: VDRIVE and BASIC are about to be overwritten
    txs
    lda #0
    sta msgflag                ; no SEARCHING/LOADING for the second request
    lda fname_len
    ldx #<fname
    ldy #>fname
    jsr setnam
    jsr vdrive_load_handoff
    bcs fail
    sei                        ; the IRQ vector at $0314 may be overwritten

segment:
    jsr getb
    sta ptr
    jsr getb
    sta ptr+1
    jsr getb
    sta count
    jsr getb
    sta count+1
    ora count
    beq go                     ; length 0: the end, ptr = where to go
    ldy #0
-   lda #$10                   ; FLAG2: the WiC64 has the next byte
--  bit $dd0d
    beq --
    lda $dd01                  ; reading it hands the byte back (PC2)
    ldx #$34                   ; all RAM while storing
    stx $01
    sta (ptr),y
    ldx #$37
    stx $01
    iny
    bne +
    inc ptr+1
+   inc count
    bne -
    inc count+1
    bne -
    beq segment                ; always

go:
    lda #$00                   ; user port back to input, FLAG2 cleared (as wic64_finalize)
    sta $dd03
    lda $dd0d
    lda #$80
    sta msgflag
    cli
    jmp (ptr)

run_basic:                     ; go here for a BASIC program ($2D/$2E written by the server)
    jsr $a659                  ; CLR, start of program
    jmp $a7ae                  ; RUN

fail:
    lda #$80
    sta msgflag
    cli
    ldx #$04                   ; ?FILE NOT FOUND ERROR
    jmp ($0300)

getb:
    lda #$10
-   bit $dd0d
    beq -
    lda $dd01
    rts

; --- filled in by the server -------------------------------------------------------
fname_len:  !byte 0
fname:      ; the program's name + ",P2" (up to 35 bytes; the PRG ends after it)

!if fname + 35 > $01b0 { !error "stub too long: the stack needs the top of the page" }
