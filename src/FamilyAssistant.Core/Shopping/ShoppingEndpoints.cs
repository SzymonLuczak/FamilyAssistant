using Microsoft.AspNetCore.Antiforgery;
using System.Text.Encodings.Web;

namespace FamilyAssistant.Core.Shopping;

public static class ShoppingEndpoints
{
    public sealed record UpdateRequest(string Status, string? Name);
    public static void MapShopping(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/shopping")) { await next(); return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            if (context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]")) { context.Response.StatusCode = 403; return; }
            try { await next(); }
            catch (ShoppingFailure ex) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
            catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Odśwież stronę i spróbuj ponownie." }); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "Moduł Biedronki jeszcze się uruchamia albo jest niedostępny. Spróbuj ponownie za chwilę." }); }
        });
        app.MapGet("/shopping", (HttpContext context, IAntiforgery csrf) => Results.Content(Page.Replace("{{TOKEN}}", HtmlEncoder.Default.Encode(csrf.GetAndStoreTokens(context).RequestToken!)), "text/html; charset=utf-8"));
        app.MapGet("/shopping/data", (ShoppingStore store) => store.Read());
        app.MapGet("/shopping/biedronka", () => Results.Content(ConnectionPage, "text/html; charset=utf-8"));
        app.MapGet("/shopping/biedronka/status", (BrowserReceiptInbox inbox) => inbox.Status());
        app.MapPost("/shopping/biedronka/enable", async (HttpContext context, IAntiforgery csrf) =>
        {
            await csrf.ValidateRequestAsync(context);
            return Results.Json(new { error = "Włącz sprawdzanie w dodatku Chrome lub Edge." }, statusCode: 410);
        });
        app.MapPost("/shopping/import", async (HttpContext context, IAntiforgery csrf, ShoppingStore store) =>
        {
            await csrf.ValidateRequestAsync(context);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int count;
            while ((count = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) != 0)
            {
                if (buffer.Length + count > ReceiptParser.MaxBytes) throw new ShoppingFailure("Plik jest za duży (maksymalnie 8 MB).");
                buffer.Write(chunk, 0, count);
            }
            var receipt = ReceiptParser.Parse(buffer.ToArray());
            var added = await store.Import(receipt, context.Request.Query["account"].ToString());
            return Results.Ok(new { added, items = receipt.Lines.Length, total = receipt.Total });
        });
        app.MapPost("/shopping/whatsapp/proposal", async (HttpContext context, IAntiforgery csrf, ShoppingMessenger messenger) =>
        {
            await csrf.ValidateRequestAsync(context);
            if (!messenger.Enabled) throw new ShoppingFailure("Wysyłka zakupów na WhatsApp jest wyłączona (SHOPPING_WHATSAPP_ENABLED).");
            return Results.Ok(new { sent = await messenger.SendProposal(context.RequestAborted) });
        });
        app.MapPost("/shopping/whatsapp/list", async (HttpContext context, IAntiforgery csrf, ShoppingMessenger messenger) =>
        {
            await csrf.ValidateRequestAsync(context);
            if (!messenger.Enabled) throw new ShoppingFailure("Wysyłka zakupów na WhatsApp jest wyłączona (SHOPPING_WHATSAPP_ENABLED).");
            await messenger.SendList(context.RequestAborted);
            return Results.Ok(new { sent = true });
        });
        app.MapPost("/shopping/products/{id}", async (string id, UpdateRequest request, HttpContext context, IAntiforgery csrf, ShoppingStore store) =>
        {
            await csrf.ValidateRequestAsync(context);
            await store.Update(id, request.Status, request.Name);
            return Results.Ok(new { saved = true });
        });
    }
    private const string ConnectionPage = """
    <!doctype html><html lang="pl"><meta charset="utf-8"><title>Biedronka — dodatek</title>
    <style>body{font:18px system-ui;max-width:850px;margin:40px auto;padding:20px;line-height:1.6}code{overflow-wrap:anywhere}</style>
    <h1>Paragony z dwóch kont Biedronki</h1><p>Poprzednie okno noVNC zostało wyłączone.</p>
    <ol><li>Utwórz dwa profile zwykłego Chrome lub Edge, po jednym na konto.</li>
    <li>W każdym otwórz <code>chrome://extensions</code> lub <code>edge://extensions</code>. Włącz tryb dewelopera, wybierz „Załaduj rozpakowane” i wskaż <code>C:\development\FamilyAssistant\src\FamilyAssistant.BiedronkaExtension</code>.</li>
    <li>Zaloguj się na <a href="https://moja.biedronka.pl/panel/paragons">stronie Biedronki</a>. W dodatku wybierz Konto 1 lub 2, wpisz imię z powitania i zapisz ustawienia.</li>
    <li>Wybierz pobieranie wybranego zakresu. Po sprawdzeniu wyniku włącz sprawdzanie co 6 godzin. Oba profile muszą pozostać uruchomione.</li></ol>
    <p>Domyślnym katalogiem pobierania powinien być Downloads użytkownika. Pliki trafią do Downloads/FamilyAssistant/account-1 lub account-2. Core odczytuje JSON-y co 30 sekund i pozostawia oryginały.</p>
    <p>Automatyczne sprawdzanie obejmuje domyślny zakres Biedronki (14 dni). Starsze daty wybierz na stronie i uruchom pobieranie ręcznie. Przy 50 transakcjach zawęź zakres, aby niczego nie pominąć. PDF-y wymagają jeszcze OCR.</p>
    <p>Weryfikację i ponowne logowanie wykonujesz samodzielnie. Dodatek nie omija zabezpieczeń. Pierwsza synchronizacja wymaga sprawdzenia po instalacji.</p>
    <p><a href="/shopping/biedronka/status">Liczniki importu</a> nie potwierdzają kompletności historii ani zalogowania kont.</p><a href="/shopping">Lista zakupów</a></html>
    """;

    private const string Page = """
    <!doctype html><html lang="pl"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <title>Family Assistant — zakupy</title>
    <style>body{font:17px system-ui;background:#f2f6f4;color:#173e30;margin:0}main{max-width:950px;margin:24px auto;padding:28px;background:white;border-radius:18px}button,input{font:inherit;padding:9px;margin:5px}button{cursor:pointer}article{border-bottom:1px solid #dce6df;padding:15px 0}small{display:block;color:#52685d}a{color:#176c52}#message{white-space:pre-wrap}h2{margin-top:32px}</style>
    <main><a href="/summary">← Plan rodziny</a> · <a href="/shopping/biedronka">Połączenie kont Biedronki</a><h1>Wspólna lista zakupów</h1>
    <p>Historia z obu kart trafia do jednej listy. Wybierz produkty, które chcesz kupić. Ilości z paragonów opisują wcześniejsze zakupy, a nie obecne zapasy.</p>
    <details><summary>Dodaj paragony JSON z Biedronki</summary><p><label>Nazwa karty (opcjonalnie) <input id="account" maxlength="40" placeholder="np. karta Szymona"></label></p><input id="files" type="file" accept=".json,application/json" multiple><button id="import">Importuj</button><p>Przy imporcie z drugiej karty zmień nazwę. Ten sam paragon nie zostanie policzony ponownie.</p></details>
    <p><button id="propose">Wyślij propozycje na WhatsApp</button><button id="sendList">Wyślij listę na tablicę</button><br><small>Propozycje trafiają na grupę zakupową; odpowiedź numerami (np. 1 3 5) dodaje produkty i wysyła listę na grupę z planem rodziny.</small></p>
    <p id="message" role="status"></p><p id="totals"></p>
    <h2>Do kupienia</h2><div id="confirmed"></div>
    <h2>Produkty do rozważenia</h2><p>Przy mniej niż trzech dniach zakupów produktu nie wyznaczamy terminu ponownego zakupu. Zakupy okazjonalne możesz odrzucić.</p><div id="suggested"></div>
    <details><summary>Kupione i odrzucone</summary><div id="archived"></div></details>
    <details><summary>Historia paragonów</summary><div id="receipts"></div></details></main>
    <script>
    const $=id=>document.getElementById(id),token='{{TOKEN}}',money=n=>(n/100).toLocaleString('pl-PL',{style:'currency',currency:'PLN'});
    async function api(path,options={}){const r=await fetch('/shopping'+path,{cache:'no-store',...options,headers:{'X-CSRF-TOKEN':token,...options.headers}});const d=await r.json();if(!r.ok)throw Error(d.error||'Nie udało się zapisać zmian.');return d;}
    async function change(p,status,name){try{await api('/products/'+p.id,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({status,name})});await refresh();}catch(e){$('message').textContent=e.message;}}
    function button(text,action){const b=document.createElement('button');b.textContent=text;b.onclick=action;return b;}
    async function refresh(){const d=await api('/data');for(const id of ['confirmed','suggested','archived','receipts'])$(id).replaceChildren();$('totals').textContent=d.receipts.length+' paragonów · '+money(d.receipts.reduce((s,r)=>s+r.total,0))+' · '+d.products.length+' produktów';
    for(const p of d.products){const card=document.createElement('article'),title=document.createElement('strong'),info=document.createElement('small');title.textContent=(p.mayRunOut?'Może się kończyć: ':'')+p.name;info.textContent='Łącznie kupiono: '+p.totalQuantity.toLocaleString('pl-PL')+' · '+(p.typicalIntervalDays?'zwykle co '+p.typicalIntervalDays.toLocaleString('pl-PL')+' dni · ':'')+'Ostatni zakup: '+p.lastPurchase+' · ilość na paragonach tego dnia: '+p.lastQuantity+' · dni zakupów: '+p.purchaseDays+'. '+p.reason;card.append(title,info);
    if(p.status==='Suggested')card.append(button('Dodaj do listy',()=>change(p,'Confirmed')),button('Odrzuć',()=>change(p,'Dismissed')));
    else if(p.status==='Confirmed')card.append(button('Kupione',()=>change(p,'Purchased')),button('Usuń z listy',()=>change(p,'Dismissed')));
    else{const label=document.createElement('small');label.textContent=p.status==='Purchased'?'Kupione':'Odrzucone';card.append(label,button('Dodaj ponownie',()=>change(p,'Confirmed')));}
    card.append(button('Zmień nazwę',()=>{const name=prompt('Czytelna nazwa produktu:',p.name);if(name!==null)change(p,p.status,name);}));$(p.status==='Confirmed'?'confirmed':p.status==='Suggested'?'suggested':'archived').append(card);}
    for(const id of ['confirmed','suggested','archived'])if(!$(id).children.length)$(id).textContent=id==='confirmed'?'Lista jest pusta. Dodaj produkty z propozycji poniżej.':'Brak produktów.';
    for(const r of d.receipts){const row=document.createElement('p');row.textContent=new Date(r.purchasedAt*1000).toLocaleDateString('pl-PL',{timeZone:'Europe/Warsaw'})+' · '+money(r.total)+' · '+(r.account||'Karta nieoznaczona');$('receipts').append(row);}}
    $('import').onclick=async()=>{$('import').disabled=true;const messages=[];try{if(!$('files').files.length)throw Error('Wybierz co najmniej jeden plik JSON.');for(const f of $('files').files){try{if(f.size>8388608)throw Error('Maksymalny rozmiar to 8 MB.');const r=await api('/import?account='+encodeURIComponent($('account').value),{method:'POST',headers:{'Content-Type':'application/json'},body:f});messages.push(f.name+': '+(r.added?'dodano '+r.items+' pozycji, '+money(r.total):'już zaimportowany'));}catch(e){messages.push(f.name+': '+e.message);}}await refresh();$('message').textContent=messages.join('\\n');}catch(e){$('message').textContent=e.message;}finally{$('import').disabled=false;}};
    async function whatsapp(path,done){try{const r=await api(path,{method:'POST'});$('message').textContent=done(r);}catch(e){$('message').textContent=e.message;}}
    $('propose').onclick=()=>whatsapp('/whatsapp/proposal',r=>'Wysłano '+r.sent+' propozycji. Odpowiedz numerami w grupie zakupowej.');
    $('sendList').onclick=()=>whatsapp('/whatsapp/list',()=>'Lista „Do kupienia” wysłana na tablicę.');
    refresh().catch(()=>{$('message').textContent='Nie udało się odczytać listy. Odśwież stronę.';});
    </script></html>
    """;
}
