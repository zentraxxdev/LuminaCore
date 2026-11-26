using LuminaCore.Core.Engine;
using LuminaCore.Core.Models;
using MoonSharp.Interpreter;

namespace LuminaCore.Core.Plugins;

public sealed class LuaPluginEffect
{
    public string Id { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Author { get; init; } = "Community";
    public string Description { get; init; } = "Lua shader plugin";
    public string AccentHex { get; init; } = "#00E5FF";

    private readonly Script _script;
    private readonly DynValue _renderFunc;
    private readonly object _evalLock = new();

    public LuaPluginEffect(string filePath, Script script, DynValue renderFunc)
    {
        FilePath = filePath;
        Id = "plugin:" + Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
        _script = script;
        _renderFunc = renderFunc;
    }

    /// <summary>
    /// Evaluates the Lua shader onto a compact 24x1 strip and stretches cleanly across the CanvasSurface
    /// so Lua plugins maintain locked 60 FPS with minimal CPU usage.
    /// </summary>
    public void Render(CanvasSurface surface, double elapsedSeconds, EffectParameters parameters)
    {
        const int sampleCols = 24;
        Span<RgbColor> strip = stackalloc RgbColor[sampleCols];
        double speed = Math.Clamp(parameters.Speed, 0.1, 5.0);
        double scale = Math.Clamp(parameters.Scale, 0.25, 4.0);
        double dir = parameters.ReverseDirection ? -1.0 : 1.0;

        lock (_evalLock)
        {
            try
            {
                for (int i = 0; i < sampleCols; i++)
                {
                    double u = i / (double)(sampleCols - 1);
                    var res = _script.Call(_renderFunc, u, 0.5, elapsedSeconds * dir, speed, scale);
                    if (res.Type == DataType.Tuple && res.Tuple.Length >= 3)
                    {
                        byte r = (byte)Math.Clamp(res.Tuple[0].Number, 0, 255);
                        byte g = (byte)Math.Clamp(res.Tuple[1].Number, 0, 255);
                        byte b = (byte)Math.Clamp(res.Tuple[2].Number, 0, 255);
                        strip[i] = new RgbColor(r, g, b);
                    }
                    else
                    {
                        strip[i] = RgbColor.NeonCyan;
                    }
                }
            }
            catch
            {
                strip.Fill(RgbColor.ElectricViolet);
            }
        }

        int w = surface.Width;
        int h = surface.Height;
        for (int x = 0; x < w; x++)
        {
            float fx = (x / (float)(w - 1)) * (sampleCols - 1);
            int x0 = (int)fx;
            int x1 = Math.Min(x0 + 1, sampleCols - 1);
            var col = RgbColor.Lerp(strip[x0], strip[x1], fx - x0);
            for (int y = 0; y < h; y++)
            {
                surface.SetPixel(x, y, col);
            }
        }
    }
}

/// <summary>
/// Watches the local Plugins/ directory using FileSystemWatcher to hot-reload Lua effect scripts without restarting.
/// </summary>
public sealed class LuaPluginManager : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, LuaPluginEffect> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;

    public string PluginsDirectory { get; }
    public event Action? PluginsReloaded;

    public LuaPluginManager(string? customDirectory = null)
    {
        PluginsDirectory = customDirectory ?? Path.Combine(AppContext.BaseDirectory, "Plugins");
        Directory.CreateDirectory(PluginsDirectory);
        ReloadAll();
        StartWatcher();
    }

    public IReadOnlyList<LuaPluginEffect> GetLoadedPlugins()
    {
        lock (_lock) return _plugins.Values.OrderBy(p => p.Name).ToList();
    }

    public LuaPluginEffect? TryGetPlugin(string? pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return null;
        lock (_lock) return _plugins.TryGetValue(pluginId, out var p) ? p : null;
    }

    public void ReloadAll()
    {
        var loaded = new Dictionary<string, LuaPluginEffect>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(PluginsDirectory))
        {
            foreach (var file in Directory.GetFiles(PluginsDirectory, "*.lua"))
            {
                var effect = TryLoadFile(file);
                if (effect is not null)
                    loaded[effect.Id] = effect;
            }
        }

        lock (_lock)
        {
            _plugins.Clear();
            foreach (var kv in loaded)
                _plugins[kv.Key] = kv.Value;
        }

        PluginsReloaded?.Invoke();
    }

    private static LuaPluginEffect? TryLoadFile(string path)
    {
        try
        {
            string code = File.ReadAllText(path);
            var script = new Script(CoreModules.Preset_SoftSandbox | CoreModules.Math);
            script.DoString(code);

            var renderFunc = script.Globals.Get("render");
            if (renderFunc.Type != DataType.Function)
                return null;

            string name = script.Globals.Get("name").String ?? Path.GetFileNameWithoutExtension(path);
            string author = script.Globals.Get("author").String ?? "Community";
            string desc = script.Globals.Get("description").String ?? "Hot-reloaded Lua shader";
            string accent = script.Globals.Get("accent").String ?? "#00E5FF";

            return new LuaPluginEffect(path, script, renderFunc)
            {
                Name = name,
                Author = author,
                Description = desc,
                AccentHex = accent
            };
        }
        catch
        {
            return null;
        }
    }

    private void StartWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(PluginsDirectory, "*.lua")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFsEvent;
            _watcher.Changed += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Renamed += (_, _) => DebouncedReload();
        }
        catch { }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => DebouncedReload();

    private void DebouncedReload()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(180);
            ReloadAll();
        });
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
