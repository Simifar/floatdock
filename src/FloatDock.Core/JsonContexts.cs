using System.Text.Json.Serialization;

namespace FloatDock.Core;

/// <summary>One line of the local log (the property names are the ones written to the file); the pid tells the dock's lines from Guardian's.</summary>
public sealed record LogEntry(
    [property: JsonPropertyName("time")] DateTimeOffset Time,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("eventName")] string EventName,
    [property: JsonPropertyName("errorType")] string? ErrorType,
    [property: JsonPropertyName("hresult")] int? HResult,
    [property: JsonPropertyName("pid")] int ProcessId);

/// <summary>
/// JSON by source generation for the files the recovery helper (Guardian) reads and writes: it is compiled ahead of time,
/// so nothing it touches can use reflection. The settings file stays on reflection, see <see cref="SettingsStore"/>.
/// </summary>
[JsonSerializable(typeof(ShellState))]
[JsonSerializable(typeof(AppBarRegistration))]
[JsonSerializable(typeof(List<AppBarRegistration>))]
[JsonSerializable(typeof(List<DateTimeOffset>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(Dictionary<string, long>))]
[JsonSerializable(typeof(List<AppEntry>))]
[JsonSerializable(typeof(LogEntry))]
public sealed partial class StateJson : JsonSerializerContext;
