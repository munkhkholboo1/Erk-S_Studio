using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The visualisation store is swept by REFERENCE, never by age.
///
/// 🔴 THE OTHER FIX CREATED THIS NEED. The store names each copy by the SHA-256 of
/// its content, so overwriting a render makes a NEW file and leaves the old one -
/// and the owner's way of working is to overwrite renders repeatedly: «тэр
/// хангалтгүй хэмжээнд байгаа зурагнуудаа сайжруулсаар байх болно». At 26 images of
/// about 68 MB each, a round adds roughly 1.8 GB. Until the album started noticing
/// overwritten renders that growth never happened, so the fix that made the owner's
/// iteration work is exactly what makes this necessary.
///
/// 🔴 AN AGE RULE WOULD BE WRONG IN BOTH DIRECTIONS: it would delete a copy still
/// on the sheet for being old, and keep this morning's orphan.
/// </summary>
public sealed class THEORPHANIsFoundByReferenceNotAgeTests
{
    private const string Store = "sources/visualizations/images/";

    [Fact]
    public void ANUNREFERENCEDCopyIsTheOnlyThingRemoved()
    {
        VisualizationStoreSweep sweep = VisualizationStoreCleanup.Plan(
            [Store + "aaa.png", Store + "bbb.png", Store + "ccc.png"],
            [Store + "aaa.png", Store + "ccc.png"]);

        Assert.Equal("", sweep.RefusalMn);
        Assert.Equal(Store + "bbb.png", Assert.Single(sweep.OrphanRelativePaths));
        Assert.Equal(2, sweep.KeptCount);
        Assert.True(sweep.WillRemoveAnything);
    }

    [Fact]
    public void ANEMPTYReferenceSetREMOVESNOTHINGAndSaysWhy()
    {
        // 🔴 THE ONE ANSWER THAT MUST NEVER BE «DELETE EVERYTHING». A project whose
        // visualisation source carries another project's id answers with an EMPTY
        // image list rather than an error - ImagesForProject returns [] on a
        // mismatch - so «nothing referenced» is reachable from an ordinary
        // mismatch, and on that answer a sweep would remove every render the owner
        // has.
        VisualizationStoreSweep sweep = VisualizationStoreCleanup.Plan(
            [Store + "aaa.png", Store + "bbb.png"],
            []);

        Assert.Empty(sweep.OrphanRelativePaths);
        Assert.False(sweep.WillRemoveAnything);
        Assert.Equal(2, sweep.KeptCount);

        // 🔴 THE SENTENCE IS ASSERTED FOR CONTENT, NOT AGAINST ITSELF. Written as
        // «equals NothingReferencedMn» this compared the constant with the
        // constant: emptying it to "" left the test green while the refusal became
        // a silent no-op - the one thing this design forbids. The assertion had
        // walked into the same trap as its subject.
        Assert.Equal(VisualizationStoreCleanup.NothingReferencedMn, sweep.RefusalMn);
        Assert.False(
            string.IsNullOrWhiteSpace(sweep.RefusalMn),
            "the refusal has no words - nobody can act on it");
        Assert.Contains("бүртгэл", sweep.RefusalMn, StringComparison.Ordinal);
        Assert.Contains("цэвэрлэгээ", sweep.RefusalMn, StringComparison.Ordinal);

        // Null is the same event from the other side.
        Assert.False(
            VisualizationStoreCleanup.Plan([Store + "aaa.png"], null).WillRemoveAnything);
    }

    [Fact]
    public void SEPARATORSAreNormalisedOrEVERYFileLooksLikeAnOrphan()
    {
        // 🔴 THE RECORD KEEPS FORWARD SLASHES AND WINDOWS HANDS BACK BACKSLASHES.
        // Comparing them raw would call every present file unreferenced - and the
        // sweep would delete the whole store while reporting that it had tidied up.
        VisualizationStoreSweep sweep = VisualizationStoreCleanup.Plan(
            [@"sources\visualizations\images\aaa.png"],
            ["sources/visualizations/images/aaa.png"]);

        Assert.Empty(sweep.OrphanRelativePaths);
        Assert.Equal(1, sweep.KeptCount);
    }

    [Fact]
    public void CASEAloneDoesNotMakeAnOrphan()
    {
        // The store writes lower-case hex; a record read back from another tool may
        // not. On Windows they are the same file.
        VisualizationStoreSweep sweep = VisualizationStoreCleanup.Plan(
            [Store + "AAA.PNG"],
            [Store + "aaa.png"]);

        Assert.Empty(sweep.OrphanRelativePaths);
        Assert.Equal(1, sweep.KeptCount);
    }

    [Fact]
    public void ANEXCLUDEDImageKeepsItsCopy()
    {
        // 🔴 «ХУУДАСНААС ХАСАХ» KEEPS THE FILE AND ONLY DROPS IT FROM THE LAYOUT -
        // the button says so: «Сонгосон зургуудыг эх үүсвэрт нь хадгалж, альбумд
        // идэвхгүй болгоно». A sweep that read «not on a page» as «not referenced»
        // would destroy work the owner deliberately set aside.
        //
        // The rule takes REFERENCES, not page membership, so an excluded image
        // survives as long as its record still names the file. This test states the
        // obligation on the caller: pass every record's path, not the laid-out ones.
        VisualizationStoreSweep sweep = VisualizationStoreCleanup.Plan(
            [Store + "kept.png", Store + "excluded.png"],
            [Store + "kept.png", Store + "excluded.png"]);

        Assert.Empty(sweep.OrphanRelativePaths);
        Assert.Equal(2, sweep.KeptCount);
    }

    [Fact]
    public void ANEmptyStoreIsNOTARefusal()
    {
        // Nothing to sweep is not a fault, and must not read as one - otherwise a
        // fresh project would report a problem on its first album.
        VisualizationStoreSweep sweep = VisualizationStoreCleanup.Plan([], [Store + "aaa.png"]);

        Assert.Equal("", sweep.RefusalMn);
        Assert.Empty(sweep.OrphanRelativePaths);
        Assert.Equal(0, sweep.KeptCount);
        Assert.False(sweep.WillRemoveAnything);
    }

    [Fact]
    public void AREFERENCEToAFileTheStoreDoesNOTHoldIsNotAnError()
    {
        // The record can point at a copy somebody deleted by hand. That is the
        // reconciler's problem - it restores the copy - and not a reason for this
        // sweep to refuse or to remove anything.
        VisualizationStoreSweep sweep = VisualizationStoreCleanup.Plan(
            [Store + "aaa.png"],
            [Store + "aaa.png", Store + "gone.png"]);

        Assert.Equal("", sweep.RefusalMn);
        Assert.Empty(sweep.OrphanRelativePaths);
        Assert.Equal(1, sweep.KeptCount);
    }

    [Fact]
    public void EVERYOrphanReturnedIsGENUINELYUnreferenced()
    {
        // 🔴 THE INVARIANT, CHECKED OVER A GRID RATHER THAN AN EXAMPLE. Whatever
        // the inputs, a path that is referenced must never appear in the removal
        // list - this is the assertion that stands between a tidy-up and the
        // owner's renders.
        string[] pool = [Store + "a.png", Store + "b.png", Store + "c.jpg", Store + "d.jpg"];

        for (var mask = 0; mask < 16; mask++)
        {
            var referenced = new List<string>();
            for (var bit = 0; bit < pool.Length; bit++)
            {
                if ((mask & (1 << bit)) != 0)
                    referenced.Add(pool[bit]);
            }

            VisualizationStoreSweep sweep = VisualizationStoreCleanup.Plan(pool, referenced);

            foreach (string orphan in sweep.OrphanRelativePaths)
            {
                Assert.DoesNotContain(
                    orphan,
                    referenced.Select(path => path.Replace('\\', '/')));
            }

            if (referenced.Count > 0)
            {
                Assert.Equal(referenced.Count, sweep.KeptCount);
                Assert.Equal(pool.Length - referenced.Count, sweep.OrphanRelativePaths.Count);
            }
        }
    }
}
