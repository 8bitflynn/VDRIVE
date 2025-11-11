* = $c000

; ZP read only pointers filled in by LOAD/SAVE 
; such as filename, device number, etc.
fnlen          = $b7
lfn            = $b8
filename       = $bb
devnum         = $ba
sec_addr       = $b9
load_mode      = $a7
targ_addr_lo   = $a0
targ_addr_hi   = $a1

; writable ZP pointers used for temporary pointers
temp_ptr_lo    = $fd
temp_ptr_hi    = $fe
dest_ptr_lo    = $ae
dest_ptr_hi    = $af

; vars for VDRIVE WiC64 implementation
user_input_length    = $cef4
org_iload_lo         = $cef5
org_iload_hi         = $cef6
org_isave_lo         = $cef7
org_isave_hi         = $cef8
vdrive_devnum        = $cef9
vdrive_retcode       = $cefa
temp_workspace1      = $cefb
temp_workspace2      = $cefc
temp_workspace3      = $cefd
temp_workspace4      = $cefe
temp_workspace5      = $ceff

; jmp table
jmp enable_vdrive
jmp disable_vdrive
jmp vdrive_search_floppies
jmp vdrive_mount_floppy

!src "wic64.h"
!src "wic64.asm"
!src "macros.asm"

enable_vdrive:   
    lda #$08
    sta vdrive_devnum

    sei

    lda $0330
    sta org_iload_lo
    lda $0331
    sta org_iload_hi

    lda #<iload_handler
    sta $0330
    lda #>iload_handler
    sta $0331

    ; lda $0332
    ; sta org_isave_lo
    ; lda $0333
    ; sta org_isave_hi

    ; lda #<isave_handler
    ; sta $0332
    ; lda #>isave_handler
    ; sta $0333

    cli

    jsr cleanup_basic_pointers

    rts

disable_vdrive:

    sei

    lda org_iload_lo
    sta $0330
    lda org_iload_hi
    sta $0331

    ; lda org_isave_lo
    ; sta $0332
    ; lda org_isave_hi
    ; sta $0333

    cli

    rts

iload_handler:   

    sta temp_workspace1
    stx temp_workspace2
    sty temp_workspace3
 
    php
    pla
    sta temp_workspace4 

    ; check if devnum matches vdrive
    lda vdrive_devnum
    cmp devnum
    beq vdrive_load

    ; no, use original ILOAD handler
    lda temp_workspace4
    pha                    
    plp
    lda org_iload_lo
    sta temp_ptr_lo
    lda org_iload_hi
    sta temp_ptr_hi
    ldx temp_workspace2
    stx $c3             
    ldy temp_workspace3
    sty $c4
    lda temp_workspace1
    sta $93
    jmp (temp_ptr_lo) ; orignal ILOAD entry point

vdrive_load:   

    jsr build_filename
    
    ; searching
    jsr $f5af

    jsr init_wic64   

    lda #2 ; index 2 = load prefix "load?file="
    jsr build_url
    beq load_fail
    jsr copy_url_to_request

    +wic64_set_request http_request
    +wic64_send_header
    bcs handle_wic64_timeout

    +wic64_send
    bcs handle_wic64_timeout

    ; show loading
    jsr $f5d2
    
    +wic64_receive_header
    bcs handle_wic64_timeout

    ; receive first two bytes (PRG load address) into dest_ptr_lo/hi
    +wic64_set_response dest_ptr_lo
    +wic64_receive dest_ptr_lo, 2
    bcs handle_wic64_timeout

    ; set runtime response pointer to the address we just received
    lda dest_ptr_lo
    sta wic64_response
    lda dest_ptr_hi
    sta wic64_response+1

    ; receive payload and write into the destination
    +wic64_receive
    bcs handle_wic64_timeout

    jsr cleanup_wic64

load_done:     
    jsr compute_endaddress
    
    lda #$ff
    sta vdrive_retcode
    lda vdrive_retcode
    clc
    ldx dest_ptr_lo
    ldy dest_ptr_hi
    rts

load_fail:
    lda #0
    sta vdrive_retcode
    sec
    ldx #0
    ldy #0
    lda vdrive_retcode
    rts

handle_wic64_timeout:
    +print timeout_error_message
    rts

handle_wic64_error: 
    +wic64_execute status_request, response_buffer
    bcs handle_wic64_timeout

    ; numeric status code (0..5)
    lda wic64_status
    clc
    adc #$30        ; convert 0->'0', 1->'1', etc.
    jsr $ffd2  
    lda #13
    jsr $ffd2

    +print status_prefix
    jsr print_response_buffer
    rts

init_wic64
    +wic64_initialize
    +wic64_set_timeout_handler handle_wic64_timeout
    +wic64_set_error_handler handle_wic64_error

    lda #$20 ; longer timeout
    sta wic64_timeout
    rts

cleanup_wic64
    +wic64_unset_timeout_handler
    +wic64_unset_error_handler
    +wic64_finalize
    rts

cleanup_basic_pointers:
    lda #$01
    sta $2B       ; TXTPTR low
    lda #$08
    sta $2C       ; TXTPTR high

    lda #$03
    sta $2D       ; VARTAB low
    sta $31       ; ARYTAB low
    sta $33       ; STREND low
    sta $35       ; FRETOP low
    sta $2F       ; ARYTAB low


    lda #$08
    sta $2E       ; VARTAB high
    sta $32       ; ARYTAB high
    sta $34       ; STREND high
    sta $36       ; FRETOP high
    sta $30       ; ARYTAB high → $0803

    rts

compute_endaddress
    clc
    lda dest_ptr_lo
    adc wic64_response_size
    sta dest_ptr_lo
    lda dest_ptr_hi
    adc wic64_response_size+1
    sta dest_ptr_hi

    ; subtract headersize (2 bytes) from total size to get actual PRG size
    sec
    lda dest_ptr_lo
    sbc #2
    sta dest_ptr_lo
    lda dest_ptr_hi
    sbc #0
    sta dest_ptr_hi
    rts

build_filename
    ; copy filename from KERNAL into user_input
    ldy #0
copy_fn_loop:
    cpy fnlen
    beq fn_copy_done
    lda (filename),y
    sta user_input,y
    iny
    bne copy_fn_loop
fn_copy_done:    

    ; record filename length for build_url
    sty user_input_length      
    rts

isave_handler:

    lda vdrive_devnum
    cmp devnum
    beq vdrive_save ; use VDRIVE to save

    lda org_isave_lo
    sta temp_ptr_lo
    lda org_isave_hi
    sta temp_ptr_hi
    jmp (temp_ptr_lo) ; use original ISAVE handler

   ; jsr restore_up9600_timing_issue_state

    clc
    rts

vdrive_save

        ; setup save pointers
        jsr setup_save_ptrs      

        jsr build_filename ; untested
        
        ; send the save request header
        jsr send_save_request

        ; print saving message
        ldy #$51 ; constant for saving 
        jsr $f12f ; saving message (61743)      

        ; TODO send payload using WiC64    
        
        ; return success
        clc      
        rts

setup_save_ptrs:
    ; lda dest_ptr_lo
    ; sec
    ; sbc $c1
    ; sta byte_count_lo
    ; lda dest_ptr_hi
    ; sbc $c2
    ; sta byte_count_mid
    ; lda #$00
    ; sta byte_count_hi

    ; lda $c1
    ; sta dest_ptr_lo
    ; lda $c2
    ; sta dest_ptr_hi

    ; lda #$00
    ; sta chunk_size_lo
    ; lda #$04
    ; sta chunk_size_hi
    rts

send_save_request:
;     lda #$2b
;     jsr send_byte
;     lda #$02
;     jsr send_byte
;     lda fnlen
;     jsr send_byte
;     lda lfn
;     jsr send_byte
;     lda sec_addr
;     jsr send_byte
;     lda devnum
;     jsr send_byte
;     lda $c1
;     jsr send_byte
;     lda $c2
;     jsr send_byte
;     lda byte_count_lo
;     jsr send_byte
;     lda byte_count_mid
;     jsr send_byte
;     lda byte_count_hi
;     jsr send_byte
;     lda chunk_size_lo
;     jsr send_byte
;     lda chunk_size_hi
;     jsr send_byte

;     ldy #$00
; filename_loop_save:
;     cpy fnlen
;     beq filename_done_save
;     lda (filename),y
;     jsr send_byte
;     iny
;     bne filename_loop_save
; filename_done_save:
;     lda #$10
;     sec
;     sbc fnlen
;     beq no_padding_save
;     tay
; filename_pad_loop_save:
;     lda #$00
;     jsr send_byte
;     dey
;     bne filename_pad_loop_save
; no_padding_save:
    rts

recv_save_response:
    ; jsr get_syncbyte
    ; jsr recv_byte
    ; sta vdrive_retcode
    rts

vdrive_search_floppies:
    jsr get_user_input
    lda user_input_length
    beq exit_search
    lda #1              ; index 1 = "search?q="
    jsr build_url
    beq exit_search
    jsr copy_url_to_request
   
    ; TODO: convert this to use same code pattern as ILOAD
    ; so header information can be extracted which will 
    ; contain the result count

     ; search for floppies matching the query
    +wic64_execute http_request, response_buffer, $06    

    jsr print_response_buffer 
    ; TODO - jmp to exit_search if no results are found 

    jmp vdrive_mount_floppy

exit_search:
    rts

vdrive_mount_floppy:
    jsr get_user_input
    lda user_input_length
    beq mount_floppy_exit
    lda #0              ; index 0 = "mount?id="
    jsr build_url
    beq mount_floppy_exit
    jsr copy_url_to_request

    +wic64_execute http_request, response_buffer, $06

    jsr print_response_buffer

mount_floppy_exit:
    rts

get_user_input:
    lda #$0d
    jsr $ffd2
    lda #'>' 
    jsr $ffd2 
    ldy #0
    sty user_input_length

readchar:
    jsr $ffcf
    cmp #$0d
    beq done
    sta user_input,y
    iny
    bne readchar
done:
    sty user_input_length
    rts

; -----------------------------------
; build_url
; A = index of prefix string to use
; 0=mount, 1=search, 2=load, 3=save
; TODO: convert vdrive to use WiC64 HTTP POST instead instead of GET to avoid having to encode the filename
; -----------------------------------
build_url:
    tay
    ; save mode (A) so we can conditionally skip appending the filename for load
    sty temp_workspace5
    lda prefix_lo,y
    sta temp_ptr_lo
    lda prefix_hi,y
    sta temp_ptr_hi

    ; copy prefix into http_path
    ldy #0
copy_prefix:
    lda (temp_ptr_lo),y
    beq prefix_done
    sta http_path,y
    iny
    bne copy_prefix
prefix_done:

    ; append user input (use the user_input buffer filled from KERNAL filename)
    ldx #0
append_input:
    cpx user_input_length
    bne .do_copy
    jmp input_done
.do_copy:
    lda user_input,x
    ; percent-encode space, %, &, ? to keep URL valid
    cmp #$20        ; space
    beq .enc_space
    cmp #$25        ; '%'
    beq .enc_pct
    cmp #$26        ; '&'
    beq .enc_amp
    cmp #$2b        ; '+'
    beq .enc_plus
    cmp #$3f        ; '?'
    beq .enc_qm

    ; normal character -> copy
    sta http_path,y
    inx
    iny
    jmp append_input

.enc_space
    ; write "%20"
    lda #$25
    sta http_path,y
    iny
    lda #$32
    sta http_path,y
    iny
    lda #$30
    sta http_path,y
    iny
    inx
    jmp append_input

.enc_pct
    ; write "%25"
    lda #$25
    sta http_path,y
    iny
    lda #$32
    sta http_path,y
    iny
    lda #$35
    sta http_path,y
    iny
    inx
    jmp append_input

.enc_amp
    ; write "%26"
    lda #$25
    sta http_path,y
    iny
    lda #$32
    sta http_path,y
    iny
    lda #$36
    sta http_path,y
    iny
    inx
    jmp append_input

.enc_qm
    ; write "%3F"
    lda #$25
    sta http_path,y
    iny
    lda #$33
    sta http_path,y
    iny
    lda #$46
    sta http_path,y
    iny
    inx
    jmp append_input

.enc_plus
    ; write "%2B"
    lda #$25
    sta http_path,y
    iny
    lda #$32
    sta http_path,y
    iny
    lda #$42
    sta http_path,y
    iny
    inx
    jmp append_input
input_done:
    lda #0
    sta http_path,y
    lda user_input_length
    rts

parse_floppy_id:
    ldx #0
    lda #0
    sta temp_workspace1      ; low byte
    sta temp_workspace2      ; high byte

parse_loop:
    cpx user_input_length
    beq done_parse

    ; convert ASCII digit to binary
    lda user_input,x
    sec
    sbc #$30
    sta temp_workspace5      ; digit

    ; ----------------------------
    ; multiply current value by 10
    ; ----------------------------

    ; copy original value
    lda temp_workspace1
    sta temp_workspace3
    lda temp_workspace2
    sta temp_workspace4

    ; value * 2 → (temp_workspace3,temp_workspace4)
    asl temp_workspace3
    rol temp_workspace4

    ; value * 8 → (temp_workspace1,temp_workspace2)
    ldx #3
shift_loop:
    asl temp_workspace1
    rol temp_workspace2
    dex
    bne shift_loop

    ; add (value*2) back in
    clc
    lda temp_workspace1
    adc temp_workspace3
    sta temp_workspace1
    lda temp_workspace2
    adc temp_workspace4
    sta temp_workspace2

    ; ----------------------------
    ; add digit
    ; ----------------------------
    clc
    lda temp_workspace1
    adc temp_workspace5
    sta temp_workspace1
    bcc parse_no_carry
    inc temp_workspace2
parse_no_carry:

    inx
    jmp parse_loop

done_parse:
    rts

copy_url_to_request:
    ; prepare header bytes
    lda #"R"
    sta http_request
    lda #WIC64_HTTP_GET
    sta http_request+1

    ; --- copy base URL into http_request starting at offset 4 ---
    ldx #0                ; X = index into http_url
    ldy #4                ; Y = index into http_request where payload starts
copy_base_safe:
    lda http_url,x
    beq base_copied
    sta http_request,y
    inx
    iny
    ; prevent payload from overflowing http_request (max payload = 251 bytes)
    cpx #251
    bcs base_copied
    jmp copy_base_safe
base_copied:

    ; X now = base length. Use X as running payload length counter.
    ; copy path bytes after the base
    ldy #0                ; Y = index into http_path
copy_path_safe:
    lda http_path,y
    beq copy_done
    ; store at http_request[4 + X]
    sta http_request+4,X
    inx
    iny
    ; prevent payload overflow (max payload = 251 bytes)
    cpx #251
    bcs copy_done
    jmp copy_path_safe
copy_done:
    ; X now holds total payload length (base + path)
    ; store payload length (little endian) into header at offsets 2/3
    stx payload_len_lo
    lda #0
    sta payload_len_hi
    lda payload_len_lo
    sta http_request+2
    lda payload_len_hi
    sta http_request+3

    ;terminate payload with zero at http_request[4 + X]
    lda #0
    sta http_request+4,X

;     ; TEMP DEBUG: print the constructed payload (http_request+4, payload_len_lo bytes)
;     lda payload_len_lo
;     beq .dbg_cu_done
;     ldx #0
; .dbg_cu_loop:
;     lda http_request+4,x
;     jsr $ffd2
;     inx
;     cpx payload_len_lo
;     bne .dbg_cu_loop
;     lda #13
;     jsr $ffd2
; .dbg_cu_done:
    rts

; ----------------------
; Zero-terminated printing
; Print starting at response_buffer[0] until a NUL byte is found.
; Preserves X/Y.
print_response_buffer:   
    ; point temp_ptr at response_buffer (16-bit pointer)
    lda #<response_buffer
    sta temp_ptr_lo
    lda #>response_buffer
    sta temp_ptr_hi

    ldy #0
print_response_loop
    lda (temp_ptr_lo),y
    beq print_response_done
    jsr $ffd2

    ; advance 16-bit pointer
    inc temp_ptr_lo
    bne print_next_char
    inc temp_ptr_hi
print_next_char
    jmp print_response_loop

print_response_done    
    rts

; --- dynamic http request buffer ---
payload_len_lo: !byte 0
payload_len_hi: !byte 0

timeout_error_message: !pet "?timeout error", $00
status_request: !byte "R", WIC64_GET_STATUS_MESSAGE, $01, $00, $01
status_prefix: !pet "?request failed: ", $00

; table of prefix strings
prefix_lo:
    !byte <mount_prefix, <search_prefix, <load_prefix, <save_prefix
prefix_hi:
    !byte >mount_prefix, >search_prefix, >load_prefix, >save_prefix

mount_prefix:  !text "mount?id=",0
search_prefix: !text "search?q=",0
load_prefix:   !text "load?file=",0
save_prefix:   !text "save?file=",0

user_input:
    !fill 80,0

http_url:
    !text "http://192.168.1.222/",0
http_url_end:

http_path:
    !fill 96,0        ; space for "mount?id=123" or "search?q=TERM"

http_request:
    !fill 256,0

response_buffer:
    !fill 512,0 



