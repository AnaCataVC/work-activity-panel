using System.Collections.Generic;
using System.Runtime.Versioning;
using WorkActivityPanel.Helpers;
using Xunit;

namespace WorkActivityPanel.Tests;

[SupportedOSPlatform("windows")]
public class AutostartHelperTests
{
    [Fact]
    public void BuildAutostartCommandLine_FormatsCorrectlyWithQuotesAndArgument()
    {
        var exePath = @"C:\Program Files\WorkActivityPanel\WorkActivityPanel.exe";
        var result = AutostartHelper.BuildAutostartCommandLine(exePath);

        Assert.Equal("\"C:\\Program Files\\WorkActivityPanel\\WorkActivityPanel.exe\" --autostart", result);
    }

    [Theory]
    [InlineData(new[] { "WorkActivityPanel.exe", "--autostart" }, true)]
    [InlineData(new[] { "WorkActivityPanel.exe", "--AUTOSTART" }, true)]
    [InlineData(new[] { "--autostart" }, true)]
    [InlineData(new[] { "WorkActivityPanel.exe" }, false)]
    [InlineData(new[] { "WorkActivityPanel.exe", "--other-flag" }, false)]
    [InlineData(new string[0], false)]
    [InlineData(null, false)]
    public void HasAutostartArgument_DetectsFlagCorrectly(string[]? args, bool expected)
    {
        var result = AutostartHelper.HasAutostartArgument(args);
        Assert.Equal(expected, result);
    }
}
