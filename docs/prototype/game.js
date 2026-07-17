"use strict";
/* «Спасибо, не надо» — прототип.
   Архитектура: слой ВВОДА (InputSource) отделён от логики. Игра слушает СМЫСЛОВЫЕ
   события (ANSWER_YES/NO, MONEY_TICK, RELATION_AXIS, ENERGY_PULSE, CHILD_PRESS, CONFIRM),
   а не клавиши. Сейчас — KeyboardInput; для автомата — SerialInput (COM-порт), логика не меняется. */

/* ============================ СЛОЙ ВВОДА ============================ */
class InputSource {
  constructor(){ this.handlers=[]; this.held={money:false,relUp:false,relDown:false}; this.energyPulse=0; }
  onEvent(fn){ this.handlers.push(fn); }
  emit(e){ for(const h of this.handlers) h(e); }
  start(){} stop(){}
}

class KeyboardInput extends InputSource {
  start(){
    const norm=(ev)=>{ // устойчиво к code/key (в т.ч. синтетические события)
      const c=ev.code, k=ev.key;
      if(c==="ArrowLeft"||k==="ArrowLeft") return "left";
      if(c==="ArrowRight"||k==="ArrowRight") return "right";
      if(c==="ArrowUp"||k==="ArrowUp") return "up";
      if(c==="ArrowDown"||k==="ArrowDown") return "down";
      if(c==="KeyE"||k==="e"||k==="E"||k==="е"||k==="Е") return "energy";
      if(c==="Enter"||k==="Enter") return "enter";
      if(c==="Space"||k===" "||k==="Spacebar") return "space";
      return "";
    };
    this._down = (ev)=>{
      const n=norm(ev);
      switch(n){
        case "left":  this.emit({type:"ANSWER_YES"}); break;
        case "right": this.emit({type:"ANSWER_NO"});  break;
        case "up":    this.held.relUp=true;  break;
        case "down":  this.held.relDown=true; break;
        case "energy":this.energyPulse=performance.now(); this.emit({type:"ENERGY_PULSE"}); break;
        case "enter": this.emit({type:"CONFIRM"}); this.emit({type:"CHILD_PRESS"}); break;
        case "space": this.held.money=true; this.emit({type:"CONFIRM_SPACE"}); break;
        default: return;
      }
      if(["left","right","up","down","space"].includes(n)) ev.preventDefault();
    };
    this._up = (ev)=>{
      const n=norm(ev);
      if(n==="up") this.held.relUp=false;
      if(n==="down") this.held.relDown=false;
      if(n==="space") this.held.money=false;
    };
    window.addEventListener("keydown", this._down);
    window.addEventListener("keyup", this._up);
  }
  stop(){ window.removeEventListener("keydown",this._down); window.removeEventListener("keyup",this._up); }
}

/* Заготовка для железа: тот же интерфейс, читает строки из COM-порта через Web Serial.
   Включается заменой одной строки: input = new SerialInput().  (не активна в прототипе) */
class SerialInput extends InputSource {
  async start(){
    const port = await navigator.serial.requestPort();
    await port.open({ baudRate: 9600 });
    const reader = port.readable.pipeThrough(new TextDecoderStream()).getReader();
    let buf="";
    const map = { LEVER_L:"ANSWER_YES", LEVER_R:"ANSWER_NO", MONEY_TICK:"MONEY_TICK",
                  ENERGY:"ENERGY_PULSE", CHILD:"CHILD_PRESS", CONFIRM:"CONFIRM" };
    while(true){
      const {value,done}=await reader.read(); if(done) break;
      buf+=value; let i;
      while((i=buf.indexOf("\n"))>=0){
        const msg=buf.slice(0,i).trim(); buf=buf.slice(i+1);
        if(msg.startsWith("REL:")){ this.held.relUp=false; this.held.relDown=false;
          const v=parseFloat(msg.slice(4)); if(v>0)this.held.relUp=true; if(v<0)this.held.relDown=true; }
        else if(msg==="MONEY_TICK"){ this.held.money=true; setTimeout(()=>this.held.money=false,120); }
        else if(msg==="ENERGY"){ this.energyPulse=performance.now(); this.emit({type:"ENERGY_PULSE"}); }
        else if(map[msg]) this.emit({type:map[msg]});
      }
    }
  }
}

/* ============================ УТИЛИТЫ CSV ============================ */
function parseCSV(text){
  const rows=[]; let row=[],f="",q=false; text=text.replace(/\r/g,"");
  for(let i=0;i<text.length;i++){ const c=text[i];
    if(q){ if(c==='"'){ if(text[i+1]==='"'){f+='"';i++;} else q=false; } else f+=c; }
    else if(c==='"') q=true;
    else if(c===',') { row.push(f); f=""; }
    else if(c==='\n'){ row.push(f); if(row.length>1||row[0]!=="") rows.push(row); row=[]; f=""; }
    else f+=c;
  }
  if(f!==""||row.length){ row.push(f); if(row.length>1||row[0]!=="") rows.push(row); }
  return rows;
}
const SCALE_KEY = { "Здр":"health", "Эн":"energy", "Дн":"money", "Отн":"relationship", "Реб":"child" };
const DELTA_UNIT = { health:9, energy:9, relationship:9, money:15, child:25 };
function parseDelta(s){ // -> [{scale, op:'add'|'set'|'rand', val}]
  const out=[]; if(!s||s==="—") return out;
  for(let part of s.split(",")){
    part=part.trim(); if(!part) continue;
    const m=part.match(/^(Здр|Эн|Дн|Отн|Реб)\s*(.*)$/); if(!m) continue;
    const scale=SCALE_KEY[m[1]], rest=m[2].replace(/\u2212/g,"-").trim();
    if(rest.startsWith("→")){ const v=parseFloat(rest.replace(/[^0-9.]/g,"")); out.push({scale,op:"set",val:v}); continue; }
    if(rest.startsWith("±")){ const n=parseFloat(rest.slice(1))||1; out.push({scale,op:"rand",val:n*DELTA_UNIT[scale]}); continue; }
    const n=parseFloat(rest); if(!isNaN(n)) out.push({scale,op:"add",val:n*DELTA_UNIT[scale]});
  }
  return out;
}
function parseAge(id,when){
  if(id==="MD02") return 31;
  const m=(when||"").match(/\d+/); if(m) return parseInt(m[0],10);
  if(/^CR/.test(id)) return 47;
  return 40;
}

/* ============================ ДАННЫЕ КАРТОЧЕК ============================ */
function buildCards(rows){
  const H=rows[0]; const cards=[];
  for(let r=1;r<rows.length;r++){ const c=rows[r]; if(!c[0]) continue;
    const flags=(c[12]||"").split(/,\s*/).map(x=>x.trim()).filter(Boolean);
    cards.push({
      id:c[0], text:c[1], when:c[2], type:c[3],
      daDelta:parseDelta(c[5]), netDelta:parseDelta(c[7]),
      hostYes:c[8], hostNo:c[9], necroYes:c[10], necroNo:c[11],
      flags, age:parseAge(c[0],c[2]),
      chain:(flags.find(f=>f.startsWith("CHAIN→"))||"").replace("CHAIN→",""),
      opens:flags.filter(f=>f.startsWith("OPEN:")).map(f=>SCALE_KEY[f.slice(5)]).filter(Boolean),
    });
  }
  return cards;
}
const CHAIN_TARGETS = new Set(["LT07","MD07","YA02","MD02","LT04"]);
const CAUSE = { CH02:"вы сунули палец в розетку", RND03:"вы попробовали странный белый порошок",
  RND06:"вы делали селфи на краю крыши", RND01:"за вами пришли" };

/* карточка считается «случайным событием жизни» (вбрасывается в середине, не в детстве).
   Кризисные карточки (CR*, BLITZ, INVERT) исключаем — они живут внутри кризис-раунда. */
function isRandomEvent(c){
  if(/^CR/.test(c.id) || c.flags.includes("BLITZ") || c.flags.includes("INVERT")) return false;
  return c.flags.includes("RANDOM") || /^RND/.test(c.id) || /^KEK/.test(c.id);
}

/* Обучающие подсказки (GDD §22) */
const TUTORIALS = {
  money:{t:"ПОРА ЗАРАБАТЫВАТЬ!", p:"Поздравляем! Теперь у вас есть работа. Крутите ручку — жмите ПРОБЕЛ — и будут деньги. Не крутите — денег не будет. Всё просто!"},
  relationship:{t:"ПЕРВАЯ ЛЮБОВЬ!", p:"Держите отношения в зелёной зоне: стрелки ↑ / ↓. Забьёте — станет холодно; переборщите — задушите. Баланс!"},
  energy:{t:"ПЕРВАЯ УСТАЛОСТЬ!", p:"Дышите в ритм — жмите E — так восстанавливается энергия. Забудете — выгорите!"},
  health:{t:"А ЭТО ЧТО?", p:"Впервые пора обратить внимание на здоровье: с этого момента оно будет только СНИЖАТЬСЯ! Успехов!"},
  child:{t:"РЕБЁНОК!", p:"Когда загорится кнопка ребёнка — быстро жмите ENTER. Пропустите пару раз — и вы плохой родитель!"},
};

/* ============================ СОСТОЯНИЕ ============================ */
const S = {
  screen:"opener", cards:[], deck:[], randomPool:[], usedRandom:new Set(),
  age:0, ageTarget:0, paused:false,
  money:0, moneyMult:1, earned:0, burnout:false,
  health:100, energy:100, rel:55,
  opened:{money:false,health:false,energy:false,relationship:false,child:false},
  relLostTime:0, relLost:false,
  child:{active:false, timer:6, missed:0, val:100},
  flags:{ya01yes:false, married:false, hasChild:false, relLost:false, ch07yes:false, ch08yes:false},
  unlocked:new Set(),
  necro:[], dead:false, cause:null, pendingFatal:null,
  cardResolve:null, last:0, depress:false, lateShown:false,
};
const $ = id=>document.getElementById(id);
let input;

/* ============================ РЕНДЕР HUD ============================ */
function fmtMoney(v){ return Math.round(v).toLocaleString("ru-RU")+" ₽"; }
const METER = {money:"m-money",health:"m-health",energy:"m-energy",relationship:"m-rel",child:"m-child"};
function renderHUD(){
  $("age").textContent = Math.floor(S.age);
  $("money").textContent = fmtMoney(S.money);
  $("health").style.width = Math.max(0,Math.min(100,S.health))+"%";
  $("energy").style.width = Math.max(0,Math.min(100,S.energy))+"%";
  $("rel-needle").style.left = Math.max(0,Math.min(100,S.rel))+"%";
  $("child").style.width = Math.max(0,Math.min(100,S.child.val))+"%";
  $("child-ic").textContent = S.child.active ? "🔔" : "🍼";

  // видимость шкал (постепенное появление)
  for(const k in METER) $(METER[k]).classList.toggle("on", !!S.opened[k]);

  // активное управление — дофаминовый отклик
  const mMoney=$("m-money"), mRel=$("m-rel"), mEn=$("m-energy");
  mMoney.classList.toggle("active", S.opened.money && input.held.money);
  mRel.classList.toggle("active", S.opened.relationship && !S.relLost && (input.held.relUp||input.held.relDown));
  if(input.energyPulse && performance.now()-input.energyPulse<250) mEn.classList.add("pulse");
  else mEn.classList.remove("pulse");

  // индикаторы плохой зоны
  $("m-health").classList.toggle("danger", S.opened.health && S.health<15);
  $("m-energy").classList.toggle("danger", S.opened.energy && (S.energy<15||S.burnout));
  $("m-rel").classList.toggle("danger", S.opened.relationship && !S.relLost && (S.rel<40||S.rel>88));
  $("m-child").classList.toggle("danger", S.opened.child && S.child.missed>=1 && !S.child.active);

  // кнопка ребёнка — крупно и ярко, когда горит
  $("m-child").classList.toggle("flash", S.child.active);
}

/* ============================ ЦИКЛ ШКАЛ (rAF) ============================ */
function loop(now){
  const dt=Math.min(.05,(now-S.last)/1000||0); S.last=now;
  if(S.screen==="game" && !S.dead && !S.paused){
    const old = S.age>70 ? 1.6 : (S.age>55?1.25:1); // старость ускоряет распад
    // возраст плавно догоняет цель
    if(S.age < S.ageTarget) S.age = Math.min(S.ageTarget, S.age + dt*6);
    // деньги: доход при зажатом «кране», постоянный дренаж
    if(S.opened.money){
      if(input.held.money){ const inc=dt*12*S.moneyMult*(S.burnout?0.5:1); S.money+=inc; S.earned+=inc; }
      S.money -= dt*0.5; if(S.money<0) S.money=0;
    }
    // энергия
    if(S.opened.energy){ S.energy -= dt*0.6*old; if(S.energy<0) S.energy=0;
      S.burnout = S.energy<10; if(S.energy<=0) die("полное выгорание"); }
    // здоровье
    if(S.opened.health){ S.health -= dt*0.8*old; if(S.health<=0){S.health=0; die("здоровье не выдержало");} }
    // отношения
    if(S.opened.relationship && !S.relLost){
      let v=S.rel - dt*0.6;
      if(input.held.relUp) v += dt*22;
      if(input.held.relDown) v -= dt*22;
      S.rel=Math.max(0,Math.min(100,v));
      if(S.rel<40){ S.relLostTime+=dt; if(S.relLostTime>10){ S.relLost=true; S.flags.relLost=true; S.opened.relationship=false; } }
      else S.relLostTime=0;
    }
    // ребёнок
    if(S.opened.child){
      S.child.timer-=dt;
      if(!S.child.active && S.child.timer<=0){ S.child.active=true; S.child.timer=2.0; }
      else if(S.child.active && S.child.timer<=0){ // прозевал вспышку
        S.child.active=false; S.child.timer=8; S.child.missed++; S.child.val=Math.max(0,S.child.val-25);
        if(S.child.missed>=2){ S.rel=Math.max(0,S.rel-10); }
      }
    }
  }
  // виньетка старости
  const vig=$("vignette");
  if(vig){ const o=Math.max(0,Math.min(.72,(S.age-55)/45*0.72));
    vig.style.opacity = (S.screen==="game")? o.toFixed(3):0;
    vig.classList.toggle("beat", S.opened.health && S.health<25 && S.screen==="game");
  }
  renderHUD();
  requestAnimationFrame(loop);
}
function die(cause){ if(S.dead) return; S.dead=true; S.cause=S.cause||cause; if(S.cardResolve) S.cardResolve("dead"); }

/* ============================ ВВОД → ЛОГИКА ============================ */
function dispatch(e){
  if(S.screen==="opener"){ if(e.type==="CONFIRM"||e.type==="CONFIRM_SPACE") startGame(); return; }
  if(S.screen==="ending"){ if(e.type==="CONFIRM"||e.type==="CONFIRM_SPACE") location.reload(); return; }
  if(S.screen==="hint"){ if(e.type==="CONFIRM"||e.type==="CONFIRM_SPACE") closeHint(); return; }
  if(S.screen==="game"){
    if(S.opened.energy && e.type==="ENERGY_PULSE") S.energy=Math.min(100,S.energy+18);
    if(S.child.active && e.type==="CHILD_PRESS"){ S.child.active=false; S.child.timer=8; S.child.missed=0; S.child.val=Math.min(100,S.child.val+8); }
    if(S.cardResolve){
      if(e.type==="ANSWER_YES") S.cardResolve("yes");
      else if(e.type==="ANSWER_NO") S.cardResolve("no");
    }
  }
}

/* ============================ ЭКРАНЫ / ПОДСКАЗКИ ============================ */
function show(id){ ["opener","game","ending"].forEach(x=>$(x).classList.toggle("hidden",x!==id)); }
let hintResolve=null;
function openHint(key){
  const t=TUTORIALS[key]; if(!t) return Promise.resolve();
  S.paused=true; S.screen="hint"; $("hint-title").textContent=t.t; $("hint-text").textContent=t.p; $("hint").classList.remove("hidden");
  return new Promise(res=>{ hintResolve=res; });
}
function closeHint(){ $("hint").classList.add("hidden"); S.screen="game"; S.paused=false; if(hintResolve){const r=hintResolve;hintResolve=null;r();} }

function banner(text,ms=1400){
  return new Promise(res=>{ S.paused=true; const b=$("banner"); b.textContent=text; b.classList.remove("hidden");
    setTimeout(()=>{ b.classList.add("hidden"); S.paused=false; res(); }, ms); });
}
function bubble(text){ if(!text||text==="—") return; const b=$("bubble"); b.textContent=text; b.classList.remove("hidden");
  clearTimeout(bubble._t); bubble._t=setTimeout(()=>b.classList.add("hidden"),1500); }

/* звёздочки-искры при выборе */
function spawnStars(el){
  const r=el.getBoundingClientRect(), cx=r.left+r.width/2, cy=r.top+r.height/2;
  for(let i=0;i<9;i++){ const s=document.createElement("div"); s.className="spark"; s.textContent="★";
    const a=Math.random()*Math.PI*2, d=60+Math.random()*70;
    s.style.left=cx+"px"; s.style.top=cy+"px";
    s.style.setProperty("--dx",(Math.cos(a)*d).toFixed(0)+"px");
    s.style.setProperty("--dy",(Math.sin(a)*d).toFixed(0)+"px");
    s.style.color=i%2?"var(--yellow)":"#fff";
    $("stage").appendChild(s); setTimeout(()=>s.remove(),600);
  }
}

/* появление шкалы: крупно, вибрирует, встаёт на место */
function appearScale(scale){ const el=$(METER[scale]); if(!el) return;
  el.classList.add("on"); el.classList.remove("appear"); void el.offsetWidth;
  el.classList.add("appear"); setTimeout(()=>el.classList.remove("appear"),1050); }

/* старческое зрение: размытие + буквы иногда меняются местами */
function scrambleText(s){
  const a=s.split("");
  for(let i=1;i<a.length-1;i+=2+Math.floor(Math.random()*3)){
    const j=i+1; if(j>=a.length||a[i]===" "||a[j]===" ") continue;
    [a[i],a[j]]=[a[j],a[i]];
  }
  return a.join("");
}
function visionBlur(){ return S.age<58?0:Math.min(3.2,(S.age-58)/11); }
function startVision(cardEl, text){
  cardEl.dataset.base=text;
  let stop=false;
  const tick=()=>{
    if(stop) return;
    const blur=visionBlur();
    cardEl.style.filter=blur>0?`blur(${blur.toFixed(2)}px)`:"";
    cardEl.classList.toggle("fuzzy", blur>1.2);
    if(blur>0 && !S.paused && Math.random()<(0.08+(S.age-58)/120)){
      cardEl.textContent=scrambleText(text);
      setTimeout(()=>{ if(!stop) cardEl.textContent=text; }, 70+Math.random()*130);
    }
    cardEl._visionT=setTimeout(tick, 650+Math.random()*500);
  };
  tick();
  return ()=>{ stop=true; clearTimeout(cardEl._visionT); cardEl.style.filter=""; cardEl.classList.remove("fuzzy"); };
}

/* ============================ ПОКАЗ КАРТОЧКИ ============================ */
function presentCard(card, seconds=5, invert=false){
  return new Promise(resolve=>{
    S.screen="game";
    const cardEl=$("card");
    cardEl.textContent=card.text;
    const stopVision=startVision(cardEl, card.text);
    cardEl.classList.remove("exit"); cardEl.classList.remove("enter"); void cardEl.offsetWidth;
    cardEl.classList.add("enter"); setTimeout(()=>cardEl.classList.remove("enter"),330);
    $("answers").className=""; // обычные кнопки
    $("ans-yes").className="ans yes"; $("ans-yes").innerHTML='ДА<span class="hint">← влево</span>';
    $("ans-no").className="ans no";  $("ans-no").innerHTML='СПАСИБО, НЕ НАДО<span class="hint">вправо →</span>';
    const bar=$("timer").firstElementChild; bar.style.transition="none"; bar.style.width="100%";
    void bar.offsetWidth; bar.style.transition=`width ${seconds}s linear`; bar.style.width="0%";
    let done=false;
    const finish=(ans, byUser)=>{ if(done) return; done=true; clearTimeout(to); S.cardResolve=null; stopVision();
      if(ans==="dead"){ resolve(ans); return; }
      if(byUser){
        const el = ans==="yes"?$("ans-yes"):$("ans-no");
        el.classList.add("picked"); spawnStars(el);
        bubble(ans==="yes"?card.hostYes:card.hostNo); // реплика Ведущего — сразу
      }
      cardEl.classList.remove("enter"); cardEl.classList.add("exit"); // карточка уходит вниз
      setTimeout(()=>resolve(ans), 300);
    };
    const to=setTimeout(()=>finish(invert?"yes":(Math.random()<0.5?"yes":"no"), false), seconds*1000);
    S.cardResolve=(a)=> finish(a, a==="yes"||a==="no");
  });
}

/* ============================ ПРИМЕНЕНИЕ ВЫБОРА ============================ */
function applyDelta(list){
  for(const d of list){
    let v=d.val; if(d.op==="rand") v=(Math.random()<0.5?1:-1)*Math.abs(v);
    if(d.scale==="money"){ if(d.op==="set") S.money=v; else S.money=Math.max(0,S.money+v); }
    else if(d.scale==="health"){ S.health = d.op==="set"? v : Math.max(0,Math.min(100,S.health+v)); }
    else if(d.scale==="energy"){ S.energy = d.op==="set"? v : Math.max(0,Math.min(100,S.energy+v)); }
    else if(d.scale==="relationship"){ if(!S.relLost) S.rel=Math.max(0,Math.min(100,S.rel+v)); }
    else if(d.scale==="child"){ S.child.val=Math.max(0,Math.min(100,S.child.val+v)); }
  }
}
async function openScale(scale, viaYes){
  // отношения и ребёнок открываются ТОЛЬКО на ДА
  if((scale==="relationship"||scale==="child") && !viaYes) return;
  if(S.opened[scale]) return;
  if(scale==="money") S.money=Math.max(S.money,20);
  if(scale==="child"){ S.child.timer=6; S.child.missed=0; S.child.val=100; }
  await openHint(scale);
  S.opened[scale]=true;
  appearScale(scale);
}
async function resolveCard(card, ans){
  const yes = ans==="yes";
  applyDelta(yes?card.daDelta:card.netDelta);
  // некролог
  const nl = yes?card.necroYes:card.necroNo;
  if(nl && nl!=="—") S.necro.push(nl);
  // спец-логика
  if(card.id==="YA01" && yes){ S.flags.ya01yes=true; S.moneyMult=2; }
  if(card.id==="MD01" && yes){ S.flags.married=true; }
  if(card.id==="MD02" && yes){ S.flags.hasChild=true; }
  if(card.id==="CH07" && yes){ S.flags.ch07yes=true; }
  if(card.id==="CH08" && yes){ S.flags.ch08yes=true; }
  if(card.chain && yes) S.unlocked.add(card.chain);
  // открытие шкал (+ подсказка) — на ДА для отношений/ребёнка, иначе всегда
  for(const sc of card.opens) await openScale(sc, yes);
  // фатальные
  if(yes && card.flags.includes("FATAL")){
    if(card.flags.some(f=>f.startsWith("DELAY"))){ S.pendingFatal={id:card.id, left:3}; } // криминал позже
    else { S.cause=CAUSE[card.id]||"явная дичь"; S.dead=true; }
  }
}

/* ============================ УСЛОВИЯ ПОКАЗА ============================ */
function canPlay(card){
  if(CHAIN_TARGETS.has(card.id) && !S.unlocked.has(card.id)) return false;
  switch(card.id){
    case "YA02": return S.flags.ya01yes;
    case "MD03": case "MD04": case "LT02": return S.opened.money;
    case "MD06": return S.relLost;
    case "MD07": return S.flags.ch08yes;
    case "LT04": return S.flags.hasChild;
    case "LT07": return S.flags.ch07yes;
    case "RND05": return S.opened.relationship;
    case "LT08": return S.health<40 && S.age>=30;
  }
  return true;
}

/* автозапуск шкалы здоровья по возрасту (без карточки) */
async function autoOpenHealth(){
  if(!S.opened.health && S.age>=30){
    await openHint("health"); S.opened.health=true; appearScale("health");
  }
}

/* вброс случайного события в середине жизни */
async function maybeRandomEvent(){
  if(S.dead) return;
  if(S.age<18 || S.age>72) return;         // не в детстве и не в глубокой старости
  if(S.usedRandom.size>=6) return;         // не заваливать
  if(Math.random()>0.33) return;
  const pool=S.randomPool.filter(c=>!S.usedRandom.has(c.id) && canPlay(c));
  if(!pool.length) return;
  const card=pool[Math.floor(Math.random()*pool.length)];
  S.usedRandom.add(card.id);
  const sec = S.age>70?4 : S.age>55?4.5 : 5;
  const ans=await presentCard(card,sec,false);
  if(ans==="dead") return;
  await resolveCard(card,ans);
  if(S.pendingFatal){ if(--S.pendingFatal.left<=0){ S.cause=CAUSE[S.pendingFatal.id]; S.dead=true; } }
  await sleep(120);
}

/* ============================ КРИЗИС-БЛИЦ ============================ */
function presentBlitz(card){
  return new Promise(resolve=>{
    S.screen="game"; const cardEl=$("card"); cardEl.textContent=card.text;
    const stopVision=startVision(cardEl, card.text);
    cardEl.classList.remove("exit"); cardEl.classList.remove("enter"); void cardEl.offsetWidth; cardEl.classList.add("enter");
    const normLeft = Math.random()<0.5;
    $("answers").className="blitz";
    const yesEl=$("ans-yes"), noEl=$("ans-no");
    // «ВСЁ НОРМАЛЬНО» — правильный; ставим на случайную сторону
    if(normLeft){ yesEl.className="ans norm"; yesEl.textContent="ВСЁ НОРМАЛЬНО"; noEl.className="ans ohno"; noEl.textContent="О НЕТ"; }
    else { yesEl.className="ans ohno"; yesEl.textContent="О НЕТ"; noEl.className="ans norm"; noEl.textContent="ВСЁ НОРМАЛЬНО"; }
    const bar=$("timer").firstElementChild; bar.style.transition="none"; bar.style.width="100%";
    void bar.offsetWidth; bar.style.transition="width 2s linear"; bar.style.width="0%";
    let done=false;
    const finish=(sideYes)=>{ if(done)return; done=true; clearTimeout(to); S.cardResolve=null; stopVision();
      const pressedNorm = (sideYes===null)?false:(sideYes===normLeft);
      if(sideYes!==null){ const el=sideYes?yesEl:noEl; el.classList.add("picked"); spawnStars(el); }
      cardEl.classList.add("exit");
      setTimeout(()=>resolve(pressedNorm), 250);
    };
    const to=setTimeout(()=>finish(null),2000);
    S.cardResolve=(a)=> finish(a==="yes");
  });
}
async function runCrisis(){
  await banner("КРИЗИС СРЕДНЕГО ВОЗРАСТА! БЛИЦ!",1600);
  const thoughts=S.cards.filter(c=>c.flags.includes("BLITZ") && c.id!=="CR00");
  let fails=0;
  for(const th of thoughts){ const ok=await presentBlitz(th); if(!ok) fails++; if(S.dead) return; await sleep(120); }
  if(fails>=2){
    await banner("КРИЗИС БЕРЁТ ВЕРХ!",1300);
    const impulse=S.cards.filter(c=>c.flags.includes("INVERT"));
    for(const im of impulse){
      const ans=await presentCard(im,3,true); // INVERT: молчание = ДА
      if(S.dead) break; await resolveCard(im,ans); await sleep(120);
    }
  }
  if(Math.random()<0.6){ // Депрессия
    const dep=S.cards.find(c=>c.id==="CR09");
    $("stage").classList.add("depress"); await banner("ДЕПРЕССИЯ!",1600);
    if(dep){ applyDelta(dep.daDelta); if(dep.necroYes&&dep.necroYes!=="—") S.necro.push(dep.necroYes); }
    setTimeout(()=>$("stage").classList.remove("depress"),4000);
  }
}

/* ============================ ГЛАВНЫЙ ПРОГОН ============================ */
async function runLife(){
  // случайные события — в отдельный пул, из основной ленты убираем
  S.randomPool = S.cards.filter(isRandomEvent);
  const randIds = new Set(S.randomPool.map(c=>c.id));
  S.deck = S.cards
    .filter(c=>{
      if(c.id==="CR00") return true; // старт кризиса — в ленте, мысли блица — отдельно
      if(c.flags.includes("BLITZ") || c.flags.includes("INVERT")) return false;
      if(c.id==="CR09") return false;
      return !randIds.has(c.id);
    })
    .sort((a,b)=> a.age-b.age);
  for(const card of S.deck){
    if(S.dead) break;
    if(!canPlay(card)) continue;
    S.ageTarget = Math.max(S.ageTarget, card.age);
    await autoOpenHealth();
    // рубрика-баннер для одноразовых крупных событий
    if(card.id==="YA01") await banner("ПОРА ЗАРАБАТЫВАТЬ!");
    if(card.id==="YA03") await banner("ПЕРВАЯ ЛЮБОВЬ!");
    if(card.id==="MD01") await banner("СВАДЬБА!");
    if(!S.lateShown && S.age>=63){ S.lateShown=true; await banner("ОСЕНЬ ЖИЗНИ…",1500); }
    if(card.id==="CR00"){ await runCrisis(); continue; }
    const sec = S.age>70?4 : S.age>55?4.5 : 5;
    const ans = await presentCard(card,sec,false);
    if(ans==="dead") break;
    await resolveCard(card,ans);
    if(S.pendingFatal){ if(--S.pendingFatal.left<=0){ S.cause=CAUSE[S.pendingFatal.id]; S.dead=true; break; } }
    if(S.dead) break;
    await sleep(120);
    await maybeRandomEvent(); // случайное событие посреди жизни
  }
  // естественный финал
  if(!S.dead){ S.ageTarget=100; while(S.age<99.5 && !S.dead){ await sleep(60); } if(!S.cause) S.cause=naturalCause(); }
  endGame();
}
function naturalCause(){
  const social = S.flags.married || S.flags.hasChild || (S.opened.relationship && !S.relLost);
  if(social) return "весёлая старость";
  if(!S.flags.married && !S.flags.hasChild && S.relLost) return "одинокая старость";
  if(!S.opened.relationship && !S.flags.married) return "одинокая старость";
  return "спокойная старость";
}
function sleep(ms){ return new Promise(r=>setTimeout(r,ms)); }

/* ============================ ФИНАЛ ============================ */
function moneyVerdict(earned){
  if(earned<80000) return "пару отпусков";
  if(earned<300000) return "квартиру мечты";
  if(earned<800000) return "дом у моря";
  return "собственный маленький остров";
}
function endGame(){
  S.screen="ending"; $("hud").classList.add("hidden"); const vig=$("vignette"); if(vig) vig.style.opacity=0;
  show("ending");
  $("cause").textContent = "Причина конца игры: "+(S.cause||"старость")+".";
  const story = "Но не переживайте! Ведь вы родились у прекрасных родителей. " + S.necro.join(" ");
  $("story").textContent = story;

  // сводка по деньгам (в рублях): «ушли с вами»
  const earned=Math.round(S.earned), left=Math.round(S.money);
  let sum;
  if(earned<1000){
    sum = "Денег вы почти не накопили — жили как жили. Может, оно и к лучшему: в гроб их всё равно не унести.";
  } else {
    sum = `За жизнь вы заработали <b>${fmtMoney(earned)}</b>. Этого хватило бы на ${moneyVerdict(earned)}. `;
    if(left>1000) sum += `Но <b>${fmtMoney(left)}</b> так и остались лежать нетронутыми — вы забрали их с собой. Зачем они вам там?`;
    else sum += "И вы почти всё потратили — по крайней мере, деньги пожили вместе с вами.";
  }
  $("summary").innerHTML = sum;
}

/* ============================ СТАРТ ============================ */
function startGame(){
  S.screen="game"; show("game"); $("hud").classList.remove("hidden");
  runLife();
}
function makeStars(){
  const box=$("stars"); const pts=[[8,70],[16,30],[26,55],[80,22],[88,60],[72,78],[40,12],[60,85]];
  pts.forEach((p,i)=>{ const s=document.createElement("div"); s.className="star"+(i%2?" b":""); s.textContent="★";
    s.style.left=p[0]+"%"; s.style.top=p[1]+"%"; s.style.fontSize=(22+Math.random()*22)+"px"; box.appendChild(s); });
}

async function init(){
  makeStars();
  input=new KeyboardInput(); input.onEvent(dispatch); input.start();
  requestAnimationFrame(t=>{S.last=t; loop(t);});
  try{
    const r=await fetch("../docs/scenes.csv",{cache:"no-store"});
    if(!r.ok) throw new Error("no csv");
    S.cards=buildCards(parseCSV(await r.text()));
    $("load-note").textContent = "Готово: "+S.cards.length+" карточек. Жми НАЧАТЬ ЖИЗНЬ (Enter).";
  }catch(e){
    $("load-note").textContent = "Не удалось прочитать scenes.csv — запусти через play.command (нужен локальный сервер).";
  }
}
init();
