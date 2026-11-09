using System.ComponentModel;
using System.Diagnostics;

namespace HomeControl.Api.Alarm;

// Blink-Kameras schreiben gelegentlich leere Videopakete ohne Bild in ihre Clips. Chrome bricht die Wiedergabe
// daran ab. ffmpeg verpackt die heruntergeladene Kopie deshalb neu und lässt solche Pakete weg.
// Bild und Ton werden dabei nicht neu kodiert, und der Clip auf dem USB-Stick bleibt unverändert.
public static class ClipRepair
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    // Ein Paket mit echtem Bildinhalt ist größer. Das gemessene leere Paket hatte 9 Byte.
    private const string DropEmptyPackets = @"noise=drop=lt(size\,12)";

    // Gelingt die Reparatur nicht, wird das Original geliefert: lieber ein evtl. abbrechendes Video als gar keines.
    public static async Task<byte[]> RemoveEmptyPacketsAsync(byte[] original, ILogger logger, CancellationToken cancellationToken)
    {
        // Eigener Ordner, nur für diesen Benutzer lesbar. Das Video bleibt nur für die Dauer dieses Aufrufs dort.
        var folder = Directory.CreateTempSubdirectory("homecontrol-clip-");
        try
        {
            var input = Path.Combine(folder.FullName, "eingang.mp4");
            var output = Path.Combine(folder.FullName, "ausgang.mp4");
            await File.WriteAllBytesAsync(input, original, cancellationToken);

            var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-v", "error", "-y", "-i", input, "-map", "0", "-c", "copy", "-bsf:v", DropEmptyPackets, "-movflags", "+faststart", output })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            var drainOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var drainError = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(drainOutput, drainError);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                logger.LogWarning("ffmpeg hat die Zeit überschritten, der Clip wird unverändert geliefert.");
                return original;
            }

            if (process.ExitCode != 0 || !File.Exists(output))
            {
                logger.LogWarning("ffmpeg konnte den Clip nicht aufbereiten (Exit-Code {ExitCode}).", process.ExitCode);
                return original;
            }

            var repaired = await File.ReadAllBytesAsync(output, cancellationToken);
            return Blink.BlinkHttpCameraClient.IsMp4(repaired) ? repaired : original;
        }
        catch (Win32Exception)
        {
            logger.LogWarning("ffmpeg ist nicht installiert, der Clip wird unverändert geliefert.");
            return original;
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
