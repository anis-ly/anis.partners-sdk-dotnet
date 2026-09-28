using System.Diagnostics;
using System.Diagnostics.Metrics;
using Anis.Partners.Sdk.Observability;
using Microsoft.Extensions.Logging;

namespace Anis.Partners.Sdk.Tests;

/// <summary>Captures every log line, span tag and metric tag the SDK emits during a block of work.</summary>
internal sealed class TelemetryCapture : IDisposable
{
    private readonly ActivityListener _activities;
    private readonly MeterListener _meters;

    public TelemetryCapture()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AnisPartnersTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (Spans)
                {
                    Spans.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(_activities);

        _meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AnisPartnersTelemetry.Name)
                    listener.EnableMeasurementEvents(instrument);
            },
        };

        _meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meters.Start();
    }

    public List<Activity> Spans { get; } = [];

    public List<(string Instrument, double Value, Dictionary<string, string?> Tags)> Measurements { get; } = [];

    public LoggerFactory Loggers { get; } = new([new CapturingProvider()]);

    public List<string> LogLines => CapturingProvider.Lines;

    /// <summary>Everything the SDK emitted, as one blob to search for things that must not be in it.</summary>
    public string Everything
    {
        get
        {
            var parts = new List<string>(LogLines);

            foreach (var span in Spans)
            {
                parts.Add(span.DisplayName);
                parts.AddRange(span.Tags.Select(tag => $"{tag.Key}={tag.Value}"));
            }

            foreach (var measurement in Measurements)
            {
                parts.Add(measurement.Instrument);
                parts.AddRange(measurement.Tags.Select(tag => $"{tag.Key}={tag.Value}"));
            }

            return string.Join("\n", parts);
        }
    }

    public void Dispose()
    {
        _activities.Dispose();
        _meters.Dispose();
        Loggers.Dispose();
        CapturingProvider.Lines.Clear();
    }

    private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct
    {
        var captured = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var tag in tags)
            captured[tag.Key] = tag.Value?.ToString();

        lock (Measurements)
        {
            Measurements.Add((instrument.Name, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), captured));
        }
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        public static List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger();

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);

                lock (Lines)
                {
                    // The rendered message AND the structured state, because a secret could hide in a
                    // property that the template never prints.
                    Lines.Add($"{logLevel} {eventId.Id} {formatter(state, exception)} :: {state}");
                }
            }
        }
    }
}
