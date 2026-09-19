10 ip=peek(49179)+256*peek(49180)
20 il=peek(49179):ih=peek(49180)
30 lp=peek(49181)+256*peek(49182)
40 d$="data4.d64"
50 for i=1 to len(d$)
60 poke ip+i-1,asc(mid$(d$,i,1))
70 next i
80 poke lp,len(d$)
90 sys 49164
100 sys 49167
110 f$="testsave"
120 for i=1 to len(f$)
130 poke ip+i-1,asc(mid$(f$,i,1))
140 next i
150 poke lp,len(f$)
160 poke 186,8
170 poke 183,len(f$)
180 poke 187,il
190 poke 188,ih
200 poke 193,1:poke 194,8
210 en=peek(45)+peek(46)*256
220 poke 174,en-256*int(en/256):poke 175,int(en/256)
230 sys 49173
