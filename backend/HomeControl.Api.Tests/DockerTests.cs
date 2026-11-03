using System.Net;
using HomeControl.Api.Tests.Fakes;

namespace HomeControl.Api.Tests;

// Docker (docs/DOCKER.md): Gesundheitsprüfung und die wichtigsten Regeln der Docker-Dateien.
// Die Dateien werden als Text geprüft, so fällt ein versehentliches "USER root" oder eine fehlende
// .env-Zeile schon bei ./pruefen.sh auf, nicht erst beim Bauen.
public class DockerTests
{
    [Fact]
    public async Task Healthz_antwortet_ohne_Anmeldung_auch_mit_Leseschutz()
    {
        using var factory = new LoginRequiredApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        // Zum Vergleich: Daten gibt es ohne Anmeldung nicht.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/devices")).StatusCode);
    }

    [Fact]
    public void Image_laeuft_nicht_als_root_und_hat_einen_Healthcheck()
    {
        var dockerfile = File.ReadAllText(RepoFile("Dockerfile"));

        Assert.Contains("USER $APP_UID", dockerfile);
        Assert.DoesNotContain("USER root", dockerfile);
        Assert.Contains("HEALTHCHECK", dockerfile);
        // Mehrstufig: Node und SDK nur zum Bauen, am Ende das schlanke aspnet-Image.
        Assert.Contains("AS frontend", dockerfile);
        Assert.Contains("AS backend", dockerfile);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:", dockerfile);
    }

    [Fact]
    public void Geheimnisse_und_Datenbanken_kommen_nie_ins_Image_oder_ins_Repo()
    {
        var dockerignore = File.ReadAllLines(RepoFile(".dockerignore"));
        var gitignore = File.ReadAllLines(RepoFile(".gitignore"));

        Assert.Contains(".env", dockerignore);
        Assert.Contains("*.db", dockerignore);
        Assert.Contains(".env", gitignore);
        // Die Vorlage enthält keine Werte für Geheimnisse.
        foreach (var line in File.ReadAllLines(RepoFile(".env.example")))
        {
            if (line.StartsWith("Jwt__SigningKey=") || line.Contains("Secret=") || line.Contains("ApiKey=")
                || line.Contains("RefreshToken="))
            {
                Assert.EndsWith("=", line);
            }
        }
    }

    [Fact]
    public void Demo_im_Container_braucht_keine_env_Datei_und_keine_Volumes()
    {
        var compose = File.ReadAllText(RepoFile("docker-compose.yml"));
        var demo = compose[compose.IndexOf("  demo:", StringComparison.Ordinal)..compose.IndexOf("\nvolumes:", StringComparison.Ordinal)];

        Assert.Contains("profiles: [demo]", demo);
        Assert.Contains("ASPNETCORE_ENVIRONMENT: Demo", demo);
        Assert.Contains("homecontrol-demo.db", demo);
        Assert.DoesNotContain("env_file", demo);
        Assert.Contains("tmpfs:", demo);
    }

    private static string RepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relativePath)))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, relativePath);
    }
}
