using System.Text.Json;
using System.Text.Json.Nodes;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using PvZAssetEditor.Models;

namespace PvZAssetEditor.Core;

public sealed class RecipeDeckDocument : IDisposable
{
    private readonly AssetsManager _manager;
    private readonly MemoryStream _bundleData;
    private readonly BundleFileInstance _bundleInstance;
    private readonly AssetBundleFile _bundle;
    private readonly AssetsFileInstance _assetsInstance;
    private readonly AssetBundleDirectoryInfo _serializedEntry;
    private readonly IReadOnlyList<GenericAsset> _genericAssets;

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    private RecipeDeckDocument(
        AssetsManager manager,
        MemoryStream bundleData,
        BundleFileInstance bundleInstance,
        AssetBundleFile bundle,
        AssetsFileInstance assetsInstance,
        AssetBundleDirectoryInfo serializedEntry,
        IReadOnlyList<DeckModel> decks,
        IReadOnlyList<GenericAsset> genericAssets,
        string sourceName)
    {
        _manager = manager;
        _bundleData = bundleData;
        _bundleInstance = bundleInstance;
        _bundle = bundle;
        _assetsInstance = assetsInstance;
        _serializedEntry = serializedEntry;
        _genericAssets = genericAssets;
        Decks = decks;
        SourceName = sourceName;
    }

    public string SourceName { get; }

    public string UnityVersion => _bundle.Header.EngineVersion;

    public int AssetCount => _assetsInstance.file.AssetInfos.Count;

    public IReadOnlyList<DeckModel> Decks { get; }

    public static RecipeDeckDocument Load(Stream source, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(source);

        var manager = new AssetsManager();
        var sourceCopy = new MemoryStream();
        source.CopyTo(sourceCopy);
        sourceCopy.Position = 0;

        BundleFileInstance originalInstance = manager.LoadBundleFile(sourceCopy, sourceName, false);
        AssetBundleFile originalBundle = originalInstance.file;

        var unpacked = new MemoryStream();
        originalBundle.Unpack(new AssetsFileWriter(unpacked));
        unpacked.Position = 0;
        originalBundle.Close();

        var bundle = new AssetBundleFile();
        bundle.Read(new AssetsFileReader(unpacked));
        originalInstance.file = bundle;

        AssetBundleDirectoryInfo? serializedEntry = bundle.BlockAndDirInfo.DirectoryInfos
            .FirstOrDefault(entry => (entry.Flags & 0x04) != 0);
        if (serializedEntry is null)
            throw new InvalidDataException("This bundle does not contain a serialized Unity asset file.");

        var entryStream = new SegmentStream(
            bundle.DataReader.BaseStream,
            serializedEntry.Offset,
            serializedEntry.DecompressedSize);

        string virtualPath = Path.Combine(sourceName, serializedEntry.Name);
        AssetsFileInstance assetsInstance = manager.LoadAssetsFile(
            entryStream,
            virtualPath,
            true,
            originalInstance);

        assetsInstance.file.GenerateQuickLookup();
        var decks = new List<DeckModel>();
        var genericAssets = new List<GenericAsset>();

        foreach (AssetFileInfo info in assetsInstance.file.AssetInfos)
        {
            string className = GetClassName(info.TypeId);
            try
            {
                AssetTypeValueField field = manager.GetBaseField(assetsInstance, info, AssetReadFlags.None);
                JsonNode json = AssetFieldJson.ToJson(field);
                string displayName = GetDisplayName(field, className, info.PathId);
                genericAssets.Add(new GenericAsset(info, field.TemplateField, json, className, displayName, null));

                if (info.TypeId != (int)AssetClassID.MonoBehaviour ||
                    field["m_Name"].IsDummy ||
                    field["Cards"].IsDummy ||
                    field["SuperpowerOverrides"].IsDummy)
                {
                    continue;
                }

                JsonObject deckJson = json as JsonObject
                    ?? throw new InvalidDataException("Deck root did not deserialize as an object.");

                var deck = new DeckModel(
                    info,
                    field.TemplateField,
                    deckJson,
                    field["m_Name"].AsString,
                    field["Faction"].AsInt);

                ReadCards(deckJson, "Cards", deck, false);
                ReadCards(deckJson, "SuperpowerOverrides", deck, true);
                decks.Add(deck);
            }
            catch (Exception ex)
            {
                if (genericAssets.All(asset => asset.AssetInfo.PathId != info.PathId))
                {
                    genericAssets.Add(new GenericAsset(
                        info,
                        null,
                        null,
                        className,
                        $"{className} #{info.PathId}",
                        ex.Message));
                }
            }
        }

        return new RecipeDeckDocument(
            manager,
            unpacked,
            originalInstance,
            bundle,
            assetsInstance,
            serializedEntry,
            decks.OrderBy(deck => deck.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            genericAssets,
            sourceName);
    }

    public string ExportFullJson()
    {
        var assets = new JsonArray();
        foreach (GenericAsset asset in _genericAssets)
        {
            var entry = new JsonObject
            {
                ["$pathId"] = asset.AssetInfo.PathId,
                ["$classId"] = asset.AssetInfo.TypeId,
                ["$className"] = asset.ClassName,
                ["$name"] = asset.DisplayName,
                ["$editable"] = asset.Data is not null
            };

            if (asset.Data is not null)
                entry["$data"] = asset.Data.DeepClone();
            else
                entry["$error"] = asset.Error ?? "This asset could not be decoded from its Unity type tree.";

            assets.Add(entry);
        }

        var root = new JsonObject
        {
            ["$format"] = "UnityAssetEditor.FullFile.v1",
            ["$source"] = SourceName,
            ["$unityVersion"] = UnityVersion,
            ["$assetCount"] = AssetCount,
            ["assets"] = assets
        };

        return root.ToJsonString(PrettyJsonOptions);
    }

    public static string FormatFullJson(string text)
    {
        JsonObject root = ParseFullJsonRoot(text);
        return root.ToJsonString(PrettyJsonOptions);
    }

    public byte[] Build()
    {
        DeckModel[] changedDecks = Decks.Where(deck => deck.IsDirty).ToArray();
        if (changedDecks.Length == 0)
            throw new InvalidOperationException("There are no unsaved changes.");

        foreach (DeckModel deck in changedDecks)
        {
            byte[] bytes = AssetFieldJson.Write(
                deck.Template,
                deck.BuildUpdatedJson(),
                _assetsInstance.file.Header.Endianness);
            deck.AssetInfo.SetNewData(bytes);
        }

        return WriteBundle();
    }

    public byte[] BuildFromFullJson(string text)
    {
        JsonObject root = ParseFullJsonRoot(text);
        JsonArray inputAssets = (JsonArray)root["assets"]!;

        Dictionary<long, GenericAsset> knownAssets = _genericAssets.ToDictionary(asset => asset.AssetInfo.PathId);
        var inputByPathId = new Dictionary<long, JsonObject>();

        foreach (JsonNode? node in inputAssets)
        {
            JsonObject entry = node as JsonObject
                ?? throw new InvalidDataException("Every item in assets must be an object.");
            long pathId = entry["$pathId"]?.GetValue<long>()
                ?? throw new InvalidDataException("An asset is missing $pathId.");

            if (!inputByPathId.TryAdd(pathId, entry))
                throw new InvalidDataException($"Asset path ID {pathId} appears more than once.");

            if (!knownAssets.TryGetValue(pathId, out GenericAsset? known))
                throw new InvalidDataException($"Asset path ID {pathId} does not exist in the opened file.");

            int classId = entry["$classId"]?.GetValue<int>()
                ?? throw new InvalidDataException($"Asset path ID {pathId} is missing $classId.");
            if (classId != known.AssetInfo.TypeId)
                throw new InvalidDataException($"The class ID for asset path ID {pathId} cannot be changed.");
        }

        var updates = new List<(GenericAsset Asset, byte[] Bytes)>();
        foreach (GenericAsset asset in _genericAssets.Where(asset => asset.Data is not null))
        {
            long pathId = asset.AssetInfo.PathId;
            if (!inputByPathId.TryGetValue(pathId, out JsonObject? entry))
                throw new InvalidDataException($"The editable asset with path ID {pathId} is missing.");

            JsonNode updatedData = entry["$data"]
                ?? throw new InvalidDataException($"Asset path ID {pathId} is missing $data.");
            if (JsonNode.DeepEquals(asset.Data, updatedData))
                continue;

            try
            {
                byte[] bytes = AssetFieldJson.Write(
                    asset.Template!,
                    updatedData,
                    _assetsInstance.file.Header.Endianness);
                updates.Add((asset, bytes));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"Asset path ID {pathId} ({asset.DisplayName}) is not valid: {ex.Message}",
                    ex);
            }
        }

        if (updates.Count == 0)
            throw new InvalidOperationException("There are no changes in the full-file JSON.");

        foreach ((GenericAsset asset, byte[] bytes) in updates)
            asset.AssetInfo.SetNewData(bytes);

        return WriteBundle();
    }

    private byte[] WriteBundle()
    {
        byte[] serializedAssets;
        using (var assetsStream = new MemoryStream())
        using (var assetsWriter = new AssetsFileWriter(assetsStream))
        {
            _assetsInstance.file.Write(assetsWriter, 0);
            serializedAssets = assetsStream.ToArray();
        }

        _serializedEntry.SetNewData(serializedAssets);

        using var output = new MemoryStream();
        using (var bundleWriter = new AssetsFileWriter(output))
            _bundle.Write(bundleWriter, 0);

        return output.ToArray();
    }

    public void MarkClean()
    {
        foreach (DeckModel deck in Decks)
            deck.MarkClean();
    }

    public void Dispose()
    {
        _manager.UnloadAllAssetsFiles(true);
        _manager.UnloadAllBundleFiles();
        _bundleData.Dispose();
    }

    private static void ReadCards(JsonObject root, string sectionName, DeckModel deck, bool superpowerOverride)
    {
        JsonArray? array = root[sectionName]?["CardEntries"]?["Array"] as JsonArray;
        if (array is null)
            return;

        foreach (JsonNode? node in array)
        {
            if (node is not JsonObject card)
                continue;

            deck.AddLoadedCard(
                superpowerOverride,
                card["Faction"]?.GetValue<int>() ?? 0,
                card["CardGuid"]?.GetValue<int>() ?? 0,
                card["Guid"]?.GetValue<string>() ?? string.Empty,
                card["NumCopies"]?.GetValue<int>() ?? 0,
                card["Filter"]?.GetValue<string>() ?? string.Empty);
        }
    }

    private static JsonNode ParseJson(string text)
        => JsonNode.Parse(
               text,
               new JsonNodeOptions { PropertyNameCaseInsensitive = false },
               new JsonDocumentOptions
               {
                   AllowTrailingCommas = true,
                   CommentHandling = JsonCommentHandling.Skip
               })
           ?? throw new InvalidDataException("The JSON editor is empty.");

    private static JsonObject ParseFullJsonRoot(string text)
    {
        JsonObject root = ParseJson(text) as JsonObject
            ?? throw new InvalidDataException("The full-file JSON must start with an object.");

        if (root["$format"]?.GetValue<string>() != "UnityAssetEditor.FullFile.v1")
            throw new InvalidDataException("This text is not a Unity Asset Editor full-file JSON document.");
        if (root["assets"] is not JsonArray)
            throw new InvalidDataException("The JSON document is missing its assets array.");

        return root;
    }

    private static string GetClassName(int typeId)
        => Enum.IsDefined(typeof(AssetClassID), typeId)
            ? ((AssetClassID)typeId).ToString()
            : $"ClassID_{typeId}";

    private static string GetDisplayName(AssetTypeValueField field, string className, long pathId)
    {
        AssetTypeValueField nameField = field["m_Name"];
        if (!nameField.IsDummy && !string.IsNullOrWhiteSpace(nameField.AsString))
            return nameField.AsString;

        return $"{className} #{pathId}";
    }

    private sealed record GenericAsset(
        AssetFileInfo AssetInfo,
        AssetTypeTemplateField? Template,
        JsonNode? Data,
        string ClassName,
        string DisplayName,
        string? Error);
}
