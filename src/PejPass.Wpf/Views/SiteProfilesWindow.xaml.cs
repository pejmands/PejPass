using PejPass.Domain.Entities;
using PejPass.Wpf.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace PejPass.Wpf.Views;

public partial class SiteProfilesWindow : Window
{
    public ObservableCollection<SiteProfileEditorItem> Profiles { get; } = [];

    public IReadOnlyList<SiteProfile> SiteProfiles { get; private set; } = [];

    public SiteProfilesWindow(IEnumerable<SiteProfile> profiles)
    {
        InitializeComponent();
        App.PrepareCustomChrome(this);
        DataContext = this;

        foreach (var profile in profiles)
        {
            Profiles.Add(new SiteProfileEditorItem(
                profile.Id,
                profile.Name,
                string.Join(Environment.NewLine, profile.Domains ?? [])));
        }
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = new SiteProfileEditorItem(Guid.NewGuid(), string.Empty, string.Empty);
        Profiles.Add(profile);
        ProfilesScrollViewer.ScrollToBottom();
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SiteProfileEditorItem selected })
            Profiles.Remove(selected);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var profiles = new List<SiteProfile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domains = new List<(string Domain, string Name)>();

        foreach (var item in Profiles)
        {
            var name = item.Name.Trim();
            if (name.Length is 0 or > 100)
            {
                ShowValidation("Each group needs a name of 1–100 characters.");
                return;
            }

            if (!names.Add(name))
            {
                ShowValidation($"The group name '{name}' is duplicated.");
                return;
            }

            var normalizedDomains = new List<string>();
            foreach (var rawDomain in item.DomainsText.Split(
                         ['\r', '\n', ',', ';'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!BrowserStatusPipeService.TryNormalizeDomain(rawDomain, out var domain) ||
                    BrowserStatusPipeService.GetRegistrableDomain(domain) is null)
                {
                    ShowValidation($"'{rawDomain}' is not a valid registrable hostname. Public suffixes such as 'com' or 'co.uk' cannot be used.");
                    return;
                }

                if (!normalizedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                    normalizedDomains.Add(domain);
            }

            if (normalizedDomains.Count < 2 || normalizedDomains.Count > 100)
            {
                ShowValidation($"'{name}' needs between 2 and 100 different domains.");
                return;
            }

            profiles.Add(new SiteProfile
            {
                Id = item.Id,
                Name = name,
                Domains = normalizedDomains
            });
        }

        foreach (var profile in profiles)
        {
            foreach (var domain in profile.Domains)
            {
                foreach (var existing in domains)
                {
                    if (!string.Equals(existing.Name, profile.Name, StringComparison.OrdinalIgnoreCase) &&
                        DomainScopesOverlap(domain, existing.Domain))
                    {
                        ShowValidation(
                            $"'{domain}' in '{profile.Name}' overlaps with '{existing.Domain}' in '{existing.Name}'. A hostname can belong to only one group.");
                        return;
                    }
                }

                domains.Add((domain, profile.Name));
            }
        }

        SiteProfiles = profiles;
        DialogResult = true;
    }

    private static bool DomainScopesOverlap(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
        left.EndsWith("." + right, StringComparison.OrdinalIgnoreCase) ||
        right.EndsWith("." + left, StringComparison.OrdinalIgnoreCase);

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }
}

public sealed class SiteProfileEditorItem(Guid id, string name, string domainsText) : INotifyPropertyChanged
{
    private string _name = name;
    private string _domainsText = domainsText;

    public Guid Id { get; } = id;

    public string Name
    {
        get => _name;
        set
        {
            if (string.Equals(_name, value, StringComparison.Ordinal))
                return;

            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public string DomainsText
    {
        get => _domainsText;
        set
        {
            if (string.Equals(_domainsText, value, StringComparison.Ordinal))
                return;

            _domainsText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DomainsText)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}