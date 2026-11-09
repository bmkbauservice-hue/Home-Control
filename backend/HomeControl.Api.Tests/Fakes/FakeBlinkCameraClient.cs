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

    // "Neues Bild": Standard ist Erfolg mit fester Aufnahmezeit. Test kann es ändern.
    public Func<string, BlinkSnapshotResult?> Snapshot { get; set; } =
        _ => new BlinkSnapshotResult(true, new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.Zero));

    public List<string> RequestedSnapshots { get; } = [];

    public Task<BlinkSnapshotResult?> RequestNewThumbnailAsync(string cameraId, CancellationToken cancellationToken)
    {
        RequestedSnapshots.Add(cameraId);
        return Task.FromResult(Snapshot(cameraId));
    }

    public List<BlinkSystemInfo> Systems { get; set; } = [new BlinkSystemInfo("77", "Haus", false)];

    // Scharfschalten: Standard ist ein vom Gerät bestätigter Erfolg. Test kann es ändern.
    public Func<string, BlinkArmResult?> Arm { get; set; } = _ => new BlinkArmResult(true, true, false);

    public List<string> ArmRequests { get; } = [];

    public Task<IReadOnlyList<BlinkSystemInfo>> GetSystemsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BlinkSystemInfo>>(Systems);

    public Task<BlinkArmResult?> ArmSystemAsync(string systemId, CancellationToken cancellationToken)
    {
        lock (ArmRequests) ArmRequests.Add(systemId);
        return Task.FromResult(Arm(systemId));
    }

    public Task<BlinkLiveProbeResult?> ProbeLiveViewAsync(string cameraId, CancellationToken cancellationToken) =>
        Task.FromResult<BlinkLiveProbeResult?>(new BlinkLiveProbeResult(true, 5, 900, null));

    public BlinkClipList Clips { get; set; } = new([], null);

    public Func<string, BlinkClipVideo?> ClipVideo { get; set; } = _ => null;

    public Task<BlinkClipList> GetClipsAsync(bool refresh, CancellationToken cancellationToken) =>
        Task.FromResult(Clips);

    public Task<BlinkClipVideo?> GetClipVideoAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(ClipVideo(key));

    public Task<BlinkThumbnail?> GetThumbnailAsync(string cameraId, CancellationToken cancellationToken)
    {
        RequestedThumbnails.Add(cameraId);
        return Task.FromResult(Thumbnail(cameraId));
    }
}
