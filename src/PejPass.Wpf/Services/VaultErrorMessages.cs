namespace PejPass.Wpf.Services;

public static class VaultErrorMessages
{
    public static string ForUnlock(bool authenticationFailed) =>
        authenticationFailed
            ? "The master password is incorrect, or the vault file is damaged."
            : "Could not unlock or create the vault. Check the file and try again.";

    public static string ForWindowsHelloUnlock(bool authenticationFailed) =>
        authenticationFailed
            ? "Could not unlock the vault with Windows Hello. Enter your master password."
            : "Could not unlock the vault. Please try again.";

    public static string ForChangeMasterPassword(bool authenticationFailed) =>
        authenticationFailed
            ? "Current master password is incorrect, or the vault file is damaged."
            : "Could not change the master password. Please try again.";

    public static string ForBackupOpen(bool authenticationFailed) =>
        authenticationFailed
            ? "The password is incorrect, or the backup file is damaged."
            : "Could not open the backup. Check the file and try again.";
}
