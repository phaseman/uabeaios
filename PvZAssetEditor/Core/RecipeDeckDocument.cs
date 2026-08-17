using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
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
        IReadOnlyList<UnityAssetModel> assets,
        string sourceName)
    {
        _manager = manager;
        _bundleData = bundleData;
        _bundleInstance = bundleInstance;
        _bundle = bundle;
        _assetsInstance = assetsInstance;
        _serializedEntry = serializedEntry;
        Decks = decks;
        Assets = assets;
        SourceName = sourceName;
    }

    public string SourceName { get; }

    public string UnityVersion => _bundle.Header.EngineVersion;

    public int AssetCount => _assetsInstance.file.AssetInfos.Count;

    public IReadOnlyList<DeckModel> Decks { get; }

    public IReadOnlyList<UnityAssetModel> Assets { get; }

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

        UnityAssetModel[] assets = assetsInstance.file.AssetInfos
            .Select(info => new UnityAssetModel(info, GetClassName(info.TypeId)))
            .OrderBy(asset => asset.ClassId == (int)AssetClassID.MonoBehaviour ? 0 : 1)
            .ThenBy(asset => asset.ClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.PathId)
            .ToArray();

        foreach (UnityAssetModel asset in assets.Where(asset => asset.ClassId == (int)AssetClassID.TextAsset))
        {
            string? fastName = TryReadTextAssetName(assetsInstance.file, asset.AssetInfo);
            if (!string.IsNullOrWhiteSpace(fastName))
                asset.SetDisplayName(fastName);
        }

        var decks = new List<DeckModel>();
        if (sourceName.Contains("recipe_decks", StringComparison.OrdinalIgnoreCase))
        {
            foreach (UnityAssetModel asset in assets.Where(
                         asset => asset.ClassId == (int)AssetClassID.MonoBehaviour))
            {
                try
                {
                    AssetTypeValueField field = manager.GetBaseField(
                        assetsInstance,
                        asset.AssetInfo,
                        AssetReadFlags.None);

                    if (field["m_Name"].IsDummy ||
                        field["Cards"].IsDummy ||
                        field["SuperpowerOverrides"].IsDummy)
                    {
                        continue;
                    }

                    JsonObject deckJson = AssetFieldJson.ToJson(field) as JsonObject
                        ?? throw new InvalidDataException("Deck root did not deserialize as an object.");
                    asset.SetDecoded(
                        field.TemplateField,
                        deckJson,
                        GetDisplayName(field, asset.ClassName, asset.PathId));

                    var deck = new DeckModel(
                        asset.AssetInfo,
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
                    asset.SetDecodeError(ex.Message);
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
            assets,
            sourceName);
    }

    public string ExportAssetText(UnityAssetModel asset)
    {
        EnsureAssetDecoded(asset);

        if (TryGetTextAssetContents(asset, out string? contents))
            return contents;

        var root = new JsonObject
        {
            ["$format"] = "UnityAssetEditor.Asset.v1",
            ["$source"] = SourceName,
            ["$unityVersion"] = UnityVersion,
            ["$pathId"] = asset.PathId,
            ["$classId"] = asset.ClassId,
            ["$className"] = asset.ClassName,
            ["$name"] = asset.DisplayName,
            ["$data"] = asset.OriginalData!.DeepClone()
        };

        return root.ToJsonString(PrettyJsonOptions);
    }

    public string FormatAssetText(string text, UnityAssetModel asset)
    {
        if (!Assets.Contains(asset))
            throw new InvalidOperationException("The selected asset does not belong to the opened file.");

        if (asset.ClassId == (int)AssetClassID.TextAsset)
            return ParseJson(text).ToJsonString(PrettyJsonOptions);

        JsonObject root = ParseAssetJsonRoot(text);
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

    public byte[] BuildFromAssetText(string text, UnityAssetModel asset)
    {
        EnsureAssetDecoded(asset);

        if (TryGetTextAssetContents(asset, out _))
            return BuildFromTextAssetContents(text, asset);

        JsonObject root = ParseAssetJsonRoot(text);

        long pathId = root["$pathId"]?.GetValue<long>()
            ?? throw new InvalidDataException("The asset JSON is missing $pathId.");
        if (pathId != asset.PathId)
            throw new InvalidDataException("The selected asset's $pathId cannot be changed.");

        int classId = root["$classId"]?.GetValue<int>()
            ?? throw new InvalidDataException("The asset JSON is missing $classId.");
        if (classId != asset.ClassId)
            throw new InvalidDataException("The selected asset's $classId cannot be changed.");

        JsonNode updatedData = root["$data"]
            ?? throw new InvalidDataException("The asset JSON is missing $data.");
        if (JsonNode.DeepEquals(asset.OriginalData, updatedData))
            throw new InvalidOperationException("There are no changes in the selected asset JSON.");

        byte[] bytes;
        try
        {
            bytes = AssetFieldJson.Write(
                asset.Template!,
                updatedData,
                _assetsInstance.file.Header.Endianness);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Asset path ID {asset.PathId} ({asset.DisplayName}) is not valid: {ex.Message}",
                ex);
        }

        asset.AssetInfo.SetNewData(bytes);
        return WriteBundle();
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

    private void EnsureAssetDecoded(UnityAssetModel asset)
    {
        if (!Assets.Contains(asset))
            throw new InvalidOperationException("The selected asset does not belong to the opened file.");
        if (asset.IsDecoded)
            return;
        if (!string.IsNullOrWhiteSpace(asset.DecodeError))
            throw new InvalidDataException(asset.DecodeError);

        try
        {
            AssetTypeValueField field = _manager.GetBaseField(
                _assetsInstance,
                asset.AssetInfo,
                AssetReadFlags.None);
            JsonNode data = AssetFieldJson.ToJson(field);
            asset.SetDecoded(
                field.TemplateField,
                data,
                GetDisplayName(field, asset.ClassName, asset.PathId));
        }
        catch (Exception ex)
        {
            asset.SetDecodeError(ex.Message);
            throw new InvalidDataException(
                $"Could not decode {asset.ClassName} path ID {asset.PathId}: {ex.Message}",
                ex);
        }
    }

    private byte[] BuildFromTextAssetContents(string text, UnityAssetModel asset)
    {
        JsonObject updatedData = asset.OriginalData!.DeepClone() as JsonObject
            ?? throw new InvalidDataException("The selected TextAsset data is not an object.");
        updatedData["m_Script"] = text;

        if (JsonNode.DeepEquals(asset.OriginalData, updatedData))
            throw new InvalidOperationException("There are no changes in the selected text component.");

        byte[] bytes;
        try
        {
            bytes = AssetFieldJson.Write(
                asset.Template!,
                updatedData,
                _assetsInstance.file.Header.Endianness);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Text component {asset.DisplayName} is not valid: {ex.Message}",
                ex);
        }

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

    private static JsonObject ParseAssetJsonRoot(string text)
    {
        JsonObject root = ParseJson(text) as JsonObject
            ?? throw new InvalidDataException("The asset JSON must start with an object.");

        if (root["$format"]?.GetValue<string>() != "UnityAssetEditor.Asset.v1")
            throw new InvalidDataException("This text is not a Unity Asset Editor asset JSON document.");
        if (root["$data"] is null)
            throw new InvalidDataException("The asset JSON is missing $data.");

        return root;
    }

    public static bool IsEditableTextAsset(UnityAssetModel asset)
        => asset.ClassId == (int)AssetClassID.TextAsset;

    private static bool TryGetTextAssetContents(UnityAssetModel asset, out string contents)
    {
        contents = string.Empty;
        if (!IsEditableTextAsset(asset) || asset.OriginalData is not JsonObject data)
            return false;

        JsonNode? script = data["m_Script"];
        if (script is null)
            return false;

        try
        {
            contents = script.GetValue<string>();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetClassName(int typeId)
        => Enum.IsDefined(typeof(AssetClassID), typeId)
            ? ((AssetClassID)typeId).ToString()
            : $"ClassID_{typeId}";

    private static string? TryReadTextAssetName(AssetsFile file, AssetFileInfo info)
    {
        const int maximumNameBytes = 4096;

        try
        {
            AssetsFileReader reader = file.Reader;
            reader.Position = info.GetAbsoluteByteOffset(file);
            int byteCount = reader.ReadInt32();
            if (byteCount <= 0 || byteCount > maximumNameBytes || byteCount > info.ByteSize - sizeof(int))
                return null;

            return Encoding.UTF8.GetString(reader.ReadBytes(byteCount));
        }
        catch
        {
            return null;
        }
    }

    private static string GetDisplayName(AssetTypeValueField field, string className, long pathId)
    {
        AssetTypeValueField nameField = field["m_Name"];
        if (!nameField.IsDummy && !string.IsNullOrWhiteSpace(nameField.AsString))
            return nameField.AsString;

        return $"{className} #{pathId}";
    }
}
