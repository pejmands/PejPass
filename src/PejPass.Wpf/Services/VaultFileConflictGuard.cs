using PejPass.Wpf.Dialogs;

namespace PejPass.Wpf.Services;

internal static class VaultFileConflictGuard
{
    public static bool ConfirmOverwriteIfChanged(
        VaultSession session,
        long sessionGeneration,
        PejPass.Domain.Entities.Vault vault,
        string path)
    {
        if (!session.IsCurrent(sessionGeneration, vault))
            return false;

        if (!session.HasExternalFileChanges(sessionGeneration, path))
            return true;

        var confirmed = DialogService.Confirm(
            "This vault file has changed on disk since it was opened or last saved. " +
            "It may have been updated by another device or application.\n\n" +
            "Continuing will overwrite those disk changes with this session's current contents. " +
            "PejPass cannot merge the two versions automatically.\n\n" +
            "To keep the newer disk version, cancel and reopen the vault.\n\n" +
            $"File:\n{path}\n\nOverwrite the disk version?",
            "Vault changed on disk",
            yesText: "Overwrite",
            noText: "Cancel",
            isDestructive: true);

        return confirmed && session.IsCurrent(sessionGeneration, vault);
    }
}
