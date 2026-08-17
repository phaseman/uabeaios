using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PvZAssetEditor.Core;
using PvZAssetEditor.Models;

namespace PvZAssetEditor.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private RecipeDeckDocument? _document;
    private string _loadedAssetBaseline = string.Empty;

    [ObservableProperty]
    private string _fileName = "No file open";

    [ObservableProperty]
    private string _fileSummary = "Choose a Unity bundle to begin.";

    [ObservableProperty]
    private string _statusMessage = "Edits stay on this device. A backup is created before saving.";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _assetSearchText = string.Empty;

    [ObservableProperty]
    private DeckModel? _selectedDeck;

    [ObservableProperty]
    private UnityAssetModel? _selectedAsset;

    [ObservableProperty]
    private UnityAssetModel? _loadedAsset;

    [ObservableProperty]
    private bool _hasDocument;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _assetJsonText = string.Empty;

    [ObservableProperty]
    private bool _isDeckEditorVisible;

    [ObservableProperty]
    private bool _isAssetEditorVisible;

    [ObservableProperty]
    private bool _canSwitchEditorMode;

    [ObservableProperty]
    private bool _canEditSelectedAsset;

    [ObservableProperty]
    private string _assetEditorTitle = "Selected component data";

    [ObservableProperty]
    private string _assetFormatButtonText = "Format / Check";

    [ObservableProperty]
    private string _editorModeButtonText = "Components";

    public ObservableCollection<DeckModel> VisibleDecks { get; } = [];

    public ObservableCollection<UnityAssetModel> VisibleAssets { get; } = [];

    public RecipeDeckDocument? Document => _document;

    public void LoadDocument(RecipeDeckDocument document)
    {
        _document?.Dispose();
        _document = document;
        FileName = document.SourceName;
        FileSummary = document.Decks.Count > 0
            ? $"Unity {document.UnityVersion} • {document.Decks.Count} decks • {document.AssetCount} components"
            : $"Unity {document.UnityVersion} • {document.AssetCount} components";
        HasDocument = true;
        CanSwitchEditorMode = document.Decks.Count > 0;
        IsDeckEditorVisible = CanSwitchEditorMode;
        IsAssetEditorVisible = !CanSwitchEditorMode;
        EditorModeButtonText = IsAssetEditorVisible ? "Strategy decks" : "Components";
        AssetJsonText = string.Empty;
        _loadedAssetBaseline = string.Empty;
        LoadedAsset = null;
        CanEditSelectedAsset = false;

        SearchText = string.Empty;
        AssetSearchText = string.Empty;
        ApplyDeckFilter();
        ApplyAssetFilter();
        SelectedDeck = VisibleDecks.FirstOrDefault();
        SelectedAsset = VisibleAssets.FirstOrDefault(asset =>
                            asset.DisplayName.Equals("DeckRecipesConfig", StringComparison.OrdinalIgnoreCase))
                        ?? VisibleAssets.FirstOrDefault();

        StatusMessage = IsAssetEditorVisible
            ? $"Indexed {document.AssetCount} components without expanding them. Search or scroll, select one, then tap View data."
            : "Ready. Select a deck, make changes, then tap Save.";
    }

    public byte[] BuildDocument()
    {
        if (_document is null)
            throw new InvalidOperationException("No file is open.");

        if (!IsAssetEditorVisible)
            return _document.Build();
        if (LoadedAsset is null)
            throw new InvalidOperationException("Select a component and tap View data before saving.");

        return _document.BuildFromAssetText(AssetJsonText, LoadedAsset);
    }

    public void ToggleEditorMode()
    {
        if (_document is null || !CanSwitchEditorMode)
            return;

        IsAssetEditorVisible = !IsAssetEditorVisible;
        IsDeckEditorVisible = !IsAssetEditorVisible;
        EditorModeButtonText = IsAssetEditorVisible ? "Strategy decks" : "Components";
        StatusMessage = IsAssetEditorVisible
            ? $"Indexed {_document.AssetCount} components. Search or scroll, select one, then tap View data."
            : "Strategy deck mode. Select a deck, make changes, then tap Save.";
    }

    public void LoadSelectedAsset()
    {
        if (_document is null || SelectedAsset is null)
        {
            StatusMessage = "Select a component first.";
            return;
        }

        if (LoadedAsset is not null &&
            !ReferenceEquals(LoadedAsset, SelectedAsset) &&
            !string.Equals(AssetJsonText, _loadedAssetBaseline, StringComparison.Ordinal))
        {
            StatusMessage = "Save the current component, or undo its text changes, before viewing another component.";
            return;
        }

        try
        {
            AssetJsonText = _document.ExportAssetText(SelectedAsset);
            _loadedAssetBaseline = AssetJsonText;
            LoadedAsset = SelectedAsset;
            CanEditSelectedAsset = true;
            bool isTextAsset = RecipeDeckDocument.IsEditableTextAsset(SelectedAsset);
            AssetEditorTitle = isTextAsset ? $"{SelectedAsset.DisplayName} text" : "Selected component data";
            AssetFormatButtonText = isTextAsset ? "Format JSON" : "Format / Check";
            StatusMessage = isTextAsset
                ? $"Editing the text inside {SelectedAsset.DisplayName}."
                : $"Editing {SelectedAsset.DisplayName} ({SelectedAsset.ClassName}, path ID {SelectedAsset.PathId}).";
        }
        catch (Exception ex)
        {
            CanEditSelectedAsset = false;
            StatusMessage = $"Could not view this component: {ex.Message}";
        }
    }

    public void FormatAssetJson()
    {
        if (!CanEditSelectedAsset)
        {
            StatusMessage = "Select a component and tap View data first.";
            return;
        }

        try
        {
            AssetJsonText = _document!.FormatAssetText(AssetJsonText, LoadedAsset!);
            StatusMessage = RecipeDeckDocument.IsEditableTextAsset(LoadedAsset!)
                ? "The embedded JSON is valid and has been formatted."
                : "Component JSON is valid and has been formatted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"JSON is not valid: {ex.Message}";
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyDeckFilter();

    partial void OnAssetSearchTextChanged(string value) => ApplyAssetFilter();

    private void ApplyDeckFilter()
    {
        DeckModel? previous = SelectedDeck;
        VisibleDecks.Clear();

        if (_document is null)
            return;

        IEnumerable<DeckModel> filtered = _document.Decks;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(deck =>
                deck.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        foreach (DeckModel deck in filtered)
            VisibleDecks.Add(deck);

        SelectedDeck = previous is not null && VisibleDecks.Contains(previous)
            ? previous
            : VisibleDecks.FirstOrDefault();
    }

    private void ApplyAssetFilter()
    {
        UnityAssetModel? previous = SelectedAsset;
        VisibleAssets.Clear();

        if (_document is null)
            return;

        IEnumerable<UnityAssetModel> filtered = _document.Assets;
        if (!string.IsNullOrWhiteSpace(AssetSearchText))
        {
            string search = AssetSearchText.Trim();
            filtered = filtered.Where(asset =>
                asset.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        foreach (UnityAssetModel asset in filtered)
            VisibleAssets.Add(asset);

        SelectedAsset = previous is not null && VisibleAssets.Contains(previous)
            ? previous
            : VisibleAssets.FirstOrDefault();
    }
}
