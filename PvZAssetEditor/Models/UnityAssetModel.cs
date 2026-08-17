using System.Text.Json.Nodes;
using AssetsTools.NET;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PvZAssetEditor.Models;

public sealed class UnityAssetModel : ObservableObject
{
    private string _displayName;

    internal UnityAssetModel(AssetFileInfo assetInfo, string className)
    {
        AssetInfo = assetInfo;
        ClassName = className;
        _displayName = $"{className} #{assetInfo.PathId}";
    }

    internal AssetFileInfo AssetInfo { get; }

    internal AssetTypeTemplateField? Template { get; private set; }

    internal JsonNode? OriginalData { get; private set; }

    internal string? DecodeError { get; private set; }

    public long PathId => AssetInfo.PathId;

    public int ClassId => AssetInfo.TypeId;

    public uint ByteSize => AssetInfo.ByteSize;

    public string ClassName { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public string SearchText => $"{DisplayName} {ClassName} {PathId} {ClassId}";

    internal bool IsDecoded => OriginalData is not null;

    internal void SetDecoded(AssetTypeTemplateField template, JsonNode data, string displayName)
    {
        Template = template;
        OriginalData = data;
        DecodeError = null;
        DisplayName = displayName;
        OnPropertyChanged(nameof(SearchText));
    }

    internal void SetDisplayName(string displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            DisplayName = displayName;
        OnPropertyChanged(nameof(SearchText));
    }

    internal void SetDecodeError(string message) => DecodeError = message;
}
