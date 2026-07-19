#!/usr/bin/env python3
# Эталонные макеты экранов «Спасибо, не надо» — 1920x1080, Pillow.
import os, math
from PIL import Image, ImageDraw, ImageFilter, ImageFont
BASE="/Users/ipuzanova/AI_GAME_STUDIO/projects/life-choices/docs"
SPR=f"{BASE}/assets/sprites"; FONT=f"{BASE}/assets/fonts/Rubik-VariableWght.ttf"
OUT=f"{BASE}/screens/mockups"; os.makedirs(OUT,exist_ok=True)
W,H=1920,1080
# токены
COBALT=(47,84,200,255); CDEEP=(31,58,150,255); CINK=(22,41,107,255); INK=(20,26,61,255)
WHITE=(255,255,255,255); DA=(92,191,95,255); DA_DK=(63,158,70,255)
NO=(232,68,58,255); HEALTH=(229,67,59,255); ENERGY=(246,201,69,255)
BULB=(255,216,115,255); YEL=(248,210,76,255); REL_R=(232,68,58,255)
REL_Y=(246,201,69,255); REL_G=(92,191,95,255); TRACK=(231,233,245,255)
BROWN=(58,42,0,255); GREY=(216,220,240,255)
_bg=Image.open(f"{SPR}/sunburst-bg.png").convert("RGBA")
_fcache={}
def font(sz):
    sz=int(sz)
    if sz not in _fcache:
        f=ImageFont.truetype(FONT,sz)
        try: f.set_variation_by_axes([900])
        except: pass
        _fcache[sz]=f
    return _fcache[sz]
_icons={}
def icon(name,size):
    k=(name,size)
    if k not in _icons:
        _icons[k]=Image.open(f"{SPR}/icon-{name}.png").convert("RGBA").resize((int(size),int(size)),Image.LANCZOS)
    return _icons[k]
def arrow(img,cx,cy,size,color,left=True):
    d=ImageDraw.Draw(img); s=size
    if left: pts=[(cx+s*0.5,cy-s*0.6),(cx-s*0.6,cy),(cx+s*0.5,cy+s*0.6)]
    else: pts=[(cx-s*0.5,cy-s*0.6),(cx+s*0.6,cy),(cx-s*0.5,cy+s*0.6)]
    d.polygon(pts,fill=color,outline=INK,width=4)
def face(img,cx,cy,col=INK,r=22):
    d=ImageDraw.Draw(img)
    d.ellipse([cx-r,cy-r,cx+r,cy+r],outline=col,width=6)
    d.ellipse([cx-r*0.45-4,cy-r*0.3,cx-r*0.45+4,cy-r*0.3+10],fill=col)
    d.ellipse([cx+r*0.45-4,cy-r*0.3,cx+r*0.45+4,cy-r*0.3+10],fill=col)
    d.arc([cx-r*0.55,cy-r*0.2,cx+r*0.55,cy+r*0.55],20,160,fill=col,width=6)
_sd=ImageDraw.Draw(Image.new("RGBA",(10,10)))
def tw(t,f): b=_sd.textbbox((0,0),t,font=f); return b[2]-b[0]
def wrap(t,f,mw):
    words=t.split(); lines=[]; cur=""
    for w in words:
        cand=(cur+" "+w).strip()
        if tw(cand,f)<=mw or not cur: cur=cand
        else: lines.append(cur); cur=w
    if cur: lines.append(cur)
    return lines
def fit(t,mw,mh,hi=110,lo=18,wrapping=True):
    lines=[t]; f=font(lo); lh=0
    for s in range(int(hi),int(lo)-1,-2):
        f=font(s); lines=wrap(t,f,mw) if wrapping else [t]
        wmax=max(tw(l,f) for l in lines)
        asc,desc=f.getmetrics(); lh=asc+desc+int(s*0.10)
        if wmax<=mw and lh*len(lines)<=mh: return lines,f,lh
    return lines,f,lh
def block(img,cx,cy,lines,f,lh,fill,sw=0,scol=None):
    d=ImageDraw.Draw(img); tot=lh*len(lines); y=cy-tot//2+lh//2
    for ln in lines:
        d.text((cx,y),ln,font=f,fill=fill,anchor="mm",stroke_width=sw,stroke_fill=scol); y+=lh
def panel(img,box,r,fill,outline=None,ow=0,shadow=True,shoff=(0,12),shblur=18,shcol=(0,0,0,95)):
    box=[int(v) for v in box]
    if shadow:
        lay=Image.new("RGBA",img.size,(0,0,0,0)); ld=ImageDraw.Draw(lay)
        ld.rounded_rectangle([box[0]+shoff[0],box[1]+shoff[1],box[2]+shoff[0],box[3]+shoff[1]],r,fill=shcol)
        img.alpha_composite(lay.filter(ImageFilter.GaussianBlur(shblur)))
    ImageDraw.Draw(img).rounded_rectangle(box,r,fill=fill,outline=outline,width=ow)
def bg(): return _bg.copy()
def save(img,name): img.convert("RGB").save(f"{OUT}/{name}"); print("  ",name)

# ---------- компоненты ----------
def badge(img,x,y,age,w=200,h=224):
    panel(img,[x,y,x+w,y+h],44,COBALT,WHITE,14)
    d=ImageDraw.Draw(img); d.text((x+w//2,y+40),"ВОЗРАСТ",font=font(28),fill=WHITE,anchor="mm")
    ln,f,lh=fit(str(age),w-40,h-90,hi=140,wrapping=False); block(img,x+w//2,y+h//2+28,ln,f,lh,WHITE)
def _hudlabel(img,x,y,w,text):  # подпись под элементом (как в styleframe), тёмная
    ImageDraw.Draw(img).text((x+w//2,y+20),text,font=font(27),fill=CDEEP,anchor="mm")
def money(img,x,y,val,w=330,mult=None):  # СИНЯЯ пилюля (styleframe), подпись «ДЕНЬГИ ◎» под ней
    ph=88; panel(img,[x,y,x+w,y+ph],28,COBALT,WHITE,9)
    d=ImageDraw.Draw(img)
    ln,f,lh=fit(val,w-40,60,hi=54,wrapping=False); d.text((x+w//2,y+ph//2),ln[0],font=f,fill=WHITE,anchor="mm")
    lbl="ДЕНЬГИ"+("  "+mult if mult else ""); lf=font(26); lw=tw(lbl,lf); lx=x+w//2-(lw+38)//2
    d.text((lx,y+ph+22),lbl,font=lf,fill=CDEEP,anchor="lm"); img.alpha_composite(icon("coin",28),(lx+lw+8,y+ph+8))
def stat(img,x,y,label,frac,col,ic=None,w=290):  # белая капсула: иконка+бар, подпись под ней
    ph=72; panel(img,[x,y,x+w,y+ph],ph//2,WHITE,INK,6); d=ImageDraw.Draw(img)
    if ic: img.alpha_composite(icon(ic,ph-16),(x+8,y+8))
    bx0,bx1=x+ph+2,x+w-18; by0,by1=y+ph//2-13,y+ph//2+13
    d.rounded_rectangle([bx0,by0,bx1,by1],13,fill=TRACK,outline=(0,0,0,45),width=2)
    d.rounded_rectangle([bx0,by0,bx0+int((bx1-bx0)*frac),by1],13,fill=col)
    _hudlabel(img,x,y+ph+2,w,label)
def balancer(img,x,y,pos,w=380):  # белая капсула: зона-бар + HOLD ZONE + стрелки-маркер, подпись под ней
    ph=72; panel(img,[x,y,x+w,y+ph],ph//2,WHITE,INK,6); d=ImageDraw.Draw(img)
    bx0,bx1,by0,by1=x+20,x+w-20,y+18,y+ph-18
    for a,b,c in [(0,.18,REL_R),(.18,.34,REL_Y),(.34,.66,REL_G),(.66,.82,REL_Y),(.82,1,REL_R)]:
        d.rectangle([bx0+(bx1-bx0)*a,by0,bx0+(bx1-bx0)*b,by1],fill=c)
    d.rounded_rectangle([bx0,by0,bx1,by1],9,outline=(0,0,0,55),width=2)
    cx=(bx0+bx1)/2; hf=font(15)
    d.text((cx,(by0+by1)/2-8),"HOLD",font=hf,fill=(26,74,30,255),anchor="mm")
    d.text((cx,(by0+by1)/2+8),"ZONE",font=hf,fill=(26,74,30,255),anchor="mm")
    mx=bx0+(bx1-bx0)*pos
    d.polygon([(mx-10,by0-7),(mx+10,by0-7),(mx,by0+7)],fill=WHITE,outline=INK,width=3)
    d.polygon([(mx-10,by1+7),(mx+10,by1+7),(mx,by1-7)],fill=WHITE,outline=INK,width=3)
    _hudlabel(img,x,y+ph+2,w,"ОТНОШЕНИЯ")
def child(img,cx,cy,r=54,lit=False):
    if lit:
        g=Image.new("RGBA",img.size,(0,0,0,0)); ImageDraw.Draw(g).ellipse([cx-r-22,cy-r-22,cx+r+22,cy+r+22],fill=(246,201,69,120))
        img.alpha_composite(g.filter(ImageFilter.GaussianBlur(14)))
    panel(img,[cx-r,cy-r,cx+r,cy+r],26,ENERGY if lit else WHITE,INK,6,shadow=True)
    face(img,cx,cy,INK,r=int(r*0.5))
def timer(img,cx,cy,frac,num,rad=90):
    d=ImageDraw.Draw(img)
    d.ellipse([cx-rad,cy-rad,cx+rad,cy+rad],fill=WHITE,outline=INK,width=4)
    d.pieslice([cx-rad+8,cy-rad+8,cx+rad-8,cy+rad-8],-90,-90+360*frac,fill=NO)
    inr=rad-30; d.ellipse([cx-inr,cy-inr,cx+inr,cy+inr],fill=COBALT)
    d.text((cx,cy),str(num),font=font(66),fill=WHITE,anchor="mm")
def bubble(img,x,y,text,w=320,h=150):
    panel(img,[x,y,x+w,y+h-30],34,YEL,INK,7)
    d=ImageDraw.Draw(img); d.polygon([(x+60,y+h-46),(x+140,y+h-46),(x+60,y+h)],fill=YEL,outline=INK,width=6)
    d.rectangle([x+66,y+h-52,x+134,y+h-44],fill=YEL)
    ln,f,lh=fit(text,w-44,h-70,hi=46); block(img,x+w//2,y+(h-30)//2,ln,f,lh,BROWN)
def card(img,cx,cy,text,w=900,h=430,sub=None,dim=False,textcol=WHITE):
    box=[cx-w//2,cy-h//2,cx+w//2,cy+h//2]
    panel(img,box,46,COBALT,WHITE,18)
    d=ImageDraw.Draw(img)
    d.rounded_rectangle([box[0]+34,box[1]+34,box[2]-34,box[3]-34],30,outline=CDEEP,width=6)
    # лампочки
    n=[(box[0]+58+i*(w-116)/9,box[1]+58) for i in range(10)]+[(box[0]+58+i*(w-116)/9,box[3]-58) for i in range(10)]
    n+=[(box[0]+58,box[1]+58+j*(h-116)/4) for j in range(1,4)]+[(box[2]-58,box[1]+58+j*(h-116)/4) for j in range(1,4)]
    for (bx,by) in n: d.ellipse([bx-11,by-11,bx+11,by+11],fill=BULB,outline=(180,120,20,255),width=2)
    ln,f,lh=fit(text,w-180,h-150,hi=104); block(img,cx,cy,ln,f,lh,textcol,4,(20,20,50,140))
    if dim:
        ov=Image.new("RGBA",img.size,(0,0,0,0)); ImageDraw.Draw(ov).rounded_rectangle(box,46,fill=(20,25,55,120)); img.alpha_composite(ov)
    if sub:
        sf=font(30); sww=tw(sub,sf)
        panel(img,[cx-sww//2-26,box[3]+16,cx+sww//2+26,box[3]+76],16,(18,24,54,235),None,0,shadow=False)
        d.text((cx,box[3]+46),sub,font=sf,fill=(232,238,255,255),anchor="mm")
def plate(img,cx,cy,w,h,text,fill,textcol):
    panel(img,[cx-w//2,cy-h//2,cx+w//2,cy+h//2],40,fill,INK,10)
    ln,f,lh=fit(text,w-56,h-40,hi=66); block(img,cx,cy,ln,f,lh,textcol)
def full(fill=COBALT):
    img=bg(); ov=Image.new("RGBA",img.size,fill); img.alpha_composite(ov)
    rays=Image.new("RGBA",img.size,(0,0,0,0)); rd=ImageDraw.Draw(rays)
    for a in range(0,360,12): rd.pieslice([W//2-1600,H//2-1600,W//2+1600,H//2+1600],a,a+6,fill=(255,255,255,28))
    img.alpha_composite(rays); return img

# ---------- HUD-ряд ----------
def hud_full(img,age,val,hp,en,rel,mult=None,child_lit=None):
    x=46
    badge(img,x,30,age); x+=224
    money(img,x,30,val,mult=mult); x+=356
    stat(img,x,30,"ЗДОРОВЬЕ",hp,HEALTH,ic="heart"); x+=314
    stat(img,x,30,"ЭНЕРГИЯ",en,ENERGY,ic="lightning"); x+=314
    balancer(img,x,30,rel)
    if child_lit is not None: child(img,1850,88,44,lit=child_lit)

def answers(img,yes="ДА",no="СПАСИБО, НЕ НАДО",y=940,yfill=DA,nfill=NO):
    plate(img,610,y,420,190,yes,yfill,INK)
    plate(img,1310,y,470,190,no,nfill,WHITE)
    arrow(img,352,y,54,DA,left=True); arrow(img,1602,y,54,NO,left=False)

# ==================== ЭКРАНЫ ====================
def S1():
    img=full()
    d=ImageDraw.Draw(img)
    block(img,W//2,150,['«СПАСИБО, НЕ НАДО»'],font(84),96,WHITE,4,(20,20,50,160))
    rules=["Проживите ЦЕЛУЮ ЖИЗНЬ за пару минут — в прямом эфире!",
           "На каждый вопрос — рычаг: ДА или СПАСИБО, НЕ НАДО. 5 секунд — дальше решаем за вас!",
           "С возрастом откроются ручки жизни. Рук две — всё удержать нельзя, и это нормально!",
           "Правильного ответа нет. Есть только ВАША жизнь."]
    y=340
    for r in rules:
        ln,f,lh=fit(r,1300,120,hi=40); panel(img,[W//2-720,y-14,W//2+720,y+lh*len(ln)+14],20,(31,58,150,235),None,0,shadow=False)
        block(img,W//2,y+lh*len(ln)//2,ln,f,lh,WHITE); y+=lh*len(ln)+34
    plate(img,W//2,y+70,560,150,"НАЧАТЬ ЖИЗНЬ  (Enter)",DA,INK)
    save(img,"S1-opener.png")
def S2():
    img=bg(); badge(img,46,30,7); timer(img,W//2,150,0.72,5)
    card(img,W//2,470,"Съесть жука?",w=820,h=380); answers(img); save(img,"S2-childhood.png")
def S3():
    img=bg(); hud_full(img,34,"₽ 10 000",0.68,0.52,0.56,mult="×2",child_lit=False)
    timer(img,W//2,250,0.6,5); bubble(img,1500,300,"А вот это зря…")
    card(img,W//2,560,"Взять ипотеку на 30 лет?",w=920,h=380,sub="мелким шрифтом: −40 ₽ взнос, затем −0.3 ₽/сек ×20 лет")
    answers(img); save(img,"S3-adult.png")
def S4():
    img=full(); block(img,W//2,H//2-40,["ПОРА","ЗАРАБАТЫВАТЬ!"],font(150),168,YEL,6,NO)
    ImageDraw.Draw(img).text((W//2,H//2+180),"★ теперь у вас есть работа ★",font=font(48),fill=WHITE,anchor="mm")
    save(img,"S4-banner.png")
def S5():
    img=bg(); hud_full(img,18,"₽ 0",1.0,1.0,0.5); card(img,W//2,520,"…",w=760,h=300)
    ov=Image.new("RGBA",img.size,(16,23,51,150)); img.alpha_composite(ov)
    panel(img,[W//2-560,300,W//2+560,760],40,YEL,INK,8)
    block(img,W//2,410,wrap("Поздравляем, теперь у вас есть работа!",font(56),1000),font(56),72,BROWN)
    block(img,W//2,560,wrap("Крутите вот эту ручку — и у вас будут деньги. Не крутите — денег не будет!",font(38),960),font(38),50,(74,54,0,255))
    plate(img,W//2,690,420,120,"ПОНЯТНО  (Enter)",COBALT,WHITE); save(img,"S5-tutorial.png")
def S6():
    img=bg(); badge(img,46,30,47); timer(img,W//2,150,0.9,2)
    d=ImageDraw.Draw(img)
    panel(img,[1500,40,1874,150],20,(20,25,55,210),None,0,shadow=False)
    d.text((1520,70),"МЫСЛЬ 3/5",font=font(30),fill=WHITE); d.text((1520,108),"ПРОВАЛОВ: 1",font=font(30),fill=(255,150,140,255))
    ov=Image.new("RGBA",img.size,(20,20,50,90)); img.alpha_composite(ov)
    card(img,W//2,470,"А чего ты вообще достиг?",w=880,h=320)
    plate(img,610,940,470,180,"ВСЁ НОРМАЛЬНО",DA,INK); plate(img,1310,940,360,180,"О НЕТ",GREY,INK)
    save(img,"S6-blitz.png")
def S7():
    img=full((36,58,134,255)); block(img,W//2,H//2-40,["ВЫГОРАНИЕ!"],font(150),168,YEL,6,NO)
    ImageDraw.Draw(img).text((W//2,H//2+150),"крутите деньги — идёт туго · подышите рычагом",font=font(42),fill=WHITE,anchor="mm")
    save(img,"S7-burnout.png")
def S8():
    img=bg(); badge(img,46,30,51); stat(img,270,30,"ЗДОРОВЬЕ",0.3,HEALTH,ic="heart"); stat(img,584,30,"ЭНЕРГИЯ",0.18,ENERGY,ic="lightning")
    card(img,W//2,470,"Встать сегодня с кровати?",w=860,h=340)
    d=ImageDraw.Draw(img);
    # пульс
    img.alpha_composite((lambda g: (ImageDraw.Draw(g).ellipse([W//2-40,760,W//2+40,840],fill=(255,255,255,90)),g)[1])(Image.new("RGBA",img.size,(0,0,0,0))).filter(ImageFilter.GaussianBlur(10)))
    plate(img,W//2,940,460,150,"СОБРАТЬСЯ",WHITE,INK)
    d.text((W//2,860),"…нажми в такт пульсу…",font=font(34),fill=(210,210,210,255),anchor="mm")
    img=Image.merge("RGB",[c.point(lambda v:int(v*0.85)) for c in img.convert("RGB").convert("L").split()*1]) if False else img
    gr=img.convert("L").convert("RGBA")
    # зерно
    import random; random.seed(7); noise=Image.new("L",(W,H),0); npx=noise.load()
    for i in range(0,W,3):
        for j in range(0,H,3): npx[i,j]=random.randint(0,40)
    gr.putalpha(255); gimg=Image.blend(gr, Image.new("RGBA",(W,H),(0,0,0,255)),0.12)
    gn=Image.new("RGBA",(W,H),(0,0,0,0)); gn.putalpha(noise.resize((W,H))); gimg.alpha_composite(gn)
    save(gimg,"S8-depression.png")
def S9():
    img=bg(); hud_full(img,39,"₽ 8 400",0.6,0.55,0.48)
    timer(img,W//2,250,0.5,5); child(img,1720,860,90,lit=True); bubble(img,300,560,"Скорее!")
    card(img,W//2,540,"Обычная жизнь идёт…",w=760,h=320); answers(img,y=940); save(img,"S9-child.png")
def S10():
    img=bg(); badge(img,46,30,58); money(img,270,30,"₽ 30"); stat(img,626,30,"ЗДОРОВЬЕ",0.22,HEALTH,ic="heart")
    card(img,W//2,470,"Пора подлечиться!",w=820,h=360,sub="цена 100 ₽",dim=True)
    plate(img,W//2,780,860,130,"Как жаль, у вас нет денег на это!",NO,WHITE)
    answers(img,y=960); save(img,"S10-blocked.png")
def finale(name,cause,story,cause_col=WHITE):
    img=full()
    block(img,W//2,180,["СПАСИБО ЗА ИГРУ!"],font(92),104,YEL,5,NO)
    ln,f,lh=fit("Причина конца: "+cause,1500,120,hi=52); block(img,W//2,320,ln,f,lh,cause_col)
    panel(img,[W//2-720,410,W//2+720,820],36,(31,58,150,235),None,0,shadow=False)
    block(img,W//2,615,wrap(story,font(40),1360),font(40),56,WHITE)
    plate(img,W//2,940,520,140,"НАЧАТЬ ЗАНОВО  (Enter)",DA,INK); save(img,name)
def S11(): finale("S11-finale-happy.png","весёлая старость",
    "Но не переживайте! Ведь вы родились у прекрасных родителей. В детстве ели жуков и ничего не боялись. Рыжий кот, его котята и их котята прожили с вами всю жизнь. Взяли ипотеку — и выплатили. Под старость раздали сбережения внукам.")
def S12(): finale("S12-finale-fatal.png","вы сунули палец в розетку",
    "Но не переживайте! Ведь вы родились у прекрасных родителей. И это, пожалуй, всё, что вы успели.",cause_col=YEL)
def S13():
    img=bg(); badge(img,46,30,48); timer(img,W//2,150,0.9,2); bubble(img,300,560,"Ну-ну…")
    ov=Image.new("RGBA",img.size,(20,20,50,90)); img.alpha_composite(ov)
    card(img,W//2,470,"КУПИТЬ МОТОЦИКЛ И ГНАТЬ 200?!",w=940,h=340,sub="⚠ молчание = ДА")
    plate(img,1310,940,470,180,"СПАСИБО, НЕ НАДО",NO,WHITE); plate(img,610,940,420,180,"ДА",DA,INK)
    arrow(img,1602,940,54,NO,left=False); save(img,"S13-impulse.png")
def S14():
    img=bg(); hud_full(img,40,"₽ 5 200",0.6,0.5,0.5); timer(img,W//2,250,0.5,5); bubble(img,1520,320,"Ещё разок?")
    card(img,W//2,560,"Дать любви второй шанс?",w=880,h=360); answers(img); save(img,"S14-second-chance.png")
def S15():
    img=bg(); hud_full(img,60,"₽ 3 100",0.4,0.4,0.6); timer(img,W//2,250,0.6,5); child(img,1720,860,80,lit=False)
    card(img,W//2,560,"Ребёнок вырос. Помочь всё равно?",w=900,h=360); answers(img); save(img,"S15-kids-grown.png")
def S16():
    img=bg(); badge(img,46,30,21); money(img,270,30,"₽ 1 500",mult="×1"); timer(img,W//2,250,0.6,5); bubble(img,1520,320,"Смело!")
    card(img,W//2,560,"Бросить универ ради стартапа?",w=900,h=360,sub="ДА: доход ×5 … или деньги обнуляются")
    answers(img); save(img,"S16-startup.png")
def COMP_bubble():
    img=full((31,58,150,255));
    for i,(t,x) in enumerate([("Красавчик!",480),("А вот это зря…",1180)]):
        bubble(img,x-160,420,t,w=360,h=170)
    ImageDraw.Draw(img).text((W//2,240),"Облачко Ведущего — компонент",font=font(48),fill=WHITE,anchor="mm")
    save(img,"C1-host-bubble.png")
def COMP_hud():
    img=bg(); hud_full(img,34,"₽ 10 000",0.68,0.52,0.56,mult="×2",child_lit=False)
    ImageDraw.Draw(img).text((W//2,H-120),"HUD-ряд: возраст · деньги · здоровье · энергия · отношения · (кнопка-ребёнок)",font=font(40),fill=INK,anchor="mm")
    save(img,"C2-hud-row.png")

print("Рендер макетов →",OUT)
for fn in [S1,S2,S3,S4,S5,S6,S7,S8,S9,S10,S11,S12,S13,S14,S15,S16,COMP_bubble,COMP_hud]:
    fn()
print("Готово.")
