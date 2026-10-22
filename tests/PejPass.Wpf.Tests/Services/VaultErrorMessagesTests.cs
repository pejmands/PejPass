using PejPass.Wpf.Services;

namespace PejPass.Wpf.Tests.Services;

public sealed class VaultErrorMessagesTests
{
    [Theory]
    [InlineData(true, "The master password is incorrect, or the vault file is damaged.")]
    [InlineData(false, "Could not unlock or create the vault. Check the file and try again.")]
    public void ForUnlock_ReturnsSafeMessage(bool authenticationFailed, string expected)
    {
        Assert.Equal(expected, VaultErrorMessages.ForUnlock(authenticationFailed));
    }

    [Theory]
    [InlineData(true, "Could not unlock the vault with Windows Hello. Enter your master password.")]
    [InlineData(false, "Could not unlock the vault. Please try again.")]
    public void ForWindowsHelloUnlock_ReturnsSafeMessage(bool authenticationFailed, string expected)
    {
        Assert.Equal(expected, VaultErrorMessages.ForWindowsHelloUnlock(authenticationFailed));
    }

    [Theory]
    [InlineData(true, "Current master password is incorrect, or the vault file is damaged.")]
    [InlineData(false, "Could not change the master password. Please try again.")]
    public void ForChangeMasterPassword_ReturnsSafeMessage(bool authenticationFailed, string expected)
    {
        Assert.Equal(expected, VaultErrorMessages.ForChangeMasterPassword(authenticationFailed));
    }

    [Theory]
    [InlineData(true, "The password is incorrect, or the backup file is damaged.")]
    [InlineData(false, "Could not open the backup. Check the file and try again.")]
    public void ForBackupOpen_ReturnsSafeMessage(bool authenticationFailed, string expected)
    {
        Assert.Equal(expected, VaultErrorMessages.ForBackupOpen(authenticationFailed));
    }
}
