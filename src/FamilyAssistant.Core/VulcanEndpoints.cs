using Microsoft.AspNetCore.Antiforgery;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FamilyAssistant.Core;

public static class VulcanEndpoints
{
    public static async Task<string> Status(IHttpClientFactory clients, IConfiguration config)
    {
        var url = config["Gateways:Vulcan"];
        if (string.IsNullOrWhiteSpace(url)) return "disabled";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var response = await clients.CreateClient("vulcan").GetAsync(url.TrimEnd('/') + "/status", timeout.Token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return doc.RootElement.GetProperty("connection").GetString() ?? "unknown";
        }
        catch { return "unavailable"; }
    }

    public static void MapVulcan(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/vulcan")) { await next(); return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            if (context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]"))
            { context.Response.StatusCode = 403; return; }
            try { await next(); }
            catch (AntiforgeryValidationException)
            { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "invalid_form_token" }); }
            catch
            { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "vulcan_unavailable" }); }
        });
        app.MapGet("/vulcan", (HttpContext context, IAntiforgery csrf) => Results.Content(
            Page.Replace("{{TOKEN}}", HtmlEncoder.Default.Encode(csrf.GetAndStoreTokens(context).RequestToken!)), "text/html; charset=utf-8"));
        app.MapGet("/vulcan/status", (HttpContext context, IHttpClientFactory clients, IConfiguration config) =>
            Proxy(context, clients, config, "/status"));
        app.MapPost("/vulcan/register", async (HttpContext context, IAntiforgery csrf, IHttpClientFactory clients, IConfiguration config) =>
        {
            await csrf.ValidateRequestAsync(context);
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                if (body.Length + count > 1_000_000) return Results.Json(new { error = "export_too_large" }, statusCode: 413);
                body.Write(buffer, 0, count);
            }
            return await Proxy(context, clients, config, "/register", body.ToArray());
        });
        app.MapGet("/vulcan/students/{student}/schedule/{day}", (string student, string day, HttpContext context, IHttpClientFactory clients, IConfiguration config) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(student, "^[a-f0-9]{24}$") ||
                !DateOnly.TryParseExact(day, "yyyy-MM-dd", out _))
                return Task.FromResult<IResult>(Results.BadRequest(new { error = "invalid_request" }));
            return Proxy(context, clients, config, $"/students/{student}/schedule/{day}");
        });
    }

    private static async Task<IResult> Proxy(HttpContext context, IHttpClientFactory clients, IConfiguration config, string path, byte[]? body = null)
    {
        var url = config["Gateways:Vulcan"];
        if (string.IsNullOrWhiteSpace(url)) return Results.Json(new { error = "not_configured" }, statusCode: 503);
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url.TrimEnd('/') + path);
        if (body is not null) request.Content = new ByteArrayContent(body);
        using var response = await clients.CreateClient("vulcan").SendAsync(request, context.RequestAborted);
        var text = await response.Content.ReadAsStringAsync(context.RequestAborted);
        // The internal gateway returns only normalized DTOs and fixed error codes.
        using var parsed = JsonDocument.Parse(text);
        return Results.Content(text, "application/json", statusCode: (int)response.StatusCode);
    }

    private const string Page = """
    <!doctype html><html lang="pl"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <title>Family Assistant — szkoła</title>
    <style>body{font:17px system-ui;background:#f2f6f4;color:#173e30;margin:0}main{max-width:800px;margin:32px auto;padding:28px;background:white;border-radius:18px}button,input,select{padding:12px;margin:8px 0;max-width:100%;box-sizing:border-box}li{margin:12px 0}.warning{color:#904b00}#message{white-space:pre-wrap}</style>
    <main><h1>Szkoła — eduVULCAN</h1><p id="status">Sprawdzam połączenie…</p>
    <details id="setup" open><summary>Połącz konto eduVULCAN</summary>
    <ol><li><a href="https://eduvulcan.pl/api/ap" target="_blank" rel="noopener noreferrer">Otwórz logowanie eduVULCAN</a> i zaloguj się na stronie dostawcy.</li>
    <li>Po powrocie na adres <strong>eduvulcan.pl/api/ap</strong> zapisz stronę przez <strong>Ctrl+S</strong>, jako „Strona internetowa, tylko HTML”. Strona może wyglądać na pustą.</li>
    <li>Wybierz zapisany plik poniżej. Zawiera tymczasowy dostęp do konta — importuj go tylko tutaj, na swoim komputerze. Po udanym połączeniu usuń pobrany plik.</li></ol>
    <p>Jeżeli nie ma powrotu na wskazany adres albo strona zgłasza błąd, zatrzymaj się i podaj sam komunikat. Nie wklejaj hasła ani zawartości pliku do rozmowy.</p>
    <input id="file" type="file" accept=".html,.htm,.json"><br><button id="register">Połącz uczniów</button>
    <p>Import zastępuje dotychczasowe konto i jego lokalne kopie planu. Nie zmienia danych w dzienniku. Integracja jest nieoficjalna i wymaga dostępu dostępnego dla Twojego konta.</p></details>
    <section id="schedule" hidden><h2>Plan zajęć</h2><label>Uczeń <select id="student"></select></label><br>
    <label>Data <input id="day" type="date"></label><br><button id="read">Pobierz plan</button><p id="summary"></p><ul id="lessons"></ul></section>
    <p id="message" role="status"></p><p><a href="/google">Kalendarze rodziny</a></p></main>
    <script>
    const $=id=>document.getElementById(id),token='{{TOKEN}}';
    const errors={registration_export_not_found:'W pliku nie ma danych rejestracji. Zapisz stronę po zalogowaniu, z adresu eduvulcan.pl/api/ap.',invalid_or_expired_export:'Dane wygasły lub są nieprawidłowe. Otwórz stronę eduVULCAN ponownie i zapisz świeży plik.',consent_required:'Dokończ wymagane zgody w eduVULCAN.',no_student_tokens:'Brak uczniów w danych rejestracji.',multiple_tenants_not_supported:'Ten import obejmuje różne jednostki eduVULCAN. Obsługa takiego konta wymaga rozszerzenia integracji.',registration_failed:'eduVULCAN nie potwierdził rejestracji. Konto nie zostało zastąpione. Nie ponawiaj wielokrotnie — sprawdzimy przyczynę.',provider_unavailable_no_snapshot:'Nie udało się odczytać planu. Brak zapisanej kopii dla tego dnia.',export_too_large:'Plik jest za duży. Zapisz stronę jako tylko HTML.'};
    async function api(path,options){const r=await fetch('/vulcan'+path,{cache:'no-store',...options});const data=await r.json();if(!r.ok)throw Error(errors[data.error]||'Nie udało się wykonać operacji. Spróbuj później.');return data;}
    async function load(){const data=await api('/status');$('status').textContent=data.connection==='registered'?'Dostęp zarejestrowany. Odczytaj plan, aby sprawdzić połączenie.':'Konto nie jest połączone.';$('schedule').hidden=!data.students.length;$('setup').open=!data.students.length;$('student').replaceChildren();for(const s of data.students){const o=document.createElement('option');o.value=s.id;o.textContent=s.name+' · '+s.school+ (s.class?' · '+s.class:'');$('student').append(o);}}
    $('register').onclick=async()=>{const file=$('file').files[0];if(!file){$('message').textContent='Najpierw wybierz zapisany plik.';return;}if(file.size>1000000){$('message').textContent=errors.export_too_large;return;}$('register').disabled=true;$('message').textContent='Rejestruję dostęp…';try{await api('/register',{method:'POST',headers:{'X-CSRF-TOKEN':token,'Content-Type':'text/plain'},body:await file.text()});$('file').value='';$('lessons').replaceChildren();$('summary').textContent='';await load();$('message').textContent='Dostęp zapisany. Możesz usunąć pobrany plik.';}catch(e){$('message').textContent=e.message;}finally{$('register').disabled=false;}};
    $('read').onclick=async()=>{$('read').disabled=true;$('lessons').replaceChildren();$('summary').textContent='';$('message').textContent='Pobieram plan…';try{const d=await api('/students/'+encodeURIComponent($('student').value)+'/schedule/'+$('day').value);$('message').textContent=d.stale?'Uwaga: odczyt nie powiódł się. Pokazuję starą kopię z '+new Date(d.fetchedAt).toLocaleString('pl-PL'): 'Odczytano: '+new Date(d.fetchedAt).toLocaleString('pl-PL');$('message').className=d.stale?'warning':'';$('summary').textContent=d.requiresReview?'Zmiana planu wymaga sprawdzenia w eduVULCAN. Godzina odbioru nie została wyliczona.':d.firstLesson?'Zajęcia: '+d.firstLesson.slice(0,5)+'–'+d.lastLesson.slice(0,5):'Brak aktywnych zajęć w pobranym planie.';for(const l of d.lessons){const li=document.createElement('li');li.textContent=l.start.slice(0,5)+'–'+l.end.slice(0,5)+' '+l.subject+(l.status==='cancelled'?' — odwołane':l.status==='change_requires_review'?' — zmiana: sprawdź w dzienniku':'')+(l.note?' · '+l.note:'');$('lessons').append(li);}}catch(e){$('message').textContent=e.message;}finally{$('read').disabled=false;}};
    $('day').value=new Intl.DateTimeFormat('sv-SE',{timeZone:'Europe/Warsaw',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date());load().catch(e=>$('message').textContent=e.message);
    </script></html>
    """;
}
