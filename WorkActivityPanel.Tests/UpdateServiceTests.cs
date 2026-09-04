using System;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services;
using Xunit;

namespace WorkActivityPanel.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("V2.0.1", "2.0.1")]
    [InlineData("1.1.0", "1.1.0")]
    [InlineData("  v3.0.0  ", "3.0.0")]
    [InlineData("", "0.0.0")]
    public void NormalizeVersionString_StripsPrefixesAndWhitespace(string raw, string expected)
    {
        var result = UpdateService.NormalizeVersionString(raw);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1.1.0", "v1.2.0", true)]
    [InlineData("1.1.0", "1.1.1", true)]
    [InlineData("1.1.0", "v2.0.0", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("1.2.0", "1.1.0", false)]
    [InlineData("2.0.0", "v1.9.9", false)]
    [InlineData("1.0.0", "1.0.0", false)]
    public void IsNewerVersion_EvaluatesCorrectly(string currentVersion, string latestVersion, bool expectedResult)
    {
        var isNewer = UpdateService.IsNewerVersion(currentVersion, latestVersion);
        Assert.Equal(expectedResult, isNewer);
    }

    [Fact]
    public void UpdateInfo_IsSuccess_TrueWhenNoErrorMessage()
    {
        var info = new UpdateInfo
        {
            CurrentVersion = "1.1.0",
            LatestVersion = "1.2.0",
            IsUpdateAvailable = true,
            DownloadUrl = "https://github.com/AnaCataVC/work-activity-panel/releases/download/v1.2.0/WorkActivityPanel-Setup-v1.2.0.exe"
        };

        Assert.True(info.IsSuccess);
        Assert.True(info.IsUpdateAvailable);
    }

    [Fact]
    public void UpdateInfo_IsSuccess_FalseWhenErrorMessagePresent()
    {
        var info = new UpdateInfo
        {
            CurrentVersion = "1.1.0",
            ErrorMessage = "Network timeout"
        };
        Assert.False(info.IsSuccess);
    }

    [Fact]
    public void CurrentAppVersion_ReturnsCorrectVersion()
    {
        var service = new UpdateService();
        Assert.Equal("2.0.0", service.CurrentAppVersion);
    }
}

