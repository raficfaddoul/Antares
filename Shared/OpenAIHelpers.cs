using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helpers
{
    /// <summary>
    /// Fluent builder for JSON Schemas (draft-07).
    /// </summary>
    public class JsonSchemaBuilder
    {
        private readonly Dictionary<string, object> _schema = new();
        private readonly Dictionary<string, object> _properties = new();
        private readonly List<string> _required = new();

        private JsonSchemaBuilder(string title)
        {
            _schema["$schema"] = "https://json-schema.org/draft-07/schema";
            _schema["title"] = title;
            _schema["type"] = "object";
            _schema["additionalProperties"] = false;
        }

        /// <summary>
        /// Start a new schema with the given title.
        /// </summary>
        public static JsonSchemaBuilder Create(string title) => new JsonSchemaBuilder(title);

        public JsonSchemaBuilder SchemaDescription(string description)
        {
            if (!string.IsNullOrWhiteSpace(description))
                _schema["description"] = description;
            return this;
        }

        /// <summary>
        /// Add a simple property (string, number, etc.).
        /// </summary>
        /// 
        public JsonSchemaBuilder Property(
    string name,
    string type,
    string? format = null,
    bool isRequired = true,
    string? description = null)
        {
            var def = new Dictionary<string, object> { ["type"] = type };
            if (!string.IsNullOrWhiteSpace(format)) def["format"] = format;
            if (!string.IsNullOrWhiteSpace(description)) def["description"] = description;

            _properties[name] = def;
            if (isRequired) _required.Add(name);
            return this;
        }



        public JsonSchemaBuilder PropertyWithPattern(
            string name,
            string type,
            string pattern,
            bool isRequired = true,
            string? description = null)
        {
            var def = new Dictionary<string, object>
            {
                ["type"] = type,
                ["pattern"] = pattern
            };
            if (!string.IsNullOrWhiteSpace(description)) def["description"] = description;

            _properties[name] = def;
            if (isRequired) _required.Add(name);
            return this;
        }

        /// <summary>
        /// Add an enum property for arbitrary string values.
        /// </summary>
        public JsonSchemaBuilder EnumProperty(
            string name,
            IEnumerable<string> enumValues,
            bool isRequired = true,
            string? description = null)
        {
            var def = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["enum"] = enumValues.ToArray()
            };
            if (!string.IsNullOrWhiteSpace(description)) def["description"] = description;

            _properties[name] = def;
            if (isRequired) _required.Add(name);
            return this;
        }

        /// <summary>
        /// Add an array property where each item is constrained to a predefined list of strings.
        /// This allows OpenAI to return multiple values from a predefined set.
        /// </summary>
        public JsonSchemaBuilder ArrayOfEnumProperty(
            string name,
            IEnumerable<string> allowedValues,
            bool isRequired = true,
            string? description = null,
            int? minItems = null,
            int? maxItems = null
            )
        {
            var itemDef = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["enum"] = allowedValues.ToArray()
            };

            var def = new Dictionary<string, object>
            {
                ["type"] = "array",
                ["items"] = itemDef
            };

            if (!string.IsNullOrWhiteSpace(description)) def["description"] = description;
            if (minItems is not null) def["minItems"] = minItems;
            if (maxItems is not null) def["maxItems"] = maxItems;
         

            _properties[name] = def;
            if (isRequired) _required.Add(name);
            return this;
        }


        /// <summary>
        /// Add an array of primitives property.
        /// </summary>
        public JsonSchemaBuilder ArrayProperty(
            string name,
            string itemType,
            string? itemFormat = null,
            bool isRequired = true,
            string? description = null,
            // item-level numeric bounds (apply when itemType is "number" or "integer")
            decimal? itemMinimum = null,
            decimal? itemMaximum = null,
            decimal? itemExclusiveMinimum = null,
            decimal? itemExclusiveMaximum = null,
            decimal? itemMultipleOf = null,
            // array-level constraints (optional)
            int? minItems = null,
            int? maxItems = null,
            bool? uniqueItems = null)
        {
            var itemDef = new Dictionary<string, object> { ["type"] = itemType };
            if (!string.IsNullOrWhiteSpace(itemFormat)) itemDef["format"] = itemFormat;

            // Only add numeric constraints if they’re supplied
            if (itemMinimum is not null) itemDef["minimum"] = itemMinimum;
            if (itemMaximum is not null) itemDef["maximum"] = itemMaximum;
            if (itemExclusiveMinimum is not null) itemDef["exclusiveMinimum"] = itemExclusiveMinimum;
            if (itemExclusiveMaximum is not null) itemDef["exclusiveMaximum"] = itemExclusiveMaximum;
            if (itemMultipleOf is not null) itemDef["multipleOf"] = itemMultipleOf;

            var def = new Dictionary<string, object>
            {
                ["type"] = "array",
                ["items"] = itemDef
            };
            if (!string.IsNullOrWhiteSpace(description)) def["description"] = description;
            if (minItems is not null) def["minItems"] = minItems;
            if (maxItems is not null) def["maxItems"] = maxItems;
            if (uniqueItems is not null) def["uniqueItems"] = uniqueItems;

            _properties[name] = def;
            if (isRequired) _required.Add(name);
            return this;
        }



        /// <summary>
        /// Add an array property of objects defined by a nested schema.
        /// </summary>
        public JsonSchemaBuilder ArrayProperty(
            string name,
            Action<JsonSchemaBuilder> configureItems,
            bool isRequired = true,
            string? description = null,
            string? itemDescription = null)
        {
            var itemBuilder = new JsonSchemaBuilder(name + "Item");
            configureItems(itemBuilder);

            var itemDef = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = itemBuilder._properties,
                ["required"] = itemBuilder._required,
                ["additionalProperties"] = false
            };
            if (!string.IsNullOrWhiteSpace(itemDescription)) itemDef["description"] = itemDescription;

            var def = new Dictionary<string, object>
            {
                ["type"] = "array",
                ["items"] = itemDef
            };
            if (!string.IsNullOrWhiteSpace(description)) def["description"] = description;

            _properties[name] = def;
            if (isRequired) _required.Add(name);
            return this;
        }



        public JsonSchemaBuilder ObjectProperty(
            string name,
            Action<JsonSchemaBuilder> configure,
            bool isRequired = true,
            string? description = null)
        {
            var objBuilder = new JsonSchemaBuilder(name);
            configure(objBuilder);

            var def = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = objBuilder._properties,
                ["required"] = objBuilder._required,
                ["additionalProperties"] = false
            };
            if (!string.IsNullOrWhiteSpace(description)) def["description"] = description;

            _properties[name] = def;
            if (isRequired) _required.Add(name);
            return this;
        }


        /// <summary>
        /// Finalize and serialize the schema to a JSON string (indented).
        /// </summary>
        public string Build()
        {
            _schema["properties"] = _properties;
            if (_required.Any()) _schema["required"] = _required;
            return JsonSerializer.Serialize(_schema, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// Generic converter that maps JSON string values to GUIDs via a lookup dictionary.
    /// </summary>
    public class StringToGuidConverter : JsonConverter<Guid>
    {
        private readonly IReadOnlyDictionary<string, Guid> _map;

        public StringToGuidConverter(IReadOnlyDictionary<string, Guid> map)
        {
            _map = map;
        }

        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("Expected string for GUID conversion.");

            var str = reader.GetString()!;
            if (_map.TryGetValue(str, out var guid))
                return guid;

            if (Guid.TryParse(str, out var parsed))
                return parsed;

            throw new JsonException($"Unknown GUID string '{str}'");
        }

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}




