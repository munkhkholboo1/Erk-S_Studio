namespace ErkS.Studio;

internal sealed partial class ShellView
{
    /// <summary>
    /// «project-info»: the project page's «Засварлах» → «Хадгалах» for the
    /// corner table's ciphers - the same edit mode and the same save as the
    /// buttons. No argument reports the current values.
    /// </summary>
    private async Task<StudioBridgeResponse> BridgeProjectInfoAsync(StudioBridgeRequest request)
    {
        if (!state.HasOpenProject)
            return StudioBridgeProtocol.Refuse(request.Id, "no_project", "Нээлттэй төсөл алга. Эхлээд «open-project».");
        string? general = request.Arg("general-cipher");
        string? technical = request.Arg("technical-cipher");
        object Current() => new
        {
            generalCipher = state.Project.Identity.GeneralDesignCipher ?? "",
            technicalCipher = state.Project.Identity.TechnicalDesignCipher ?? "",
        };
        if (general is null && technical is null)
            return StudioBridgeProtocol.Accept(request.Id, Current());

        BeginFoundationEdit(focusProjectName: false);
        if (!foundationEditMode)
            return StudioBridgeProtocol.Refuse(request.Id, "not_editable", statusText.Text);
        if (general is not null)
            generalDesignCipherBox.Text = general.Trim();
        if (technical is not null)
            technicalDesignCipherBox.Text = technical.Trim();
        await SaveFoundationChangesAsync();
        if (foundationEditMode)
        {
            string reason = statusText.Text;
            CancelFoundationEdit();
            return StudioBridgeProtocol.Refuse(request.Id, "save_failed", reason);
        }
        return StudioBridgeProtocol.Accept(request.Id, Current(), statusText.Text);
    }
}
