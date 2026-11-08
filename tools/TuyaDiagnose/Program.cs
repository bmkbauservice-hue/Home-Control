using System.Globalization;
using System.Text.Json;
using HomeControl.Api.Tuya;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// Eigenes, rein lesendes Werkzeug. Keine Web-App starten und keine Datenbank öffnen.
var gatewayMode = args is ["--gateway"];
var deviceMode = args is ["--device"];
if (args.Length != 0 && !gatewayMode && !deviceMode)
{
    Console.Error.WriteLine("Aufruf: TuyaDiagnose [--gateway | --device]");
    return 1;
}

DirectoryInfo? folder = new DirectoryInfo(AppContext.BaseDirectory);
while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "HomeControl.slnx")))
    folder = folder.Parent;
if (folder is null)
{
    Console.Error.WriteLine("Projektwurzel nicht gefunden. Das Werkzeug im vorhandenen Checkout starten.");
    return 1;
}

var config = new ConfigurationBuilder()
    .SetBasePath(Path.Combine(folder.FullName, "backend", "HomeControl.Api"))
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets(typeof(TuyaClient).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();
var services = new ServiceCollection();
services.AddLogging();
services.Configure<TuyaOptions>(config.GetSection(TuyaOptions.SectionName));
services.AddSingleton(TimeProvider.System);
services.AddSingleton<TuyaTokenStore>();
services.AddHttpClient<TuyaClient>((factoryServices, http) =>
{
    http.BaseAddress = new Uri(factoryServices.GetRequiredService<IOptionsMonitor<TuyaOptions>>().CurrentValue.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(10);
});
using var provider = services.BuildServiceProvider();
var missing = provider.GetRequiredService<IOptionsMonitor<TuyaOptions>>().CurrentValue.GetMissingCredentials();
if (missing.Count > 0)
{
    Console.Error.WriteLine("Tuya-Zugang noch nicht vollständig eingerichtet: " + string.Join(", ", missing));
    return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var client = provider.GetRequiredService<TuyaClient>();
try
{
    if (gatewayMode || deviceMode)
    {
        Console.Write(gatewayMode
            ? "Virtuelle Geräte-ID von Muju Gateway (leer = beenden): "
            : "Virtuelle Geräte-ID aus der App (leer = beenden): ");
        var id = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(id)) return 0;
        if (id.Length > 128 || id.Any(character => !char.IsAsciiLetterOrDigit(character) &&
            character is not ('-' or '_')))
        {
            Console.Error.WriteLine("Bitte die virtuelle ID direkt aus den Geräteinformationen kopieren.");
            return 1;
        }

        if (gatewayMode)
        {
            var children = await client.GetGatewayDevicesAsync(id, cancellation.Token);
            Console.WriteLine($"Tuya meldet {children.Count} Geräte unter diesem Gateway.");
            foreach (var child in children.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
                Console.WriteLine($"  {child.Name} | Kategorie {child.Category} | {Online(child.Online)}");
            return 0;
        }

        var linked = (await client.GetLinkedAppDevicesAsync(cancellation.Token)).FirstOrDefault(device => device.Id == id);
        Console.WriteLine(linked is null
            ? "Geräte-ID in der verknüpften Tuya-Liste nicht vorhanden."
            : $"App-Liste: {linked.Name} | {Online(linked.Online)}");
        await ReadSection("Direkte Geräteinformation", async () =>
        {
            var info = await client.GetDeviceInformationAsync(id, cancellation.Token);
            Console.WriteLine($"  {info.Name} | {info.ProductName} | {Online(info.Online)}");
        });
        await ReadSection("Schaltstatus", async () =>
        {
            var status = await client.GetDeviceStatusAsync(id, cancellation.Token);
            var power = TuyaDeviceSyncer.GetBoolean(status, TuyaSwitchRecorder.SwitchCode);
            Console.WriteLine(power switch { true => "  switch_1: EIN", false => "  switch_1: AUS", _ => "  Kein boolesches switch_1 gemeldet." });
        });
        return 0;
    }

    var devices = (await client.GetLinkedAppDevicesAsync(cancellation.Token))
        .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    Console.WriteLine("Tuya-Diagnose: nur lesen, kein Schaltbefehl und keine Speicherung.");
    Console.WriteLine("Geräte-IDs, lokale Schlüssel und Zugangsdaten werden nicht ausgegeben.");
    if (devices.Count == 0)
    {
        Console.WriteLine("Das verknüpfte Cloud-Projekt liefert keine Geräte.");
        return 0;
    }
    for (var i = 0; i < devices.Count; i++)
        Console.WriteLine($"{i + 1}. {devices[i].Name} | {devices[i].ProductName} | Kategorie {devices[i].Category} | App-Liste: {Online(devices[i].Online)}");
    Console.Write("Gerätenummern für die Diagnose, mit Komma getrennt (leer = beenden): ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) return 0;
    var numbers = new HashSet<int>();
    foreach (var part in input.Split(','))
    {
        if (!int.TryParse(part.Trim(), out var number) || number < 1 || number > devices.Count)
        {
            Console.Error.WriteLine("Ungültige Auswahl. Es wurde noch kein Gerät näher abgefragt.");
            return 1;
        }
        numbers.Add(number);
    }
    if (numbers.Count > 8)
    {
        Console.Error.WriteLine("Bitte höchstens acht Geräte pro Diagnose auswählen.");
        return 1;
    }
    foreach (var number in numbers.Order())
    {
        var device = devices[number - 1];
        Console.WriteLine($"\n=== {device.Name} ===");
        Console.WriteLine("Online aus App-Geräteliste: " + Online(device.Online));
        await ReadSection("Direkte Geräteinformation", async () =>
        {
            var info = await client.GetDeviceInformationAsync(device.Id, cancellation.Token);
            Console.WriteLine("Direkter Online-Status: " + Online(info.Online));
        });
        await ReadSection("Statuswerte", async () =>
        {
            var status = await client.GetDeviceStatusAsync(device.Id, cancellation.Token);
            if (status.Count == 0) Console.WriteLine("Keine Statuswerte von Tuya geliefert.");
            foreach (var item in status)
                Console.WriteLine($"  {item.Code} = {item.Value.GetRawText()}");
        });
        await ReadSection("Definitionen und Einheiten", async () =>
        {
            var specification = await client.GetDeviceSpecificationAsync(device.Id, cancellation.Token);
            foreach (var definition in specification.Functions)
                Console.WriteLine($"  Funktion {definition.Code} | {definition.Type} | {definition.Values}");
            foreach (var definition in specification.Status)
                Console.WriteLine($"  Status {definition.Code} | {definition.Name} | {definition.Type} | {definition.Values}");
            if (specification.Status.Count == 0 && specification.Functions.Count == 0)
                Console.WriteLine("Keine Definitionen geliefert; Einheiten und Skalierung bleiben ungeklärt.");
        });
        await ReadSection("Letzte Cloud-Eigenschaftswerte", async () =>
        {
            var properties = await client.GetReportedPropertiesAsync(device.Id, cancellation.Token);
            if (properties.Count == 0) Console.WriteLine("Keine Eigenschaften von Tuya geliefert.");
            foreach (var property in properties)
                Console.WriteLine($"  {property.Code} = {property.Value.GetRawText()} | Meldezeit UTC: " +
                    (property.ReportedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "unbekannt"));
        });
    }
    Console.WriteLine("\nDiagnose beendet. Meldezeit beschreibt die Eigenschaft, nicht automatisch den Zeitpunkt einer Wiegung.");
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Diagnose abgebrochen.");
    return 130;
}
catch (Exception exception) when (exception is TuyaApiException or HttpRequestException or JsonException)
{
    PrintError(exception);
    return 1;
}

static string Online(bool? value) => value switch { true => "online", false => "offline", _ => "unbekannt" };

async Task ReadSection(string name, Func<Task> read)
{
    Console.WriteLine(name + ":");
    try { await read(); }
    catch (Exception exception) when (exception is TuyaApiException or HttpRequestException or JsonException
                                     || (exception is TaskCanceledException && !cancellation.IsCancellationRequested))
    {
        PrintError(exception);
    }
}

static void PrintError(Exception exception)
{
    // Fachliche Fehlermeldungen können Geräte-IDs enthalten. Nur den Tuya-Code ausgeben.
    Console.WriteLine(exception is TuyaApiException tuya
        ? $"  Tuya lehnt den Abruf ab (Code {tuya.TuyaCode}). Kein Messwert daraus ableiten."
        : "  Abruf fehlgeschlagen oder Zeitlimit erreicht. Keine Werte ersetzt.");
}

