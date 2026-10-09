using System.Security.Cryptography;

namespace PejPass.Wpf.Services;

public static class VaultErrorMessages
{
    public static string ForUnlock(Exception exception) =>
        exception is AuthenticationTagMismatchException
            ? "The master password is incorrect, or the vault file is damaged."
            : "Could not unlock or create the vault. Check the file and try again.";

    public static string ForWindowsHelloUnlock(Exception exception) =>
        exception is AuthenticationTagMismatchException
            ? "Could not unlock the vault with Windows Hello. Enter your master password."
            : "Could not unlock the vault. Please try again.";

    public static string ForChangeMasterPassword(Exception exception) =>
        exception is AuthenticationTagMismatchException
            ? "Current master password is incorrect, or the vault file is damaged."
            : "Could not change the master password. Please try again.";

    public static string ForBackupOpen(Exception exception) =>
        exception is AuthenticationTagMismatchException
            ? "The password is incorrect, or the backup file is damaged."
            : "Could not open the backup. Check the file and try again.";
}
