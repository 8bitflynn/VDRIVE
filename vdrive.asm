; memory map
; $c000 - jmp table
; $c200 - up9600 bitbanger/vdrive
; $c500 - vars and constants
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

dest_ptr_lo_save = $c500
dest_ptr_hi_save = $c501
byte_count_lo_save = $c502
byte_count_hi_save = $c503
chunk_size_lo_save = $c504
chunk_size_hi_save = $c505
byte_count_lo = $c506
byte_count_hi = $c507
chunk_size_lo = $c508
chunk_size_hi = $c509
vdisk_devnum  = $c50a
spinner_char_save = $c50b
vdrive_retcode = $c50c
org_iload_lo   = $c50d
org_iload_hi   = $c50e
org_isave_lo   = $c50f
org_isave_hi   = $c510
temp_workspace1 = $c520
temp_workspace2 = $c521
temp_workspace3 = $c522
temp_workspace4 = $c523

*= $c000
        ; jump table
        jmp enable_vdrive
; $c003
        jmp disable_vdrive
        rts
   
enable_vdrive
        ; install before hooking vectors 
        ; so disable will restore original 
        ; vector
        jsr install_up9600

        ; default devnum for vdisk_devnum
        ; setting this memory location to
        ; 2 will route devnum 8 to original
        ; iload
        lda #$08
        sta vdisk_devnum

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
        stx temp_workspace2 ; should be same as $C3
        sty temp_workspace3 ; should be same as $C4
        php             
        pla            
        sta temp_workspace4 
        lda vdisk_devnum
        cmp devnum ; compare with number from LOAD and if different, call original ILOAD
        beq vdrive_load  
        jsr disable_up9600 ; just in case          
        lda temp_workspace4 ; original status
        pha ; push status onto stack
        plp ; pull status into CPU status reg            
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

        ; fixup to turn off sprites
        ; or anything needed to keep
        ; up9600 timing stable
        jsr clear_up9600_timing_issues 

        jsr enable_up9600 ; rs232 buffer @ $c600

        ; searching
        jsr $f5af

        jsr send_load_request

        lda #$00 ; bit 6 eoi (had $40 here bit 6 for EOI)
        sta $90 ; kernal i/o status word
        lda #$00
        sta $93 ; 0=loading and 1=verifying msg
        lda #$ff ; ff = success
        sta vdrive_retcode 

        ; show loading
        jsr $f5d2

        lda $07e7
        sta spinner_char_save 

        jsr recv_load_response
        ; A has lvdrive_retcode
        cmp #$ff
        bne return_init

recv_payload
        ; bitbanger/vdisk receiver entry
        jsr recv_data

        lda spinner_char_save
        sta $07e7

        ; turn off up9600
        jsr disable_up9600     

return_init
        lda vdrive_retcode ; holds any error or break message
        cmp #$ff
        beq return_success
        sec
        jmp exit

return_success
        clc
        ;lda #$00 ; success
exit
        ldx dest_ptr_lo ; registers hold end address
        ldy dest_ptr_hi
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
        lda vdisk_devnum 
        cmp devnum 
        beq vdisk_save
        lda org_isave_lo
        sta temp_ptr_lo
        lda org_isave_hi
        sta temp_ptr_hi
        jmp (org_isave_lo)

vdisk_save

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
        lda byte_count_hi
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

; bitbanger/vdisk chunked transfer
*= $c200

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
        bne bytes_left_check
        lda chunk_size_lo
        bne bytes_left_check

        ; buffer is zero
        ; reset counter
        lda chunk_size_lo_save
        sta chunk_size_lo
        lda chunk_size_hi_save
        sta chunk_size_hi

        ; run stop check
        ; emulating so it does not close the channels
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
        ; future expansion might be to do
        ; a crc check on bytes and if not
        ; correct send different byte to
        ; request send last chunk again
send_sync_byte_for_next_chunk
        lda #$2b
        jsr send_byte 
        lda #$01 ; send next chunk
        jsr send_byte

bytes_left_check
        ; check if there are any more bytes
        ; based on remaining bytes
        lda byte_count_hi
        bne store_bytes_loop
        lda byte_count_lo
        bne store_bytes_loop
        rts

send_data        
        lda #$2b ; sync byte
        jsr send_byte 
        
        ldy #$00
send_data_loop
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
;wait_for_sync_byte
;        jsr recv_byte
;        cmp #$2b
;        bne wait_for_sync_byte       

       ; rts

bytes_left_check2
        ; check if there are any more bytes
        ; based on remaining bytes
        lda byte_count_hi
        bne send_data_loop
        lda byte_count_lo
        bne send_data_loop

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
        lda #$00
        sta $d015 ; turn sprites off - causes corruption
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



