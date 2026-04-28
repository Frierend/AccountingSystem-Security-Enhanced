using System.Text.Json;
using System.Text.Json.Nodes;

namespace AccountingSystem.API.Services
{
    public static class AuditLogSanitizer
    {
        private const int DefaultMaxLength = 2000;
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static string SerializeAndTrim(object metadata, int maxLength = DefaultMaxLength)
        {
            var serializedNode = JsonSerializer.SerializeToNode(metadata, SerializerOptions);
            var sanitizedNode = SanitizeNode(serializedNode);
            var serialized = sanitizedNode?.ToJsonString(SerializerOptions) ?? "{}";

            if (serialized.Length <= maxLength)
            {
                return serialized;
            }

            var previewLength = Math.Min(256, serialized.Length);
            var truncatedPayload = new JsonObject
            {
                ["truncated"] = true,
                ["originalLength"] = serialized.Length,
                ["preview"] = serialized[..previewLength]
            };

            var truncatedSerialized = truncatedPayload.ToJsonString(SerializerOptions);
            if (truncatedSerialized.Length <= maxLength)
            {
                return truncatedSerialized;
            }

            return truncatedSerialized[..maxLength];
        }

        public static object CreateRequestPayloadSummary(string payload, string? contentType)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new
                {
                    hasBody = false
                };
            }

            var summary = new JsonObject
            {
                ["hasBody"] = true,
                ["contentType"] = contentType ?? "unknown",
                ["size"] = payload.Length
            };

            if (!LooksLikeJson(contentType, payload))
            {
                summary["format"] = "text";
                return summary;
            }

            try
            {
                var parsedPayload = JsonNode.Parse(payload);
                summary["format"] = "json";

                if (parsedPayload is JsonObject jsonObject)
                {
                    summary["topLevelType"] = "object";
                    summary["propertyCount"] = jsonObject.Count;
                    summary["sensitiveFieldCount"] = CountSensitiveFields(jsonObject);
                }
                else if (parsedPayload is JsonArray jsonArray)
                {
                    summary["topLevelType"] = "array";
                    summary["itemCount"] = jsonArray.Count;
                    summary["sensitiveFieldCount"] = CountSensitiveFields(jsonArray);
                }
                else
                {
                    summary["topLevelType"] = "value";
                    summary["sensitiveFieldCount"] = 0;
                }
            }
            catch (JsonException)
            {
                summary["format"] = "text";
            }

            return summary;
        }

        public static bool IsSecurityAction(string? action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            return action.StartsWith("AUTH-", StringComparison.OrdinalIgnoreCase) ||
                   action.StartsWith("SECURITY-", StringComparison.OrdinalIgnoreCase);
        }

        public static string DeriveCategory(string? action)
        {
            return IsSecurityAction(action) ? "Security" : "System";
        }

        private static JsonNode? SanitizeNode(JsonNode? node, string? propertyName = null)
        {
            if (node == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(propertyName) && IsSensitiveKey(propertyName))
            {
                return JsonValue.Create("[REDACTED]");
            }

            if (node is JsonObject jsonObject)
            {
                var sanitizedObject = new JsonObject();
                foreach (var pair in jsonObject)
                {
                    sanitizedObject[pair.Key] = SanitizeNode(pair.Value, pair.Key);
                }

                return sanitizedObject;
            }

            if (node is JsonArray jsonArray)
            {
                var sanitizedArray = new JsonArray();
                foreach (var item in jsonArray)
                {
                    sanitizedArray.Add(SanitizeNode(item));
                }

                return sanitizedArray;
            }

            return node.DeepClone();
        }

        private static int CountSensitiveFields(JsonNode node)
        {
            var count = 0;

            if (node is JsonObject jsonObject)
            {
                foreach (var pair in jsonObject)
                {
                    if (IsSensitiveKey(pair.Key))
                    {
                        count++;
                    }

                    if (pair.Value != null)
                    {
                        count += CountSensitiveFields(pair.Value);
                    }
                }
            }
            else if (node is JsonArray jsonArray)
            {
                foreach (var item in jsonArray)
                {
                    if (item != null)
                    {
                        count += CountSensitiveFields(item);
                    }
                }
            }

            return count;
        }

        private static bool LooksLikeJson(string? contentType, string payload)
        {
            if (!string.IsNullOrWhiteSpace(contentType) &&
                contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var trimmedPayload = payload.TrimStart();
            return trimmedPayload.StartsWith('{') || trimmedPayload.StartsWith('[');
        }

        private static bool IsSensitiveKey(string key)
        {
            var normalized = NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (normalized.Contains("password", StringComparison.Ordinal) ||
                normalized.Contains("token", StringComparison.Ordinal) ||
                normalized.Contains("authorization", StringComparison.Ordinal) ||
                normalized.Contains("secret", StringComparison.Ordinal) ||
                normalized.Contains("apikey", StringComparison.Ordinal) ||
                normalized.Contains("smtpassword", StringComparison.Ordinal) ||
                normalized.Contains("paymongosecret", StringComparison.Ordinal) ||
                normalized.Contains("recaptchasecret", StringComparison.Ordinal))
            {
                return true;
            }

            return normalized == "key" || normalized.EndsWith("key", StringComparison.Ordinal);
        }

        private static string NormalizeKey(string key)
        {
            return new string(key
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }
    }
}
