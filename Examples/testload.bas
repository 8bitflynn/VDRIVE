10 ip=peek(49188)+peek(49189)*256
20 il=peek(49188):ih=peek(49189)
30 lp=peek(49190)+peek(49191)*256
32 rb=peek(49194)+peek(49195)*256
35 s$="data4.d64":gosub 200
60 sys 49164
70 sys 49167
80 s$="portal":gosub 200
110 poke 185, 1:poke 186,8:poke 183,len(s$):poke 187,il:poke 188,ih:goto 300
200 for i=1 to len(s$):poke ip+i-1,asc(mid$(s$,i,1)):next
210 poke lp,len(s$):return
300 sys 49170



