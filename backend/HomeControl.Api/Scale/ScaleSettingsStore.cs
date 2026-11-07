using System.Text.Json;

namespace HomeControl.Api.Scale;

public record ScaleBinding(string TuyaId, string Name, string? WeightCode);

// Nur die Gerätezuordnung speichern, keine Körperwerte. Die Datei liegt außerhalb des Repos.
public sealed class ScaleSettingsStore(IConfiguration configuration)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string SettingsPath => configuration["Scale:SettingsPath"] ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HomeControl", "scale-settings.json");

    public async Task<ScaleBinding?> ReadAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            return JsonSerializer.Deserialize<ScaleBinding>(await File.ReadAllTextAsync(SettingsPath, ct));
        }
        finally { gate.Release(); }
    }

    public async Task WriteAsync(ScaleBinding? binding, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        string? temporary = null;
        try
        {
            var path = Path.GetFullPath(SettingsPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var file = new FileStream(temporary, options))
                await JsonSerializer.SerializeAsync(file, binding, cancellationToken: ct);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            gate.Release();
        }
    }
}
