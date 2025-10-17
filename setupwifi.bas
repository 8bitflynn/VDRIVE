10 gosub 310: rem init up9600
20 print chr$(14): rem mixed case
30 input "ssid: "; sd$
40 sc$=sd$: gosub 440: sd$=sc$: rem cleanup ssid
50 ? sd$
60 input "pwd: "; pd$
70 sc$=pd$: gosub 440 : pd$=sc$: rem cleanup pwd
80 ? pd$
90 rem sync byte 0x2b
100 sb = 43
110 poke 780, sb: sys 51200+12
112 rem good spot for a operation byte
115 rem ib = 10
118 rem poke 780, sb: sys 51200+12
120 rem length of ssid
130 sdl = len(sd$)
140 poke 780, sdl: sys 51200+12
150 rem ssid
160 ob$ = sd$: gosub 270
170 rem length of pwd
180 pdl = len(pd$)
190 poke 780, pdl
200 sys 51200+12
210 rem pwd
220 ob$ = pd$: gosub 270
230 gosub 370: gosub 370: gosub 370: gosub 370
240 print chr$(142): rem upper case
250 end

260 rem send string bytes in ob$
270 l=len(ob$)
280 for b=1 to l:poke 780,asc(mid$(ob$, b, 1)):sys51200+12:next
290 return

300 rem init up9600
310 sys 51200
320 poke 247,96
330 poke 248,40
340 sys 51200+3
350 return

360 rem receive bytes
370 sys 51200+15
380 a=peek(780)
390 sr=peek(783)
400 cf=sr and 1
410 if cf=0 then print chr$(a);:goto 370:rem get all bytes
420 return

430 rem convert screen codes to binary
440 rt$ = "":l=len(sc$)
450 for b=1 to l
460 c=asc(mid$(sc$,b,1))
470 if c > 63 and c < 96 then c=c+32:goto 500
480 if c > 95 and c < 128 then c=c-32:goto 500
490 if c > 191 and c < 224 then c=c-128:goto 500
500 rt$ = rt$ + chr$(c)
510 ? c
520 next
530 sc$ = rt$ : rem reassign fixed var
540 return