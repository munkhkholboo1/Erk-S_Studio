using System.Net;
using System.Net.Http;

namespace ErkS.Studio;

/// <summary>
/// A write the server refused, said in words the person acts on: which right is missing, that
/// someone else changed it first, or that the server could not be reached.
/// </summary>
/// <remarks>
/// The owner sent an invitation, the server refused it (403 membership_invitation_forbidden) and
/// the owner understood it was sent (Мастер, 2026-10-31): the refusal went to the status line and
/// the next status line took its place. A refusal is shown in a window, not passed over.
/// </remarks>
internal static class StudioWriteRefusal
{
    public static string Describe(string whatFailedMn, Exception exception)
    {
        string reason = exception switch
        {
            StudioAccountException { StatusCode: HttpStatusCode.Forbidden } =>
                "энэ үйлдэлд төслийн админ эрх шаардлагатай — сервер татгалзлаа.",
            StudioAccountException { StatusCode: HttpStatusCode.Unauthorized } =>
                "нэвтрэлт дууссан байна — дахин нэвтэрнэ үү.",
            StudioAccountException { StatusCode: HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed } =>
                "өөр хэрэглэгч энэ төслийг түрүүлж өөрчилсөн байна — төслөө дахин ачаалаад оролдоно уу.",
            HttpRequestException or TaskCanceledException =>
                "серверт холбогдож чадсангүй — интернэт холболтоо шалгаад дахин оролдоно уу.",
            _ => exception.Message,
        };
        return $"{whatFailedMn}: {reason}";
    }
}
