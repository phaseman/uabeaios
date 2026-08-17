using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using AssetsTools.NET;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PvZAssetEditor.Models;

public sealed class DeckModel : ObservableObject
{
    private readonly JsonObject _sourceJson;
    private string _name;
    private int _faction;
    private bool _isDirty;

    internal DeckModel(
        AssetFileInfo assetInfo,
        AssetTypeTemplateField template,
        JsonObject sourceJson,
        string name,
        int faction)
    {
        AssetInfo = assetInfo;
        Template = template;
        _sourceJson = sourceJson;
        _name = name;
        _faction = faction;
    }

    internal AssetFileInfo AssetInfo { get; }

    internal AssetTypeTemplateField Template { get; }

    public long PathId => AssetInfo.PathId;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
                IsDirty = true;
        }
    }

    public int Faction
    {
        get => _faction;
        set
        {
            if (SetProperty(ref _faction, value))
                IsDirty = true;
        }
    }

    public ObservableCollection<CardEntryModel> Cards { get; } = [];

    public ObservableCollection<CardEntryModel> SuperpowerOverrides { get; } = [];

    public int TotalCards => Cards.Sum(card => card.NumCopies);

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public void AddCard(bool superpowerOverride)
    {
        CardEntryModel card = CreateCard(0, 0, string.Empty, 1, string.Empty);
        (superpowerOverride ? SuperpowerOverrides : Cards).Add(card);
        MarkDirty();
    }

    public void RemoveCard(CardEntryModel card, bool superpowerOverride)
    {
        (superpowerOverride ? SuperpowerOverrides : Cards).Remove(card);
        MarkDirty();
    }

    internal void AddLoadedCard(
        bool superpowerOverride,
        int faction,
        int cardGuid,
        string guid,
        int numCopies,
        string filter)
    {
        (superpowerOverride ? SuperpowerOverrides : Cards)
            .Add(CreateCard(faction, cardGuid, guid, numCopies, filter));
    }

    internal JsonObject BuildUpdatedJson()
    {
        JsonObject result = (JsonObject)_sourceJson.DeepClone();
        result["m_Name"] = Name;
        result["Faction"] = Faction;
        ReplaceCardArray(result, "Cards", Cards);
        ReplaceCardArray(result, "SuperpowerOverrides", SuperpowerOverrides);
        return result;
    }

    internal void MarkClean() => IsDirty = false;

    private CardEntryModel CreateCard(int faction, int cardGuid, string guid, int numCopies, string filter)
        => new(faction, cardGuid, guid, numCopies, filter, MarkDirty);

    private void MarkDirty()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(TotalCards));
    }

    private static void ReplaceCardArray(
        JsonObject root,
        string sectionName,
        IEnumerable<CardEntryModel> cards)
    {
        JsonObject section = root[sectionName] as JsonObject
            ?? throw new InvalidDataException($"Missing {sectionName} section.");
        JsonObject entries = section["CardEntries"] as JsonObject
            ?? throw new InvalidDataException($"Missing {sectionName}.CardEntries section.");

        var array = new JsonArray();
        foreach (CardEntryModel card in cards)
        {
            array.Add(new JsonObject
            {
                ["Faction"] = card.Faction,
                ["CardGuid"] = card.CardGuid,
                ["Guid"] = card.Guid,
                ["NumCopies"] = card.NumCopies,
                ["Filter"] = card.Filter
            });
        }

        entries["Array"] = array;
    }
}
