using System;
using System.Collections.Generic;
using System.IO;

namespace ErkS.Updates;

/// <summary>
/// Puts a staged plan in place (contract §4) once the host has closed: every replaced or removed
/// file is moved into «.rollback» first, and the first failure puts everything back. The new
/// install manifest is written last, so a half-applied update is never mistaken for a finished one.
/// User files are never touched - only paths the manifests name.
/// </summary>
public static class UpdateApplier
{
    public const string RollbackFolder = ".rollback";

    public static void Apply(string installDirectory, string stagingDirectory, UpdatePlan plan)
    {
        string root = Path.GetFullPath(installDirectory);
        string rollback = Path.Combine(root, RollbackFolder);
        if (Directory.Exists(rollback))
            Directory.Delete(rollback, recursive: true);
        Directory.CreateDirectory(rollback);

        // Every staged file is checked again before anything moves.
        foreach (UpdateFile file in plan.Changed)
        {
            string staged = DeltaDownloader.StagedPath(stagingDirectory, file.Sha256);
            if (!File.Exists(staged) || Hashes.Sha256OfFile(staged) != file.Sha256)
                throw new InvalidDataException("Шинэчлэлийн файл бүрэн биш: " + file.Path);
        }

        var done = new List<(string Target, string? Kept, bool Placed)>();
        try
        {
            foreach (string removed in plan.Removed)
            {
                string target = InstallManifest.Local(root, removed);
                if (!File.Exists(target))
                    continue;
                string kept = Keep(root, rollback, target);
                done.Add((target, kept, false));
            }
            foreach (UpdateFile file in plan.Changed)
            {
                string target = InstallManifest.Local(root, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string? kept = File.Exists(target) ? Keep(root, rollback, target) : null;
                done.Add((target, kept, true));
                string temporary = target + ".erks-new";
                File.Copy(DeltaDownloader.StagedPath(stagingDirectory, file.Sha256), temporary, overwrite: true);
                File.Move(temporary, target);
            }
            InstallManifest.Save(root, plan.Target);
        }
        catch
        {
            Undo(done);
            throw;
        }
    }

    private static string Keep(string root, string rollback, string target)
    {
        string kept = Path.Combine(rollback, InstallManifest.Relative(root, target).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        File.Move(target, kept);
        return kept;
    }

    private static void Undo(List<(string Target, string? Kept, bool Placed)> done)
    {
        for (int index = done.Count - 1; index >= 0; index--)
        {
            (string target, string? kept, bool placed) = done[index];
            try
            {
                if (placed && File.Exists(target))
                    File.Delete(target);
                if (kept is not null && File.Exists(kept))
                    File.Move(kept, target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Keep going: every other file still goes back.
            }
        }
    }
}
