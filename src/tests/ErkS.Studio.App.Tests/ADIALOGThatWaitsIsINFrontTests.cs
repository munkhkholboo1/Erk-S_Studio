using System.Windows;

namespace ErkS.Studio.App.Tests;

/// <summary>Master, 2026-10-27: the update dialog opened behind an elevated window and took no clicks.</summary>
public sealed class ADIALOGThatWaitsIsINFrontTests
{
    [Fact]
    public void ADialogIsKEPTInFrontAndHasAnOWNERWhenThereIsOne()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new Window();
                StudioDialogFront.Keep(dialog);
                Assert.True(dialog.Topmost);
                Assert.Null(StudioDialogFront.Owner(new Window())); // never shown: no owner to centre on
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }
}
