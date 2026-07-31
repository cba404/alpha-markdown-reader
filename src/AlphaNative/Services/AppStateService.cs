using System.IO;
using System.Text.Json;

namespace AlphaNative.Services;

public sealed class AppStateService
{
    private readonly string _statePath;

    public AppStateService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpha");
        Directory.CreateDirectory(directory);
        _statePath = Path.Combine(directory, "state.json");
    }

    public AppState Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return new AppState();
            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(_statePath)) ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public void Save(AppState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_statePath, json);
        }
        catch
        {
            // State persistence must never prevent the editor from closing.
        }
    }
}

public sealed record AppState
{
    public string? LastFile { get; init; }
    public string? RecoveryText { get; init; }
    public bool DarkMode { get; init; }
    public bool SyncScroll { get; init; } = true;
    public double WindowWidth { get; init; } = 1400;
    public double WindowHeight { get; init; } = 900;
}
