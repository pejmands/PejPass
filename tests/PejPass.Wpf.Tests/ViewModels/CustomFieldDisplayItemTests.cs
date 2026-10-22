using PejPass.Domain.Entities;
using PejPass.Wpf.ViewModels;
using System.Reflection;

namespace PejPass.Wpf.Tests.ViewModels;

public sealed class CustomFieldDisplayItemTests
{
    private const string Secret = "super-secret-token-value";

    [Fact]
    public void RawValue_IsNotPubliclyBindable()
    {
        var publicValue = typeof(CustomFieldDisplayItem).GetProperty(
            "Value",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.Null(publicValue);
    }

    [Fact(Timeout = 15000)]
    public void HiddenSecret_DisplayValueDoesNotContainSecret_AndRevealHideRoundTrips()
    {
        WpfTestHost.Run(_ =>
        {
            var item = new CustomFieldDisplayItem(new CustomField
            {
                Name = "API token",
                Value = Secret,
                IsSecret = true
            });

            try
            {
                Assert.DoesNotContain(Secret, item.DisplayValue);
                Assert.All(item.DisplayValue, c => Assert.Equal('•', c));

                item.Reveal(30);
                Assert.Equal(Secret, item.DisplayValue);

                item.Hide();
                Assert.DoesNotContain(Secret, item.DisplayValue);
            }
            finally
            {
                item.Dispose();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 15000)]
    public void NonSecret_DisplayValueIsPlainText()
    {
        WpfTestHost.Run(_ =>
        {
            var item = new CustomFieldDisplayItem(new CustomField
            {
                Name = "Department",
                Value = "Research",
                IsSecret = false
            });

            try
            {
                Assert.Equal("Research", item.DisplayValue);
            }
            finally
            {
                item.Dispose();
            }
        }, TestContext.Current.CancellationToken);
    }
}
