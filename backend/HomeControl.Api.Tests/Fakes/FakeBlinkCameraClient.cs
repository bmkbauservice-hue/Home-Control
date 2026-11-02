using HomeControl.Api.Blink;

namespace HomeControl.Api.Tests.Fakes;

// Ersetzt den echten Blink-Client in Endpunkt-Tests. Was er liefert oder wirft, legt der Test fest.
public class FakeBlinkCameraClient : IBlinkCameraClient
{
    public Func<string, BlinkThumbnail?> Thumbnail { get; set; } = _ => null;

    public List<string> RequestedThumbnails { get; } = [];

    public Task<BlinkStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BlinkStatusResult(BlinkConnectionState.Connected, DateTimeOffset.UnixEpoch));

    public Task<IReadOnlyList<BlinkCameraInfo>> GetCamerasAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BlinkCameraInfo>>([new BlinkCameraInfo("1", "Wohnzimmer", "ok", null, true, true, null)]);

    public Task<BlinkThumbnail?> GetThumbnailAsync(string cameraId, CancellationToken cancellationToken)
    {
        RequestedThumbnails.Add(cameraId);
        return Task.FromResult(Thumbnail(cameraId));
    }
}
