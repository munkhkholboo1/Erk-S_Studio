namespace ErkS.Studio;

/// <summary>
/// What actually happened when a sync stopped on a conflict, and the sentence
/// that says so.
///
/// 🔴 THE SENTENCE USED TO PROMISE SOMETHING NOTHING HAD DONE. On a project
/// conflict it said «дахин синк хийхэд илгээгдэнэ» - re-sync and it will go up.
/// That is only true if the base actually moved to the server's current one, and
/// the sentence was written where nobody could see whether it had. A person who
/// presses again against an unchanged base meets the same refusal, presses
/// again, and the promise is what keeps them doing it.
///
/// 🔴 AND THE SERVER DOES NOT ALWAYS STATE A BASE. The contract says a 409
/// carries the current revision; a real refusal measured on the owner's machine
/// carried an EMPTY body. So «we rebased» cannot be assumed from the status
/// either - it has to be reported by whoever tried.
///
/// Decision and sentence travel together for the same reason they do in
/// StudioProjectAccessVerdict: separated, they drift, and the sentence is the
/// half a person acts on.
/// </summary>
internal sealed record StudioConflictOutcome(bool BaseMoved, string Sentence)
{
    /// <summary>
    /// The client re-read the server and its base is now current. Pressing
    /// again is a real next step.
    /// </summary>
    public static StudioConflictOutcome Rebased(bool albumConflict) => new(
        true,
        albumConflict
            ? "Sync зогслоо: серверийн альбомын суурь хувилбар өөрчлөгдсөн. " +
              "Таны засвар хэвээр байна, ба суурь нь серверийн одоогийнхоор " +
              "шинэчлэгдлээ — дахин синк хийхэд илгээгдэнэ."
            : "Sync зогслоо: төслийн хувилбар та засварлаж эхэлснээс хойш " +
              "өөрчлөгдсөн (альбом байршуулах зэрэг өөрийн үйлдэл ч үүнийг " +
              "үүсгэдэг). Таны засвар хэвээр байна, дахин бичих шаардлагагүй — " +
              "суурь шинэчлэгдсэн тул дахин синк хийхэд илгээгдэнэ.");

    /// <summary>
    /// The re-read did not happen or did not succeed, so the base is unchanged.
    ///
    /// 🔴 IT MUST NOT SAY «TRY AGAIN». Pressing again against the same base
    /// reproduces the same refusal, and a sentence that recommends it builds the
    /// loop it is describing.
    /// </summary>
    public static StudioConflictOutcome BaseUnchanged(bool albumConflict, string whyMn) => new(
        false,
        (albumConflict
            ? "Sync зогслоо: серверийн альбомын суурь хувилбар өөрчлөгдсөн. "
            : "Sync зогслоо: төслийн хувилбар та засварлаж эхэлснээс хойш өөрчлөгдсөн. ") +
        "Таны засвар хэвээр байна. Суурийг шинэчилж чадсангүй" +
        (string.IsNullOrWhiteSpace(whyMn) ? "" : ": " + whyMn.Trim()) +
        " — дахин дарвал ижил зөрчилдөөн давтагдана. Төслөө хааж нээгээд " +
        "эсвэл сүлжээгээ шалгаад дахин оролдоно уу.");
}
