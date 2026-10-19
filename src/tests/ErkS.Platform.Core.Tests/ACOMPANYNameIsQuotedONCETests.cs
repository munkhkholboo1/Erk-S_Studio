using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The representative line on a cover: «"Компани" Гүйцэтгэх захирал».
///
/// 🔴 FOUND BY GENERATING A REAL ALBUM, NOT BY READING CODE. The Batnasan
/// working-drawing cover, built by Studio on 2026-10-19, printed
/// «""Алтанхөхийн Очир" ХХК Гүйцэтгэх Захирал»: the writer wrapped the company
/// name in quotes, and the name it wrapped already carried its own - Mongolian
/// company names are stored as «"Нэр" ХХК». Every project whose company is
/// registered that way got the doubled quote, on both the concept and the
/// working-drawing cover.
///
/// The owner's hand-made Revit cover writes the short name in quotes and the
/// role after it, without the legal form: «"Erk-S" Гүйцэтгэх Захирал». That is
/// the form this holds.
/// </summary>
public sealed class ACOMPANYNameIsQuotedONCETests
{
    private const string Role = "Гүйцэтгэх захирал";

    [Fact]
    public void TheShortNameIsQuotedOnceAndTheLegalFormLeftOut()
    {
        var company = new CompanyProfile
        {
            Name = "\"Алтанхөхийн Очир\" ХХК",
            DisplayName = "\"Алтанхөхийн Очир\" ХХК",
            ShortName = "Алтанхөхийн Очир",
        };

        Assert.Equal(
            "\"Алтанхөхийн Очир\" Гүйцэтгэх захирал",
            PdfSharpAlbumWriter.CompanyRepresentativeLine(company, "", Role));
    }

    [Fact]
    public void WithoutAShortNameTheQuotedPartOfTheNameIsUsed()
    {
        // The case the cover actually hit: a name carrying its own quotes and
        // no short name to fall back on.
        var company = new CompanyProfile { DisplayName = "\"Алтанхөхийн Очир\" ХХК" };

        Assert.Equal(
            "\"Алтанхөхийн Очир\" Гүйцэтгэх захирал",
            PdfSharpAlbumWriter.CompanyRepresentativeLine(company, "", Role));
    }

    [Theory]
    [InlineData("«Эрк-С» ХХК")]
    [InlineData("“Эрк-С” ХХК")]
    [InlineData("„Эрк-С“ ХХК")]
    public void EveryQuoteStyleMongolianNamesUseIsRecognised(string name)
    {
        var company = new CompanyProfile { DisplayName = name };

        Assert.Equal(
            "\"Эрк-С\" Гүйцэтгэх захирал",
            PdfSharpAlbumWriter.CompanyRepresentativeLine(company, "", Role));
    }

    [Fact]
    public void AnUnquotedNameIsQuotedOnce()
    {
        // Positive control: the case the old code was written for still works.
        var company = new CompanyProfile { DisplayName = "Эрк-С" };

        Assert.Equal(
            "\"Эрк-С\" Гүйцэтгэх захирал",
            PdfSharpAlbumWriter.CompanyRepresentativeLine(company, "", Role));
    }

    [Fact]
    public void WithNoCompanyNameOnlyTheRoleIsPrinted()
    {
        Assert.Equal(
            Role,
            PdfSharpAlbumWriter.CompanyRepresentativeLine(new CompanyProfile(), "", Role));
    }

    [Fact]
    public void NoQuoteCharacterIsEVERPrintedTwiceInARow()
    {
        // The defect in its own words, independent of any single input.
        foreach (string name in new[]
                 {
                     "\"А\" ХХК", "А", "«А» ХХК", "“А” ХХК", "\"\"А\"\" ХХК",
                 })
        {
            string line = PdfSharpAlbumWriter.CompanyRepresentativeLine(
                new CompanyProfile { DisplayName = name }, "", Role);
            Assert.DoesNotContain("\"\"", line, StringComparison.Ordinal);
        }
    }
}
