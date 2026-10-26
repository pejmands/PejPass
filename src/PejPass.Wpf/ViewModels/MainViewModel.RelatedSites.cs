using CommunityToolkit.Mvvm.Input;
using PejPass.Wpf.Dialogs;
using PejPass.Wpf.Services;
using System.Collections.ObjectModel;

namespace PejPass.Wpf.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<string> RelatedSites { get; } = [];

    public bool HasRelatedSites => RelatedSites.Count > 0;

    private void RefreshRelatedSites()
    {
        RelatedSites.Clear();

        var entry = SelectedEntry;
        if (entry is null || !TryGetWebsiteHost(entry.Url, out var host))
        {
            OnPropertyChanged(nameof(HasRelatedSites));
            return;
        }

        var vault = _vaultSession.Vault;
        if (vault is null)
        {
            OnPropertyChanged(nameof(HasRelatedSites));
            return;
        }

        foreach (var profile in vault.SiteProfiles)
        {
            var domains = new List<string>();
            foreach (var rawDomain in profile.Domains ?? [])
            {
                if (BrowserStatusPipeService.TryNormalizeDomain(rawDomain, out var domain) &&
                    BrowserStatusPipeService.GetRegistrableDomain(domain) is not null &&
                    !domains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                {
                    domains.Add(domain);
                }
            }

            if (!domains.Any(domain => HostMatchesConfiguredDomain(host, domain)))
                continue;

            foreach (var domain in domains)
            {
                if (HostMatchesConfiguredDomain(host, domain))
                    continue;

                if (!RelatedSites.Contains(domain, StringComparer.OrdinalIgnoreCase))
                    RelatedSites.Add(domain);
            }

            break;
        }

        OnPropertyChanged(nameof(HasRelatedSites));
    }

    [RelayCommand]
    private void OpenRelatedSite(string? domain)
    {
        if (!BrowserStatusPipeService.TryNormalizeDomain(domain, out var normalizedDomain) ||
            BrowserStatusPipeService.GetRegistrableDomain(normalizedDomain) is null)
        {
            return;
        }

        try
        {
            if (!UpdateService.OpenUrl($"https://{normalizedDomain}"))
            {
                DialogService.Warning(
                    "The related site URL is invalid or uses an unsupported scheme.",
                    "Open Related Site");

                return;
            }

            SnackbarService.Show("Opened in browser");
        }
        catch
        {
            DialogService.Warning(
                "Could not open the related site.",
                "Open Related Site");
        }
    }

    private static bool TryGetWebsiteHost(string? value, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096)
            return false;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            if (!Uri.TryCreate("https://" + value.Trim(), UriKind.Absolute, out uri))
                return false;
        }

        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host.Length > 0;
    }

    private static bool HostMatchesConfiguredDomain(string host, string configuredDomain) =>
        host.Equals(configuredDomain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + configuredDomain, StringComparison.OrdinalIgnoreCase);
}
