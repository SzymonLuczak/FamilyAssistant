namespace FamilyAssistant.Core;

// One start page for all panels. Each tile reads the existing status endpoints of its module.
public static class Dashboard
{

    public static bool WantsHtml(HttpRequest request) => request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

    public static IResult Page(HttpContext context)
    {
        if (context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]")) return Results.StatusCode(403);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        return Results.Content(Html, "text/html; charset=utf-8");
    }

    private const string Html = """
    <!doctype html><html lang="pl"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <title>Family Assistant</title>
    <style>
    :root{--bg:#f2f6f4;--card:#fff;--text:#173e30;--muted:#52685d;--link:#176c52;--ok:#1b7a3d;--warn:#a15c00;--bad:#b3261e;--shadow:0 1px 3px #0001;color-scheme:light}
    @media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#0f1714;--card:#18231f;--text:#dce8e2;--muted:#9bb1a6;--link:#6fd3a8;--ok:#5fd38a;--warn:#f0b35a;--bad:#ff8a80;--shadow:0 1px 3px #0006;color-scheme:dark}}
    :root[data-theme="dark"]{--bg:#0f1714;--card:#18231f;--text:#dce8e2;--muted:#9bb1a6;--link:#6fd3a8;--ok:#5fd38a;--warn:#f0b35a;--bad:#ff8a80;--shadow:0 1px 3px #0006;color-scheme:dark}
    body{font:17px system-ui;background:var(--bg);color:var(--text);margin:0}main{max-width:1100px;margin:24px auto;padding:0 16px}
    .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:16px}
    section{background:var(--card);border-radius:16px;padding:20px;box-shadow:var(--shadow)}
    h1{margin:8px 0 18px;display:flex;justify-content:space-between;align-items:center;gap:12px}h2{margin:0 0 10px;font-size:20px}a{color:var(--link)}
    .ok{color:var(--ok)}.warn{color:var(--warn)}.bad{color:var(--bad)}small{color:var(--muted);display:block;margin-top:6px}
    ul{padding-left:20px;margin:6px 0}.status{font-weight:600}
    #theme{font:15px system-ui;padding:8px 14px;border-radius:20px;border:1px solid var(--muted);background:var(--card);color:var(--text);cursor:pointer}
    </style>
    <main>
    <h1>Family Assistant <button id="theme" type="button">🌙 Ciemny</button></h1>
    <div class="grid">
      <section><h2>📱 WhatsApp</h2><p id="wa" class="status">…</p><small id="waInfo"></small><a href="/whatsapp/pair">Parowanie</a></section>
      <section><h2>📅 Plan rodziny</h2><p id="sum" class="status">…</p><ul id="sumList"></ul><a href="/summary">Podgląd i historia</a></section>
      <section><h2>🛒 Do kupienia</h2><p id="shop" class="status">…</p><ul id="shopList"></ul><a href="/shopping">Lista zakupów</a></section>
      <section><h2>🏷 Okazje z gazetek</h2><p id="deals" class="status">…</p><ul id="dealList"></ul><a href="/shopping#deals">Wszystkie okazje</a></section>
      <section><h2>🧾 Paragony Biedronki</h2><p id="rec" class="status">…</p><small id="recInfo"></small><a href="/shopping/biedronka">Dodatek i konta</a></section>
      <section><h2>🏫 Szkoła</h2><p id="school" class="status">…</p><a href="/vulcan">eduVULCAN</a></section>
      <section><h2>🗓 Kalendarze</h2><p id="cal" class="status">…</p><small id="calInfo"></small><a href="/google">Kalendarze Google</a></section>
    </div>
    <p><small>Odświeża się co minutę. Ostatnio: <span id="stamp"></span></small></p>
    </main>
    <script>
    const $=id=>document.getElementById(id);
    // Theme: system setting by default; the button stores an explicit choice in this browser.
    function applyTheme(t){if(t)document.documentElement.dataset.theme=t;else delete document.documentElement.dataset.theme;
      const dark=t?t==='dark':matchMedia('(prefers-color-scheme: dark)').matches;$('theme').textContent=dark?'☀️ Jasny':'🌙 Ciemny';}
    let saved=null;try{saved=localStorage.getItem('fa-theme');}catch{}
    applyTheme(saved);
    $('theme').onclick=()=>{const dark=document.documentElement.dataset.theme?document.documentElement.dataset.theme==='dark':matchMedia('(prefers-color-scheme: dark)').matches;
      const next=dark?'light':'dark';try{localStorage.setItem('fa-theme',next);}catch{}applyTheme(next);};
    const get=path=>fetch(path,{cache:'no-store'}).then(r=>r.ok?r.json():Promise.reject(r.status));
    function set(id,text,cls){$(id).textContent=text;$(id).className='status '+(cls||'');}
    function list(id,items){$(id).replaceChildren(...items.map(t=>Object.assign(document.createElement('li'),{textContent:t})));}
    const when=s=>new Date(s*1000).toLocaleString('pl-PL',{day:'numeric',month:'short',hour:'2-digit',minute:'2-digit'});
    const pln=n=>n.toLocaleString('pl-PL',{style:'currency',currency:'PLN'});
    const names={ready:'połączony',qr:'czeka na kod QR',disconnected:'rozłączony',disabled:'wyłączony',unavailable:'niedostępny',authenticated:'łączy się',starting:'uruchamia się',auth_failure:'błąd logowania',error:'błąd'};
    async function load(){
      get('/health/integrations').then(h=>{
        set('wa','WhatsApp: '+(names[h.whatsapp]||h.whatsapp),h.whatsapp==='ready'?'ok':'bad');
        const v=typeof h.vulcan==='string'?h.vulcan:(h.vulcan?.status||JSON.stringify(h.vulcan));set('school','eduVULCAN: '+v,/ok|ready|connected|registered/i.test(v)?'ok':'warn');
        const g=typeof h.googleCalendar==='string'?h.googleCalendar:(h.googleCalendar?.status||JSON.stringify(h.googleCalendar));set('cal','Google: '+g,/connected|ok|authorized/i.test(g)?'ok':'warn');
      }).catch(()=>{set('wa','Brak odpowiedzi','bad');});
      get('/summary/status').then(s=>{set('sum',(s.mode==='scheduled_delivery'?'Wysyłka na WhatsApp włączona':'Tylko podgląd'),s.mode==='scheduled_delivery'?'ok':'warn');
        $('waInfo').textContent='Plan rano o '+s.morning+', na jutro o '+s.tomorrow+'.';}).catch(()=>set('sum','Wymaga konfiguracji','warn'));
      get('/summary/deliveries').then(d=>list('sumList',(d||[]).slice(0,3).map(x=>(x.kind||x.Kind||'wysyłka')+' · '+(x.status||x.Status||'')+(x.updatedAt?' · '+when(x.updatedAt):'')))).catch(()=>{});
      get('/shopping/data').then(d=>{const c=d.products.filter(p=>p.status==='Confirmed'),r=d.products.filter(p=>p.status==='Suggested'&&p.mayRunOut);
        set('shop',c.length?c.length+' na liście':'Lista jest pusta',c.length?'ok':'');
        list('shopList',[...c.slice(0,6).map(p=>p.name),...(r.length?['Może się kończyć: '+r.slice(0,4).map(p=>p.name).join(', ')]:[])]);
        const last=d.receipts[0];set('rec',d.receipts.length+' paragonów');$('recInfo').textContent=last?'Ostatni: '+when(last.purchasedAt)+' · '+pln(last.total/100)+' · '+(last.account||''):'';
      }).catch(()=>set('shop','Brak danych','warn'));
      get('/shopping/deals').then(d=>{if(!d.enabled){set('deals','Wyłączone (brak klucza API)','warn');return;}
        set('deals',d.running?'Czytam gazetki…':d.deals.length+' okazji dla Was',d.error?'bad':'ok');
        list('dealList',d.deals.slice(0,5).map(m=>m.productName+' — '+(m.offer.price!=null?pln(m.offer.price):'promocja')+(m.offer.validTo?' do '+m.offer.validTo.slice(5).split('-').reverse().join('.'):'')));
      }).catch(()=>set('deals','Brak danych','warn'));
      $('stamp').textContent=new Date().toLocaleTimeString('pl-PL');
    }
    load();setInterval(load,60000);
    </script></html>
    """;
}
