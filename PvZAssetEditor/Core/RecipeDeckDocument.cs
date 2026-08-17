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

    private RecipeDeckDocument(
        AssetsManager manager,
        MemoryStream bundleData,
        BundleFileInstance bundleInstance,
        AssetBundleFile bundle,
        AssetsFileInstance assetsInstance,
        AssetBundleDirectoryInfo serializedEntry,
        IReadOnlyList<DeckModel> decks,
        string sourceName)
    {
        _manager = manager;
        _bundleData = bundleData;
        _bundleInstance = bundleInstance;
        _bundle = bundle;
        _assetsInstance = assetsInstance;
        _serializedEntry = serializedEntry;
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

        foreach (AssetFileInfo info in assetsInstance.file.AssetInfos)
        {
            if (info.TypeId != (int)AssetClassID.MonoBehaviour)
                continue;

            AssetTypeValueField field;
            try
            {
                field = manager.GetBaseField(assetsInstance, info, AssetReadFlags.None);
            }
            catch
            {
                continue;
            }

            if (field["m_Name"].IsDummy || field["Cards"].IsDummy || field["SuperpowerOverrides"].IsDummy)
                continue;

            JsonObject json = AssetFieldJson.ToJson(field) as JsonObject
                ?? throw new InvalidDataException("Deck root did not deserialize as an object.");

            var deck = new DeckModel(
                info,
                field.TemplateField,
                json,
                field["m_Name"].AsString,
                field["Faction"].AsInt);

            ReadCards(json, "Cards", deck, false);
            ReadCards(json, "SuperpowerOverrides", deck, true);
            decks.Add(deck);
        }

        if (decks.Count == 0)
            throw new InvalidDataException("No editable deck assets were found in this file.");

        return new RecipeDeckDocument(
            manager,
            unpacked,
            originalInstance,
            bundle,
            assetsInstance,
            serializedEntry,
            decks.OrderBy(deck => deck.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            sourceName);
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
}
