using Microsoft.AspNetCore.Antiforgery;
using System.Text.Encodings.Web;

namespace FamilyAssistant.Core.Google;

public static class GoogleEndpoints
{
    public static void MapGoogle(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/google")) { await next(); return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            if (context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]"))
            { context.Response.StatusCode = 403; return; }
            try { await next(); }
            catch (GoogleFailure ex) { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(new { error = ex.Code }); }
            catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "invalid_form_token" }); }
            catch (Exception)
            {
                // No provider response bodies, secrets, auth codes, or event titles in logs.
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new { error = "google_operation_failed" });
            }
        });
        app.MapGet("/google", (HttpContext context, IAntiforgery csrf) =>
            Results.Content(Page.Replace("{{TOKEN}}", HtmlEncoder.Default.Encode(csrf.GetAndStoreTokens(context).RequestToken!)), "text/html; charset=utf-8"));
        app.MapGet("/google/status", async (GoogleAuthorization auth, GoogleCalendarReader reader) =>
            Results.Ok(new { status = await auth.Status(), selected = await reader.Selected(), timeZone = reader.TimeZoneId }));
        app.MapPost("/google/connect", async (HttpContext context, IAntiforgery csrf, GoogleAuthorization auth) =>
        {
            await csrf.ValidateRequestAsync(context);
            return Results.Redirect(await auth.Begin(context));
        });
        app.MapGet("/google/callback", async (HttpContext context, GoogleAuthorization auth) =>
        {
            await auth.Complete(context, context.Request.Query["state"], context.Request.Query["code"],
                context.Request.Query.ContainsKey("error") ? "denied" : null);
            return Results.Redirect("/google");
        });
        app.MapGet("/google/calendars", async (GoogleCalendarReader reader) => Results.Ok(await reader.Calendars()));
        app.MapPost("/google/calendars", async (HttpContext context, IAntiforgery csrf, GoogleCalendarReader reader) =>
        {
            await csrf.ValidateRequestAsync(context);
            var ids = await context.Request.ReadFromJsonAsync<string[]>() ?? [];
            await reader.Select(ids);
            return Results.Ok(new { selected = await reader.Selected() });
        });
        app.MapGet("/google/events/{day}", async (string day, GoogleCalendarReader reader) =>
        {
            if (day is not ("today" or "tomorrow")) throw new GoogleFailure("invalid_day", 400);
            return Results.Ok(await reader.Events(day == "today" ? reader.Today : reader.Today.AddDays(1)));
        });
    }

    private const string Page = """
    <!doctype html><html lang="pl"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <title>Family Assistant — kalendarze</title>
    <style>body{font:17px system-ui;background:#f2f6f4;color:#173e30;margin:0}main{max-width:760px;margin:32px auto;padding:28px;background:white;border-radius:18px}button{padding:12px 18px;margin:8px 8px 8px 0;cursor:pointer}label{display:block;padding:8px}li{margin:12px 0}#message{white-space:pre-wrap}small{color:#52685e}</style>
    <main><nav style="font:15px system-ui;margin:0 0 18px;display:flex;gap:14px;flex-wrap:wrap"><a href="/">🏠 Pulpit</a><a href="/summary">Plan rodziny</a><a href="/shopping">Zakupy</a><a href="/vulcan">Szkoła</a><a href="/google">Kalendarze</a><a href="/whatsapp/pair">WhatsApp</a><a href="/shopping/biedronka">Biedronka</a></nav><h1>Kalendarze rodziny</h1><p id="status">Sprawdzam konfigurację…</p>
    <p id="setup" hidden>Zapisz plik klienta OAuth w <code>secrets/google-oauth.json</code>.
    Adres przekierowania w Google: <code>http://localhost:8080/google/callback</code>.</p>
    <form action="/google/connect" method="post"><input type="hidden" name="__RequestVerificationToken" value="{{TOKEN}}">
    <button id="connect" disabled>Połącz konto Google — tylko odczyt</button></form>
    <section id="selection" hidden><h2>Wybierz kalendarze</h2><div id="calendars"></div>
    <button id="save">Zapisz wybór</button><h2>Wydarzenia</h2>
    <button id="today">Dzisiaj</button><button id="tomorrow">Jutro</button><p id="date"></p><ul id="events"></ul></section>
    <p id="message" role="status"></p><small>Nie zmieniamy wydarzeń i nie wysyłamy ich jeszcze na WhatsApp.</small></main>
    <script>
    const $=id=>document.getElementById(id), token='{{TOKEN}}';
    const labels={not_configured:'Brakuje konfiguracji Google.',not_connected:'Konto nie jest jeszcze połączone.',authorized:'Konto autoryzowane. Wybierz kalendarze.'};
    const errors={no_calendars_selected:'Najpierw wybierz i zapisz kalendarze.',not_connected:'Połącz konto Google.',reauthorization_required:'Google wymaga ponownego logowania.',calendar_access_denied:'Brak dostępu do kalendarza. Sprawdź uprawnienia i włączenie Calendar API.',select_1_to_20_calendars:'Wybierz od 1 do 20 kalendarzy.'};
    async function api(url,options){const r=await fetch(url,{cache:'no-store',...options});const data=await r.json();if(!r.ok)throw Error(errors[data.error]||'Operacja nie powiodła się. Spróbuj ponownie.');return data;}
    async function load(){try{const s=await api('/google/status');$('status').textContent=labels[s.status]||s.status;$('setup').hidden=s.status!=='not_configured';$('connect').disabled=s.status==='not_configured';
      if(s.status!=='authorized')return;
      const calendars=await api('/google/calendars');$('selection').hidden=false;$('calendars').replaceChildren();
      for(const c of calendars){const label=document.createElement('label'),input=document.createElement('input');input.type='checkbox';input.value=c.id;input.checked=s.selected.includes(c.id);label.append(input,document.createTextNode(' '+c.name));$('calendars').append(label);}
    }catch(e){$('message').textContent=e.message;}}
    $('save').onclick=async()=>{try{const ids=Array.from($('calendars').querySelectorAll('input:checked')).map(x=>x.value);await api('/google/calendars',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':token},body:JSON.stringify(ids)});$('message').textContent='Wybór zapisany.';}catch(e){$('message').textContent=e.message;}};
    async function events(day){$('events').replaceChildren();$('message').textContent='Pobieram…';try{const data=await api('/google/events/'+day);$('date').textContent=data.date+' · '+data.timeZone;
      for(const event of data.events){const li=document.createElement('li');const time=event.isAllDay?'Cały dzień':new Intl.DateTimeFormat('pl-PL',{timeZone:data.timeZone,hour:'2-digit',minute:'2-digit'}).format(new Date(event.start));li.textContent=time+' — '+event.title+(event.location?' · '+event.location:'');$('events').append(li);}
      $('message').textContent=data.errors.length?'Nie udało się pobrać wszystkich kalendarzy. Wynik może być niepełny.':data.events.length?'':'Brak wydarzeń.';
    }catch(e){$('message').textContent=e.message;}}
    $('today').onclick=()=>events('today');$('tomorrow').onclick=()=>events('tomorrow');load();
    </script></html>
    """;
}
