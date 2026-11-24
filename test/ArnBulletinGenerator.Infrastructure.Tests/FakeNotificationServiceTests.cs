using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Fakes;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests;

public sealed class FakeNotificationServiceTests
{
    private readonly FakeExternalCallLog _callLog = new();
    private readonly RecordingLogger<FakeNotificationService> _logger = new();
    private readonly FakeNotificationService _sut;

    public FakeNotificationServiceTests()
    {
        _sut = new FakeNotificationService(_callLog, _logger);
    }

    [Fact]
    public async Task SendAlertNotificationAsync_Critical_EmitsE2EAlertMarker()
    {
        await _sut.SendAlertNotificationAsync(
            "SYD", "2GB", "Broadcast job j-1 failed after max dequeue.", AlertSeverity.Critical);

        _logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Critical &&
            e.Message.Contains("[E2E-Alert]", StringComparison.Ordinal) &&
            e.Message.Contains("market=SYD", StringComparison.Ordinal) &&
            e.Message.Contains("station=2GB", StringComparison.Ordinal) &&
            e.Message.Contains("severity=Critical", StringComparison.Ordinal) &&
            e.Message.Contains("message=Broadcast job j-1 failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAlertNotificationAsync_Critical_StillAppendsToCallLog()
    {
        await _sut.SendAlertNotificationAsync(
            "SYD", "2GB", "Failed.", AlertSeverity.Critical);

        _callLog.Entries.ShouldContain(e => e.Contains("notification alert market=SYD station=2GB severity=Critical"));
    }

    [Fact]
    public async Task SendAlertNotificationAsync_Critical_StillReturnsSuccess()
    {
        var result = await _sut.SendAlertNotificationAsync(
            "SYD", "2GB", "Failed.", AlertSeverity.Critical);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task SendAlertNotificationAsync_NonCritical_DoesNotEmitAlertMarker()
    {
        await _sut.SendAlertNotificationAsync(
            "SYD", "2GB", "Info message.", AlertSeverity.Warning);

        _logger.Entries.ShouldBeEmpty(
            "Non-critical alerts should not emit [E2E-Alert] markers");
    }

    [Fact]
    public async Task SendAlertNotificationAsync_NonCritical_StillAppendsToCallLog()
    {
        await _sut.SendAlertNotificationAsync(
            "SYD", "2GB", "Info.", AlertSeverity.Warning);

        _callLog.Entries.ShouldContain(e => e.Contains("notification alert market=SYD station=2GB severity=Warning"));
    }

    [Fact]
    public async Task SendJobNotificationAsync_StillAppendsToCallLog()
    {
        var job = Job.Create("j-test", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;

        await _sut.SendJobNotificationAsync(job, NotificationKind.ReadyForReview);

        _callLog.Entries.ShouldContain(e =>
            e.Contains("notification job=j-test") && e.Contains("kind=ReadyForReview"));
    }

    [Fact]
    public async Task SendJobNotificationAsync_DoesNotEmitAlertMarker()
    {
        var job = Job.Create("j-test2", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;

        await _sut.SendJobNotificationAsync(job);

        _logger.Entries.ShouldBeEmpty(
            "Job notifications should not emit [E2E-Alert] markers");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
