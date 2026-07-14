using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace WojoPersistentEditor.Models
{
    public class PersistentDecodeResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("root_type")]
        public string RootType { get; set; } = string.Empty;

        [JsonPropertyName("unknown_classes")]
        public List<string> UnknownClasses { get; set; } = new List<string>();

        [JsonPropertyName("variables")]
        public List<PersistentVariable> Variables { get; set; } =
            new List<PersistentVariable>();

        [JsonPropertyName("error")]
        public PythonError? Error { get; set; }
    }

    public class PersistentVariable
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("is_editable")]
        public bool IsEditable { get; set; }

        [JsonPropertyName("is_collection")]
        public bool IsCollection { get; set; }

        [JsonPropertyName("values")]
        public List<PersistentEditorValue> Values { get; set; } =
            new List<PersistentEditorValue>();

        public bool HasChanged()
        {
            return IsEditable && Values.Any(value =>
                !string.Equals(value.Text, value.OriginalText)
            );
        }
    }

    public class PersistentEditorValue
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonIgnore]
        public string OriginalText { get; set; } = string.Empty;
    }

    public class PythonError
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class PersistentChangesPayload
    {
        [JsonPropertyName("changes")]
        public List<PersistentVariable> Changes { get; set; } =
            new List<PersistentVariable>();
    }

    public class PersistentEncodeResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("output")]
        public string Output { get; set; } = string.Empty;

        [JsonPropertyName("applied_changes")]
        public int AppliedChanges { get; set; }

        [JsonPropertyName("error")]
        public PythonError? Error { get; set; }
    }
}
