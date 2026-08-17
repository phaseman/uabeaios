using System.Text.Json.Nodes;
using AssetsTools.NET;

namespace PvZAssetEditor.Core;

internal static class AssetFieldJson
{
    public static JsonNode ToJson(AssetTypeValueField field)
    {
        AssetTypeTemplateField template = field.TemplateField;

        if (template.IsArray)
        {
            var array = new JsonArray();
            if (template.ValueType == AssetValueType.ByteArray)
            {
                foreach (byte value in field.AsByteArray)
                    array.Add(value);
            }
            else
            {
                foreach (AssetTypeValueField child in field.Children)
                    array.Add(ToJson(child));
            }

            return array;
        }

        if (field.Value is not null)
        {
            return field.Value.ValueType switch
            {
                AssetValueType.Bool => JsonValue.Create(field.AsBool)!,
                AssetValueType.Int8 => JsonValue.Create(field.AsSByte)!,
                AssetValueType.UInt8 => JsonValue.Create(field.AsByte)!,
                AssetValueType.Int16 => JsonValue.Create(field.AsShort)!,
                AssetValueType.UInt16 => JsonValue.Create(field.AsUShort)!,
                AssetValueType.Int32 => JsonValue.Create(field.AsInt)!,
                AssetValueType.UInt32 => JsonValue.Create(field.AsUInt)!,
                AssetValueType.Int64 => JsonValue.Create(field.AsLong)!,
                AssetValueType.UInt64 => JsonValue.Create(field.AsULong)!,
                AssetValueType.Float => JsonValue.Create(field.AsFloat)!,
                AssetValueType.Double => JsonValue.Create(field.AsDouble)!,
                AssetValueType.String => JsonValue.Create(field.AsString)!,
                AssetValueType.ByteArray => BytesToJson(field.AsByteArray),
                AssetValueType.ManagedReferencesRegistry => throw new NotSupportedException(
                    "Managed-reference fields are not supported by the mobile editor yet."),
                _ => throw new NotSupportedException($"Unsupported Unity value type: {field.Value.ValueType}.")
            };
        }

        var result = new JsonObject();
        foreach (AssetTypeValueField child in field.Children)
            result[child.FieldName] = ToJson(child);

        return result;
    }

    public static byte[] Write(AssetTypeTemplateField template, JsonNode node, bool bigEndian)
    {
        using var stream = new MemoryStream();
        using var writer = new AssetsFileWriter(stream) { BigEndian = bigEndian };
        WriteField(template, node, writer);
        return stream.ToArray();
    }

    private static void WriteField(AssetTypeTemplateField template, JsonNode node, AssetsFileWriter writer)
    {
        bool align = template.IsAligned;

        if (!template.HasValue && !template.IsArray)
        {
            JsonObject obj = node as JsonObject
                ?? throw new InvalidDataException($"Expected an object for {template.Type} {template.Name}.");

            foreach (AssetTypeTemplateField child in template.Children)
            {
                JsonNode? childNode = obj[child.Name];
                if (childNode is null)
                    throw new InvalidDataException($"Missing field {child.Name} in {template.Name}.");

                WriteField(child, childNode, writer);
            }

            if (align)
                writer.Align();
            return;
        }

        if (template.IsArray && template.ValueType != AssetValueType.ByteArray)
        {
            JsonArray array = node as JsonArray
                ?? throw new InvalidDataException($"Expected an array for {template.Name}.");

            writer.Write(array.Count);
            AssetTypeTemplateField itemTemplate = template.Children[1];
            foreach (JsonNode? item in array)
                WriteField(itemTemplate, item ?? throw new InvalidDataException("Array item cannot be null."), writer);

            if (align)
                writer.Align();
            return;
        }

        switch (template.ValueType)
        {
            case AssetValueType.Bool:
                writer.Write(node.GetValue<bool>());
                break;
            case AssetValueType.Int8:
                writer.Write(node.GetValue<sbyte>());
                break;
            case AssetValueType.UInt8:
                writer.Write(node.GetValue<byte>());
                break;
            case AssetValueType.Int16:
                writer.Write(node.GetValue<short>());
                break;
            case AssetValueType.UInt16:
                writer.Write(node.GetValue<ushort>());
                break;
            case AssetValueType.Int32:
                writer.Write(node.GetValue<int>());
                break;
            case AssetValueType.UInt32:
                writer.Write(node.GetValue<uint>());
                break;
            case AssetValueType.Int64:
                writer.Write(node.GetValue<long>());
                break;
            case AssetValueType.UInt64:
                writer.Write(node.GetValue<ulong>());
                break;
            case AssetValueType.Float:
                writer.Write(node.GetValue<float>());
                break;
            case AssetValueType.Double:
                writer.Write(node.GetValue<double>());
                break;
            case AssetValueType.String:
                writer.WriteCountStringInt32(node.GetValue<string>());
                align = true;
                break;
            case AssetValueType.ByteArray:
            {
                JsonArray array = node as JsonArray
                    ?? throw new InvalidDataException($"Expected a byte array for {template.Name}.");
                writer.Write(array.Count);
                foreach (JsonNode? item in array)
                    writer.Write(item?.GetValue<byte>() ?? throw new InvalidDataException("Byte item cannot be null."));
                break;
            }
            case AssetValueType.ManagedReferencesRegistry:
                throw new NotSupportedException("Managed-reference fields are not supported by the mobile editor yet.");
            default:
                throw new NotSupportedException($"Unsupported Unity value type: {template.ValueType}.");
        }

        if (align)
            writer.Align();
    }

    private static JsonArray BytesToJson(byte[] bytes)
    {
        var array = new JsonArray();
        foreach (byte value in bytes)
            array.Add(value);
        return array;
    }
}
