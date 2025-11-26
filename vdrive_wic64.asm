* = $c000

; ZP pointers and variables
; set by KERNAL SETNAM before ILOAD/ISAVE
; OR by external code needing to ILOAD/ISAVE ram using direct jmps
fnlen          = $b7
lfn            = $b8
filename       = $bb
devnum         = $ba
sec_addr       = $b9
load_mode      = $a7
start_addr_lo  = $c1
start_addr_hi  = $c2

; read/write indirect pointers in ZP
temp_ptr_lo    = $fd
temp_ptr_hi    = $fe
dest_ptr_lo    = $ae
dest_ptr_hi    = $af

; interacive mode jmps
jmp enable_vdrive
jmp disable_vdrive
jmp vdrive_search_floppies
jmp vdrive_mount_floppy

; direct mode jmps (programmatic)
jmp vdrive_search_direct
jmp vdrive_mount_direct
jmp vdrive_iload_direct
jmp vdrive_isave_direct

; reboot WiC64 to original state
jmp reboot_wic64

; programmatic interface pointers
api_user_input_ptr:
    !word user_input          ; $C015: Pointer to 64-byte input buffer
api_user_input_len_ptr:
    !word user_input_length   ; $C017: Pointer to length byte
api_http_url_ptr:
    !word http_url            ; $C019: Pointer to 80-byte URL buffer
api_response_buffer_ptr:
    !word response_buffer     ; $C01B: Pointer to 512-byte response buffer
api_vdrive_devnum_ptr:
    !word vdrive_devnum       ; $C01D: Pointer to device number byte
api_vdrive_retcode_ptr:
    !word vdrive_retcode      ; $C01F: Pointer to return code byte

wic64_build_report = 1
wic64_optimize_for_size = 1 ; optimize for size over speed

!src "wic64.h"
!src "wic64.asm"
!src "macros.asm"

enable_vdrive:   

    +wic64_detect
    bcc .wic64_found
    
    +print wic64_not_found_msg
    rts

.wic64_found:
    lda #$08
    sta vdrive_devnum    
    
    jsr init_vars

    sei

    lda $0330
    sta org_iload_lo
    lda $0331
    sta org_iload_hi

    lda #<vdrive_iload_direct
    sta $0330
    lda #>vdrive_iload_direct
    sta $0331

    lda $0332
    sta org_isave_lo
    lda $0333
    sta org_isave_hi

    lda #<vdrive_isave_direct
    sta $0332
    lda #>vdrive_isave_direct
    sta $0333

    cli

    ; set basic to safe values to avoid ?OUT OF MEMORY errors
    jsr cleanup_basic_pointers

    ; Set remote timeout once at enable
    +wic64_initialize
    +wic64_execute set_remote_timeout, response_buffer
    +wic64_finalize

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

init_vars:

    ; Initialize session to 0
    lda #0
    sta session_id
    sta session_id+1
    
    ; Clear working variables to default values
    sta user_input_length
    sta vdrive_retcode
    sta interactive_mode
    sta post_data_ptr
    sta post_data_ptr+1
    sta payload_len_lo
    sta payload_len_hi
    rts

; avoid ?OUT OF MEMORY errors by setting basic pointers to safe values
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

vdrive_iload_direct:

    sta temp_workspace1
    stx temp_workspace2
    sty temp_workspace3
 
    php
    pla
    sta temp_workspace4      

    ; Intercept only vdrive
    lda vdrive_devnum
    cmp devnum
    beq vdrive_load

    ; Not our device: restore state and tail-call original ILOAD
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

    ; Prepend session ID to filename
    jsr prepend_session_to_data

    ; Send HTTP POST with "load" endpoint (X/Y already point to response_buffer)
    lda #2                  ; index 2 = load prefix
    jsr send_http_post
    bcc .load_post_ok
    jmp load_fail
.load_post_ok:

    ; receive response header
    +wic64_receive_header
    bcc .load_hdr_ok
    jmp load_fail
.load_hdr_ok:

    ; receive first 3 bytes (session + error_code) into response_buffer
    +wic64_set_response response_buffer
    +wic64_receive response_buffer, 3
    bcc .load_got_status
    jmp load_fail
.load_got_status:

    ; Extract session ID from first 2 bytes
    lda response_buffer
    sta session_id
    lda response_buffer+1
    sta session_id+1

    ; Store error code from byte 2
    lda response_buffer+2
    sta vdrive_retcode

    ; Check error code - 0xff is success, anything else is error
    cmp #$ff
    bne load_error
    
    ; show loading message only on success
    jsr $f5d2
    
    ; Success - receive 2 more bytes for load address
    +wic64_receive response_buffer, 2
    bcc .load_got_addr
    jmp load_fail
.load_got_addr:
    
    ; Extract PRG load address from response_buffer into dest_ptr
    lda response_buffer
    sta dest_ptr_lo
    lda response_buffer+1
    sta dest_ptr_hi

    ; Check secondary address to determine final load address
    ; SA=0: Always use PRG file's address (BASIC LOAD without ,1)
    ; SA=1: Use PRG file's address (BASIC LOAD with ,1 or machine code load)
    ; For machine code: if caller wants custom address, they set SA=2
    lda sec_addr
    cmp #2
    bcc .use_prg_address    ; SA=0 or SA=1: use PRG address
    
    ; SA=2+: use custom address from $AE/$AF (for machine code multi-load)
    lda start_addr_lo
    sta dest_ptr_lo
    lda start_addr_hi
    sta dest_ptr_hi

.use_prg_address:
    ; Set response pointer to the load address and receive remaining data directly
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
    
    ; Return success with error code already in vdrive_retcode
    clc
    ldx dest_ptr_lo
    ldy dest_ptr_hi
    lda vdrive_retcode
    rts

load_fail:
    jmp common_fail

load_error:
    ; Error code already in vdrive_retcode
    jsr cleanup_wic64
    sec
    ldx #0
    ldy #0
    lda vdrive_retcode
    rts

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
    ; Clear session for new search
    lda #0
    sta session_id
    sta session_id+1
    jsr get_user_input
    lda #1              ; flag: 1 = interactive (auto-mount after search)
    sta interactive_mode
    jmp search_common
    
vdrive_search_direct:
    lda #0              ; flag: 0 = programmatic (no auto-mount)
    sta interactive_mode
    
search_common:
    ; check if user entered anything
    lda user_input_length
    beq exit_search  ; Exit immediately if empty, don't prompt for mount
    
    jsr init_wic64
        
    ; Prepend session ID to search term
    jsr prepend_session_to_data
    
    ; Send HTTP POST with "search" endpoint (X/Y already point to response_buffer)
    lda #1                  ; index 1 = search prefix
    jsr send_http_post
    bcc .post_sent
    jmp exit_search
.post_sent:

    ; Receive the response
    +wic64_receive_header
    bcc .got_header
    jmp exit_search
.got_header:

    ; Receive response body
    +wic64_set_response response_buffer
    +wic64_receive
    bcc .data_received
    jmp exit_search
.data_received:

    ; Extract session ID from first 2 bytes of response
    lda response_buffer
    sta session_id
    lda response_buffer+1
    sta session_id+1
        
    ; Print response buffer only in interactive mode
    lda interactive_mode
    beq .skip_print         ; programmatic mode (0), skip printing
    
    ; Print response buffer starting at byte 2 (skip session header only)
    lda #<(response_buffer+2)
    sta temp_ptr_lo
    lda #>(response_buffer+2)
    sta temp_ptr_hi
    jsr print_from_ptr
.skip_print:

    jsr cleanup_wic64   

search_done:
    ; hack until search returns the result count in headers
    ; Check if server returned empty results or "No results" message
    lda response_buffer+4
    beq exit_search         ; Empty response, skip mount
    cmp #'N'                ; Starts with 'N' (likely "No results")?
    beq exit_search         ; Skip mount
    cmp #'0'                ; Starts with '0' (zero results)?
    beq exit_search         ; Skip mount
    
    ; Check interactive mode: if programmatic, exit without mounting
    lda interactive_mode
    beq exit_search         ; programmatic mode (0), exit
    
    ; Interactive mode: continue to paging loop
    jmp page_loop

exit_search:
    jsr cleanup_wic64
    rts

; -----------------------------------
; page_loop
; After search results, prompt for mount number or paging command
; +/-/+N/-N for paging, number/filename for mounting
; Empty input exits
; WiC64 stays initialized for entire session
; -----------------------------------
page_loop:
    jsr get_user_input
    
    ; Check if empty - exit if so
    lda user_input_length
    beq exit_page_loop      ; Empty input, exit
    
    ; Check if first char is + or -
    lda user_input
    cmp #'+'
    beq do_page_request
    cmp #'-'
    beq do_page_request
    
    ; Not paging, treat as mount request
    ; Skip get_user_input in mount path since we already have input
    lda #1
    sta interactive_mode
    jmp mount_common

; -----------------------------------
; do_page_request
; Send paging request (+/-/+N/-N) to server
; WiC64 initialized and finalized per request
; -----------------------------------
do_page_request:
    ; Init WiC64 for this request
    jsr init_wic64
    
    ; Prepend session to page command
    jsr prepend_session_to_data
    
    ; Send as search request (index 1)
    lda #1
    jsr send_http_post
    bcc .page_post_sent
    jmp page_fail
.page_post_sent:
    
    ; Receive response
    +wic64_receive_header
    bcc .page_got_header
    jmp page_fail
.page_got_header:
    
    +wic64_set_response response_buffer
    +wic64_receive
    bcc .page_data_received
    jmp page_fail
.page_data_received:
    
    ; Update session from response
    lda response_buffer
    sta session_id
    lda response_buffer+1
    sta session_id+1
    
    ; Print results (skip 2-byte session header)
    lda #<(response_buffer+2)
    sta temp_ptr_lo
    lda #>(response_buffer+2)
    sta temp_ptr_hi
    jsr print_from_ptr
    
    ; Cleanup WiC64 after this request
    jsr cleanup_wic64
    
    jmp page_loop

page_fail:
    ; Cleanup WiC64 on failure
    jsr cleanup_wic64
    ; Print error and exit to BASIC
    lda #<page_error_msg
    sta temp_ptr_lo
    lda #>page_error_msg
    sta temp_ptr_hi
    jsr print_from_ptr
    rts

exit_page_loop:
    rts  

; Print from pointer in temp_ptr (max 512 bytes for safety)
print_from_ptr:
    ldy #0
    ldx #0              ; byte counter low
    stx temp_workspace1
    ldx #2              ; byte counter high (max 512 = $0200)
    stx temp_workspace2
.loop:
    lda (temp_ptr_lo),y
    beq .done           ; null terminator
    jsr $ffd2
    inc temp_ptr_lo
    bne .no_carry
    inc temp_ptr_hi
.no_carry:
    inc temp_workspace1
    bne .loop
    inc temp_workspace2
    beq .done           ; hit 512 byte limit
    jmp .loop
.done:
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
    ; Clear user_input buffer first
    ldx #0
    lda #0
.clear_input:
    sta user_input,x
    inx
    cpx #64
    bne .clear_input
    
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
    lda #1              ; flag: 1 = interactive (show results)
    sta interactive_mode
    jmp mount_common
    
vdrive_mount_direct:
    lda #0              ; flag: 0 = programmatic (no printing)
    sta interactive_mode
    ; Fall through to mount_common
    
mount_common:
    ; Always init WIC64 even if empty input (to ensure cleanup happens)
    jsr init_wic64
    
    ; check if user entered anything
    lda user_input_length
    beq mount_floppy_exit
    
    ; Prepend session ID to mount ID
    jsr prepend_session_to_data
    
    ; Send HTTP POST with "mount" endpoint (X/Y already point to response_buffer)
    lda #0                  ; index 0 = mount prefix
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
    
    ; Extract session ID from first 2 bytes of response
    lda response_buffer
    sta session_id
    lda response_buffer+1
    sta session_id+1
    
    jsr cleanup_wic64
    
    ; Print response buffer only in interactive mode
    lda interactive_mode
    beq mount_floppy_exit   ; programmatic mode (0), skip printing and exit
    
    ; Print response buffer (skip session header)
    lda #<(response_buffer+2)
    sta temp_ptr_lo
    lda #>(response_buffer+2)
    sta temp_ptr_hi
    jsr print_from_ptr

mount_floppy_exit:
    jsr cleanup_wic64
mount_floppy_exit_no_cleanup:
    rts

vdrive_isave_direct:
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
    ; Print "SAVING <filename>" immediately before anything else
    ; $bb/$bc and $b7 are already set up by KERNAL SETNAM
    ldy #$51            ; Offset to "SAVING " message
    jsr $f12f           ; Print "SAVING "
    jsr $f5c1           ; Print filename (OUTFN)
    lda #$0d            ; CR
    jsr $ffd2           ; CHROUT - print carriage return
    
    ; Save $c1/$c2 (start address) for calculations
    lda start_addr_lo
    sta temp_workspace1
    lda start_addr_hi
    sta temp_workspace2
    
    ; Save $ae/$af (end address)
    lda dest_ptr_lo
    sta temp_workspace3
    lda dest_ptr_hi
    sta temp_workspace4
    
    ; Now copy filename from KERNAL into user_input
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
    
    jsr init_wic64
    
    ; Compute PRG data size: end - start
    sec
    lda temp_workspace3  ; end_lo
    sbc temp_workspace1  ; start_lo
    sta temp_workspace5  ; data size lo
    lda temp_workspace4  ; end_hi
    sbc temp_workspace2  ; start_hi
    sta temp_workspace6  ; data size hi
    
    ; Calculate total POST_DATA size: 2 (session) + 1 (length) + filename + 2 (PRG header) + data
    lda user_input_length
    clc
    adc #5                  ; 2 bytes session + 1 byte length + 2 byte PRG header
    clc
    adc temp_workspace5     ; add data size
    sta temp_workspace5     ; reuse temp_workspace5 for total POST size lo
    lda #0
    adc temp_workspace6     ; add data size hi
    sta temp_workspace6     ; reuse temp_workspace6 for total POST size hi
    
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
    lda temp_workspace5  ; total POST size lo
    sta http_request+2
    lda temp_workspace6  ; total POST size hi
    sta http_request+3
    
    +wic64_set_request http_request
    +wic64_send_header
    bcc .data_header_ok
    jmp save_fail
.data_header_ok:
    
    ; Send session ID first (2 bytes)
    lda session_id
    sta temp_workspace5
    lda #1
    sta wic64_bytes_to_transfer
    lda #0
    sta wic64_bytes_to_transfer+1
    +wic64_send temp_workspace5, ~wic64_bytes_to_transfer
    bcc .session_lo_sent
    jmp save_fail
.session_lo_sent:
    lda session_id+1
    sta temp_workspace5
    +wic64_send temp_workspace5, ~wic64_bytes_to_transfer
    bcc .session_sent
    jmp save_fail
.session_sent:
    
    ; Send length byte
    lda user_input_length
    sta temp_workspace5
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
    
    ; Send 2-byte PRG header (the load address where data should load)
    ; This is the value in temp_workspace1/2 (the start address)
    lda temp_workspace1
    sta temp_workspace5
    lda #1
    sta wic64_bytes_to_transfer
    lda #0
    sta wic64_bytes_to_transfer+1
    +wic64_send temp_workspace5, ~wic64_bytes_to_transfer
    bcc .header_lo_sent
    jmp save_fail
.header_lo_sent:
    lda temp_workspace2
    sta temp_workspace5
    +wic64_send temp_workspace5, ~wic64_bytes_to_transfer
    bcc .header_sent
    jmp save_fail
.header_sent:
    
    ; Send program data from memory
    ; Size: (end_addr) - (start_addr)
    sec
    lda temp_workspace3
    sbc temp_workspace1
    sta wic64_bytes_to_transfer
    lda temp_workspace4
    sbc temp_workspace2
    sta wic64_bytes_to_transfer+1
    
    ; Set source pointer to start address
    lda temp_workspace1
    sta wic64_request
    lda temp_workspace2
    sta wic64_request+1
    
    ; Send the program data
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
    
    ; Extract session ID from first 2 bytes of response
    lda response_buffer
    sta session_id
    lda response_buffer+1
    sta session_id+1
    
    jsr cleanup_wic64
    
    ; Print server response (skip session header + error code = 3 bytes)
    lda #<(response_buffer+3)
    sta temp_ptr_lo
    lda #>(response_buffer+3)
    sta temp_ptr_hi
    jsr print_from_ptr
    
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
    +wic64_dont_disable_irqs
    +wic64_set_timeout_handler handle_wic64_timeout
    +wic64_set_error_handler handle_wic64_error

    lda #$ff ; max timeout (255 decimal)
    sta wic64_timeout
    rts

cleanup_wic64
    +wic64_unset_timeout_handler
    +wic64_unset_error_handler
    +wic64_finalize
    rts

reboot_wic64:
    ; Send WIC64 reboot command to clear stale state
    ; This helps with soft resets via IDE64 boot button
    +wic64_initialize
    lda #"R"
    sta http_request
    lda #WIC64_REBOOT
    sta http_request+1
    lda #0
    sta http_request+2
    sta http_request+3
    +wic64_set_request http_request
    +wic64_send_header
    jsr wic64_send
    ; Wait a moment for ESP to reboot (roughly 0.5 seconds)
    ldx #$ff
.outer_loop:
    ldy #$ff
.inner_loop:
    dey
    bne .inner_loop
    dex
    bne .outer_loop

    jsr cleanup_wic64
    jsr init_vars

    rts

; -----------------------------------
; prepend_session_to_data
; Prepends 2-byte session_id to data in user_input
; Copies user_input to response_buffer with session header
; Input: user_input (data), user_input_length (length)
; Output: response_buffer ([session_lo][session_hi][data])
;         user_input_length updated to include 2-byte session
; Returns: X = pointer to response_buffer
;          Y = high byte of response_buffer
; -----------------------------------
prepend_session_to_data:
    ; Clear entire 512-byte response_buffer to prevent contamination
    lda #0
    tax
.clear_buffer:
    sta response_buffer,x
    sta response_buffer+256,x
    inx
    bne .clear_buffer
    
    ; Put session ID first (2 bytes)
    lda session_id
    sta response_buffer
    lda session_id+1
    sta response_buffer+1
    
    ; Put length byte at position 2
    lda user_input_length
    sta response_buffer+2
    
    ; Copy data after session + length
    ldx #0
.copy_loop:
    cpx user_input_length
    beq .copy_done
    lda user_input,x
    sta response_buffer+3,x
    inx
    jmp .copy_loop
.copy_done:
    
    ; Calculate total length (session + length byte + data)
    ; Store in payload_len_lo for use by send_http_post
    lda user_input_length
    clc
    adc #3
    sta payload_len_lo
    lda #0
    sta payload_len_hi
    
    ; Return pointer to response_buffer
    ldx #<response_buffer
    ldy #>response_buffer
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
    ; Set wic64_bytes_to_transfer from POST_URL header
    lda http_request+2
    sta wic64_bytes_to_transfer
    lda http_request+3
    sta wic64_bytes_to_transfer+1
    jsr wic64_send
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
    
    ; Prepare POST_DATA request header (buffer already cleared in copy_url_to_request)
    lda #"R"
    sta http_request
    lda #WIC64_HTTP_POST_DATA
    sta http_request+1
    
    ; Set data size based on mode
    lda temp_workspace5
    cmp #3                  ; save mode?
    beq .post_use_save_size
    
    ; Load/mount/search: use payload_len_lo/hi (set by prepend_session_to_data)
    lda payload_len_lo
    sta http_request+2
    lda payload_len_hi
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
    
    ; Send the actual data with explicit size from header
    lda http_request+2
    sta wic64_bytes_to_transfer
    lda http_request+3
    sta wic64_bytes_to_transfer+1
    
    jsr wic64_send
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
    ; Clear http_request buffer (128 bytes)
    lda #0
    ldx #0
.clear_request:
    sta http_request,x
    inx
    bpl .clear_request
    
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
    ; prevent payload from overflowing http_request (max payload = 124 bytes for 128-byte buffer)
    cpx #124
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
    ; prevent payload overflow (max payload = 124 bytes for 128-byte buffer)
    cpx #124
    bcs copy_done
    jmp copy_path_safe
copy_done:
    ; X now holds total payload length (base + path)
    ; store payload length (little endian) into header at offsets 2/3
    ; Don't use payload_len_lo here - that's for POST_DATA size!
    stx http_request+2    ; Store URL length directly in request
    lda #0
    sta http_request+3   
    rts

; --- dynamic http request buffer ---
post_data_ptr: !byte 0, 0
payload_len_lo: !byte 0
payload_len_hi: !byte 0
session_id: !byte 0, 0  ; 16-bit session ID from server

timeout_error_message: !pet "?timeout error", $00
wic64_not_found_msg: !pet "?wic64 not detected", $0d, $00
page_error_msg: !pet $0d, "?page error", $0d, $00
status_request: !byte "R", WIC64_GET_STATUS_MESSAGE, $01, $00, $01
status_prefix: !pet "?request failed: ", $00
set_remote_timeout: !byte "R", WIC64_SET_REMOTE_TIMEOUT, $01, $00, $0f  ; 15 seconds
no_response: !byte 0

; Reserve space for variables at end to prevent overwrite
user_input_length:
    !byte 0
org_iload_lo:
    !byte 0
org_iload_hi:
    !byte 0
org_isave_lo:
    !byte 0
org_isave_hi:
    !byte 0
vdrive_devnum:
    !byte 0
vdrive_retcode:
    !byte 0
interactive_mode:
    !byte 0  ; 0 = programmatic (no prompts), 1 = interactive (prompts)
temp_workspace1:
    !byte 0
temp_workspace2:
    !byte 0
temp_workspace3:
    !byte 0
temp_workspace4:
    !byte 0
temp_workspace5:
    !byte 0
temp_workspace6:
    !byte 0

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
    !fill 64,0

; *** SERVER URL - Can be modified with BASIC config program ***
; To change: Load this PRG to $C000, modify bytes at http_url, save back
; Format: Null-terminated string, max 80 bytes
http_url:
    !text "http://192.168.1.222/",0
    !fill 59,0  ; Pad to 80 bytes total (21 bytes used + 59 padding)

http_path:
    !fill 16,0 

http_request:
    !fill 128,0

response_buffer:
    !fill 512,0  ; 512 bytes response buffer at end of code
