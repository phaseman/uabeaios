using CommunityToolkit.Mvvm.ComponentModel;

namespace PvZAssetEditor.Models;

public sealed class CardEntryModel : ObservableObject
{
    private readonly Action _changed;
    private int _faction;
    private int _cardGuid;
    private string _guid;
    private int _numCopies;
    private string _filter;

    public CardEntryModel(
        int faction,
        int cardGuid,
        string guid,
        int numCopies,
        string filter,
        Action changed)
    {
        _faction = faction;
        _cardGuid = cardGuid;
        _guid = guid;
        _numCopies = numCopies;
        _filter = filter;
        _changed = changed;
    }

    public int Faction
    {
        get => _faction;
        set => SetAndNotify(ref _faction, value);
    }

    public int CardGuid
    {
        get => _cardGuid;
        set => SetAndNotify(ref _cardGuid, value);
    }

    public string Guid
    {
        get => _guid;
        set => SetAndNotify(ref _guid, value ?? string.Empty);
    }

    public int NumCopies
    {
        get => _numCopies;
        set => SetAndNotify(ref _numCopies, Math.Max(0, value));
    }

    public string Filter
    {
        get => _filter;
        set => SetAndNotify(ref _filter, value ?? string.Empty);
    }

    private void SetAndNotify<T>(ref T field, T value)
    {
        if (SetProperty(ref field, value))
            _changed();
    }
}
