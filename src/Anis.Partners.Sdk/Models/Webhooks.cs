using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anis.Partners.Sdk.Models;

/// <summary>The current endpoint registration state.</summary>
[JsonConverter(typeof(LenientEnumConverter<WebhookEndpointStatus>))]
public enum WebhookEndpointStatus
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>NotConfigured.</summary>
    NotConfigured = 1,

    /// <summary>PendingVerification.</summary>
    PendingVerification = 2,

    /// <summary>Verified.</summary>
    Verified = 3,

    /// <summary>Disabled.</summary>
    Disabled = 4,
}

/// <summary>The current delivery state.</summary>
[JsonConverter(typeof(LenientEnumConverter<WebhookDeliveryStatus>))]
public enum WebhookDeliveryStatus
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>Pending.</summary>
    Pending = 1,

    /// <summary>Delivering.</summary>
    Delivering = 2,

    /// <summary>Delivered.</summary>
    Delivered = 3,

    /// <summary>Retrying.</summary>
    Retrying = 4,

    /// <summary>Failed.</summary>
    Failed = 5,

    /// <summary>WaitingForProjection.</summary>
    WaitingForProjection = 6,

    /// <summary>WaitingForEndpoint.</summary>
    WaitingForEndpoint = 7,

    /// <summary>Cancelled.</summary>
    Cancelled = 8,
}

/// <summary>Why the last delivery attempt failed.</summary>
[JsonConverter(typeof(LenientEnumConverter<WebhookFailureKind>))]
public enum WebhookFailureKind
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>HttpStatus.</summary>
    HttpStatus = 1,

    /// <summary>Timeout.</summary>
    Timeout = 2,

    /// <summary>Connect.</summary>
    Connect = 3,

    /// <summary>Tls.</summary>
    Tls = 4,

    /// <summary>TargetRefused.</summary>
    TargetRefused = 5,

    /// <summary>ResponseTooLarge.</summary>
    ResponseTooLarge = 6,

    /// <summary>ChallengeMismatch.</summary>
    ChallengeMismatch = 7,

    /// <summary>ProjectionUnavailable.</summary>
    ProjectionUnavailable = 8,

    /// <summary>EndpointDisabled.</summary>
    EndpointDisabled = 9,

    /// <summary>SigningUnavailable.</summary>
    SigningUnavailable = 10,
}

/// <summary>An operational or control notification type.</summary>
[JsonConverter(typeof(WebhookEventTypeConverter))]
public enum WebhookEventType
{
    /// <summary>A value this SDK does not recognize.</summary>
    Unknown = 0,

    /// <summary>MeterChargeSucceeded.</summary>
    MeterChargeSucceeded = 1,

    /// <summary>MeterChargeFailed.</summary>
    MeterChargeFailed = 2,

    /// <summary>DirectRechargePaid.</summary>
    DirectRechargePaid = 3,

    /// <summary>DirectRechargePaymentFailed.</summary>
    DirectRechargePaymentFailed = 4,

    /// <summary>DirectRechargeSuspended.</summary>
    DirectRechargeSuspended = 5,

    /// <summary>DirectRechargeCompleted.</summary>
    DirectRechargeCompleted = 6,

    /// <summary>DirectRechargeRejected.</summary>
    DirectRechargeRejected = 7,

    /// <summary>DirectRechargePartiallyRefunded.</summary>
    DirectRechargePartiallyRefunded = 8,

    /// <summary>EndpointVerification.</summary>
    EndpointVerification = 9,

    /// <summary>EndpointTest.</summary>
    EndpointTest = 10,
}

/// <summary>Registers one externally reachable HTTPS URL.</summary>
public sealed record SetWebhookEndpointRequest
{
    /// <summary>Url.</summary>
    [JsonPropertyName("url")] public required string Url { get; init; }
}

/// <summary>The application endpoint and authoritative registration generation.</summary>
[JsonConverter(typeof(WebhookEndpointConverter))]
public sealed record WebhookEndpoint
{
    /// <summary>Url.</summary>
    [JsonPropertyName("url")] public required string? Url { get; init; }

    /// <summary>Status.</summary>
    [JsonPropertyName("status")] public required WebhookEndpointStatus Status { get; init; }

    /// <summary>Generation.</summary>
    [JsonPropertyName("generation")] public required long? Generation { get; init; }
}

/// <summary>Reads and writes all three endpoint members, emitting an absent url or generation as an explicit null.</summary>
internal sealed class WebhookEndpointConverter : JsonConverter<WebhookEndpoint>
{
    public override WebhookEndpoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("url", out var url) || !root.TryGetProperty("status", out var status)
            || !root.TryGetProperty("generation", out var generation))
            throw new JsonException("The webhook endpoint requires url, status and generation.");
        return new WebhookEndpoint
        {
            Url = url.ValueKind == JsonValueKind.Null ? null : url.GetString(),
            Status = status.Deserialize<WebhookEndpointStatus>(options),
            Generation = generation.ValueKind == JsonValueKind.Null ? null : generation.GetInt64(),
        };
    }

    public override void Write(Utf8JsonWriter writer, WebhookEndpoint value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Url is null)
            writer.WriteNull("url");
        else
            writer.WriteString("url", value.Url);
        writer.WritePropertyName("status");
        JsonSerializer.Serialize(writer, value.Status, options);
        if (value.Generation is null)
            writer.WriteNull("generation");
        else
            writer.WriteNumber("generation", value.Generation.Value);
        writer.WriteEndObject();
    }
}

/// <summary>A queued test or redelivery acknowledgment.</summary>
public sealed record WebhookEventAccepted
{
    /// <summary>Id.</summary>
    [JsonPropertyName("id")] public required Guid Id { get; init; }

    /// <summary>RawType.</summary>
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RawType { get; init; }

    /// <summary>Status.</summary>
    [JsonPropertyName("status")] public required WebhookDeliveryStatus Status { get; init; }

    /// <summary>The known type, when supplied.</summary>
    [JsonIgnore] public WebhookEventType Type => WebhookEventTypeConverter.Parse(RawType);

    /// <summary>QueuedAt.</summary>
    [JsonPropertyName("queuedAt")] public required DateTimeOffset QueuedAt { get; init; }
}

/// <summary>A delivery summary without notification contents or recipient data.</summary>
public sealed record WebhookEvent
{
    /// <summary>Id.</summary>
    [JsonPropertyName("id")] public required Guid Id { get; init; }

    /// <summary>RawType.</summary>
    [JsonPropertyName("type")] public required string RawType { get; init; }

    /// <summary>The known type or Unknown for a future type.</summary>
    [JsonIgnore] public WebhookEventType Type => WebhookEventTypeConverter.Parse(RawType);

    /// <summary>Status.</summary>
    [JsonPropertyName("status")] public required WebhookDeliveryStatus Status { get; init; }

    /// <summary>Attempts.</summary>
    [JsonPropertyName("attempts")] public required int Attempts { get; init; }

    /// <summary>LastAttemptAt.</summary>
    [JsonPropertyName("lastAttemptAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public required DateTimeOffset? LastAttemptAt { get; init; }

    /// <summary>NextAttemptAt.</summary>
    [JsonPropertyName("nextAttemptAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public required DateTimeOffset? NextAttemptAt { get; init; }

    /// <summary>LastResponseCode.</summary>
    [JsonPropertyName("lastResponseCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public required int? LastResponseCode { get; init; }

    /// <summary>LastFailureKind.</summary>
    [JsonPropertyName("lastFailureKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public required WebhookFailureKind? LastFailureKind { get; init; }
}

/// <summary>Optional filters; cursors must be reused with the same filters.</summary>
public sealed record WebhookEventsQuery
{
    /// <summary>Status.</summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WebhookDeliveryStatus? Status { get; init; }

    /// <summary>Since.</summary>
    [JsonPropertyName("since")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? Since { get; init; }

    /// <summary>Cursor.</summary>
    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Cursor { get; init; }
}

/// <summary>A verified registration challenge. Echo only after verification.</summary>
public sealed record WebhookVerificationData
{
    /// <summary>Challenge.</summary>
    [JsonPropertyName("challenge")] public required string Challenge { get; init; }

    /// <summary>Generation.</summary>
    [JsonPropertyName("generation")] public required long Generation { get; init; }

    /// <summary>The challenge is echoed once and never reaches a log through formatting.</summary>
    public override string ToString() => $"WebhookVerificationData {{ Generation = {Generation}, Challenge = <redacted> }}";
}

/// <summary>A verified endpoint test, separate from a money outcome.</summary>
public sealed record WebhookTestData
{
    /// <summary>Message.</summary>
    [JsonPropertyName("message")] public required string Message { get; init; }
}

/// <summary>A verified notification. Durably deduplicate its id before processing or acknowledging.</summary>
public sealed record WebhookEnvelope
{
    /// <summary>The stable event identity, shared by retries.</summary>
    [JsonPropertyName("id")] public required Guid Id { get; init; }

    /// <summary>The exact type, including a future type this SDK does not recognize.</summary>
    [JsonPropertyName("type")] public required string RawType { get; init; }

    /// <summary>The recognized type; Unknown does not imply a financial outcome.</summary>
    [JsonIgnore] public WebhookEventType Type => WebhookEventTypeConverter.Parse(RawType);

    /// <summary>When the triggering event occurred.</summary>
    [JsonPropertyName("occurredAt")] public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The configured application identity verified against the signed header.</summary>
    [JsonPropertyName("applicationId")] public required Guid ApplicationId { get; init; }

    /// <summary>
    /// A typed MeterCharge, DirectRecharge, control data, or JsonElement for a future type.
    /// The current data status is authoritative and can be later than the event type.
    /// </summary>
    [JsonPropertyName("data")] public required object Data { get; init; }

    /// <summary>Diagnostics omit payloads and raw type strings, which can contain personal data.</summary>
    public override string ToString() => $"WebhookEnvelope {{ Id = {Id}, Type = {Type}, Data = <redacted> }}";
}

internal sealed class WebhookEventTypeConverter : JsonConverter<WebhookEventType>
{
    private static readonly Dictionary<string, WebhookEventType> ByWire = new(StringComparer.Ordinal)
    {
        ["meterCharge.succeeded"] = WebhookEventType.MeterChargeSucceeded,
        ["meterCharge.failed"] = WebhookEventType.MeterChargeFailed,
        ["directRecharge.paid"] = WebhookEventType.DirectRechargePaid,
        ["directRecharge.paymentFailed"] = WebhookEventType.DirectRechargePaymentFailed,
        ["directRecharge.suspended"] = WebhookEventType.DirectRechargeSuspended,
        ["directRecharge.completed"] = WebhookEventType.DirectRechargeCompleted,
        ["directRecharge.rejected"] = WebhookEventType.DirectRechargeRejected,
        ["directRecharge.partiallyRefunded"] = WebhookEventType.DirectRechargePartiallyRefunded,
        ["webhook_endpoint.verification"] = WebhookEventType.EndpointVerification,
        ["webhook_endpoint.test"] = WebhookEventType.EndpointTest,
    };

    internal static WebhookEventType Parse(string? value)
        => value is not null && ByWire.TryGetValue(value, out var type) ? type : WebhookEventType.Unknown;

    public override WebhookEventType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String ? Parse(reader.GetString()) : WebhookEventType.Unknown;

    public override void Write(Utf8JsonWriter writer, WebhookEventType value, JsonSerializerOptions options)
        => writer.WriteStringValue(ByWire.FirstOrDefault(pair => pair.Value == value).Key ?? "unknown");
}
