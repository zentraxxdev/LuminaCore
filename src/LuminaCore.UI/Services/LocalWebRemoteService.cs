using System.Net;
using System.Text;
using LuminaCore.Core.Engine;
using LuminaCore.Core.Models;
using Newtonsoft.Json.Linq;

namespace LuminaCore.UI.Services;

/// <summary>
/// Lightweight embedded HTTP remote control server (default http://localhost:5050/)
/// allowing phones or tablets on the local network to control effects, brightness, and blackout.
/// </summary>
public sealed class LocalWebRemoteService : IDisposable
{
    private readonly RenderPipeline _pipeline;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public bool IsRunning => _listener is { IsListening: true };
    public event Action? StateMutatedRemotely;

    public LocalWebRemoteService(RenderPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public void Configure(bool enable, int port)
    {
        if (!enable)
        {
            Stop();
            return;
        }

        if (IsRunning) return;
        Start(Math.Clamp(port, 1024, 65535));
    }

    public void Start(int port = 5050)
    {
        Stop();
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _listenTask = Task.Run(() => AcceptLoopAsync(token), token);
        }
        catch
        {
            // Port busy or URLACL restricted — fall back gracefully without crashing
            Stop();
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            try
            {
                var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequest(ctx), ct);
            }
            catch
            {
                if (ct.IsCancellationRequested) break;
            }
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            string path = ctx.Request.Url?.AbsolutePath ?? "/";

            if (path == "/api/state" && ctx.Request.HttpMethod == "GET")
            {
                var prof = _pipeline.GetActiveProfileSnapshot();
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    effect = prof.Effect.ToString(),
                    brightness = prof.MasterBrightness,
                    isBlackout = _pipeline.IsBlackout,
                    profile = prof.Name
                });
                WriteResponse(ctx, 200, "application/json", json);
                return;
            }

            if (path == "/api/control" && ctx.Request.HttpMethod == "POST")
            {
                using var reader = new System.IO.StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = JObject.Parse(reader.ReadToEnd());

                if (body["blackout"] is JToken blk)
                    _pipeline.IsBlackout = blk.Value<bool>();

                if (body["brightness"] is JToken br)
                    _pipeline.UpdateMasterBrightness(br.Value<double>());

                if (body["effect"] is JToken fx && Enum.TryParse<EffectType>(fx.ToString(), true, out var et))
                    _pipeline.UpdateEffect(et);

                StateMutatedRemotely?.Invoke();
                WriteResponse(ctx, 200, "application/json", "{\"ok\":true}");
                return;
            }

            WriteResponse(ctx, 200, "text/html; charset=utf-8", MobileHtml);
        }
        catch
        {
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private static void WriteResponse(HttpListenerContext ctx, int status, string contentType, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    public void Dispose() => Stop();

    private const string MobileHtml = """
        <!DOCTYPE html><html><head><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>LuminaCore Remote</title>
        <style>
          body{margin:0;background:#08090D;color:#F8FAFC;font-family:-apple-system,Segoe UI,sans-serif;padding:20px}
          .card{background:#12151E;border:1px solid #1E2433;border-radius:14px;padding:16px;margin-bottom:14px}
          h2{margin:0 0 12px;font-size:18px;color:#00E5FF}
          button{width:100%;padding:12px;border-radius:10px;border:1px solid #2A344D;background:#181D2B;color:#fff;font-size:14px;font-weight:600;margin-bottom:8px;cursor:pointer}
          button.primary{background:rgba(0,229,255,0.16);border-color:#00E5FF;color:#00E5FF}
          input[type=range]{width:100%;margin:10px 0}
        </style></head><body>
          <h2>LuminaCore Remote</h2>
          <div class="card">
            <div>Brightness <span id="bv">90%</span></div>
            <input type="range" id="br" min="0" max="100" value="90" oninput="setBr(this.value)">
            <button class="primary" id="blk" onclick="toggleBlk()">Toggle Blackout</button>
          </div>
          <div class="card">
            <button onclick="setFx('Solid')">Solid Color</button>
            <button onclick="setFx('Gradient')">Gradient</button>
            <button onclick="setFx('RainbowWave')">Rainbow Wave</button>
            <button onclick="setFx('Plasma')">Plasma</button>
            <button onclick="setFx('AudioVisualizer')">Audio Reactive</button>
            <button onclick="setFx('ScreenAmbilight')">Screen Ambilight</button>
          </div>
          <script>
            let isBlk=false;
            fetch('/api/state').then(r=>r.json()).then(s=>{isBlk=s.isBlackout;document.getElementById('br').value=Math.round(s.brightness*100);document.getElementById('bv').textContent=Math.round(s.brightness*100)+'%';});
            const send=p=>fetch('/api/control',{method:'POST',body:JSON.stringify(p)});
            const setBr=v=>{document.getElementById('bv').textContent=v+'%';send({brightness:v/100});};
            const setFx=fx=>send({effect:fx});
            const toggleBlk=()=>{isBlk=!isBlk;send({blackout:isBlk});};
          </script>
        </body></html>
        """;
}
