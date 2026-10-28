#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ErkS.Updates;

/// <summary>
/// Why a request to the server failed, in words a member can act on.
/// </summary>
/// <param name="Code">tls | dns | connect | refused | proxy | timeout | other - the coarse class (status.json «serverReason»).</param>
/// <param name="Detail">The precise cause: tls-clock, tls-intercepted, tls-root, tls-name, tls-handshake, … (log, Twin).</param>
/// <param name="Message">Mongolian, with the certificate, the machine's clock and the inner exception chain.</param>
public sealed record NetworkFailureReason(string Code, string Detail, string Message);

/// <summary>
/// One reading of a failed request for Studio, Erk-S Sync and the update path (EE, Master, Erk-S Sync,
/// 2026-10-28: a partner's machine said only «The SSL connection could not be established» while the
/// server was fine - the cause has to show where the member is: wrong clock, antivirus or proxy opening
/// HTTPS, an old root store, DNS). <see cref="CreateHandler"/> also notes the certificate the server
/// presented, so a TLS failure names its issuer, validity and chain status, not only .NET's sentence.
/// </summary>
public static class NetworkFailure
{
    /// <summary>The marker on an exception this handler already explained (it is not explained twice).</summary>
    private const string ReasonKey = "ErkS.NetworkFailure";

    /// <summary>Issuers the server's certificate really comes from; another issuer on a failing chain means something in between.</summary>
    private static readonly string[] KnownIssuers = ["Google Trust Services", "GTS ", "WE1", "WR1", "Cloudflare", "Let's Encrypt", "DigiCert", "Sectigo"];

    private static readonly ConditionalWeakTable<HttpRequestMessage, CertificateSeen> Seen = new();

    /// <summary>
    /// The public roots erk-s.mn's issuers chain to (EE, 4755dc4, 2026-10-28: the partner's Windows had neither
    /// GTS Root R4 nor its GlobalSign cross-sign and could not fetch them on demand - the browser, with its own
    /// roots, opened erk-s.mn; Studio and the installer did not). Used only when Windows could not bring the
    /// chain to a root: the name must still match and the dates still hold.
    /// </summary>
    private static readonly string[] CarriedRootsDer =
    [
        // GTS Root R4 (erk-s.mn behind Cloudflare: WE1)
        "MIICCTCCAY6gAwIBAgINAgPlwGjvYxqccpBQUjAKBggqhkjOPQQDAzBHMQswCQYDVQQGEwJVUzEiMCAGA1UEChMZR29vZ2xlIFRydXN0IFNlcnZpY2VzIExMQzEUMBIGA1UEAxMLR1RTIFJvb3QgUjQwHhcNMTYwNjIyMDAwMDAwWhcNMzYwNjIyMDAwMDAwWjBHMQswCQYDVQQGEwJVUzEiMCAGA1UEChMZR29vZ2xlIFRydXN0IFNlcnZpY2VzIExMQzEUMBIGA1UEAxMLR1RTIFJvb3QgUjQwdjAQBgcqhkjOPQIBBgUrgQQAIgNiAATzdHOnaItgrkO4NcWBMHtLSZ37wWHO5t5GvWvVYRg1rkDdc/eJkTBa6zzuhXyiQHY7qca4R9gq55KRanPpsXI5nymfopjTX15YhmUPoYRlBtHci8nHc8iMai/lxKvRHYqjQjBAMA4GA1UdDwEB/wQEAwIBhjAPBgNVHRMBAf8EBTADAQH/MB0GA1UdDgQWBBSATNbrdP9JNqPV2Py1PsVq8JQdjDAKBggqhkjOPQQDAwNpADBmAjEA6ED/g94D9J+uHXqnLrmvT/aDHQ4thQEd0dlq7A/Cr8deVl5c1RxYIigL9zC2L7F8AjEA8GE8p/SgguMh1YQdc4acLa/KNJvxn7kjNuK8YAOdgLOaVsjh4rsUecrNIdSUtUlD",
        // GTS Root R1
        "MIIFVzCCAz+gAwIBAgINAgPlk28xsBNJiGuiFzANBgkqhkiG9w0BAQwFADBHMQswCQYDVQQGEwJVUzEiMCAGA1UEChMZR29vZ2xlIFRydXN0IFNlcnZpY2VzIExMQzEUMBIGA1UEAxMLR1RTIFJvb3QgUjEwHhcNMTYwNjIyMDAwMDAwWhcNMzYwNjIyMDAwMDAwWjBHMQswCQYDVQQGEwJVUzEiMCAGA1UEChMZR29vZ2xlIFRydXN0IFNlcnZpY2VzIExMQzEUMBIGA1UEAxMLR1RTIFJvb3QgUjEwggIiMA0GCSqGSIb3DQEBAQUAA4ICDwAwggIKAoICAQC2EQKLHuOhd5s73L+UPreVp0A8of2C+X0yBoJx9vaMf/vo27xqLpeXo4xL+Sv2sfnOhB2x+cWX3u+58qPpvBKJXqeqUqv4IyfLpLGcY9vXmX7wCl7raKb0xlpHDU0QM+NOsROjyBhsS+z8CZDfnWQpJSMHobTSPS5g4M/SCYe7zUjwTcLCeoiKu7rPWRnWr4+wB7CeMfGCwcDfLqZtbBkOtdh+JhpFAz2weaSUKK0PfyblqAj+lug8aJRT7oM6iCsVlgmy4HqMLnXWnOunVmSPlk9orj2XwoSPwLxAwAtcvfaHszVsrBhQf4TgTM2S0yDpM7xSma8ytSmzJSq0SPly4cpk9+aCEI3oncKKiPo4Zor8Y/kB+Xj9e1x3+naH+uzfsQ55lVe0vSbv1gHR6xYKu44LtcXFilWr06zqkUspzBmkMiVOKvFlRNACzqrOSbTqn3yDsEB750Orp2yjj32JgfpMpf/VjsPOS+C12LOORc92wO1AK/1TD7Cn1TsNsYqiA94xrcx36m97PtbfkSIS5r762DL8EGMUUXLeXdYWk70paDPvOmbsB4om3xPXV2V4J95eSRQAogB/mqghtqmxlbCluQ0WEdrHbEg8QOB+DVrNVjzRlwW5y0vtOUucxD/SVRNuJLDWcfr0wbrM7Rv1/oFB2ACYPTrIrnqYNxgFlQIDAQABo0IwQDAOBgNVHQ8BAf8EBAMCAYYwDwYDVR0TAQH/BAUwAwEB/zAdBgNVHQ4EFgQU5K8rJnEaK0gnhS9SZizv8IkTcT4wDQYJKoZIhvcNAQEMBQADggIBAJ+qQibbC5u+/x6Wki4+omVKapi6Ist9wTrYggoGxval3sBOh2Z5ofmmWJyq+bXmYOfg6LEeQkEzCzc9zolwFcq1JKjPa7XSQCGYzyI0zzvFIoTgxQ6KfF2I5DUkzps+GlQebtuyh6f88/qBVRRiClmpIgUxPoLW7ttXNLwzldMXG+gnoot7TiYaelpkttGsN/H9oPM47HLwEXWdyzRSjeZ2axfG34arJ45JK3VmgRAhpuo+9K4l/3wV3s6MJT/KYnAK9y8JZgfIPxz88NtFMN9iiMG1D53Dn0reWVlHxYciNuaCp+0KueIHoI17eko8cdLiA6EfMgfdG+RCzgwARWGAtQsgWSl4vflVy2PFPEz0tv/bal8xa5meLMFrUKTX5hgUvYU/Z6tGn6D/Qqc6f1zLXbBwHSs09dR2CQzreExZBfMzQsNhFRAbd03OIozUhfJFfbdT6u9AWpQKXCBfTkBdYiJ23//OYb2MI3jSNwLgjt7RETeJ9r/tSQdirpLsQBqvFAnZ0E6yove+7u7Y/9waLd64NnHi/Hm3lCXRSHNboTXns5lndcEZOitHTtNCjv0xyBZm2tIMPNuzjsmhDYAPexZ3FL//2wmUspO8IFgV6dtxQ/PeEMMA3KgqlbbC1j+Qa3bbbP6MvPJwNQzcmRk13NfIRmPVNnGuV/u3gm3c",
        // GlobalSign Root CA (cross-signs GTS Root R4)
        "MIIDdTCCAl2gAwIBAgILBAAAAAABFUtaw5QwDQYJKoZIhvcNAQEFBQAwVzELMAkGA1UEBhMCQkUxGTAXBgNVBAoTEEdsb2JhbFNpZ24gbnYtc2ExEDAOBgNVBAsTB1Jvb3QgQ0ExGzAZBgNVBAMTEkdsb2JhbFNpZ24gUm9vdCBDQTAeFw05ODA5MDExMjAwMDBaFw0yODAxMjgxMjAwMDBaMFcxCzAJBgNVBAYTAkJFMRkwFwYDVQQKExBHbG9iYWxTaWduIG52LXNhMRAwDgYDVQQLEwdSb290IENBMRswGQYDVQQDExJHbG9iYWxTaWduIFJvb3QgQ0EwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQDaDuaZjc6j40+Kfvvxi4Mla+pIH/EqsLmVEQS98GPR4mdmzxzdzxtIK+6NiY6arymAZavpxy0Sy6scTHAHoT0KMM0VjU/43dSMUBUc71DuxC73/OlS8pF94G3VNTCOXkNz8kHp1Wrjsok6Vjk4bwY8iGlbKk3Fp1S4bInMm/k8yuX9ifUSPJJ4ltbcdG6TRGHRjcdGsnUOhugZitVtbNV4FpWi6cgKOOvyJBNPc1STE4U6G7weNLWLBYy5d4ux2x8gkasJU26Qzns3dLlwR5EiUWMWea6xrkEmCMgZK9FGqkjWZCrXgzT/LCrBbBlDSgeF59N89iFo7+ryUp9/k5DPAgMBAAGjQjBAMA4GA1UdDwEB/wQEAwIBBjAPBgNVHRMBAf8EBTADAQH/MB0GA1UdDgQWBBRge2YaRQ2XyolQL30EzTSo//z9SzANBgkqhkiG9w0BAQUFAAOCAQEA1nPnfE920I2/7LqivjTFKDK1fPxsnCwrvQmeU79rXqoRSLblCKOzyj1hTdNGCbM+w6DjY1Ub8rrvrTnhQ7k4o+YviiY776BQVvnGCv04zcQLcFGUl5gE38NflNUVyRRBnMRddWQVDf9VMOyGj/8N7yy5Y0b2qvzfvGn9LhJIZJrglfCm7ymPAbEVtQwdpf5pLGkkeB6zpxxxYu7KyJesF12KwvhHhm4qxFYxldBniYUr+WymXUadDKqC5JlR3XC321Y9YeRq4VzW9v493kHMB65jUr9TU/Qr6cf9tveCX4XSQRjbgbMEHMUfpIBvFSDJ3gyICh3WZlXi/EjJKSZp4A==",
        // ISRG Root X1 (Let's Encrypt, Cloudflare's other issuer)
        "MIIFazCCA1OgAwIBAgIRAIIQz7DSQONZRGPgu2OCiwAwDQYJKoZIhvcNAQELBQAwTzELMAkGA1UEBhMCVVMxKTAnBgNVBAoTIEludGVybmV0IFNlY3VyaXR5IFJlc2VhcmNoIEdyb3VwMRUwEwYDVQQDEwxJU1JHIFJvb3QgWDEwHhcNMTUwNjA0MTEwNDM4WhcNMzUwNjA0MTEwNDM4WjBPMQswCQYDVQQGEwJVUzEpMCcGA1UEChMgSW50ZXJuZXQgU2VjdXJpdHkgUmVzZWFyY2ggR3JvdXAxFTATBgNVBAMTDElTUkcgUm9vdCBYMTCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIBAK3oJHP0FDfzm54rVygch77ct984kIxuPOZXoHj3dcKi/vVqbvYATyjb3miGbESTtrFj/RQSa78f0uoxmyF+0TM8ukj13Xnfs7j/EvEhmkvBioZxaUpmZmyPfjxwv60pIgbz5MDmgK7iS4+3mX6UA5/TR5d8mUgjU+g4rk8Kb4Mu0UlXjIB0ttov0DiNewNwIRt18jA8+o+u3dpjq+sWT8KOEUt+zwvo/7V3LvSye0rgTBIlDHCNAymg4VMk7BPZ7hm/ELNKjD+Jo2FR3qyHB5T0Y3HsLuJvW5iB4YlcNHlsdu87kGJ55tukmi8mxdAQ4Q7e2RCOFvu396j3x+UCB5iPNgiV5+I3lg02dZ77DnKxHZu8A/lJBdiB3QW0KtZB6awBdpUKD9jf1b0SHzUvKBds0pjBqAlkd25HN7rOrFleaJ1/ctaJxQZBKT5ZPt0m9STJEadao0xAH0ahmbWnOlFuhjuefXKnEgV4We0+UXgVCwOPjdAvBbI+e0ocS3MFEvzG6uBQE3xDk3SzynTnjh8BCNAw1FtxNrQHusEwMFxIt4I7mKZ9YIqioymCzLq9gwQbooMDQaHWBfEbwrbwqHyGO0aoSCqI3Haadr8faqU9GY/rOPNk3sgrDQoo//fb4hVC1CLQJ13hef4Y53CIrU7m2Ys6xt0nUW7/vGT1M0NPAgMBAAGjQjBAMA4GA1UdDwEB/wQEAwIBBjAPBgNVHRMBAf8EBTADAQH/MB0GA1UdDgQWBBR5tFnme7bl5AFzgAiIyBpY9umbbjANBgkqhkiG9w0BAQsFAAOCAgEAVR9YqbyyqFDQDLHYGmkgJykIrGF1XIpu+ILlaS/V9lZLubhzEFnTIZd+50xx+7LSYK05qAvqFyFWhfFQDlnrzuBZ6brJFe+GnY+EgPbk6ZGQ3BebYhtF8GaV0nxvwuo77x/Py9auJ/GpsMiu/X1+mvoiBOv/2X/qkSsisRcOj/KKNFtY2PwByVS5uCbMiogziUwthDyC3+6WVwW6LLv3xLfHTjuCvjHIInNzktHCgKQ5ORAzI4JMPJ+GslWYHb4phowim57iaztXOoJwTdwJx4nLCgdNbOhdjsnvzqvHu7UrTkXWStAmzOVyyghqpZXjFaH3pO3JLF+l+/+sKAIuvtd7u+Nxe5AW0wdeRlN8NwdCjNPElpzVmbUq4JUagEiuTDkHzsxHpFKVK7q4+63SM1N95R1NbdWhscdCb+ZAJzVcoyi3B43njTOQ5yOf+1CceWxG1bQVs5ZufpsMljq4Ui0/1lvh+wjChP4kqKOJ2qxq4RgqsahDYVvTH9w7jXbyLeiNdd8XM2w9U/t7y0Ff/9yi0GE44Za4rF2LN9d11TPAmRGunUHBcnWEvgJBQl9nJEiU0Zsnvgc/ubhPgXRR4Xq37Z0j4r7g1SgEEzwxA57demyPxgcYxn/eR44/KJ4EBs+lVDR3veyJm+kXQ99b21/+jh5Xos1AnX5iItreGCc=",
    ];

    private static readonly X509Certificate2Collection CarriedRoots = new(CarriedRootsDer.Select(der => Load(Convert.FromBase64String(der))).ToArray());

    /// <summary>
    /// The handler every server client of Studio and SYN is built on: the platform's ordinary validation
    /// (a revocation server that cannot be reached is not a reason to refuse - .NET does not check it by
    /// default either), the certificate noted, and a failure rethrown with its reason as the message.
    /// </summary>
    public static HttpMessageHandler CreateHandler()
    {
        var inner = new HttpClientHandler
        {
            CheckCertificateRevocationList = false,
            ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
            {
                bool accepted = errors == SslPolicyErrors.None || OnlyRevocationUnknown(errors, chain) || ChainsToCarriedRoot(errors, certificate, chain);
                if (!accepted && certificate is not null)
                    Seen.AddOrUpdate(request, CertificateSeen.Of(certificate, chain, errors));
                return accepted;
            },
        };
        return new ExplainingHandler(inner);
    }

    /// <summary>A client on <see cref="CreateHandler"/>.</summary>
    public static HttpClient CreateClient(TimeSpan timeout, Uri? baseAddress = null)
    {
        var client = new HttpClient(CreateHandler(), disposeHandler: true) { Timeout = timeout };
        if (baseAddress is not null)
            client.BaseAddress = baseAddress;
        return client;
    }

    /// <summary>The reason for any failure of a request (explained here already, or not).</summary>
    public static NetworkFailureReason Explain(Exception exception, string host = "")
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current.Data[ReasonKey] is NetworkFailureReason told)
                return told;
        return Classify(exception, host, null);
    }

    /// <summary>Whether the failure is the connection itself (TLS, DNS, socket, timeout) rather than an answer from the server.</summary>
    public static bool IsConnection(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or TimeoutException or SocketException or AuthenticationException ||
        exception.InnerException is { } inner && IsConnection(inner);

    private static NetworkFailureReason Classify(Exception exception, string host, CertificateSeen? certificate)
    {
        string where = host.Length > 0 ? host : "сервер";
        string chain = Chain(exception);
        if (exception is TaskCanceledException or TimeoutException || Find<TimeoutException>(exception) is not null)
            return new("timeout", "timeout", $"{where} хугацаандаа хариу өгсөнгүй (хугацаа хэтэрсэн). Интернэт удаан эсвэл тасалдсан байж болно.");

        HttpRequestError? error = (exception as HttpRequestException ?? Find<HttpRequestException>(exception))?.HttpRequestError;
        AuthenticationException? authentication = Find<AuthenticationException>(exception);
        if (certificate is not null || authentication is not null || error == HttpRequestError.SecureConnectionError)
            return Tls(where, certificate, chain);

        SocketException? socket = Find<SocketException>(exception);
        if (error == HttpRequestError.NameResolutionError || socket?.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain)
            return new("dns", "dns", $"{where} хаягийг олсонгүй (DNS). Интернэт холболт эсвэл DNS тохиргоог шалгана уу. Дэлгэрэнгүй: {chain}");
        if (error == HttpRequestError.ProxyTunnelError)
            return new("proxy", "proxy", $"Прокси сервер {where} руу холбогдохыг зөвшөөрсөнгүй. Windows-ийн прокси тохиргоог шалгана уу. Дэлгэрэнгүй: {chain}");
        if (socket?.SocketErrorCode == SocketError.ConnectionRefused)
            return new("refused", "refused", $"{where} холболтыг татгалзлаа (порт хаалттай эсвэл firewall). Дэлгэрэнгүй: {chain}");
        if (socket?.SocketErrorCode == SocketError.TimedOut)
            return new("timeout", "connect-timeout", $"{where} руу холбогдох хугацаа хэтэрлээ (сүлжээ эсвэл firewall). Дэлгэрэнгүй: {chain}");
        if (error is HttpRequestError.ConnectionError || socket is not null)
            return new("connect", "connect", $"{where} руу холбогдсонгүй (сүлжээ эсвэл firewall). Дэлгэрэнгүй: {chain}");
        return new("other", "other", $"{where}-тэй харилцахад алдаа гарлаа. Дэлгэрэнгүй: {chain}");
    }

    private static NetworkFailureReason Tls(string where, CertificateSeen? certificate, string chain)
    {
        var text = new StringBuilder($"{where}-тэй аюулгүй (SSL) холболт тогтсонгүй.");
        string detail = "tls-handshake";
        DateTime now = DateTime.Now;
        if (certificate is not null)
        {
            text.Append($" Гэрчилгээ: олгогч «{certificate.Issuer}», хүчинтэй {certificate.NotBefore:yyyy-MM-dd} – {certificate.NotAfter:yyyy-MM-dd}; энэ компьютерийн цаг {now:yyyy-MM-dd HH:mm}.");
            if (certificate.Status.Length > 0)
                text.Append($" Алдаа: {certificate.Errors}; {certificate.Status}.");
            else
                text.Append($" Алдаа: {certificate.Errors}.");
            bool clockOutside = now < certificate.NotBefore || now > certificate.NotAfter;
            if (clockOutside || certificate.Status.Contains(nameof(X509ChainStatusFlags.NotTimeValid), StringComparison.Ordinal))
            {
                detail = "tls-clock";
                text.Append(" Шалтгаан: компьютерийн огноо/цаг буруу байна — Windows-ийн «Огноо, цаг» дотор цагийг автоматаар тааруулахыг асаана уу.");
            }
            else if (certificate.Errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            {
                detail = "tls-name";
                text.Append($" Шалтгаан: гэрчилгээ {where}-д олгогдоогүй — прокси, DNS эсвэл сүлжээний төхөөрөмж өөр сервер рүү чиглүүлж байна.");
            }
            else if (!KnownIssuers.Any(known => certificate.Issuer.Contains(known, StringComparison.OrdinalIgnoreCase)))
            {
                detail = "tls-intercepted";
                text.Append($" Шалтгаан: HTTPS-ийг «{certificate.Issuer}» завсарлаж байна (антивирус эсвэл прокси) — тэр программын HTTPS/SSL шалгалтыг {where}-д унтраах эсвэл үл хамаарах жагсаалтад нэмнэ үү.");
            }
            else
            {
                detail = "tls-root";
                text.Append(" Шалтгаан: Windows-ийн root гэрчилгээ хуучирсан эсвэл дутуу — Windows Update хийнэ үү.");
            }
        }
        else
        {
            text.Append($" Боломжит шалтгаан: компьютерийн огноо/цаг буруу (одоо {now:yyyy-MM-dd HH:mm}); антивирус эсвэл прокси HTTPS-ийг завсарлаж байна; Windows-ийн root гэрчилгээ хуучирсан.");
        }
        text.Append(" Дэлгэрэнгүй: ").Append(chain);
        return new("tls", detail, text.ToString());
    }

    /// <summary>Every message down the inner chain, once each: .NET's outer sentence alone says nothing.</summary>
    public static string Chain(Exception exception)
    {
        var parts = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string message = current.Message.Trim();
            if (message.Length > 0 && !parts.Contains(message))
                parts.Add(message);
        }
        return string.Join(" → ", parts);
    }

    private static T? Find<T>(Exception exception) where T : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is T found)
                return found;
        return null;
    }

    /// <summary>A chain whose only fault is that revocation could not be checked (offline CRL/OCSP) is the platform's default «accept».</summary>
    private static bool OnlyRevocationUnknown(SslPolicyErrors errors, X509Chain? chain)
    {
        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || chain is null || chain.ChainStatus.Length == 0)
            return false;
        const X509ChainStatusFlags revocation = X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation;
        return chain.ChainStatus.All(status => (status.Status & ~revocation) == 0);
    }

    /// <summary>A chain Windows could not finish, built again against <see cref="CarriedRoots"/> (and the intermediates the server sent).</summary>
    private static bool ChainsToCarriedRoot(SslPolicyErrors errors, X509Certificate2? certificate, X509Chain? chain)
    {
        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || certificate is null)
            return false;
        using var own = new X509Chain();
        own.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        own.ChainPolicy.CustomTrustStore.AddRange(CarriedRoots);
        own.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (chain is not null)
        {
            own.ChainPolicy.ExtraStore.AddRange(chain.ChainPolicy.ExtraStore);
            foreach (X509ChainElement element in chain.ChainElements)
                own.ChainPolicy.ExtraStore.Add(element.Certificate);
        }
        return own.Build(certificate);
    }

    private static X509Certificate2 Load(byte[] der) =>
#if NET9_0_OR_GREATER
        X509CertificateLoader.LoadCertificate(der);
#else
        new X509Certificate2(der);
#endif

    private sealed class ExplainingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (exception.Data[ReasonKey] is null)
            {
                Seen.TryGetValue(request, out CertificateSeen? certificate);
                NetworkFailureReason reason = Classify(exception, request.RequestUri?.Host ?? "", certificate);
                var explained = new HttpRequestException(exception.HttpRequestError, reason.Message, exception, exception.StatusCode);
                explained.Data[ReasonKey] = reason;
                throw explained;
            }
        }
    }

    private sealed record CertificateSeen(string Issuer, DateTime NotBefore, DateTime NotAfter, SslPolicyErrors Errors, string Status)
    {
        public static CertificateSeen Of(X509Certificate2 full, X509Chain? chain, SslPolicyErrors errors)
        {
            string issuer = full.GetNameInfo(X509NameType.SimpleName, forIssuer: true);
            string status = chain is null
                ? ""
                : string.Join(", ", chain.ChainStatus.Select(item => item.Status.ToString()).Distinct());
            return new(issuer.Length > 0 ? issuer : full.Issuer, full.NotBefore, full.NotAfter, errors, status);
        }
    }

    /// <summary>
    /// Looks at the connection to <paramref name="server"/> from this machine (STU bridge «net-diag»):
    /// clock, proxy, DNS, and the certificate the TLS handshake sees - whatever the outcome.
    /// </summary>
    public static async Task<string> DiagnoseAsync(Uri server, CancellationToken cancellationToken)
    {
        var report = new StringBuilder();
        report.AppendLine($"Цаг: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (UTC {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}, {TimeZoneInfo.Local.Id})");
        Uri? proxy = HttpClient.DefaultProxy.GetProxy(server);
        report.AppendLine(proxy is null || proxy == server ? "Прокси: байхгүй" : $"Прокси: {proxy}");
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(server.Host, cancellationToken).ConfigureAwait(false);
            report.AppendLine($"DNS: {server.Host} → {string.Join(", ", addresses.Select(address => address.ToString()))}");
        }
        catch (SocketException exception)
        {
            report.AppendLine($"DNS: {server.Host} олдсонгүй ({exception.SocketErrorCode})");
        }
        using HttpClient client = CreateClient(TimeSpan.FromSeconds(20));
        try
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(server, "/health"), cancellationToken).ConfigureAwait(false);
            report.AppendLine($"Холболт: OK (HTTP {(int)response.StatusCode})");
        }
        catch (Exception exception) when (IsConnection(exception))
        {
            NetworkFailureReason reason = Explain(exception, server.Host);
            report.AppendLine($"Холболт: алдаа [{reason.Code}/{reason.Detail}]");
            report.AppendLine(reason.Message);
        }
        return report.ToString().TrimEnd();
    }
}
#endif
