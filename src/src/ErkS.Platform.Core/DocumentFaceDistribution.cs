namespace ErkS.Platform.Core;

/// <summary>
/// How the faces of a scanned document - a planning task, a registration
/// certificate, a design licence - are spread across the album pages that
/// carry them.
///
/// Two rules, both from the client.
///
/// How many fit: "АТД А2 форматад 4 нүүр орж болно" and "Нийтлэг формат A3
/// үед хуудсандаа 2 нүүр л байна." A face has to stay readable, and how small
/// it can go depends on the sheet it is printed on.
///
/// How they are spread: "Хэрэв 5 хуудас байвал 3, 2 хуудсаар салгаж
/// хуудаслана. 6 байвал 3, 3. 7 байвал 4, 3." Not fill-the-first-page, which
/// would give 4 and 1 for five faces and leave a page with one lonely scan
/// beside three empty quarters. The pages come out looking like they were laid
/// out on purpose, because they were.
/// </summary>
public static class DocumentFaceDistribution
{
    /// <summary>A2 landscape, to the nearest millimetre.</summary>
    private const double A2WidthMm = 594.0;

    /// <summary>How many faces fit on one album page of the given size.</summary>
    /// <remarks>
    /// Keyed on the sheet rather than on the album template: the same album can
    /// be printed at either size, and it is the printed size that decides
    /// whether a quarter-page scan can still be read.
    ///
    /// Only the two sizes the client named are here. A third is not guessed
    /// at - it gets the A3 count, which is the conservative one, until someone
    /// says otherwise.
    /// </remarks>
    public static int Capacity(double pageWidthMm, double pageHeightMm)
    {
        double longest = Math.Max(pageWidthMm, pageHeightMm);
        return longest >= A2WidthMm - 1 ? 4 : 2;
    }

    /// <summary>
    /// How many faces go on each album page, in order.
    /// </summary>
    /// <returns>
    /// One entry per album page. Empty when there is nothing to place - the
    /// caller decides whether that still deserves a placeholder page.
    /// </returns>
    public static IReadOnlyList<int> Distribute(int faceCount, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(faceCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (faceCount == 0)
            return [];

        int pages = (faceCount + capacity - 1) / capacity;
        var counts = new List<int>(pages);
        int remaining = faceCount;
        for (int page = pages; page > 0; page--)
        {
            // Each page takes its share of what is left, rounded up, so any
            // odd face lands on an earlier page rather than the last one.
            int take = (remaining + page - 1) / page;
            counts.Add(take);
            remaining -= take;
        }

        return counts;
    }
}
