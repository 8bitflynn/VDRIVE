; memory map
; $c000-$c380 - jmp table
; $c400-$c5d8 - up9600 bitbanger/vdrive
; $c5ff-$c5e4 - vars and constants (counts down from $c5ff)
; $c600 - rs232 input buffer
; $c700 - rs232 output buffer 
; $c800 - up9600

; zp read only pointers
fnlen          = $b7
lfn            = $b8
filename       = $bb
devnum         = $ba
sec_addr       = $b9
load_mode      = $a7
targ_addr_lo   = $a0
targ_addr_hi   = $a1

; zp read/write pointers
rs232_inputbuffer_lo = $f7
rs232_inputbuffer_hi = $f8
rs232_outputbuffer_lo = $f9
rs232_outputbuffer_hi = $fa

; zp indirect pointers
temp_ptr_lo = $fd
temp_ptr_hi = $fe
dest_ptr_lo = $ae ; basic and org iload use these locations
dest_ptr_hi = $af ; as a pointer to the byte to store the recv'd byte

user_input_length     = $c5e4
dest_ptr_lo_save      = $c5e5
dest_ptr_hi_save      = $c5e6
byte_count_lo_save    = $c5e7
byte_count_mid_save   = $c5e8
byte_count_hi_save    = $c5e9
chunk_size_lo_save    = $c5ea
chunk_size_hi_save    = $c5eb
byte_count_lo         = $c5ec
byte_count_mid        = $c5ed
byte_count_hi         = $c5ee
chunk_size_lo         = $c5ef
chunk_size_hi         = $c5f0
vdrive_devnum         = $c5f1 ; poke 50673,# VDRIVE device number responds to (default 8)
spinner_char_save     = $c5f2
vdrive_retcode        = $c5f3
org_iload_lo          = $c5f4
org_iload_hi          = $c5f5
org_isave_lo          = $c5f6
org_isave_hi          = $c5f7
temp_workspace1       = $c5f8
temp_workspace2       = $c5f9
temp_workspace3       = $c5fa
temp_workspace4       = $c5fb
save_d015             = $c5fc
pad_count             = $c5fd
temp_workspace5       = $c5fe
search_result_count   = $c5ff


*= $c000
        jmp enable_vdrive
; $c003
        jmp disable_vdrive
; $c006  
        jmp vdrive_search_floppies
; $c009
        jmp vdrive_mount_floppy
   
enable_vdrive
        ; install before hooking vectors 
        ; so disable will restore original 
        ; vector
        jsr install_up9600

        ; default devnum for vdrive_devnum
        ; setting this memory location to
        ; 2 will route devnum 8 to original
        ; iload
        lda #$08
        sta vdrive_devnum

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

        rts

disable_vdrive
        jsr disable_up9600

        lda org_iload_lo
        sta $0330
        lda org_iload_hi
        sta $0331       

        lda org_isave_lo
        sta $0332
        lda org_isave_hi
        sta $0333      

        rts        

iload_handler  
        sta temp_workspace1
        stx temp_workspace2 ; should be same as $c3
        sty temp_workspace3 ; should be same as $c4
        php             
        pla            
        sta temp_workspace4 
        lda vdrive_devnum
        cmp devnum ; compare with number from load and if different, call original iload
        beq vdrive_load  
        jsr disable_up9600 ; just in case          
        lda temp_workspace4 ; original status
        pha ; push status onto stack
        plp ; pull status into cpu status reg            
        clc   
        lda org_iload_lo
        sta temp_ptr_lo
        lda org_iload_hi
        sta temp_ptr_hi
        ldx temp_workspace2 ; original x
        stx $c3
        ldy temp_workspace3 ; original y
        sty $c4
        lda temp_workspace1 ; original a
        sta $93 ; 0=loading and 1=verifying msg
        jmp (temp_ptr_lo)

vdrive_load

       ; lda dest_ptr_lo
;        sta dest_ptr_lo_save
;        lda dest_ptr_hi
;        sta dest_ptr_hi_save

        ; fixup to turn off sprites
        ; or anything needed to keep
        ; up9600 timing stable
        jsr clear_up9600_timing_issues 

        jsr enable_up9600 ; rs232 buffer @ $c600

        ; searching
        jsr $f5af

        jsr send_load_request

        lda #$00 ; bit 6 eoi (had $40 here bit 6 for eoi)
        sta $90 ; kernal i/o status word
        lda #$00
        sta $93 ; 0=loading and 1=verifying msg
        lda #$ff ; ff = success
        sta vdrive_retcode             

        jsr recv_load_response
        ; a has lvdrive_retcode
        cmp #$ff
        bne return_init

        lda $07e7
        sta spinner_char_save 

         ; show loading
        jsr $f5d2

recv_payload
        ; bitbanger/vdisk receiver entry
        jsr recv_data
return_init

        lda spinner_char_save
        sta $07e7 ; restore org char  

         ; turn off up9600
        jsr disable_up9600  

        ; restore state to original
        jsr restore_up9600_timing_issue_state

        lda vdrive_retcode ; holds any error or break message
        cmp #$ff
        beq return_success

        ; return error
        sec  
        ldx #$02
        ldy #$00
        jmp exit

return_success
        clc
        ldx dest_ptr_lo ; registers hold end address
        ldy dest_ptr_hi
exit
        lda vdrive_retcode
        rts

send_load_request
        lda #$2b ; sync byte
        jsr send_byte
        lda #$01 ; opcode for save
        jsr send_byte
        lda fnlen
        jsr send_byte
        lda lfn
        jsr send_byte
        lda sec_addr
        jsr send_byte
        lda devnum
        jsr send_byte
        lda load_mode
        jsr send_byte
        lda targ_addr_lo
        jsr send_byte
        lda targ_addr_hi
        jsr send_byte

        ldy #$00
filename_loop
        cpy fnlen
        beq filename_done
        lda (filename),y       
        jsr send_byte        
        iny
        bne filename_loop
filename_done
        lda #$10
        sec
        sbc fnlen
        beq no_padding
        tay
filename_pad_loop
        lda #$00
        jsr send_byte
        dey
        bne filename_pad_loop
no_padding
        rts

recv_load_response
        jsr get_syncbyte   
        jsr recv_byte
        sta vdrive_retcode
        rts

isave_handler
        lda vdrive_devnum 
        cmp devnum 
        beq vdrive_save
        lda org_isave_lo
        sta temp_ptr_lo
        lda org_isave_hi
        sta temp_ptr_hi
        jmp (org_isave_lo)

vdrive_save

        ; setup save pointers
        jsr setup_save_ptrs

        ; fixup to turn off sprites
        ; or anything needed
        jsr clear_up9600_timing_issues 

        jsr enable_up9600 ; rs232 buffer @ $c600
        
        ; send the save request header
        jsr send_save_request

        ; print saving message
        ldy #$51 ; constant for saving 
        jsr $f12f ; saving message (61743)    

        lda $07e7
        sta spinner_char_save
          
        ; send payload
        jsr send_data

        lda spinner_char_save
        sta $07e7

         ; turn off up9600
        jsr disable_up9600   
        
        ; restore state to original
        jsr restore_up9600_timing_issue_state

        ; return success
        clc

        rts

setup_save_ptrs
    
         ; calculate byte size
        lda dest_ptr_lo ; low byte of end
        sec
        sbc $c1
        sta byte_count_lo

        lda dest_ptr_hi
        sbc $c2
        sta byte_count_mid
        lda #$00 ; high byte not used here
        sta byte_count_hi

        ; move pointer to beginning of data
        lda $c1
        sta dest_ptr_lo 
        lda $c2
        sta dest_ptr_hi    

        lda #$00
        sta chunk_size_lo
        lda #$04
        sta chunk_size_hi
       
        rts        

send_save_request     
        lda #$2b ; sync byte
        jsr send_byte
        lda #$02 ; opcode for save
        jsr send_byte
        lda fnlen
        jsr send_byte
        lda lfn
        jsr send_byte
        lda sec_addr
        jsr send_byte
        lda devnum
        jsr send_byte       
        lda $c1
        jsr send_byte
        lda $c2
        jsr send_byte
        lda byte_count_lo
        jsr send_byte 
        lda byte_count_mid
        jsr send_byte
        lda byte_count_hi
        jsr send_byte 
        lda chunk_size_lo
        jsr send_byte
        lda chunk_size_hi
        jsr send_byte

        ldy #$00
filename_loop_save
        cpy fnlen
        beq filename_done_save
        lda (filename),y       
        jsr send_byte        
        iny
        bne filename_loop_save
filename_done_save
        lda #$10
        sec
        sbc fnlen
        beq no_padding_save
        tay
filename_pad_loop_save
        lda #$00
        jsr send_byte
        dey
        bne filename_pad_loop_save
no_padding_save
        rts

; search entry
vdrive_search_floppies 
        jsr clear_up9600_timing_issues 
        jsr enable_up9600   

        jsr get_user_input
     
        jsr send_search_request 

        lda $07e7
        sta spinner_char_save      

        lda #$ff ; ff = success, set in recv_search_response
        sta vdrive_retcode 
        jsr recv_search_response ; header         
        cmp #$ff ; vdrive_retcode cmp
        bne exit_search

search_recv_payload   

        ; bitbanger/vdrive receiver entry
        jsr recv_data

        lda spinner_char_save
        sta $07e7       

        lda search_result_count ; print filenames wipes this out
        beq exit_search ; no results
        jsr printfilenames

search_return_init      
        jsr vdrive_mount_floppy
exit_search
        jsr disable_up9600 
        jsr restore_up9600_timing_issue_state 
        rts

get_user_input
        ldy #0
readchar
        jsr $ffcf           ; chrin — waits for real input
        cmp #$0d            ; return?
        beq done
        sta temp_search_term,y
        iny
        bne readchar        ; loop until y wraps

done
        sty user_input_length
        rts

send_search_request
        lda #$2b ; sync
        jsr send_byte
        lda #$05 ; operation - search
        jsr send_byte

        lda #<temp_search_term
        sta temp_ptr_lo
        lda #>temp_search_term
        sta temp_ptr_hi

        lda user_input_length
        jsr send_byte

        ldx #$00                 ; use x as index
search_loop_x
        lda temp_search_term,x
        jsr send_byte
        inx
        cpx user_input_length
        bne search_loop_x

        lda #$71 ; pad rest (80 char + MediaType fields (not used yet))
        sec
        sbc user_input_length
        sta pad_count            ; zero-page pad counter
        beq no_padding_search        

pad_send_loop
        lda #$00        
        jsr send_byte
        dec pad_count
        bne pad_send_loop

no_padding_search       
        lda #$ff ; flags
        jsr send_byte
        rts

recv_search_response
       
        jsr get_syncbyte  
         
        jsr recv_byte 
        sta vdrive_retcode 
  
        jsr recv_byte 
        sta search_result_count  
     
        lda vdrive_retcode       
        
        rts

vdrive_mount_floppy        
         
        jsr get_user_input ; re-use to get id of floppy for now
        cmp user_input_length
        beq mount_floppy_exit ; user hit enter, exit

        jsr clear_up9600_timing_issues 
        jsr enable_up9600  

        lda #$2b ; sync
        jsr send_byte
        lda #$03 ; operation - insert floppy
        jsr send_byte

        jsr parse_floppy_id ; turn entered text into 16 bit floppy id
        lda temp_workspace1
        jsr send_byte
        lda temp_workspace2
        jsr send_byte         
        
mount_floppy_exit

        jsr disable_up9600 
        jsr restore_up9600_timing_issue_state  
        rts

parse_floppy_id

        ldx #0
        lda #0
        sta temp_workspace1
        sta temp_workspace2

parse_loop
        cpx user_input_length
        beq done_parse

        lda temp_search_term,x
        sec
        sbc #$30           ; ascii to digit
        sta temp_workspace5 ;digit

        ; multiply result by 10
        lda temp_workspace1; result_lo
        asl a
        sta temp_workspace3; temp_lo
        lda temp_workspace2; result_hi
        rol a
        sta temp_workspace4 ; temp_hi        ; result × 2

        lda temp_workspace1 ;result_lo
        asl a
        asl a
        asl a
        clc
        adc temp_workspace3;temp_lo
        sta temp_workspace1;result_lo
        lda temp_workspace2;result_hi
        rol a
        rol a
        rol a
        adc temp_workspace4;temp_hi
        sta temp_workspace2;result_hi      ; result × 10

        ; add digit
        clc
        lda temp_workspace1;result_lo
        adc temp_workspace5;digit
        sta temp_workspace1;result_lo
        bcc skip_inc_parse
        inc temp_workspace2;result_hi
skip_inc_parse
        inx
        jmp parse_loop

done_parse
      rts

printfilenames
    lda #13
    jsr $ffd2              ; print initial newline

    lda dest_ptr_lo_save ; start where raw data was stored in RAM
    sta temp_ptr_lo
    lda dest_ptr_hi_save
    sta temp_ptr_hi

    lda search_result_count
    beq done_print
    
printloop 
    ldy #0
    lda (temp_ptr_lo),y ; lo byte of floppy id
    tax
    ldy #1
    lda (temp_ptr_lo),y ; hi byte of floppy Id
    jsr $bdcd ; print id           
    lda #$20
    jsr $ffd2             

    ; ---- print filename ----
    ldy #2
    lda (temp_ptr_lo),y     ; filename length byte
    tax
    beq printemptyname

    ldy #3
printnamechar
    lda (temp_ptr_lo),y
    jsr $ffd2
    iny
    dex
    bne printnamechar

printemptyname
    lda #13
    jsr $ffd2               ; carriage return

    ; ---- advance pointer by FloppyInfo struct size 
    clc
    lda temp_ptr_lo
    adc #$43
    sta temp_ptr_lo
    lda temp_ptr_hi
    adc #0
    sta temp_ptr_hi    
    dec search_result_count
    bne printloop

done_print
    rts

 

; bitbanger/vdisk chunked transfer
*= $c400

recv_data  

        ; remove garbage on line and sync frame
        jsr get_syncbyte

; these bytes are the file length minus 2 bytes for memory location
; stored at the start of the prg
; this is decremented as bytes are received
get_byte_count_lo
        jsr recv_byte
        sta byte_count_lo
        sta byte_count_lo_save

get_byte_count_mid
        jsr recv_byte
        sta byte_count_mid
        sta byte_count_mid_save

get_byte_count_hi
        jsr recv_byte
        sta byte_count_hi
        sta byte_count_hi_save

get_chunk_size_lo
        jsr recv_byte
        sta chunk_size_lo
        sta chunk_size_lo_save

get_chunk_size_hi
        jsr recv_byte
        sta chunk_size_hi
        sta chunk_size_hi_save

; first byte from prg
; specifies low part of 16 bit address
; to store prg
get_dest_ptr_low
        jsr recv_byte
        sta dest_ptr_lo
        sta dest_ptr_lo_save

; second byte from prg
; specifies high part of 16 bit address
; to store prg
get_dest_ptr_hi
        jsr recv_byte
        sta dest_ptr_hi
        sta dest_ptr_hi_save     

        ; manually decrement chunk counter
        ; to account for 2 memory bytes
        sec
        lda chunk_size_lo
        sbc #2
        sta chunk_size_lo
        lda chunk_size_hi
        sbc #0 ; subtract carry
        sta chunk_size_hi

store_bytes_loop
        jsr recv_byte

        ldy #$00
        sta (dest_ptr_lo),y ; store at indirect location
        inc dest_ptr_lo
        bne skip_inc_dst_ptr
        inc dest_ptr_hi; increment high byte to move to next page
        jsr update_spinner
        
skip_inc_dst_ptr
        ; decrement the bytes left
        sec
        lda byte_count_lo
        sbc #1
        sta byte_count_lo
        lda byte_count_mid
        sbc #0
        sta byte_count_mid
        lda byte_count_hi
        sbc #0
        sta byte_count_hi

        ; decrement chunk counter
        sec
        lda chunk_size_lo
        sbc #1
        sta chunk_size_lo
        lda chunk_size_hi
        sbc #0 ; subtract carry
        sta chunk_size_hi

        ; check if the chunk counter is zero
        ; and if both are check if its the 
        ; end of the file
        lda chunk_size_hi
        bne recv_bytes_left_check
        lda chunk_size_lo
        bne recv_bytes_left_check

        ; buffer is zero
        ; reset counter
        lda chunk_size_lo_save
        sta chunk_size_lo
        lda chunk_size_hi_save
        sta chunk_size_hi

        ; run stop check
        ; emulating instead of calling $ffe1 so it does not close the channels
        ;jsr $ffe1
        lda $91
        cmp #$7f
        bne send_sync_byte_for_next_chunk; branch if run stop not pressed
        lda #$2b
        jsr send_byte 
        lda #$03 ; cancel
        jsr send_byte

        lda #$1e ; ?break  error
        sta vdrive_retcode

        rts ; return and cleanup

        ; send ok to send next round bytes
        ; once checksums are added this 
        ; might send 2 for resend last        
send_sync_byte_for_next_chunk
        lda #$2b
        jsr send_byte 
        lda #$01 ; send next chunk
        jsr send_byte

recv_bytes_left_check
        ; check if there are any more bytes
        ; based on remaining bytes
        lda byte_count_hi
        bne store_bytes_loop
        lda byte_count_mid
        bne store_bytes_loop
        lda byte_count_lo
        bne store_bytes_loop

        ; send last chunk was ok
        lda #$2b
        jsr send_byte 
        lda #$01 ; send next chunk
        jsr send_byte

        rts

send_data        
        lda #$2b ; sync byte
        jsr send_byte         
        
send_data_loop
        ldy #$00 ; not using y
        lda (dest_ptr_lo),y ; store at indirect location
        jsr send_byte 
        inc dest_ptr_lo
        bne skip_inc_dst_ptr2
        inc dest_ptr_hi; increment high byte to move to next page
        jsr update_spinner

skip_inc_dst_ptr2
        ; decrement the bytes left
        sec
        lda byte_count_lo
        sbc #1
        sta byte_count_lo
        lda byte_count_mid
        sbc #0
        sta byte_count_mid
        lda byte_count_hi
        sbc #0
        sta byte_count_hi

        ; decrement chunk counter
        sec
        lda chunk_size_lo
        sbc #1
        sta chunk_size_lo
        lda chunk_size_hi
        sbc #0 ; subtract carry
        sta chunk_size_hi

        ; check if the chunk counter is zero
        lda chunk_size_hi
        bne bytes_left_check2
        lda chunk_size_lo
        bne bytes_left_check2

        ; buffer is zero
        ; reset counter
        lda chunk_size_lo_save
        sta chunk_size_lo
        lda chunk_size_hi_save
        sta chunk_size_hi

        ; todo: wait for sync byte
        ; which is "send next chunk" for now until i get chunk headers
        jsr get_syncbyte
        jsr recv_byte
        cmp #$01 ; send next chunk or finish
        beq bytes_left_check2
        cmp #$02 ; resend last chunk
        beq adjust_pointers_resend_chunk
        cmp #$01 ; cancel? user cant really do that right now on server side?

       ; rts

bytes_left_check2
        ; check if there are any more bytes
        ; based on remaining bytes
        lda byte_count_hi
        bne send_data_loop
        lda byte_count_mid
        bne send_data_loop
        lda byte_count_lo
        bne send_data_loop

        ; last sync byte
wait_for_sync_byte        
        jsr recv_byte
        cmp #$2b
        bne wait_for_sync_byte   
        jsr recv_byte
        cmp #$01 ; last chunk ok
        beq finished_send
        cmp #$02 ; send again
        beq adjust_pointers_resend_chunk     
        
        
finished_send
        rts

adjust_pointers_resend_chunk ; todo
        rts

install_up9600
        jsr $c800
        jsr $c803 ; install/enable up9600 - docs say this should only be called once and $c806 re-enables
        rts

enable_up9600
       
        lda #$00  
        sta rs232_inputbuffer_lo
        lda #$c6 
        sta rs232_inputbuffer_hi        
        lda #$00
        sta rs232_outputbuffer_lo
        lda #$c7
        sta rs232_outputbuffer_hi
        lda #$01
        sta $c813 ; up9600 docs say to put a non-zero value here
        jsr $c806 ; enable up9600 
        rts

disable_up9600

        lda #$00
        sta rs232_inputbuffer_lo
        sta rs232_inputbuffer_hi
        sta rs232_outputbuffer_lo
        sta rs232_outputbuffer_hi

; this delay is to try to ensure any bytes like cancel
; are sent before disabling up9600
; todo: look into better way to check if bytes are all sent
        ldx #$ff             
delay_outer
        ldy #$ff             
delay_inner
        dey                  
        bne delay_inner      
        dex               
        bne delay_outer    
        jsr $c809
        rts

send_byte ; assumes a holds byte to send
        jsr $c80c             
        rts

recv_byte ; a holds received byte        
        jsr $c80f
        bcs recv_byte        
        rts

; sync byte to line bytes up and removes garbage on the line
get_syncbyte       
        jsr recv_byte     
        cmp #$2b ; sync byte (nothing special, just needs to be same on both sides)
        bne get_syncbyte
        rts

clear_up9600_timing_issues
        lda $d015
        sta save_d015
        lda #$00
        sta $d015 ; turn sprites off - causes timing issues with up9600?
        rts

restore_up9600_timing_issue_state
        lda save_d015
        sta $d015 ; restore sprite state
        rts

update_spinner
        ldx rotate_index
        lda rotate_chars,x
        sta $07e7               ; update screen char
        inx
        cpx #4
        bcc skip_reset
        ldx #0
skip_reset
        stx rotate_index
        rts

rotate_chars
        byte $42, $4e, $44, $4d ; | / - \
rotate_index
        byte 0

temp_search_term
        text ""




