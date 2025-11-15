* = $c000

fnlen          = $b7
lfn            = $b8
filename       = $bb
devnum         = $ba
sec_addr       = $b9
load_mode      = $a7
; targ_addr_lo   = $a0
; targ_addr_hi   = $a1
start_addr_lo  = $c1
start_addr_hi  = $c2

temp_ptr_lo    = $fd
temp_ptr_hi    = $fe
dest_ptr_lo    = $ae
dest_ptr_hi    = $af

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

    lda $0332
    sta org_isave_lo
    lda $0333
    sta org_isave_hi

    lda #<isave_handler
    sta $0332
    lda #>isave_handler
    sta $0333

    cli

    ; set basic to safe values to avoid ?OUT OF MEMORY errors
    jsr cleanup_basic_pointers

    rts

disable_vdrive:

    lda org_iload_lo
    sta $0330
    lda org_iload_hi
    sta $0331

    lda org_isave_lo
    sta $0332
    lda org_isave_hi
    sta $0333
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

iload_handler:   

    ; Save A/X/Y and P in workspaces (no stack juggling needed)
    sta temp_workspace1
    stx temp_workspace2
    sty temp_workspace3
 
    php
    pla
    sta temp_workspace4      ; save P explicitly (we’ll PLP later with its value)

    ; Intercept only our virtual device
    lda vdrive_devnum
    cmp devnum
    beq vdrive_load

    ; Not our device: restore state and tail-call original ILOAD
    lda temp_workspace4
    pha                     ; put saved P back on stack
    plp
    lda org_iload_lo
    sta temp_ptr_lo
    lda org_iload_hi
    sta temp_ptr_hi
    ldx temp_workspace2
    stx $c3                 ; restore X to KERNAL scratch (as your original did)
    ldy temp_workspace3
    sty $c4
    lda temp_workspace1
    sta $93
    jmp (temp_ptr_lo)

vdrive_load:   

    ; Copy filename from KERNAL into user_input
    ldy #0
copy_fn_loop:
    cpy fnlen
    beq fn_copy_done
    lda (filename),y
    sta user_input,y
    iny
    bne copy_fn_loop
fn_copy_done:    

    ; record filename length
    sty user_input_length      
    
    ; searching
    jsr $f5af

    jsr init_wic64

    lda #$40                ; set a longer timeout
    sta wic64_timeout

    ; Send HTTP POST with "load" endpoint and filename as POST data
    lda #2                  ; index 2 = load prefix
    ldx #<user_input  ; data pointer lo
    ldy #>user_input  ; data pointer hi
    jsr send_http_post
    bcs load_fail

    ; show loading
    jsr $f5d2

    ; receive response header
    +wic64_receive_header
    bcs load_fail

    ; receive first two bytes (PRG load address) into dest_ptr_lo/hi
    +wic64_set_response dest_ptr_lo
    +wic64_receive dest_ptr_lo, 2
    bcs load_fail

    ; set runtime response pointer to the address we just received
    lda dest_ptr_lo
    sta wic64_response
    lda dest_ptr_hi
    sta wic64_response+1

    ; receive remaining bytes directly into the destination
    +wic64_receive
    bcs load_fail

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
    jmp common_fail

handle_timeout:
    jmp common_timeout_handler

handle_error:
    jmp common_error_handler

; Consolidated handlers at end of file
common_fail:
    lda #0
    sta vdrive_retcode
    sec
    ldx #0
    ldy #0
    lda vdrive_retcode
    rts

; Original handle_timeout logic
common_timeout_handler:
    +print timeout_error_message
    rts

; Original handle_error logic
common_error_handler:
    +wic64_execute status_request, response_buffer
    bcc .skip_timeout_err
    jmp common_timeout_handler
.skip_timeout_err:

    ; Print numeric status code (0..5)
    lda wic64_status
    clc
    adc #$30
    jsr $ffd2
    lda #13
    jsr $ffd2

    +print status_prefix
    +print response_buffer
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

vdrive_search_floppies:
    jsr get_user_input
    
    ; check if user entered anything
    lda user_input_length
    bne .has_input
    jmp exit_search
.has_input:

   ;; lda #$01
   ;; sta $d020
    
    jsr init_wic64
        
    ; Send HTTP POST with "search" endpoint and search term as POST data
    lda #1                  ; index 1 = search prefix
    ldx #<user_input  ; data pointer lo
    ldy #>user_input  ; data pointer hi
    jsr send_http_post
    bcc .post_sent
    jmp exit_search
.post_sent:

    ; lda #$02
   ; sta $d020

    ; Receive the response
    +wic64_receive_header
    bcc .got_header
    jmp exit_search
.got_header:

   ;  lda #$03
    ;sta $d020

    ; Receive response body
    +wic64_set_response response_buffer
    +wic64_receive
    bcc .data_received
    jmp exit_search
.data_received:

    ;lda #$04
   ; sta $d020

    jsr cleanup_wic64   

   ; lda #$05
   ; sta $d020
        
    ; Print response buffer
    ;+print response_buffer
    jsr print_response_buffer

search_done:
    jmp vdrive_mount_floppy

exit_search:
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

vdrive_mount_floppy:
    jsr get_user_input
    
    ; check if user entered anything
    lda user_input_length
    beq mount_floppy_exit
    
    jsr init_wic64
    
    lda #$40
    sta wic64_timeout
    
    ; Send HTTP POST with "mount" endpoint and mount ID as POST data
    lda #0                  ; index 0 = mount prefix
    ldx #<user_input  ; data pointer lo
    ldy #>user_input  ; data pointer hi
    jsr send_http_post
    bcs mount_floppy_exit
    
    ; Receive mount response
    +wic64_receive_header
    bcs mount_floppy_exit
    
    +wic64_set_response response_buffer
    +wic64_receive
    bcc .mount_received
    jmp mount_floppy_exit
.mount_received:
    
    jsr cleanup_wic64
    
    ; Print response buffer
    jsr print_response_buffer

mount_floppy_exit:
    rts

isave_handler:
    sta temp_workspace1
    stx temp_workspace2
    sty temp_workspace3
    
    php
    pla
    sta temp_workspace4
    
    lda vdrive_devnum
    cmp devnum
    beq vdrive_save
    
    ; Not our device: tail-call original ISAVE
    lda temp_workspace4
    pha
    plp
    lda org_isave_lo
    sta temp_ptr_lo
    lda org_isave_hi
    sta temp_ptr_hi
    ldx temp_workspace2
    ldy temp_workspace3
    lda temp_workspace1
    jmp (temp_ptr_lo)

vdrive_save:
    ; Copy filename from KERNAL into user_input
    ldy #0
copy_fn_loop_save:
    cpy fnlen
    beq fn_copy_done_save
    lda (filename),y
    sta user_input,y
    iny
    bne copy_fn_loop_save
fn_copy_done_save:
    sty user_input_length
    
    ; Print saving message
    ldy #$51
    jsr $f12f
    
    jsr init_wic64
    
    lda #$40
    sta wic64_timeout
    
    ; Compute file data size: end ($ae/$af) - start ($c1/$c2)
    ; Note: dest_ptr ($ae/$af) points one byte PAST the last byte
    ; So end - start gives us the correct size without adding 1
    sec
    lda dest_ptr_lo
    sbc start_addr_lo
    sta temp_workspace1
    lda dest_ptr_hi
    sbc start_addr_hi
    sta temp_workspace2
    
    ; temp_workspace1/2 now holds raw data size
    ; PRG format needs 2-byte header (load address) + data
    ; Add 2 for the header
    clc
    lda temp_workspace1
    adc #2
    sta temp_workspace1
    lda temp_workspace2
    adc #0
    sta temp_workspace2
    
    ; temp_workspace1/2 now holds PRG file size (header + data)
    
    ; Calculate total POST_DATA size: 1 (length) + filename length + PRG file size
    lda user_input_length
    clc
    adc #1
    clc
    adc temp_workspace1
    sta temp_workspace3  ; total size lo
    lda #0
    adc temp_workspace2
    sta temp_workspace4  ; total size hi
    
    ; Send POST_URL using simplified approach
    lda #3  ; index 3 = save prefix
    jsr build_url_simple
    jsr copy_url_to_request
    +wic64_set_request http_request
    +wic64_send_header
    bcc .save_url_hdr_ok
    jmp save_fail
.save_url_hdr_ok:
    +wic64_send
    bcc .save_url_sent
    jmp save_fail
.save_url_sent:
    
    ; Receive POST_URL acknowledgment
    +wic64_receive_header
    bcc .save_url_ack_hdr
    jmp save_fail
.save_url_ack_hdr:
    +wic64_receive
    bcc .save_url_ack_done
    jmp save_fail
.save_url_ack_done:
    
    ; Now send POST_DATA with multi-part payload
    ; Build POST_DATA header
    lda #"R"
    sta http_request
    lda #WIC64_HTTP_POST_DATA
    sta http_request+1
    lda temp_workspace3  ; total size lo
    sta http_request+2
    lda temp_workspace4  ; total size hi
    sta http_request+3
    
    +wic64_set_request http_request
    +wic64_send_header
    bcc .data_header_ok
    jmp save_fail
.data_header_ok:
    
    ; Send length byte
    lda user_input_length
    sta temp_workspace5
    lda #1
    sta wic64_bytes_to_transfer
    lda #0
    sta wic64_bytes_to_transfer+1
    +wic64_send temp_workspace5, ~wic64_bytes_to_transfer
    bcc .len_sent
    jmp save_fail
.len_sent:
    
    ; Send filename
    lda user_input_length
    sta wic64_bytes_to_transfer
    lda #0
    sta wic64_bytes_to_transfer+1
    +wic64_send user_input, ~wic64_bytes_to_transfer
    bcc .fn_sent
    jmp save_fail
.fn_sent:
    
    ; Send 2-byte PRG header (load address)
    ; Use $c1/$c2 as the load address (start of actual data)
    ; Store in response_buffer temporarily
    lda $c1
    sta response_buffer
    lda $c2
    sta response_buffer+1
    
    +wic64_send response_buffer, 2
    bcc .header_sent
    jmp save_fail
.header_sent:
    
    ; Send PRG data directly from memory
    ; Build 16-bit pointer to $c1/$c2
    ; Size is now temp_workspace1/2 - 2 (subtract the header we just sent)
    sec
    lda temp_workspace1
    sbc #2
    sta wic64_bytes_to_transfer
    lda temp_workspace2
    sbc #0
    sta wic64_bytes_to_transfer+1
    
    ; Must manually set wic64_request since address is in zero page variable
    lda $c1
    sta wic64_request
    lda $c2
    sta wic64_request+1
    
    ; Call wic64_send directly (not macro) since we set everything manually
    jsr wic64_send
    bcc .data_sent
    jmp save_fail
.data_sent:
    
    ; Receive response header
    +wic64_receive_header
    bcc .got_resp_hdr
    jmp save_fail
.got_resp_hdr:
    
    ; Receive response body
    +wic64_set_response response_buffer
    +wic64_receive
    bcc .got_resp
    jmp save_fail
.got_resp:
    
    jsr cleanup_wic64
    
    ; Print server response
    jsr print_response_buffer
    
    clc
    rts
    
save_fail:
    jmp common_fail

handle_wic64_timeout:
    jmp common_timeout_handler

handle_wic64_error: 
    jmp common_error_handler

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

; -----------------------------------
; send_http_post
; Generic HTTP POST function
; A = index of prefix string (0=mount, 1=search, 2=load, 3=save)
; X = data pointer lo byte
; Y = data pointer hi byte
; Uses user_input_length for data size (for load/mount/search)
; Uses temp_workspace1/2 for data size (for save)
; Returns: C=0 success, C=1 failure
; -----------------------------------
send_http_post:
    sta temp_workspace5     ; save mode index
    stx post_data_ptr
    sty post_data_ptr+1
    
    ; Build URL (for load/save, this is just the endpoint without filename)
    lda temp_workspace5
    jsr build_url_simple
    bne .post_url_ok
    jmp .post_fail
.post_url_ok:
    
    ; Send POST_URL request with endpoint
    jsr copy_url_to_request
    +wic64_set_request http_request
    +wic64_send_header
    bcc .post_header_ok
    jmp .post_fail
.post_header_ok:
    +wic64_send
    bcc .post_url_sent
    jmp .post_fail
.post_url_sent:
    
    ; Receive POST_URL response (should be empty acknowledgment)
    +wic64_receive_header
    bcc .post_url_ack
    jmp .post_fail
.post_url_ack:
    ; Receive acknowledgment body (even if empty)
    +wic64_receive
    bcc .post_ack_done
    jmp .post_fail
.post_ack_done:
    
    ; Prepare POST_DATA request header
    lda #"R"
    sta http_request
    lda #WIC64_HTTP_POST_DATA
    sta http_request+1
    
    ; Set data size based on mode
    lda temp_workspace5
    cmp #3                  ; save mode?
    beq .post_use_save_size
    
    ; Load/mount/search: use user_input_length
    lda user_input_length
    sta http_request+2
    lda #0
    sta http_request+3
    jmp .post_send_data
    
.post_use_save_size:
    ; Save: use temp_workspace1/2
    lda temp_workspace1
    sta http_request+2
    lda temp_workspace2
    sta http_request+3
    
.post_send_data:
    ; Send POST_DATA header
    lda #<http_request
    sta wic64_request
    lda #>http_request
    sta wic64_request+1
    +wic64_send_header
    bcc .post_data_hdr_ok
    jmp .post_fail
.post_data_hdr_ok:
    
    ; Set wic64_request to point to actual data
    lda post_data_ptr
    sta wic64_request
    lda post_data_ptr+1
    sta wic64_request+1
    
    ; Send the actual data
    +wic64_send
    bcc .post_success
    jmp .post_fail
    
.post_success:
    clc
    rts
    
.post_fail:
    sec
    rts

; -----------------------------------
; build_url_simple
; A = index of prefix string to use
; 0=mount, 1=search, 2=load, 3=save
; Just copies the endpoint prefix - no query strings anymore
; -----------------------------------
build_url_simple:
    tay
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
    lda #0
    sta http_path,y
    lda #1              ; return non-zero for success
    rts

copy_url_to_request:
    ; prepare header bytes for POST_URL request
    lda #"R"
    sta http_request
    lda #WIC64_HTTP_POST_URL
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
    rts

; --- dynamic http request buffer ---
post_data_ptr: !byte 0, 0
payload_len_lo: !byte 0
payload_len_hi: !byte 0
size_prefix: !byte 0, 0  ; 2-byte size prefix from search response

timeout_error_message: !pet "?timeout error", $00
status_request: !byte "R", WIC64_GET_STATUS_MESSAGE, $01, $00, $01
status_prefix: !pet "?request failed: ", $00
set_remote_timeout: !byte "R", WIC64_SET_REMOTE_TIMEOUT, $01, $00, $0a  ; 10 seconds
no_response: !byte 0

; table of prefix strings for GET-style requests with query parameters
prefix_lo:
    !byte <mount_prefix, <search_prefix, <load_prefix, <save_prefix
prefix_hi:
    !byte >mount_prefix, >search_prefix, >load_prefix, >save_prefix

mount_prefix:  !text "mount",0
search_prefix: !text "search",0
load_prefix:   !text "load",0
save_prefix:   !text "save",0

user_input:
    !fill 80,0

; *** SERVER URL - Can be modified with BASIC config program ***
; To change: Load this PRG to $C000, modify bytes at http_url, save back
; Format: Null-terminated string, max 128 bytes
http_url:
    !text "http://192.168.1.222/",0
    !fill 106,0  ; Pad to 128 bytes total (22 bytes used + 106 padding)

http_path:
    !fill 96,0 

http_request:
    !fill 256,0

response_buffer:
    !fill 512,0     
