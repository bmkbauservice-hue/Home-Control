using System.Net;
using System.Text;
using System.Text.Json;
using HomeControl.Api.Tuya;

namespace HomeControl.Api.Demo;

// Fake-Tuya-Cloud für den Demo-Modus. Der echte TuyaClient schickt seine Anfragen hierher statt ins Internet
// (ConfigurePrimaryHttpMessageHandler in DemoRegistration). Gleiches Prinzip wie FakeTuyaHandler in den Tests:
// Alles hinter dem HttpClient bleibt echter Code (Signatur, Token-Cache, Abgleich, Messwerte, Online-LEDs),
// nur die Antworten sind erfunden. Werte und Codes im Format der echten Tuya-Cloud (docs/TUYA.md).
public class DemoTuyaHandler(TimeProvider timeProvider) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        // Token: GET /v1.0/token?grant_type=1
        if (path == "/v1.0/token")
        {
            return Ok(new { access_token = "demo-token", expire_time = 7200 });
        }

        // Geräteliste mit Online-Zustand: GET /v1.0/iot-01/associated-users/devices
        if (path == "/v1.0/iot-01/associated-users/devices")
        {
            var devices = DemoHouse.Devices.Select(d => new
            {
                id = d.TuyaId,
                name = d.Name,
                product_name = d.Kind.ToString(),
                category = d.Kind == DemoKind.Door ? "mcs" : "cz",
                online = d.Online,
            });
            return Ok(new { devices, has_more = false });
        }

        // Status eines Geräts: GET /v1.0/iot-03/devices/{id}/status
        const string prefix = "/v1.0/iot-03/devices/";
        if (path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith("/status", StringComparison.Ordinal))
        {
            var id = Uri.UnescapeDataString(path[prefix.Length..^"/status".Length]);
            if (DemoHouse.FindByTuyaId(id) is not { } device)
            {
                // So antwortet Tuya bei einer unbekannten Geräte-Id (mit HTTP 200).
                return Json("""{ "success": false, "code": 2009, "msg": "device not found" }""");
            }

            return Ok(StatusItems(DemoBehavior.StateAt(device, timeProvider.GetUtcNow())));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    // Statuscodes wie bei den echten Geräten: Leistung und Spannung in Zehnteln als ganze Zahl (123,4 W → 1234).
    private static List<object> StatusItems(DemoState state)
    {
        var items = new List<object>();
        if (state.SwitchOn is { } on) items.Add(new { code = TuyaSwitchRecorder.SwitchCode, value = on });
        if (state.PowerWatt is { } watt) items.Add(new { code = TuyaDeviceSyncer.PowerCode, value = Tenths(watt) });
        if (state.VoltageVolt is { } volt) items.Add(new { code = TuyaDeviceSyncer.VoltageCode, value = Tenths(volt) });
        if (state.DoorOpen is { } open) items.Add(new { code = TuyaDeviceSyncer.DoorCode, value = open });
        if (state.Battery is { } battery) items.Add(new { code = TuyaDeviceSyncer.BatteryCode, value = battery });
        return items;
    }

    private static long Tenths(double value) => (long)Math.Round(value * 10);

    // Erfolgreiche Antwort im Tuya-Format: { "success": true, "result": … }
    private static Task<HttpResponseMessage> Ok(object result) =>
        Json(JsonSerializer.Serialize(new { success = true, result, t = 0 }));

    private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    });
}
