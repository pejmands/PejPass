using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Domain.Settings;
using PejPass.Wpf.Services;
using PejPass.Wpf.ViewModels;
using System.IO;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class HistoryViewModelCustomFieldTests
{
    private const string OldSecret = "old-secret-value";
    private const string NewSecret = "new-secret-value";

    [Fact(Timeout = 15000)]
    public void SecretCustomFieldValueChange_IsListedAsChanged_AndSelectableForRestore()
    {
        WpfTestHost.Run(_ =>
        {
            using var session = new VaultSession();
            using var material = CreateMaterial();
            OpenVaultAndChange(session, material, OldSecret, NewSecret, "Router");

            using var viewModel = new HistoryViewModel(
                session,
                null!,
                new AppSettings(),
                (title, message) => { });

            var row = Assert.Single(viewModel.Items);
            Assert.Contains("Custom Fields", row.ChangedFields);

            viewModel.SelectedItem = row;

            var field = Assert.Single(
                viewModel.Fields,
                f => f.Field == EntryHistoryField.CustomFields);

            Assert.True(field.IsChanged);
            field.IsSelected = true;
            Assert.True(viewModel.HasSelectedFields);

            Assert.DoesNotContain(OldSecret, field.CurrentDisplayValue);
            Assert.DoesNotContain(NewSecret, field.CurrentDisplayValue);
            Assert.DoesNotContain(OldSecret, field.SnapshotDisplayValue);
            Assert.DoesNotContain(NewSecret, field.SnapshotDisplayValue);
            Assert.Contains("(differs)", field.CurrentDisplayValue);
            Assert.Contains("(differs)", field.SnapshotDisplayValue);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void UnchangedSecretCustomField_IsNotReportedWhenOnlyTitleChanges()
    {
        WpfTestHost.Run(_ =>
        {
            using var session = new VaultSession();
            using var material = CreateMaterial();
            OpenVaultAndChange(session, material, OldSecret, OldSecret, "Renamed router");

            using var viewModel = new HistoryViewModel(
                session,
                null!,
                new AppSettings(),
                (title, message) => { });

            var row = Assert.Single(viewModel.Items);
            Assert.Contains("Title", row.ChangedFields);
            Assert.DoesNotContain("Custom Fields", row.ChangedFields);

            viewModel.SelectedItem = row;
            var field = Assert.Single(
                viewModel.Fields,
                f => f.Field == EntryHistoryField.CustomFields);

            Assert.False(field.IsChanged);
            Assert.DoesNotContain("(differs)", field.CurrentDisplayValue);
            Assert.DoesNotContain("(differs)", field.SnapshotDisplayValue);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void NewlySecretCustomField_DoesNotLeakHistoricalOrCurrentValue()
    {
        WpfTestHost.Run(_ =>
        {
            using var session = new VaultSession();
            using var material = CreateMaterial();
            OpenVaultAndChange(
                session,
                material,
                OldSecret,
                NewSecret,
                "Router",
                oldIsSecret: false,
                newIsSecret: true);

            using var viewModel = new HistoryViewModel(
                session,
                null!,
                new AppSettings(),
                (title, message) => { });

            var row = Assert.Single(viewModel.Items);
            Assert.Contains("Custom Fields", row.ChangedFields);
            viewModel.SelectedItem = row;

            var field = Assert.Single(
                viewModel.Fields,
                f => f.Field == EntryHistoryField.CustomFields);

            Assert.True(field.IsChanged);
            Assert.DoesNotContain(OldSecret, field.CurrentDisplayValue);
            Assert.DoesNotContain(NewSecret, field.CurrentDisplayValue);
            Assert.DoesNotContain(OldSecret, field.SnapshotDisplayValue);
            Assert.DoesNotContain(NewSecret, field.SnapshotDisplayValue);
        }, TestContext.Current.CancellationToken);
    }

    private static Vault OpenVaultAndChange(
        VaultSession session,
        VaultKeyMaterial material,
        string oldSecret,
        string newSecret,
        string newTitle,
        bool oldIsSecret = true,
        bool newIsSecret = true)
    {
        var original = new VaultEntry
        {
            Title = "Original",
            CustomFields =
            [
                new CustomField
                {
                    Name = "Recovery code",
                    Value = oldSecret,
                    IsSecret = oldIsSecret
                }
            ]
        };

        var vault = new Vault { Entries = [original] };
        session.Open(
            vault,
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"),
            material);

        var updated = new VaultEntry
        {
            Id = original.Id,
            CreatedAt = original.CreatedAt,
            Title = newTitle,
            CustomFields =
            [
                new CustomField
                {
                    Name = "Recovery code",
                    Value = newSecret,
                    IsSecret = newIsSecret
                }
            ]
        };

        Assert.True(vault.UpdateEntry(updated));
        return vault;
    }

    private static VaultKeyMaterial CreateMaterial()
    {
        var key = new byte[32];
        var salt = new byte[16];
        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(salt);

        try
        {
            return new VaultKeyMaterial(key, salt, Argon2Parameters.Default);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
        }
    }
}
