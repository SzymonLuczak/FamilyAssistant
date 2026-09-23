using Microsoft.AspNetCore.Antiforgery;
using System.Text.Encodings.Web;

namespace FamilyAssistant.Core.Summary;

public static class SummaryEndpoints
{
    public static void MapSummary(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/summary")) { await next(); return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            if (context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]"))
            { context.Response.StatusCode = 403; return; }
            try { await next(); }
            catch (AntiforgeryValidationException)
            { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "invalid_form_token" }); }
            catch (SummaryFailure ex)
            { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch
            { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "summary_unavailable" }); }
        });
        app.MapGet("/summary", (HttpContext context, IAntiforgery csrf) => Results.Content(
            Page.Replace("{{TOKEN}}", HtmlEncoder.Default.Encode(csrf.GetAndStoreTokens(context).RequestToken!)), "text/html; charset=utf-8"));
        app.MapGet("/summary/status", (FamilyConfiguration family, IConfiguration config) =>
        {
            var settings = family.Read();
            return Results.Ok(new { mode = config.GetValue<bool>("Summary:SendEnabled") ? "scheduled_delivery" : "preview_only", students = settings.Family.Count(p => p.Enabled && p.Type == "child"),
                schedulerEnabled = config.GetValue<bool>("Summary:SchedulerEnabled"),
                morning = settings.Notifications.MorningSummary, tomorrow = settings.Notifications.TomorrowSummary,
                timeZone = settings.Timezone, pickupRules = settings.Rules.Pickups.Length });
        });
        app.MapGet("/summary/history", async (SummaryStore store, HttpContext context) =>
            Results.Ok(await store.Latest(context.RequestAborted)));
        app.MapGet("/summary/deliveries", async (DeliveryQueue queue, HttpContext context) =>
            Results.Ok(await queue.History(context.RequestAborted)));
        app.MapPost("/summary/preview/{day}", async (string day, HttpContext context, IAntiforgery csrf, DailySummary summary, SummaryStore store) =>
        {
            await csrf.ValidateRequestAsync(context);
            if (day is not ("today" or "tomorrow")) return Results.BadRequest(new { error = "invalid_day" });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            var preview = await summary.Build(day == "today" ? summary.Today : summary.Today.AddDays(1), deadline.Token);
            var draft = await store.Save("manual", preview, deadline.Token);
            return Results.Ok(new { preview, draftId = draft.Id, status = draft.Status });
        });
    }

    private const string Page = """
    <!doctype html><html lang="pl"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <title>Family Assistant — plan rodziny</title>
    <style>body{font:17px system-ui;background:#f2f6f4;color:#173e30;margin:0}main{max-width:850px;margin:32px auto;padding:28px;background:white;border-radius:18px}button{padding:12px 20px;margin:8px 10px 8px 0;cursor:pointer}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:17px/1.6 system-ui;background:#f1f6f3;padding:22px;border-radius:12px}#status,#message{line-height:1.6}.warning{color:#904b00}a{color:#176c52}li{margin:10px 0}</style>
    <main><h1>Plan rodziny</h1><p>Szkoła, kalendarze i ustalone odbiory w jednym miejscu.</p>
    <p id="status">Sprawdzam ustawienia…</p>
    <button id="today">Przygotuj na dzisiaj</button><button id="tomorrow">Przygotuj na jutro</button>
    <p id="message" role="status"></p><pre id="preview" hidden></pre>
    <details><summary>Ostatnie zapisane podglądy</summary><button id="history">Odśwież listę</button><ul id="items"></ul></details>
    <p><strong>Przyciski powyżej tworzą tylko podgląd. Wysyłka zgodnie z harmonogramem ma osobną historię.</strong></p>
    <details><summary>Wysyłka na WhatsApp</summary><button id="deliveries">Odśwież status wysyłki</button><ul id="deliveryItems"></ul></details>
    <p><a href="/vulcan">Szkoła</a> · <a href="/google">Kalendarze</a> · <a href="/whatsapp/pair">WhatsApp</a></p></main>
    <script>
    const $=id=>document.getElementById(id), token='{{TOKEN}}';
    async function api(path,options){const response=await fetch('/summary'+path,{cache:'no-store',...options});const data=await response.json();if(!response.ok)throw Error(data.error==='family_not_configured'?'Najpierw skonfiguruj rodzinę.':data.error==='invalid_family_configuration'?'Konfiguracja rodziny wymaga poprawienia.':'Nie udało się przygotować podsumowania. Sprawdź połączenia i spróbuj ponownie.');return data;}
    function show(text){$('preview').textContent=text;$('preview').hidden=false;}
    async function generate(day){$('today').disabled=$('tomorrow').disabled=$('history').disabled=true;$('preview').hidden=true;$('message').className='';$('message').textContent='Pobieram plany i kalendarze…';try{const d=await api('/preview/'+day,{method:'POST',headers:{'X-CSRF-TOKEN':token}});show(d.preview.text);$('message').className=d.preview.incomplete?'warning':'';$('message').textContent=(d.preview.incomplete?'Uwaga: część danych jest niepełna lub wymaga sprawdzenia. ':'')+'Podgląd na '+d.preview.date+' przygotowany '+new Date(d.preview.generatedAt).toLocaleString('pl-PL')+'.';}catch(e){$('message').textContent=e.message;}finally{$('today').disabled=$('tomorrow').disabled=$('history').disabled=false;}}
    $('today').onclick=()=>generate('today');$('tomorrow').onclick=()=>generate('tomorrow');
    $('history').onclick=async()=>{try{const items=await api('/history');$('items').replaceChildren();for(const d of items){const li=document.createElement('li'),button=document.createElement('button');button.textContent=d.date+' · '+({morning:'poranny',tomorrow:'wieczorny',manual:'ręczny'}[d.kind]||d.kind);button.onclick=()=>{show(d.text);$('message').className='warning';$('message').textContent='Zapisany podgląd z '+new Date(d.updatedAt*1000).toLocaleString('pl-PL')+'. Dane mogły się zmienić — przyciski powyżej pobiorą nowy plan.';};li.append(button);$('items').append(li);}if(!items.length)$('items').textContent='Brak zapisanych podglądów.';}catch(e){$('message').textContent=e.message;}};
    $('deliveries').onclick=async()=>{try{const items=await api('/deliveries');$('deliveryItems').replaceChildren();const labels={pending:'oczekuje',sending:'trwa wysyłka',sent:'przyjęte przez WhatsApp',unknown:'wynik niepewny — sprawdź grupę, bez automatycznej ponownej wysyłki',blocked:'wysyłka zablokowana — sprawdź połączenie i grupę',expired:'pominięto spóźnioną wiadomość'};for(const d of items){const li=document.createElement('li');li.textContent=d.slot+' — '+(labels[d.status]||d.status);$('deliveryItems').append(li);}if(!items.length)$('deliveryItems').textContent='Brak wysyłek. Pierwsza wiadomość zostanie przygotowana w najbliższym terminie harmonogramu.';}catch(e){$('message').textContent=e.message;}};
    api('/status').then(s=>{$('status').textContent='Uczniowie: '+s.students+'. '+(s.schedulerEnabled?(s.mode==='scheduled_delivery'?'Wysyłka na WhatsApp: ':'Automatyczne podglądy: ')+s.morning+' na dziś i '+s.tomorrow+' na jutro (czas polski).':'Harmonogram jest wyłączony.')+(s.pickupRules?' Zapisane zasady odbioru: '+s.pickupRules+'.':' Brak stałych zasad odbioru.');}).catch(e=>{$('status').textContent=e.message;});
    </script></html>
    """;
}
