using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The address a sheet prints, now COMPOSED from the chosen location rather than
/// typed a second time.
///
/// The user's question was the design review: «эндээс байршлаа сонгоход төслийн
/// хаяг өөрөө үүсэх ёстой. гэтэл давхар байх шаардлага юм байна вэ?» Two fields
/// holding the same fact are not redundancy - they are two sources of truth, and
/// the first time they disagree nothing can say which the drawing should
/// believe.
/// </summary>
public sealed class ProjectSiteAddressTests
{
    private static ProjectSiteLocation Chosen() => new()
    {
        ProvinceCode = "511",
        ProvinceName = "Улаанбаатар",
        DistrictCode = "51101",
        DistrictName = "Багануур",
        WardCode = "5110151",
        WardName = "1-р хороо",
        WardLabelMn = "Хороо",
    };

    [Fact]
    public void ACHOSENLocationComposesTheAddressByItself()
    {
        Assert.Equal("Улаанбаатар хот, Багануур дүүрэг, 1-р хороо", ProjectSiteAddress.Compose(Chosen(), ""));
        Assert.Equal("Улаанбаатар хот, Багануур дүүрэг, 1-р хороо", ProjectSiteAddress.Compose(Chosen(), "   "));
    }

    [Fact]
    public void THEAdditionalLineIsADDEDNeverSubstituted()
    {
        // Master's condition. If a typed line could override the chosen units,
        // the sheet and the concurring-body suggestions would read different
        // places out of one project - and the suggestion is what puts another
        // organisation's name on a signed document.
        Assert.Equal(
            "Улаанбаатар хот, Багануур дүүрэг, 1-р хороо, Их сургуулийн гудамж 12",
            ProjectSiteAddress.Compose(Chosen(), "Их сургуулийн гудамж 12"));
    }

    [Fact]
    public void APROJECTThatPredatesThePickerPrintsExactlyWhatItPrintedYesterday()
    {
        // 🔴 THE COMPATIBILITY THAT MATTERS. Three of the twenty-four projects
        // on disk carry a typed address and no chosen location. Composing must
        // not touch them - a released album that changes its own address line
        // because the program was updated is a document its owner never saw.
        const string typed = "Улаанбаатар хот, Сонгинохайрхан дүүрэг, 34-р хороо, Баянгол";

        Assert.Equal(typed, ProjectSiteAddress.Compose(new ProjectSiteLocation(), typed));
        Assert.Equal(typed, ProjectSiteAddress.Compose(null, typed));
    }

    [Fact]
    public void APARTIALChoiceCOMPOSESWhatItHas()
    {
        // 🔴 THIS RULE WAS REVERSED, DELIBERATELY, AND THE OLD REASONING IS
        // WORTH KEEPING. It used to read: "half a chain names a region, not a
        // site, and printing «Улаанбаатар» as the whole address would be worse
        // than printing what the person typed."
        //
        // The shared vectors settle it the other way - «province-only» must
        // print «Орхон аймаг» - and the reasoning above had a hole: the typed
        // line is NOT the whole address either, and dropping the chosen
        // province threw away a fact in order to avoid an incomplete one. The
        // two are now printed together, which loses nothing.
        //
        // What did NOT change is the case that reasoning was really protecting:
        // an INCONSISTENT chain still prints nothing at all, because a district
        // outside its province names a place that does not exist. That is
        // covered by ADistrictOutsideItsProvinceIsKEPTAndREFUSED.
        var half = new ProjectSiteLocation
        {
            ProvinceCode = "511",
            ProvinceName = "Улаанбаатар",
        };

        Assert.False(half.IsChosen);
        Assert.Equal("Улаанбаатар хот, гудамж 5", ProjectSiteAddress.Compose(half, "гудамж 5"));
    }

    [Fact]
    public void NOTHINGChosenAndNothingTypedIsEmptyRatherThanAComma()
    {
        Assert.Equal("", ProjectSiteAddress.Compose(new ProjectSiteLocation(), ""));
        Assert.Equal("", ProjectSiteAddress.Compose(null, null));
    }

    [Fact]
    public void THEUserIsTOLDWhenTheirTypedLineBecomesTheAdditionalOne()
    {
        // A field quietly changing meaning under somebody is how they stop
        // trusting the fields that did not. This fires once - on the save where
        // a location first completes - and only when there is typed text to
        // move.
        const string typed = "Улаанбаатар хот, Баянгол дүүрэг, 29-р хороо";

        Assert.True(ProjectSiteAddress.TypedAddressBecomesAdditional(
            new ProjectSiteLocation(), Chosen(), typed));

        // Not on later saves - the location was already chosen.
        Assert.False(ProjectSiteAddress.TypedAddressBecomesAdditional(
            Chosen(), Chosen(), typed));

        // Not when there is nothing to move.
        Assert.False(ProjectSiteAddress.TypedAddressBecomesAdditional(
            new ProjectSiteLocation(), Chosen(), ""));

        // Not on a half-made choice, which does not compose anything yet.
        Assert.False(ProjectSiteAddress.TypedAddressBecomesAdditional(
            new ProjectSiteLocation(),
            new ProjectSiteLocation { ProvinceCode = "511", ProvinceName = "Улаанбаатар" },
            typed));

        Assert.Contains("Нэмэлт хаяг", ProjectSiteAddress.TypedAddressMovedNoticeMn, StringComparison.Ordinal);
    }

    [Fact]
    public void EVERYSheetThatPrintsAnAddressASKSTheComposer()
    {
        // 🔴 THE HALF THAT WAS MISSING, found by sabotage: putting the raw typed
        // field back into the concept cover broke NO test. The rule was right
        // and no caller was checked - the shape this codebase has caught itself
        // in repeatedly, and the reason a mutation has to be aimed at the CALL
        // SITE and not only at the rule.
        //
        // Six places print the site address. All six go through Compose, so a
        // project whose address comes from its chosen location prints the same
        // line on every sheet rather than on whichever ones were remembered.
        string writer = ReadPdfSource("PdfSharpAlbumWriter.cs");
        string cover = ReadPdfSource("PdfSharpAlbumWriter.ConceptCover2026.cs");

        Assert.Equal(0, RawReads(writer));
        Assert.Equal(0, RawReads(cover));
        Assert.True(
            Occurrences(writer + cover, "ProjectSiteAddress.Compose(") >= 5,
            "every sheet that prints an address should compose it");
    }

    /// <summary>
    /// Reads of the typed field that do NOT go through the composer. Counted by
    /// stripping the composed calls first, so the check cannot be satisfied by a
    /// call that merely mentions the field.
    /// </summary>
    private static int RawReads(string source)
    {
        string withoutComposed = source.Replace(
            "ProjectSiteAddress.Compose(",
            "COMPOSED(",
            StringComparison.Ordinal);
        int raw = 0;
        foreach (string reader in new[]
                 {
                     "project.InitiationBasis.SiteAddress",
                     "request.Project.InitiationBasis.SiteAddress",
                     "basis.SiteAddress",
                 })
        {
            foreach (int index in IndexesOf(withoutComposed, reader))
            {
                // A read inside a COMPOSED(...) argument list is fine; one
                // outside it is the raw field reaching a sheet.
                int composed = withoutComposed.LastIndexOf("COMPOSED(", index, StringComparison.Ordinal);
                int closing = composed < 0 ? -1 : withoutComposed.IndexOf(')', composed);
                if (composed < 0 || closing < index)
                    raw++;
            }
        }

        return raw;
    }

    private static IEnumerable<int> IndexesOf(string text, string needle)
    {
        for (int index = text.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            yield return index;
        }
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static string ReadPdfSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Platform.Pdf", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, System.Text.Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }

    [Fact]
    public void THETypedTextIsNEVERParsedIntoCodes()
    {
        // Deliberately not attempted. «…34-р хороо, Баянгол» would parse three
        // levels and leave a fourth word unexplained, and 31% of unit names
        // repeat across the country - so a parse assigns the wrong unit sooner
        // rather than later, and does it silently.
        const string typed = "Улаанбаатар хот, Сонгинохайрхан дүүрэг, 34-р хороо, Баянгол";
        string composed = ProjectSiteAddress.Compose(Chosen(), typed);

        // Both halves survive, whole and unedited - including the duplication,
        // which the person removes because only they know what they meant.
        Assert.Contains("Багануур", composed, StringComparison.Ordinal);
        Assert.Contains(typed, composed, StringComparison.Ordinal);
    }
}
