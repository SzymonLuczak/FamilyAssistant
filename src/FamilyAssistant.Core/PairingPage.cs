namespace FamilyAssistant.Core;

public static class PairingPage
{
    public static void MapPairingPage(this WebApplication app)
    {
        app.MapGet("/whatsapp/pair", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            return Results.Content(Html, "text/html; charset=utf-8");
        });
        app.MapGet("/whatsapp/qr", async (HttpContext context, IHttpClientFactory clients, IConfiguration config) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var gatewayUrl = config["Gateways:WhatsApp"];
            if (string.IsNullOrWhiteSpace(gatewayUrl)) return Results.StatusCode(503);
            try
            {
                using var response = await clients.CreateClient("whatsapp")
                    .GetAsync($"{gatewayUrl.TrimEnd('/')}/auth/qr");
                if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
                if (response.Content.Headers.ContentType?.MediaType != "image/svg+xml") return Results.StatusCode(502);
                return Results.Bytes(await response.Content.ReadAsByteArrayAsync(), "image/svg+xml");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return Results.StatusCode(503);
            }
        });
    }

    private const string Html = """
    <!doctype html><html lang="pl"><meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Family Assistant — połącz WhatsApp</title>
    <style>
    body{font:18px system-ui;margin:0;background:#f2f6f4;color:#183c30}
    main{max-width:540px;margin:40px auto;padding:28px;background:white;border-radius:20px;text-align:center}
    img{width:320px;max-width:100%;height:auto}p{line-height:1.6}small{color:#52685e}
    </style><main><script>(()=>{let t=null;try{t=localStorage.getItem('fa-theme')}catch{}document.documentElement.dataset.theme=t||(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light')})()</script><style>
    html[data-theme=dark]{color-scheme:dark}
    html[data-theme=dark] body{background:#0f1714!important;color:#dce8e2!important}
    html[data-theme=dark] main{background:#18231f!important;box-shadow:0 1px 3px #0006}
    html[data-theme=dark] a{color:#6fd3a8!important}
    html[data-theme=dark] small{color:#9bb1a6!important}
    html[data-theme=dark] .warning{color:#f0b35a!important}
    html[data-theme=dark] pre,html[data-theme=dark] code{background:#101a16!important;color:#dce8e2!important}
    html[data-theme=dark] article,html[data-theme=dark] th,html[data-theme=dark] td{border-color:#2c3a34!important}
    html[data-theme=dark] input,html[data-theme=dark] select,html[data-theme=dark] textarea,html[data-theme=dark] button{background:#22302a;color:#dce8e2;border:1px solid #3a4b44;border-radius:6px}
    html[data-theme=dark] img#qr{background:#fff;padding:10px;border-radius:8px}
    #fa-theme-toggle{margin-left:auto;font:14px system-ui;padding:4px 12px;border-radius:14px;cursor:pointer}
    </style><nav style="font:15px system-ui;margin:0 0 18px;display:flex;gap:14px;flex-wrap:wrap;align-items:center"><a href="/">🏠 Pulpit</a><a href="/summary">Plan rodziny</a><a href="/shopping">Zakupy</a><a href="/vulcan">Szkoła</a><a href="/google">Kalendarze</a><a href="/whatsapp/pair">WhatsApp</a><a href="/shopping/biedronka">Biedronka</a><button id="fa-theme-toggle" type="button" onclick="(()=>{const d=document.documentElement,n=d.dataset.theme==='dark'?'light':'dark';d.dataset.theme=n;try{localStorage.setItem('fa-theme',n)}catch{}this.textContent=n==='dark'?'☀️ Jasny':'🌙 Ciemny'})()">🌓 Motyw</button></nav><script>document.getElementById('fa-theme-toggle').textContent=document.documentElement.dataset.theme==='dark'?'☀️ Jasny':'🌙 Ciemny'</script><h1>Połącz WhatsApp</h1>
    <p>W telefonie wybierz <b>Połączone urządzenia → Połącz urządzenie</b> i zeskanuj kod.</p>
    <p id="status" role="status">Sprawdzam połączenie…</p><img id="qr" hidden alt="Aktualny kod QR WhatsApp">
    <p><small>Kod odświeża się automatycznie. Wysyłka wiadomości pozostaje wyłączona w konfiguracji domyślnej.</small></p>
    </main><script>
    const img=document.getElementById('qr'), status=document.getElementById('status');
    let previous;
    function clear(){img.hidden=true;img.removeAttribute('src');if(previous){URL.revokeObjectURL(previous);previous=null;}}
    async function refresh(){
      try{
        const state=await fetch('/health/integrations',{cache:'no-store',signal:AbortSignal.timeout(5000)});
        if(!state.ok)throw Error();
        const {whatsapp}=await state.json();
        if(whatsapp==='ready'){clear();status.textContent='Połączono! Możemy teraz wybrać grupę rodzinną.';return;}
        if(whatsapp!=='qr'){
          clear();status.textContent=whatsapp==='authenticated'?'Zalogowano. Czekam na zakończenie synchronizacji…':
          whatsapp==='starting'?'Uruchamiam połączenie…':'Klient nie ma aktywnego kodu. Poproś o ponowne uruchomienie parowania.';
        }else{
          const response=await fetch('/whatsapp/qr?t='+Date.now(),{cache:'no-store',signal:AbortSignal.timeout(5000)});
          if(!response.ok)throw Error();
          const next=URL.createObjectURL(await response.blob());
          if(previous)URL.revokeObjectURL(previous);previous=next;img.src=next;img.hidden=false;
          status.textContent='Kod jest aktualizowany — możesz skanować.';
        }
      }catch{clear();status.textContent='Czekam na aktualny kod…';}
      setTimeout(refresh,2000);
    }refresh();
    </script></html>
    """;
}
