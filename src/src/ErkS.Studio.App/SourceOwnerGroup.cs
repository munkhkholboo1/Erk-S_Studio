namespace ErkS.Studio;

/// <summary>
/// Who a design source belongs to, as the source list files it.
/// </summary>
/// <remarks>
/// Sources registered through the cloud carry the email of whoever registered
/// them, so they can be filed under a person. A source that exists only on this
/// device carries no person at all - the record holds an organisation and
/// nothing more - so it is filed under the device instead of being credited to
/// whoever happens to be signed in. Someone else may have added it here, or it
/// may predate the current account; a name on it would be a guess presented as
/// a record.
/// </remarks>
/// <param name="Email">Empty for the device group.</param>
/// <param name="DisplayName">What the heading reads.</param>
/// <param name="Initials">Shown when there is no photograph.</param>
/// <param name="ProfileImageUrl">Empty when they have not set one.</param>
internal sealed record SourceOwnerGroup(
    string Email,
    string DisplayName,
    string Initials,
    string ProfileImageUrl)
{
    public static readonly SourceOwnerGroup ThisDevice =
        new("", "Энэ төхөөрөмж дээр", "", "");

    public bool IsDevice => Email.Length == 0;

    // Grouping compares these, and two records built from different sources for
    // the same person must land in one group. The email is the identity; the
    // name and photograph are just how it is drawn.
    public bool Equals(SourceOwnerGroup? other) =>
        other is not null &&
        string.Equals(Email, other.Email, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Email);
}
