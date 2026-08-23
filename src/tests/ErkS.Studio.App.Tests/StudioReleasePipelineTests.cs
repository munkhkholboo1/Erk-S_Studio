namespace ErkS.Studio.App.Tests;

public sealed class StudioReleasePipelineTests
{
    [Fact]
    public void ReleaseScripts_DeriveDefaultsFromAuthoritativeVersionProps()
    {
        string repositoryRoot = FindRepositoryRoot();
        string versionPropsPath = Path.Combine(
            repositoryRoot,
            "src",
            "Studio.Version.props");
        System.Xml.Linq.XDocument versionProps = System.Xml.Linq.XDocument.Load(
            versionPropsPath);
        string publishedVersion = versionProps
            .Descendants("StudioPublishedVersion")
            .Single()
            .Value
            .Trim();
        string publishedAssemblyVersion = versionProps
            .Descendants("StudioPublishedAssemblyVersion")
            .Single()
            .Value
            .Trim();

        string publishScript = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "scripts",
            "Publish-Studio-Demo.ps1"));
        string serverScript = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "scripts",
            "Publish-Studio-DemoToServer.ps1"));

        foreach (string script in new[] { publishScript, serverScript })
        {
            Assert.Contains("[string]$ReleaseVersion = \"\"", script, StringComparison.Ordinal);
            Assert.Contains("Studio.Version.props", script, StringComparison.Ordinal);
            Assert.Contains("StudioPublishedVersion", script, StringComparison.Ordinal);
            Assert.Contains("$AuthoritativeReleaseVersion", script, StringComparison.Ordinal);
            Assert.Contains("does not match authoritative Studio.Version.props", script, StringComparison.Ordinal);
            Assert.DoesNotContain($"\"V{publishedVersion}\"", script, StringComparison.Ordinal);
        }

        Assert.Contains("[string]$AssemblyVersion = \"\"", publishScript, StringComparison.Ordinal);
        Assert.Contains("StudioPublishedAssemblyVersion", publishScript, StringComparison.Ordinal);
        Assert.Contains("$AuthoritativeAssemblyVersion", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"{publishedAssemblyVersion}\"", publishScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductPublish_RunsAllRegressionSuitesInProductModeBeforePublish()
    {
        string repositoryRoot = FindRepositoryRoot();
        string scriptPath = Path.Combine(
            repositoryRoot,
            "src",
            "scripts",
            "Publish-Studio-Demo.ps1");
        string script = File.ReadAllText(scriptPath);
        string normalized = script.Replace('\\', '/');

        Assert.Contains(
            "tests/ErkS.Platform.Core.Tests/ErkS.Platform.Core.Tests.csproj",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "tests/ErkS.Studio.App.Tests/ErkS.Studio.App.Tests.csproj",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains("& dotnet test", script, StringComparison.Ordinal);
        Assert.Contains("-c \"Release\"", script, StringComparison.Ordinal);
        Assert.Contains("-p:StudioProductBuild=true", script, StringComparison.Ordinal);
        Assert.Contains("--artifacts-path", script, StringComparison.Ordinal);
        Assert.Contains("$BuildArtifactsDirectory", script, StringComparison.Ordinal);

        int testGateIndex = script.IndexOf("& dotnet test", StringComparison.Ordinal);
        int publishIndex = script.IndexOf("$PublishArguments", StringComparison.Ordinal);

        Assert.True(testGateIndex >= 0);
        Assert.True(
            publishIndex > testGateIndex,
            "Regression tests must complete before the product publish starts.");
    }

    [Fact]
    public void ProductPublish_RunsInstalledApplicationStartupSmokeTest()
    {
        string repositoryRoot = FindRepositoryRoot();
        string scriptPath = Path.Combine(
            repositoryRoot,
            "src",
            "scripts",
            "Publish-Studio-Demo.ps1");
        string script = File.ReadAllText(scriptPath);

        Assert.Contains("--release-smoke-test", script, StringComparison.Ordinal);
        Assert.Contains(
            "Final installed application startup smoke test failed",
            script,
            StringComparison.Ordinal);

        int installIndex = script.IndexOf("$SmokeExe =", StringComparison.Ordinal);
        int startupSmokeIndex = script.IndexOf("--release-smoke-test", StringComparison.Ordinal);
        int cleanupIndex = script.IndexOf(
            "Remove-Item -LiteralPath $SmokeInstallDirectory",
            startupSmokeIndex,
            StringComparison.Ordinal);

        Assert.True(installIndex >= 0);
        Assert.True(startupSmokeIndex > installIndex);
        Assert.True(
            cleanupIndex > startupSmokeIndex,
            "The installed application must start successfully before smoke files are removed.");
    }

    [Fact]
    public void ProductPublish_ReinstallsOverTheSmokeInstallationInUpdateMode()
    {
        string repositoryRoot = FindRepositoryRoot();
        string scriptPath = Path.Combine(
            repositoryRoot,
            "src",
            "scripts",
            "Publish-Studio-Demo.ps1");
        string script = File.ReadAllText(scriptPath);

        Assert.Contains("$UpdateSmokeProcess", script, StringComparison.Ordinal);
        Assert.Contains(
            "Final setup update smoke test failed",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Final updated application startup smoke test failed",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "--release-update-hold-test",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Installed Studio did not enter the running-update acceptance state",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "The signed updater completed without closing the running Studio process",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "runningProcessClosedAutomatically",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "running-update.json",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Remove-Item -LiteralPath $UpdateHoldReadyPath -Force",
            script,
            StringComparison.Ordinal);

        int firstStartupIndex = script.IndexOf(
            "Final installed application startup smoke test failed",
            StringComparison.Ordinal);
        int runningAppIndex = script.IndexOf(
            "$UpdateHoldProcess = Start-Process",
            StringComparison.Ordinal);
        int updateInstallIndex = script.IndexOf(
            "$UpdateSmokeProcess = Start-Process",
            StringComparison.Ordinal);
        int updatedStartupIndex = script.IndexOf(
            "Final updated application startup smoke test failed",
            StringComparison.Ordinal);

        Assert.True(runningAppIndex > firstStartupIndex);
        Assert.True(updateInstallIndex > runningAppIndex);
        Assert.True(updatedStartupIndex > updateInstallIndex);
    }

    [Fact]
    public void ProductPublish_RequiresExternalHostsTrustAndMultiDpiArtifacts()
    {
        string repositoryRoot = FindRepositoryRoot();
        string scriptPath = Path.Combine(
            repositoryRoot,
            "src",
            "scripts",
            "Publish-Studio-Demo.ps1");
        string script = File.ReadAllText(scriptPath);

        Assert.Contains("Test-Studio-ExternalAcceptance.ps1", script, StringComparison.Ordinal);
        Assert.Contains("-RequireExternalRepositories", script, StringComparison.Ordinal);
        Assert.Contains("-RequireInstalledHosts", script, StringComparison.Ordinal);
        Assert.Contains("Test-Studio-ReleaseArtifact.ps1", script, StringComparison.Ordinal);
        Assert.Contains("published-exe-trust.json", script, StringComparison.Ordinal);
        Assert.Contains("setup-trust.json", script, StringComparison.Ordinal);
        Assert.Contains("clean-install-exe-trust.json", script, StringComparison.Ordinal);
        Assert.Contains("updated-install-exe-trust.json", script, StringComparison.Ordinal);
        Assert.Contains("--release-smoke-output=", script, StringComparison.Ordinal);
        Assert.Contains("UI smoke must contain exactly three DPI scenarios", script, StringComparison.Ordinal);

        int hostAcceptanceIndex = script.IndexOf(
            "-RequireInstalledHosts",
            StringComparison.Ordinal);
        int publishIndex = script.IndexOf("$PublishArguments", StringComparison.Ordinal);
        Assert.True(
            hostAcceptanceIndex >= 0 && hostAcceptanceIndex < publishIndex,
            "AutoCAD/Revit acceptance must pass before product publishing starts.");
    }

    [Fact]
    public void ExternalAcceptance_CoversSupportedAutoCadAndRevitHosts()
    {
        string repositoryRoot = FindRepositoryRoot();
        string scriptPath = Path.Combine(
            repositoryRoot,
            "src",
            "scripts",
            "Test-Studio-ExternalAcceptance.ps1");
        string script = File.ReadAllText(scriptPath);

        Assert.Contains("AutoCAD 2026/2027 bundle manifest", script, StringComparison.Ordinal);
        Assert.Contains(
            "foreach ($Year in \"2026\", \"2027\")",
            script,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(script, "foreach ($Year in \"2026\", \"2027\")"));
        Assert.Contains("AutoCAD $Year host build", script, StringComparison.Ordinal);
        Assert.Contains("Revit $Year host build", script, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-Json", script, StringComparison.Ordinal);
        Assert.Contains("PASS", script, StringComparison.Ordinal);
        Assert.Contains("FAIL", script, StringComparison.Ordinal);
        Assert.Contains("SKIPPED", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ContinuousIntegration_VerifiesTheSameSelfContainedProductModeUsedByRelease()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workflowPath = Path.Combine(
            repositoryRoot,
            ".github",
            "workflows",
            "ci.yml");
        string workflow = File.ReadAllText(workflowPath);

        Assert.Contains(
            "dotnet build src/ErkS.Studio.slnx -c Release --no-restore -p:StudioProductBuild=true",
            workflow,
            StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(workflow, "-p:StudioProductBuild=true") >= 6);
        Assert.Contains("--self-contained true", workflow, StringComparison.Ordinal);
        Assert.Contains("--artifacts-path artifacts/dotnet-product", workflow, StringComparison.Ordinal);
        Assert.Contains("--release-smoke-test", workflow, StringComparison.Ordinal);
        Assert.Contains("--release-smoke-output=", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/external-acceptance.json", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/ui-smoke", workflow, StringComparison.Ordinal);
        Assert.Contains("Product startup smoke failed", workflow, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string scriptPath = Path.Combine(
                directory.FullName,
                "src",
                "scripts",
                "Publish-Studio-Demo.ps1");
            if (File.Exists(scriptPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Erk-S Studio repository root.");
    }
}
