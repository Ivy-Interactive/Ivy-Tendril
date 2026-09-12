namespace Ivy.Tendril.Test;

public class AppBrandTests
{
    [Fact]
    public void AppBrand_Constants_AreNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppBrand.AppName));
        Assert.False(string.IsNullOrWhiteSpace(AppBrand.Distribution));
        Assert.False(string.IsNullOrWhiteSpace(AppBrand.Organization));
        Assert.False(string.IsNullOrWhiteSpace(AppBrand.RepoName));
        Assert.False(string.IsNullOrWhiteSpace(AppBrand.RepoUrl));
        Assert.False(string.IsNullOrWhiteSpace(AppBrand.IssuesUrl));
        Assert.False(string.IsNullOrWhiteSpace(AppBrand.DocsUrl));
        Assert.False(string.IsNullOrWhiteSpace(AppBrand.DiscordUrl));
        Assert.False(string.IsNullOrWhiteSpace(AppBrand.PrSignatureText));
        Assert.False(string.IsNullOrWhiteSpace(AppBrand.PrSignatureUrl));
    }

    [Fact]
    public void AppBrand_Urls_AreWellFormed()
    {
        Assert.True(Uri.TryCreate(AppBrand.RepoUrl, UriKind.Absolute, out var repoUri) && (repoUri.Scheme == Uri.UriSchemeHttp || repoUri.Scheme == Uri.UriSchemeHttps));
        Assert.True(Uri.TryCreate(AppBrand.IssuesUrl, UriKind.Absolute, out var issuesUri) && (issuesUri.Scheme == Uri.UriSchemeHttp || issuesUri.Scheme == Uri.UriSchemeHttps));
        Assert.True(Uri.TryCreate(AppBrand.DocsUrl, UriKind.Absolute, out var docsUri) && (docsUri.Scheme == Uri.UriSchemeHttp || docsUri.Scheme == Uri.UriSchemeHttps));
        Assert.True(Uri.TryCreate(AppBrand.DiscordUrl, UriKind.Absolute, out var discordUri) && (discordUri.Scheme == Uri.UriSchemeHttp || discordUri.Scheme == Uri.UriSchemeHttps));
        Assert.True(Uri.TryCreate(AppBrand.PrSignatureUrl, UriKind.Absolute, out var prSigUri) && (prSigUri.Scheme == Uri.UriSchemeHttp || prSigUri.Scheme == Uri.UriSchemeHttps));
    }

    [Fact]
    public void Constants_Urls_MatchAppBrand()
    {
        Assert.Equal(AppBrand.DocsUrl, Constants.DocsUrl);
        Assert.Equal(AppBrand.DiscordUrl, Constants.DiscordUrl);
        Assert.Equal(AppBrand.IssuesUrl, Constants.IssuesUrl);
    }
}
