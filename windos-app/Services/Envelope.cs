using System.Text.Json;

namespace AgriGis.Desktop.Services;

// WebGIS との通信 envelope。WebGIS 側 bridge/messages.ts と命名一致。
public sealed record Envelope(string Type, JsonElement Payload, string? RequestId);
