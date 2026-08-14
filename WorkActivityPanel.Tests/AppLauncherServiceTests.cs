using Microsoft.Extensions.Logging.Abstractions;
using WorkActivityPanel.Services;
using Xunit;

namespace WorkActivityPanel.Tests;

public class AppLauncherServiceTests
{
    private readonly AppLauncherService _service;

    public AppLauncherServiceTests()
    {
        _service = new AppLauncherService(NullLogger<AppLauncherService>.Instance);
    }

    [Fact]
    public void IsSlackRunning_ExecutesWithoutException()
    {
        // Act
        bool isRunning = _service.IsSlackRunning();

        // Assert - Should return boolean without throwing
        Assert.True(isRunning || !isRunning);
    }

    [Fact]
    public void IsGranolaRunning_ExecutesWithoutException()
    {
        // Act
        bool isRunning = _service.IsGranolaRunning();

        // Assert - Should return boolean without throwing
        Assert.True(isRunning || !isRunning);
    }

    [Fact]
    public void GetGranolaExecutablePath_WhenInstalled_ResolvesValidPath()
    {
        // Act
        string? path = _service.GetGranolaExecutablePath();

        // If Granola is installed in the test environment, verify the resolved path exists
        if (path != null)
        {
            Assert.True(File.Exists(path), $"Resolved Granola path does not exist: {path}");
            Assert.EndsWith("Granola.exe", path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
