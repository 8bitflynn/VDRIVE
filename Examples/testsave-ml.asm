; VDRIVE SAVE Test
; Demonstrates programmatic SAVE using vdrive direct API
;
; Usage:
;   1. Load vdrivewic7.prg and enable: SYS 49152
;   2. Load this test: LOAD"TESTSAVE-ML.PRG",8,1
;   3. Run test: SYS 4096
;
; This will search for DATA4.D64, mount it, and save this program as TESTSAVE

* = $1000

input_ptr = $fb
len_ptr = $fd

; KERNAL variables for SAVE
fnlen          = $b7
filename       = $bb
devnum         = $ba
sec_addr       = $b9
start_addr_lo  = $c1
start_addr_hi  = $c2
dest_ptr_lo    = $ae
dest_ptr_hi    = $af

start:
        ; Get API pointers
        lda $c01b
        sta input_ptr
        lda $c01c
        sta input_ptr+1
        lda $c01d
        sta len_ptr
        lda $c01e
        sta len_ptr+1
        
        ; Search for disk image
        ldy #0
-       lda disk_name,y
        sta (input_ptr),y
        iny
        cpy #9
        bne -
        
        lda #9
        ldy #0
        sta (len_ptr),y
        
        jsr $c00c           ; vdrive_search_direct
        
        ; Mount disk image
        jsr $c00f           ; vdrive_mount_direct
        
        ; Set up SAVE parameters
        lda #8
        sta fnlen
        
        lda #<file_name
        sta filename
        lda #>file_name
        sta filename+1
        
        lda #8
        sta devnum
        
        lda #0
        sta sec_addr
        
        ; Save from $1000 to end of program
        lda #$00
        sta start_addr_lo
        lda #$10
        sta start_addr_hi
        
        lda #<end_marker
        sta dest_ptr_lo
        lda #>end_marker
        sta dest_ptr_hi
        
        ; Call SAVE
        jsr $c015           ; vdrive_isave_direct
        
        rts

disk_name:
        !text "DATA4.D64"

file_name:
        !text "TESTSAVE"
        
end_marker:
