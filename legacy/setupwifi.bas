0 gosub 880: rem init up9600
20 print chr$(14): rem mixed case
40 input "ssid: "; sd$
60 sc$=sd$: gosub 1140: sd$=sc$: rem cleanup ssid
80 ? sd$
100 input "pwd: "; pd$
120 sc$=pd$: gosub 1140 : pd$=sc$: rem cleanup pwd
140 ? pd$
160 input "mode (0=client,1=server): "; md$
170 sc$=md$: gosub 1140: md$=sc$: rem cleanup mode
180 ? md$
190 if md$="0" then input "client ip: "; ip$
200 if md$="0" then sc$=ip$: gosub 1140: ip$=sc$: ? ip$
210 if md$="0" then input "client port: "; cp$
220 if md$="0" then sc$=cp$: gosub 1140: cp$=sc$: ? cp$

240 rem sync byte 0x2b
260 sb = 43
280 poke 780, sb: sys 51200+12

300 rem length of ssid
320 sdl = len(sd$)
340 poke 780, sdl: sys 51200+12
360 ob$ = sd$: gosub 800

400 rem length of pwd
420 pdl = len(pd$)
440 poke 780, pdl: sys 51200+12
460 ob$ = pd$: gosub 800

500 rem length of client ip
510 if md$="0" then ipl = len(ip$): goto 530
520 ipl = 0
530 poke 780, ipl: sys 51200+12
540 if ipl > 0 then ob$ = ip$: gosub 800

600 rem client port (2 bytes)
610 if md$="0" then pt = val(cp$)
615 if md$="0" then goto 630
620 pt = 0
630 poke 780, pt and 255: sys 51200+12
640 poke 780, pt/256: sys 51200+12

660 rem mode string
670 poke 780, val(md$): sys 51200+12

700 gosub 1010
710 print chr$(142): rem upper case
720 end

780 rem send string bytes in ob$
800 l=len(ob$)
820 for b=1 to l: poke 780,asc(mid$(ob$, b, 1)): sys 51200+12: next
840 return

860 rem init up9600
880 sys 51200
900 poke 247,96
920 poke 248,40
940 sys 51200+3
960 return

980 rem receive bytes
1010 rem receive until timeout
1020 ti$ = ti$: rem reset timer
1030 rem loop for a few seconds
1040 if peek(783) and 1 = 0 then print chr$(peek(780));
1060 if ti$ < "00" then goto 1040
1080 return

1120 rem convert screen codes to binary
1140 rt$ = "": l=len(sc$)
1160 for b=1 to l
1180 c=asc(mid$(sc$,b,1))
1200 if c > 63 and c < 96 then c=c+32: goto 1260
1220 if c > 95 and c < 128 then c=c-32: goto 1260
1240 if c > 191 and c < 224 then c=c-128: goto 1260
1260 rt$ = rt$ + chr$(c)
1280 ? c
1300 next
1320 sc$ = rt$: rem reassign fixed var
1340 return
