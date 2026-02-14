using GatherWin.Services;

namespace GatherWin.Tests.Services;

public class AppLoggerTests
{
    [Fact]
    public void Log_DoesNotThrow()
    {
        // AppLogger is a static class that writes to a file.
        // This test verifies it doesn't throw, even if the file is inaccessible.
        var exception = Record.Exception(() => AppLogger.Log("Test message"));
        Assert.Null(exception);
    }

    [Fact]
    public void Log_WithCategory_DoesNotThrow()
    {
        var exception = Record.Exception(() => AppLogger.Log("TestCategory", "Test message"));
        Assert.Null(exception);
    }

    [Fact]
    public void LogError_WithException_DoesNotThrow()
    {
        var ex = new InvalidOperationException("test error");
        var exception = Record.Exception(() => AppLogger.LogError("Test error", ex));
        Assert.Null(exception);
    }

    [Fact]
    public void LogError_WithoutException_DoesNotThrow()
    {
        var exception = Record.Exception(() => AppLogger.LogError("Test error message"));
        Assert.Null(exception);
    }
}
