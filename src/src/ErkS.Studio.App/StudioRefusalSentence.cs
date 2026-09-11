namespace ErkS.Studio;

/// <summary>
/// What the person is told when the server refuses, in one place.
///
/// 🔴 THE SERVER WRITES A BETTER SENTENCE THAN STUDIO CAN. It knows which of
/// four things went wrong behind one status code, it writes in Mongolian, and
/// it names what to do next. Studio catches this exception in seventeen places;
/// ten of them show something to the person, and until now each decided for
/// itself whether the server's words survived. Most replaced them with a
/// sentence of their own, which is how «the server said the project is not
/// assigned to this seat» reached somebody as «your access has ended».
///
/// 🔴 AND A SENTENCE STUDIO WROTE IS NOT «WHAT THE SERVER SAID». When the body
/// carries no error, the client manufactures «Cloud ERA server алдаа: 500 …»
/// and puts it on the same exception property. Presenting that under «Серверийн
/// хариу» tells a person the server said something it never said - the label
/// written by the accused. A code is what a structured error body carries, so a
/// code present is what makes the sentence the server's.
///
/// This generalises BotSeatErrors.Describe, which had the rule right and only
/// for seats.
/// </summary>
internal static class StudioRefusalSentence
{
    /// <summary>
    /// Whether this refusal carries words the SERVER wrote.
    ///
    /// Public because the same question is asked wherever a refusal is shown,
    /// and two spellings of it would drift apart - one of them attributing
    /// Studio's own sentence to the server.
    /// </summary>
    public static bool ServerSpoke(string? code, string? message) =>
        (code ?? "").Trim().Length > 0 && (message ?? "").Trim().Length > 0;

    /// <summary>
    /// 🔴 NO SENTENCE-BUILDER LIVES HERE, AND THAT IS A MEASUREMENT RATHER THAN
    /// AN OVERSIGHT. A first version of this file carried one - Studio's context
    /// plus the server's words, neatly composed - and it was written because it
    /// seemed like what a class with this name should have. Nothing called it.
    ///
    /// The thirty-six places that show a refusal already write
    /// «&lt;what failed&gt;: &lt;the server's sentence&gt;», which reaches the person
    /// with the server's own words intact and attributes nothing falsely. What
    /// they were missing was never a formatter. It was this one rule, spelled
    /// once, so that the places which DO claim the words are the server's have
    /// somewhere to ask.
    /// </summary>
    public static bool ServerSpoke(Exception exception) =>
        exception is StudioAccountException known && ServerSpoke(known.ErrorCode, known.Message);
}
