; Minimal VDRIVE API Test
; Simple example: search, mount, and load a file

* = $0801

; BASIC stub: SYS 2061
        !byte $0b,$08,$01,$00,$9e,$32,$30,$36,$31,$00,$00,$00

input_ptr = $fb
len_ptr = $fd

start:
        lda #8
        sta $ba    ; Set device number to 8

        lda #1
        sta $b9    ; Set secondary address to 1 to use header address  

        ; Get API pointers
        lda $c01b           ; Input buffer pointer low
        sta input_ptr
        lda $c01c           ; Input buffer pointer high
        sta input_ptr+1
        
        lda $c01d           ; Length pointer low
        sta len_ptr
        lda $c01e           ; Length pointer high
        sta len_ptr+1
        
        ; Search for "data4.d64"
        ldy #0
-       lda disk_name,y
        sta (input_ptr),y
        iny
        cpy #9              ; Length of "data4.d64"
        bne -
        
        ; Y is already 9 from the loop
        tya                 ; A = 9
        ldy #0
        sta (len_ptr),y
        
        jsr $c00c           ; Call search direct API
        
        ; Mount "data4.d64" (name already in buffer from search)
        jsr $c00f           ; Call mount direct API
        
        ; Load "portal"
        ldy #0
-       lda file_name,y
        sta (input_ptr),y
        iny
        cpy #6              ; Length of "portal"
        bne -
        
        lda #6              ; Set length
        ldy #0
        sta (len_ptr),y
        
        ; Set KERNAL load parameters
        lda #6
        sta $b7             ; Filename length
        lda input_ptr
        sta $bb             ; Filename pointer low
        lda input_ptr+1
        sta $bc             ; Filename pointer high
        
        jmp $c012           ; Jump to load direct API (tail call)

disk_name:  !text "DATA4.D64"

file_name:  !text "PORTAL"

