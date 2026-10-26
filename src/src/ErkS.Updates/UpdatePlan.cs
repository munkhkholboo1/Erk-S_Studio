using System;
using System.Collections.Generic;
using System.Linq;

namespace ErkS.Updates;

/// <summary>What a delta update has to do: the files to fetch, the files to drop, and whether it is worth it.</summary>
public sealed class UpdatePlan
{
    public UpdateManifest Target { get; set; } = new();

    /// <summary>New files and files whose hash changed - only these are fetched.</summary>
    public List<UpdateFile> Changed { get; set; } = new();

    /// <summary>Installed paths the new version does not have.</summary>
    public List<string> Removed { get; set; } = new();

    public long DownloadBytes => Changed.Sum(file => file.Size);

    /// <summary>
    /// The delta is not worth it (over <see cref="UpdatePlanner.FullInstallerThreshold"/> of the full
    /// installer) - the product runs its full installer instead.
    /// </summary>
    public bool UseFullInstaller { get; set; }

    public bool IsEmpty => Changed.Count == 0 && Removed.Count == 0;

    /// <summary>
    /// Whether the program must close: a changed file is not «live», or a file goes away (a
    /// removal is applied with the rest, at restart). Only live changes: none (contract 5a).
    /// </summary>
    public bool NeedsRestart => Changed.Any(file => !file.IsLive) || Removed.Count > 0;
}

public static class UpdatePlanner
{
    /// <summary>Contract: a delta over 60% of the full installer is not a delta.</summary>
    public const double FullInstallerThreshold = 0.6;

    /// <param name="installed">What is installed now (<see cref="InstallManifest"/>).</param>
    /// <param name="target">The verified manifest of the version to reach.</param>
    /// <param name="fullInstallerBytes">Size of the full installer; 0 when unknown (the delta is then always used).</param>
    /// <param name="threshold">Share of the full installer above which the delta is not used.</param>
    public static UpdatePlan Plan(UpdateManifest installed, UpdateManifest target, long fullInstallerBytes = 0, double threshold = FullInstallerThreshold)
    {
        var have = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (UpdateFile file in installed.Files)
            have[file.Path] = file.Sha256;
        var plan = new UpdatePlan { Target = target };
        foreach (UpdateFile file in target.Files)
        {
            if (!Hashes.IsSha256(file.Sha256))
                throw new System.IO.InvalidDataException("Манифестын хэш буруу: " + file.Path);
            if (!have.TryGetValue(file.Path, out string? sha) || !sha.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                plan.Changed.Add(file);
        }
        var wanted = new HashSet<string>(target.Files.Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
        plan.Removed.AddRange(installed.Files.Select(file => file.Path).Where(path => !wanted.Contains(path)));
        plan.Removed.AddRange(target.Removed.Where(path => have.ContainsKey(path) && !wanted.Contains(path) && !plan.Removed.Contains(path, StringComparer.OrdinalIgnoreCase)));
        plan.UseFullInstaller = fullInstallerBytes > 0 && plan.DownloadBytes > fullInstallerBytes * threshold;
        return plan;
    }
}

/// <summary>Progress for a product's UI: percent, bytes and speed (contract: «%, МБ/с»).</summary>
public sealed class UpdateProgress
{
    public long DoneBytes { get; set; }

    public long TotalBytes { get; set; }

    public int FilesDone { get; set; }

    public int FilesTotal { get; set; }

    public double BytesPerSecond { get; set; }

    public int Percent => TotalBytes <= 0 ? 100 : (int)Math.Min(100, DoneBytes * 100 / TotalBytes);

    public string Text =>
        $"{DoneBytes / 1048576.0:0.0} / {TotalBytes / 1048576.0:0.0} МБ ({Percent}%)" +
        (BytesPerSecond > 0 ? $" · {BytesPerSecond / 1048576.0:0.0} МБ/с" : "");
}
