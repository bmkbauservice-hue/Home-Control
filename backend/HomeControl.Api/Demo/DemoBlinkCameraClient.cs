using HomeControl.Api.Blink;

namespace HomeControl.Api.Demo;

// Fake-Blink für den Demo-Modus: zwei Kameras mit mitgelieferten, selbst gezeichneten Bildern
// (Demo/Bilder, erzeugt mit tools/demo-bilder). Erfüllt dasselbe Interface wie der echte BlinkHttpCameraClient,
// deshalb merken Endpunkte und Oberfläche keinen Unterschied. Es geht nie eine Anfrage an Blink.
// Singleton: Welches Bild gerade gezeigt wird und wann es "aufgenommen" wurde, bleibt bis zum Neustart erhalten.
public class DemoBlinkCameraClient : IBlinkCameraClient
{
    // Ein Bild in zwei Fassungen pro Kamera. "Neues Bild" wechselt zwischen ihnen, damit man den Unterschied sieht.
    private sealed class DemoCamera(string id, string name, string imagePrefix, DateTimeOffset takenAtUtc)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string ImagePrefix { get; } = imagePrefix;
        public DateTimeOffset TakenAtUtc { get; set; } = takenAtUtc;
        public int Variant { get; set; } = 1;
    }

    // So lange "nimmt die Kamera auf". Kurz, aber lang genug, dass man "Kamera nimmt auf …" sieht.
    public TimeSpan SnapshotDelay { get; init; } = TimeSpan.FromSeconds(1.5);

    private readonly TimeProvider timeProvider;
    private readonly Lock sync = new();
    private readonly List<DemoCamera> cameras;

    public DemoBlinkCameraClient(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        var now = timeProvider.GetUtcNow();
        cameras =
        [
            // Ids sind Zahlen wie bei Blink (die Route verlangt {id:long}).
            new DemoCamera("9001", "Haustür", "haustuer", now.AddHours(-3)),
            // Älter als ein Tag: Die Aufnahmezeit erscheint gelb. Ein Klick auf "Neues Bild" macht sie aktuell.
            new DemoCamera("9002", "Garten", "garten", now.AddDays(-2)),
        ];
    }

    public Task<BlinkStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BlinkStatusResult(BlinkConnectionState.Connected, timeProvider.GetUtcNow()));

    public Task<IReadOnlyList<BlinkCameraInfo>> GetCamerasAsync(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            IReadOnlyList<BlinkCameraInfo> list = cameras
                .Select(c => new BlinkCameraInfo(c.Id, c.Name, "ok", null, true, true, c.TakenAtUtc))
                .ToList();
            return Task.FromResult(list);
        }
    }

    public Task<BlinkThumbnail?> GetThumbnailAsync(string cameraId, CancellationToken cancellationToken)
    {
        string resource;
        DateTimeOffset takenAt;
        lock (sync)
        {
            if (Find(cameraId) is not { } camera)
            {
                return Task.FromResult<BlinkThumbnail?>(null);
            }

            resource = $"demo-bilder/{camera.ImagePrefix}-{camera.Variant}.jpg";
            takenAt = camera.TakenAtUtc;
        }

        return Task.FromResult<BlinkThumbnail?>(new BlinkThumbnail(LoadImage(resource), "image/jpeg", takenAt));
    }

    // "Neues Bild" im Demo: kurz warten, dann die andere Bildfassung mit der Aufnahmezeit "jetzt".
    public async Task<BlinkSnapshotResult?> RequestNewThumbnailAsync(string cameraId, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (Find(cameraId) is null)
            {
                return null;
            }
        }

        await Task.Delay(SnapshotDelay, timeProvider, cancellationToken);

        lock (sync)
        {
            var camera = Find(cameraId)!;
            camera.Variant = camera.Variant == 1 ? 2 : 1;
            camera.TakenAtUtc = timeProvider.GetUtcNow();
            return new BlinkSnapshotResult(true, camera.TakenAtUtc);
        }
    }

    private DemoCamera? Find(string cameraId) => cameras.FirstOrDefault(c => c.Id == cameraId);

    // Die Bilder sind in die DLL eingebettet (EmbeddedResource in der .csproj), so fehlen sie nie.
    private static byte[] LoadImage(string resourceName)
    {
        using var stream = typeof(DemoBlinkCameraClient).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Demo-Bild {resourceName} fehlt in der DLL.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
