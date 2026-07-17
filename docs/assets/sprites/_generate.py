#!/usr/bin/env python3
"""Генерация P0-спрайтов «Спасибо, не надо» через Pillow (супер-сэмплинг)."""
import math, os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = "/Users/ipuzanova/AI_GAME_STUDIO/projects/life-choices/docs/assets/sprites"
os.makedirs(OUT, exist_ok=True)

# --- токены ---
COBALT=(47,84,200); COBALT_DEEP=(31,58,150); INK=(20,26,61)
DA=(92,191,95); DA_DK=(63,158,70); NO=(232,68,58); NO_DK=(201,50,42)
HEALTH_T=(255,91,82); HEALTH_B=(229,67,59)
ENERGY_T=(255,226,122); ENERGY_B=(246,201,69)
BULB=(255,216,115); WHITE=(255,255,255)
SUN=[(242,162,62),(248,210,76),(170,205,104),(244,184,120)]
REL_R=(232,68,58); REL_Y=(246,201,69); REL_G=(92,191,95)
TRACK=(231,233,245)

def save(img, name):
    img.save(os.path.join(OUT, name))
    print("  ", name, img.size)

def ss_canvas(w, h, s=4):
    return Image.new("RGBA", (w*s, h*s), (0,0,0,0)), s

def rrect(draw, box, r, fill=None, outline=None, width=0):
    draw.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)

def finish(img, w, h):
    return img.resize((w, h), Image.LANCZOS)

def drop_shadow(base, offset=(0,10), blur=14, color=(0,0,0,90), pad=40):
    """base RGBA -> new RGBA with soft shadow, expanded by pad."""
    w,h=base.size
    canvas=Image.new("RGBA",(w+pad*2,h+pad*2),(0,0,0,0))
    sh=Image.new("RGBA",canvas.size,(0,0,0,0))
    a=base.split()[3]
    shape=Image.new("RGBA",canvas.size,(0,0,0,0))
    shape.paste(Image.new("RGBA",base.size,color), (pad+offset[0], pad+offset[1]), a)
    shape=shape.filter(ImageFilter.GaussianBlur(blur))
    canvas=Image.alpha_composite(canvas, shape)
    canvas.alpha_composite(base, (pad, pad))
    return canvas

# ---------- 1. Санбёрст-фон ----------
def sunburst():
    W,H=1920,1080; s=2
    img=Image.new("RGBA",(W*s,H*s),(248,210,120,255))
    d=ImageDraw.Draw(img)
    cx,cy=int(W*0.5*s),int(H*0.42*s); R=int(2600*s)
    step=11
    for i,a in enumerate(range(8,368,step)):
        col=SUN[i%4]
        d.pieslice([cx-R,cy-R,cx+R,cy+R], a, a+step, fill=col+(255,))
    # звёзды
    def star(cx,cy,rad,fill,outline=None,ow=0):
        pts=[]
        for k in range(10):
            ang=math.pi/2 + k*math.pi/5
            rr=rad if k%2==0 else rad*0.42
            pts.append((cx+rr*math.cos(ang), cy-rr*math.sin(ang)))
        d.polygon(pts, fill=fill, outline=outline, width=ow)
    stars=[(0.09,0.44,52,WHITE,None),(0.15,0.66,34,None,COBALT),
           (0.86,0.34,48,WHITE,None),(0.90,0.62,40,None,COBALT),
           (0.78,0.16,32,WHITE,None),(0.24,0.20,30,None,COBALT),
           (0.66,0.80,30,WHITE,None),(0.40,0.86,26,WHITE,None)]
    for x,y,rad,fill,oc in stars:
        if fill: star(int(x*W*s),int(y*H*s),rad*s,fill+(255,))
        else: star(int(x*W*s),int(y*H*s),rad*s,None,oc+(255,),int(4*s))
    # мягкая виньетка
    vig=Image.new("L",(W*s,H*s),0); dv=ImageDraw.Draw(vig)
    dv.ellipse([-int(W*0.2*s),-int(H*0.2*s),int(W*1.2*s),int(H*1.2*s)],fill=0)
    vig=vig.filter(ImageFilter.GaussianBlur(200*s//10))
    dark=Image.new("RGBA",(W*s,H*s),(0,0,0,40))
    edge=Image.new("L",(W*s,H*s),40)
    de=ImageDraw.Draw(edge); de.ellipse([int(W*0.02*s),int(H*0.02*s),int(W*0.98*s),int(H*0.98*s)],fill=0)
    edge=edge.filter(ImageFilter.GaussianBlur(60*s))
    img.putalpha(255)
    dd=Image.new("RGBA",(W*s,H*s),(10,10,40,0)); dd.putalpha(edge)
    img=Image.alpha_composite(img,dd)
    save(finish(img.convert("RGBA"),W,H),"sunburst-bg.png")

# ---------- панель с обводкой (для 9-slice) ----------
def panel(w,h,r,fill,border,bw,name,inner=None,shadow=True):
    img,s=ss_canvas(w,h)
    d=ImageDraw.Draw(img)
    box=[bw//2*s, bw//2*s, (w-bw//2)*s-1, (h-bw//2)*s-1]
    rrect(d,box,r*s,fill=fill+(255,),outline=border+(255,),width=bw*s)
    if inner:
        ib=[ (bw+ inner[1])*s, (bw+inner[1])*s, (w-bw-inner[1])*s-1, (h-bw-inner[1])*s-1]
        rrect(d,ib,(r-bw)*s,fill=None,outline=inner[0]+(255,),width=max(1,inner[2]*s))
    out=finish(img,w,h)
    if shadow: out=drop_shadow(out)
    save(out,name)

# ---------- 3. плашки, 5. бейдж, 6. пилюля ----------
def plates():
    panel(380,190,42,DA,INK,10,"plate-yes.png",inner=((255,255,255),8,3))
    panel(380,190,42,NO,INK,10,"plate-no.png",inner=((255,255,255),8,3))
def badge():
    panel(220,248,46,COBALT,WHITE,14,"age-badge.png")
def money():
    panel(360,132,40,WHITE,INK,9,"money-pill.png")

# ---------- 2. маркиза ----------
def marquee():
    panel(600,360,54,COBALT,WHITE,26,"marquee-frame.png",inner=(COBALT_DEEP,6,4))
    # вариант с лампочками (фикс. размер)
    w,h=640,380; img,s=ss_canvas(w,h); d=ImageDraw.Draw(img)
    rrect(d,[13*s,13*s,(w-13)*s,(h-13)*s],54*s,fill=COBALT+(255,),outline=WHITE+(255,),width=26*s)
    # лампочки по периметру
    import itertools
    per=[]
    n_h=11; n_v=6
    x0,y0,x1,y1=40,40,w-40,h-40
    for i in range(n_h):
        x=x0+(x1-x0)*i/(n_h-1); per.append((x,y0)); per.append((x,y1))
    for j in range(1,n_v-1):
        y=y0+(y1-y0)*j/(n_v-1); per.append((x0,y)); per.append((x1,y))
    for (x,y) in per:
        d.ellipse([(x-11)*s,(y-11)*s,(x+11)*s,(y+11)*s],fill=BULB+(255,),outline=(180,120,20,255),width=2*s)
    out=drop_shadow(finish(img,w,h))
    save(out,"marquee-frame-bulbs.png")
    # одиночная лампочка
    bw2,bh2=44,44; img2,s2=ss_canvas(bw2,bh2); d2=ImageDraw.Draw(img2)
    d2.ellipse([4*s2,4*s2,(bw2-4)*s2,(bh2-4)*s2],fill=BULB+(255,),outline=(180,120,20,255),width=3*s2)
    d2.ellipse([13*s2,11*s2,22*s2,20*s2],fill=(255,245,210,230))
    save(finish(img2,bw2,bh2),"marquee-bulb.png")

# ---------- 7. бары ----------
def grad_fill(w,h,r,top,bot,name):
    img,s=ss_canvas(w,h); d=ImageDraw.Draw(img)
    grad=Image.new("RGBA",(w*s,h*s),(0,0,0,0)); gd=ImageDraw.Draw(grad)
    for y in range(h*s):
        t=y/(h*s-1)
        c=tuple(int(top[i]+(bot[i]-top[i])*t) for i in range(3))
        gd.line([(0,y),(w*s,y)],fill=c+(255,))
    mask=Image.new("L",(w*s,h*s),0); md=ImageDraw.Draw(mask)
    md.rounded_rectangle([2*s,2*s,(w-2)*s,(h-2)*s],radius=(r-2)*s,fill=255)
    img.paste(grad,(0,0),mask)
    save(finish(img,w,h),name)
def bars():
    panel(360,56,28,TRACK,(0,0,0),0,"bar-track.png",shadow=False)  # trackбез явной обводки
    # трек с лёгкой внутренней тенью
    w,h=360,56; img,s=ss_canvas(w,h); d=ImageDraw.Draw(img)
    d.rounded_rectangle([2*s,2*s,(w-2)*s,(h-2)*s],radius=27*s,fill=TRACK+(255,),outline=(0,0,0,40),width=2*s)
    save(finish(img,w,h),"bar-track.png")
    grad_fill(360,56,28,HEALTH_T,HEALTH_B,"bar-health-fill.png")
    grad_fill(360,56,28,ENERGY_T,ENERGY_B,"bar-energy-fill.png")

# ---------- balancer ----------
def balancer():
    w,h=660,72; img,s=ss_canvas(w,h); d=ImageDraw.Draw(img)
    mask=Image.new("L",(w*s,h*s),0); md=ImageDraw.Draw(mask)
    md.rounded_rectangle([2*s,2*s,(w-2)*s,(h-2)*s],radius=34*s,fill=255)
    zones=[(0.0,0.18,REL_R),(0.18,0.34,REL_Y),(0.34,0.66,REL_G),(0.66,0.82,REL_Y),(0.82,1.0,REL_R)]
    band=Image.new("RGBA",(w*s,h*s),(0,0,0,0)); bd=ImageDraw.Draw(band)
    for a,b,c in zones:
        bd.rectangle([int(a*w*s),0,int(b*w*s),h*s],fill=c+(255,))
    img.paste(band,(0,0),mask)
    d.rounded_rectangle([2*s,2*s,(w-2)*s,(h-2)*s],radius=34*s,outline=(0,0,0,55),width=3*s)
    save(finish(img,w,h),"balancer-track.png")
    # маркер
    mw,mh=44,84; im,sm=ss_canvas(mw,mh); dm=ImageDraw.Draw(im)
    dm.rounded_rectangle([6*sm,4*sm,(mw-6)*sm,(mh-4)*sm],radius=12*sm,fill=WHITE+(255,),outline=INK+(255,),width=5*sm)
    save(drop_shadow(finish(im,mw,mh),offset=(0,6),blur=8),"balancer-marker.png")

# ---------- timer ring ----------
def ring(name,color,d_out=220,thick=34):
    img,s=ss_canvas(d_out,d_out); dr=ImageDraw.Draw(img)
    dr.ellipse([4*s,4*s,(d_out-4)*s,(d_out-4)*s],fill=None,outline=color+(255,),width=thick*s)
    save(finish(img,d_out,d_out),name)
def timer():
    ring("timer-ring.png",NO)          # заливка (Unity: Filled Radial360)
    ring("timer-ring-track.png",(255,255,255))

# ---------- spark / stars / icons / bubble ----------
def star_poly(d,cx,cy,rad,fill,outline=None,ow=0,points=5,inner=0.42):
    pts=[]
    for k in range(points*2):
        ang=math.pi/2 + k*math.pi/points
        rr=rad if k%2==0 else rad*inner
        pts.append((cx+rr*math.cos(ang), cy-rr*math.sin(ang)))
    d.polygon(pts, fill=fill, outline=outline, width=ow)
def decor():
    # искра (4 луча)
    w=96; img,s=ss_canvas(w,w); d=ImageDraw.Draw(img)
    star_poly(d,w//2*s,w//2*s,w//2*s-6*s,WHITE+(255,),INK+(255,),int(3*s),points=4,inner=0.32)
    save(finish(img,w,w),"spark.png")
    # звёзды декор
    for nm,fill,oc in [("star-white.png",WHITE,None),("star-outline.png",None,COBALT)]:
        w2=120; im,s2=ss_canvas(w2,w2); dd=ImageDraw.Draw(im)
        if fill: star_poly(dd,w2//2*s2,w2//2*s2,w2//2*s2-6*s2,fill+(255,))
        else: star_poly(dd,w2//2*s2,w2//2*s2,w2//2*s2-8*s2,None,oc+(255,),int(6*s2))
        save(finish(im,w2,w2),nm)
    # иконки
    def newic():
        w3=120; im,s3=ss_canvas(w3,w3); return im,ImageDraw.Draw(im),s3,w3
    im,d,s3,w3=newic()  # сердце
    cx=w3//2*s3; d.pieslice([26*s3,24*s3,60*s3,58*s3],180,360,fill=HEALTH_B+(255,))
    d.pieslice([60*s3,24*s3,94*s3,58*s3],180,360,fill=HEALTH_B+(255,))
    d.polygon([(28*s3,46*s3),(92*s3,46*s3),(60*s3,96*s3)],fill=HEALTH_B+(255,))
    save(finish(im,w3,w3),"icon-heart.png")
    im,d,s3,w3=newic()  # молния
    d.polygon([(66*s3,12*s3),(34*s3,66*s3),(56*s3,66*s3),(50*s3,108*s3),(88*s3,50*s3),(62*s3,50*s3)],
              fill=ENERGY_B+(255,),outline=INK+(255,),width=3*s3)
    save(finish(im,w3,w3),"icon-lightning.png")
    im,d,s3,w3=newic()  # монета
    d.ellipse([16*s3,16*s3,104*s3,104*s3],fill=(246,201,69,255),outline=(180,130,20,255),width=5*s3)
    d.ellipse([30*s3,30*s3,90*s3,90*s3],fill=None,outline=(255,235,170,255),width=4*s3)
    save(finish(im,w3,w3),"icon-coin.png")
    # облачко ведущего (тело 9-slice + хвост)
    w4,h4=360,200; im,s4=ss_canvas(w4,h4); d=ImageDraw.Draw(im)
    d.rounded_rectangle([10*s4,10*s4,(w4-10)*s4,(h4-46)*s4],radius=40*s4,fill=BULB+(255,),outline=INK+(255,),width=8*s4)
    d.polygon([(70*s4,(h4-52)*s4),(150*s4,(h4-52)*s4),(70*s4,(h4-6)*s4)],fill=BULB+(255,),outline=INK+(255,),width=8*s4)
    d.rectangle([78*s4,(h4-58)*s4,142*s4,(h4-50)*s4],fill=BULB+(255,))
    save(drop_shadow(finish(im,w4,h4),offset=(0,8),blur=10),"bubble.png")

print("Генерирую спрайты →", OUT)
sunburst(); plates(); badge(); money(); marquee(); bars(); balancer(); timer(); decor()
print("Готово.")
