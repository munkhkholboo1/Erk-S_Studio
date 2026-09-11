namespace ErkS.Studio;

/// <summary>
/// The verdict on a refused project, carrying the decision AND the sentence
/// that agrees with it.
///
/// They travel together because they were separable once and drifted: the
/// sentence said the project had been removed from the person's list while the
/// code closed a workspace over a refusal that meant nothing of the kind.
/// </summary>
/// <param name="ProjectEnded">
/// Whether the server has STATED that this project is over for this account -
/// the only fact that may close an open workspace.
/// </param>
/// <param name="Sentence">What the person is told, true of the decision above.</param>
internal sealed record StudioProjectAccessVerdict(bool ProjectEnded, string Sentence);

/// <summary>
/// What Studio may conclude when the server refuses to hand over a project.
///
/// 🔴 THE FOURTH MEMBER OF A FAMILY THAT HAS COST REAL PEOPLE REAL WORK. Three
/// separate call sites read «Forbidden or NotFound» as «your access has ended»,
/// told the person their project had been removed from their list, and closed
/// the open project. The same sentence, letter for letter, in three files -
/// which is why the rule lives here now and the sites ask instead of deciding.
///
/// 🔴 AND THE SERVER CANNOT SUPPORT THAT CONCLUSION. Measured, not assumed:
/// GET /api/cloud-era/v1/projects/{id} answers `project_not_found` (404) when
/// FindForActor returns null, and it returns null for FOUR different worlds -
/// the project really was deleted, the person's membership ended, a SEATED
/// machine asked about a project its seat was not assigned, and the id was
/// simply wrong. One value carrying four reasons cannot be read as any one of
/// them. The seat case is the common one and it is not an ending at all: the
/// fix is to assign the project to the seat, or to leave bot state.
///
/// So the vocabulary of endings below is EMPTY today. That emptiness is the
/// measurement, not an oversight - the server already owns the discriminator
/// (IsKnownProject, which says in its own words that «not yours» is a refusal
/// and «never heard of it» is silence) and does not yet ask it on this route.
/// The day it does, this list gains a line and the behaviour returns with it.
/// </summary>
internal static class StudioProjectAccessRefusal
{
    /// <summary>
    /// The codes by which the SERVER states that the project has ended for this
    /// account - not that it is unreachable now, from here, by this identity.
    ///
    /// Empty. Every code the project routes can produce today is ambiguous
    /// between «gone» and «not from here», and a client that guesses closes a
    /// workspace somebody is working in.
    /// </summary>
    public static IReadOnlyList<string> CodesThatEndTheProject { get; } = [];

    /// <summary>
    /// Reads a refusal into a verdict.
    ///
    /// A STATUS IS NEVER ENOUGH and is deliberately not a parameter here: 403
    /// and 404 are what an expired token, an unassigned seat, a half-deployed
    /// server and a mistyped id all produce alike. Only a named code decides.
    /// </summary>
    public static StudioProjectAccessVerdict Read(string? code, string? message, bool seatedAsBot)
    {
        string named = (code ?? "").Trim();
        bool ended = false;
        foreach (string ending in CodesThatEndTheProject)
        {
            if (ending.Equals(named, StringComparison.OrdinalIgnoreCase))
            {
                ended = true;
                break;
            }
        }

        string sentence = ended
            ? "Энэ төсөл таны хувьд дууссаныг сервер мэдэгдлээ."
            : "Энэ төслийг сервер одоо нээж чадсангүй.";

        // The server's OWN words, when it used any - asked of the one place that
        // decides it. This used to spell the rule out here as well, and two
        // spellings of «did the server actually speak» are two chances for one
        // of them to start attributing Studio's own sentence to the server.
        if (StudioRefusalSentence.ServerSpoke(named, message))
            sentence += " Серверийн хариу: " + message!.Trim();
        else if (named.Length > 0)
            sentence += " Серверийн код: " + named + ".";

        if (!ended)
        {
            sentence += seatedAsBot
                ? " Энэ төхөөрөмж суудалд байгаа тул зөвхөн суудалдаа " +
                  "томилогдсон төслүүдийг нээнэ. Төслийг суудалд томилуулах, " +
                  "эсвэл ботын төлөвөөс гарч эзнээрээ нэвтрэх боломжтой."
                : " Сүлжээ болон нэвтрэлтээ шалгаад дахин оролдоно уу. Эрх " +
                  "өөрчлөгдсөн бол төслийн эзэнтэй холбогдоно уу.";
        }

        // Said in both worlds, because the sentence this replaced claimed the
        // opposite and people acted on it.
        sentence += ended
            ? " Локал эх файл болон mirror устгагдаагүй."
            : " Төсөл нээлттэй хэвээр, локал эх файл болон mirror хэвээр байна.";

        return new StudioProjectAccessVerdict(ended, sentence);
    }
}
