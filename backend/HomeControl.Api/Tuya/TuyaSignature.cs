using System.Security.Cryptography;
using System.Text;

namespace HomeControl.Api.Tuya;

// Erzeugt die Signatur für Anfragen an die Tuya-Cloud (aktueller Signaturalgorithmus, HMAC-SHA256).
// Quelle: Tuya-Dokumentation "Sign Requests for Cloud Authorization" (new signature).
//
//   stringToSign = HTTP-Methode + "\n" + SHA256(Body) + "\n" + signierte Header + "\n" + URL
//   str          = client_id + access_token + t + nonce + stringToSign
//   sign         = HMAC-SHA256(str, secret), als Hex in Großbuchstaben
//
// Beim Token-Abruf gibt es noch kein access_token; es fällt dann einfach weg.
public static class TuyaSignature
{
    public static string Sign(
        string clientId,
        string secret,
        string? accessToken,
        string timestamp,
        string nonce,
        string method,
        string pathAndQuery,
        string body = "",
        string signedHeaders = "")
    {
        // SHA256 des Bodys als Hex in Kleinbuchstaben. Auch ein leerer Body wird gehasht.
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

        // signedHeaders bleibt bei uns leer. Tuya erlaubt zusätzlich signierte Header
        // ("name:wert\n" je Header); der Parameter erlaubt den Abgleich mit dem Beispiel aus der Doku.
        // pathAndQuery muss die Query-Parameter alphabetisch sortiert enthalten.
        var stringToSign = $"{method}\n{contentHash}\n{signedHeaders}\n{pathAndQuery}";
        var str = clientId + (accessToken ?? string.Empty) + timestamp + nonce + stringToSign;

        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(str));
        return Convert.ToHexString(hash); // Großbuchstaben
    }
}
