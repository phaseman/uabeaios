using PvZAssetEditor.Core;

if (args.Length != 1)
{
    Console.Error.WriteLine("Pass the path to a recipe_decks Unity bundle.");
    return 2;
}

string samplePath = Path.GetFullPath(args[0]);
await using FileStream input = File.OpenRead(samplePath);
using RecipeDeckDocument original = RecipeDeckDocument.Load(input, Path.GetFileName(samplePath));

Require(original.UnityVersion == "2022.3.68f1", $"Unexpected Unity version {original.UnityVersion}.");
Require(original.AssetCount == 109, $"Expected 109 assets, found {original.AssetCount}.");
Require(original.Decks.Count == 107, $"Expected 107 decks, found {original.Decks.Count}.");

var greenShadow = original.Decks.Single(deck => deck.Name == "Deck_GreenShadow_R1");
Require(greenShadow.Cards.Count == 19, $"Expected 19 Green Shadow entries, found {greenShadow.Cards.Count}.");
Require(greenShadow.TotalCards == 40, $"Expected a 40-card deck, found {greenShadow.TotalCards}.");

int oldCopies = greenShadow.Cards[0].NumCopies;
greenShadow.Cards[0].NumCopies = oldCopies + 1;
byte[] rebuilt = original.Build();

await using var rebuiltStream = new MemoryStream(rebuilt, writable: false);
using RecipeDeckDocument reopened = RecipeDeckDocument.Load(rebuiltStream, "rebuilt_recipe_decks");
var reopenedGreenShadow = reopened.Decks.Single(deck => deck.Name == "Deck_GreenShadow_R1");

Require(
    reopenedGreenShadow.Cards[0].NumCopies == oldCopies + 1,
    "The edited card count did not survive the bundle rebuild.");
Require(reopened.Decks.Count == original.Decks.Count, "The rebuild changed the number of decks.");

Console.WriteLine($"PASS Unity={original.UnityVersion} Assets={original.AssetCount} Decks={original.Decks.Count} OutputBytes={rebuilt.Length}");
return 0;

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidDataException(message);
}
