using System.Text.Json.Nodes;
using PvZAssetEditor.Core;
using PvZAssetEditor.Models;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Pass recipe_decks and, optionally, data_assets_43.");
    return 2;
}

await VerifyRecipeDecks(Path.GetFullPath(args[0]));
if (args.Length == 2)
    await VerifyDataAssets(Path.GetFullPath(args[1]));

return 0;

static async Task VerifyRecipeDecks(string samplePath)
{
    await using FileStream input = File.OpenRead(samplePath);
    using RecipeDeckDocument original = RecipeDeckDocument.Load(input, Path.GetFileName(samplePath));

    Require(original.UnityVersion == "2022.3.68f1", $"Unexpected Unity version {original.UnityVersion}.");
    Require(original.AssetCount == 109, $"Expected 109 assets, found {original.AssetCount}.");
    Require(original.Decks.Count == 107, $"Expected 107 decks, found {original.Decks.Count}.");

    DeckModel greenShadow = original.Decks.Single(deck => deck.Name == "Deck_GreenShadow_R1");
    Require(greenShadow.Cards.Count == 19, $"Expected 19 Green Shadow entries, found {greenShadow.Cards.Count}.");
    Require(greenShadow.TotalCards == 40, $"Expected a 40-card deck, found {greenShadow.TotalCards}.");

    int oldCopies = greenShadow.Cards[0].NumCopies;
    greenShadow.Cards[0].NumCopies = oldCopies + 1;
    byte[] rebuilt = original.Build();

    await using var rebuiltStream = new MemoryStream(rebuilt, writable: false);
    using RecipeDeckDocument reopened = RecipeDeckDocument.Load(rebuiltStream, "rebuilt_recipe_decks");
    DeckModel reopenedGreenShadow = reopened.Decks.Single(deck => deck.Name == "Deck_GreenShadow_R1");
    Require(reopenedGreenShadow.Cards[0].NumCopies == oldCopies + 1,
        "The edited card count did not survive the bundle rebuild.");
    Require(reopened.Decks.Count == original.Decks.Count, "The rebuild changed the number of decks.");

    await using FileStream componentInput = File.OpenRead(samplePath);
    using RecipeDeckDocument componentDocument = RecipeDeckDocument.Load(componentInput, Path.GetFileName(samplePath));
    UnityAssetModel component = componentDocument.Assets.Single(asset => asset.DisplayName == "Deck_GreenShadow_R1");
    JsonObject root = JsonNode.Parse(componentDocument.ExportAssetText(component)) as JsonObject
        ?? throw new InvalidDataException("Selected component JSON did not parse.");
    const string editedName = "Deck_GreenShadow_R1_COMPONENT_TEST";
    root["$data"]!["m_Name"] = editedName;

    byte[] componentRebuilt = componentDocument.BuildFromAssetText(root.ToJsonString(), component);
    await using var componentStream = new MemoryStream(componentRebuilt, writable: false);
    using RecipeDeckDocument componentReopened = RecipeDeckDocument.Load(componentStream, "rebuilt_recipe_decks");
    Require(componentReopened.Decks.Any(deck => deck.Name == editedName),
        "The selected-component edit did not survive the bundle rebuild.");

    Console.WriteLine($"PASS recipe_decks Unity={original.UnityVersion} Assets={original.AssetCount} Decks={original.Decks.Count}");
}

static async Task VerifyDataAssets(string samplePath)
{
    await using FileStream input = File.OpenRead(samplePath);
    using RecipeDeckDocument document = RecipeDeckDocument.Load(input, Path.GetFileName(samplePath));

    Require(document.UnityVersion == "2022.3.68f1", $"Unexpected Unity version {document.UnityVersion}.");
    Require(document.AssetCount == 2408, $"Expected 2408 assets, found {document.AssetCount}.");
    Require(document.Decks.Count == 0, "data_assets should not eagerly expand deck assets.");

    UnityAssetModel target = document.Assets.Single(asset => asset.DisplayName == "DeckRecipesConfig");
    Require(target.ClassId == 49, $"Expected TextAsset class 49, found {target.ClassId}.");
    Require(target.PathId == -2453486475247905406, $"Unexpected path ID {target.PathId}.");

    string text = document.ExportAssetText(target);
    JsonObject root = JsonNode.Parse(text) as JsonObject
        ?? throw new InvalidDataException("DeckRecipesConfig text did not contain a JSON object.");
    Require(root["craftToGems"]?["default"]?.GetValue<double>() == 3.5,
        "DeckRecipesConfig did not contain the expected text.");

    root["craftToGems"]!["default"] = 3.75;
    byte[] rebuilt = document.BuildFromAssetText(root.ToJsonString(), target);

    await using var rebuiltStream = new MemoryStream(rebuilt, writable: false);
    using RecipeDeckDocument reopened = RecipeDeckDocument.Load(rebuiltStream, "data_assets_43");
    UnityAssetModel reopenedTarget = reopened.Assets.Single(asset => asset.DisplayName == "DeckRecipesConfig");
    JsonObject reopenedText = JsonNode.Parse(reopened.ExportAssetText(reopenedTarget)) as JsonObject
        ?? throw new InvalidDataException("Reopened DeckRecipesConfig text did not parse.");
    Require(reopenedText["craftToGems"]?["default"]?.GetValue<double>() == 3.75,
        "The DeckRecipesConfig text edit did not survive the bundle rebuild.");

    Console.WriteLine($"PASS data_assets Unity={document.UnityVersion} Assets={document.AssetCount} TargetPath={target.PathId}");
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidDataException(message);
}
