using Microsoft.Extensions.Logging.Abstractions;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services;
using Xunit;

namespace WorkActivityPanel.Tests;

public class GitHubAuthServiceTests
{
    private readonly GitHubAuthService _service;

    public GitHubAuthServiceTests()
    {
        _service = new GitHubAuthService(NullLogger<GitHubAuthService>.Instance);
    }

    [Fact]
    public void ParseHostsYaml_WithMultipleAccounts_CorrectlyIdentifiesActiveAndAvailableAccounts()
    {
        // Arrange
        string sampleYaml = """
            github.com:
                git_protocol: https
                users:
                    CataVillalobosC:
                    AnaCataVC:
                user: AnaCataVC
            """;

        // Act
        var result = _service.ParseHostsYaml(sampleYaml);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsGhInstalled);
        Assert.Equal("AnaCataVC", result.ActiveAccount);
        Assert.Contains("AnaCataVC", result.AvailableAccounts);
        Assert.Contains("CataVillalobosC", result.AvailableAccounts);
        Assert.Equal(2, result.AvailableAccounts.Count);
        Assert.True(result.IsAuthenticated);
        Assert.True(result.HasMultipleAccounts);
    }

    [Fact]
    public void ParseHostsYaml_WithAlternateActiveUser_IdentifiesActiveUser()
    {
        // Arrange
        string sampleYaml = """
            github.com:
                git_protocol: https
                users:
                    CataVillalobosC:
                    AnaCataVC:
                user: CataVillalobosC
            """;

        // Act
        var result = _service.ParseHostsYaml(sampleYaml);

        // Assert
        Assert.Equal("CataVillalobosC", result.ActiveAccount);
        Assert.Equal(2, result.AvailableAccounts.Count);
        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public void ParseHostsYaml_WithEmptyOrNullYaml_ReturnsEmptyInfo()
    {
        // Act
        var resultEmpty = _service.ParseHostsYaml(string.Empty);
        var resultNull = _service.ParseHostsYaml(null!);

        // Assert
        Assert.Null(resultEmpty.ActiveAccount);
        Assert.Empty(resultEmpty.AvailableAccounts);
        Assert.False(resultEmpty.IsAuthenticated);

        Assert.Null(resultNull.ActiveAccount);
        Assert.Empty(resultNull.AvailableAccounts);
        Assert.False(resultNull.IsAuthenticated);
    }

    [Fact]
    public void ParseHostsYaml_SingleAccountWithoutExplicitUser_DefaultsToSingleAccount()
    {
        // Arrange
        string sampleYaml = """
            github.com:
                git_protocol: https
                users:
                    AnaCataVC:
            """;

        // Act
        var result = _service.ParseHostsYaml(sampleYaml);

        // Assert
        Assert.Equal("AnaCataVC", result.ActiveAccount);
        Assert.Single(result.AvailableAccounts);
        Assert.True(result.IsAuthenticated);
        Assert.False(result.HasMultipleAccounts);
    }

    [Fact]
    public void GitHubAccountInfo_ComputedProperties_WorkCorrectly()
    {
        // Not installed
        var notInstalled = new GitHubAccountInfo { IsGhInstalled = false, ActiveAccount = "AnaCataVC" };
        Assert.False(notInstalled.IsAuthenticated);

        // Installed but no account
        var noAccount = new GitHubAccountInfo { IsGhInstalled = true, ActiveAccount = null };
        Assert.False(noAccount.IsAuthenticated);

        // Single account
        var single = new GitHubAccountInfo
        {
            IsGhInstalled = true,
            ActiveAccount = "AnaCataVC",
            AvailableAccounts = { "AnaCataVC" }
        };
        Assert.True(single.IsAuthenticated);
        Assert.False(single.HasMultipleAccounts);

        // Multiple accounts
        var multiple = new GitHubAccountInfo
        {
            IsGhInstalled = true,
            ActiveAccount = "AnaCataVC",
            AvailableAccounts = { "AnaCataVC", "CataVillalobosC" }
        };
        Assert.True(multiple.IsAuthenticated);
        Assert.True(multiple.HasMultipleAccounts);
    }

    [Fact]
    public async Task SwitchAccountAsync_WithEmptyUsername_ReturnsFailure()
    {
        // Act
        var result = await _service.SwitchAccountAsync(string.Empty);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("vacío", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
