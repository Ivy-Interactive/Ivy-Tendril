using System.Text.Json.Serialization;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionInitWire))]
[JsonSerializable(typeof(TextWire))]
[JsonSerializable(typeof(ThinkingWire))]
[JsonSerializable(typeof(ToolCallWire))]
[JsonSerializable(typeof(ToolResultWire))]
[JsonSerializable(typeof(PermissionRequestWire))]
[JsonSerializable(typeof(PermissionDenialWire))]
[JsonSerializable(typeof(ErrorWire))]
[JsonSerializable(typeof(ResultWire))]
[JsonSerializable(typeof(FileChangeWire))]
[JsonSerializable(typeof(UserQuestionWire))]
[JsonSerializable(typeof(QuestionOptionWire))]
[JsonSerializable(typeof(UsageWire))]
public partial class AgentJsonContext : JsonSerializerContext;
