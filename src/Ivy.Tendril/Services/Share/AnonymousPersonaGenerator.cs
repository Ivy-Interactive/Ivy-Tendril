namespace Ivy.Tendril.Services.Share;

public static class AnonymousPersonaGenerator
{
    private static readonly string[] Adjectives =
    [
        "Spectating",
        "Browsing",
        "Curious",
        "Observant",
        "Thoughtful",
        "Insightful",
        "Helpful",
        "Diligent",
        "Friendly",
        "Clever",
        "Wise",
        "Nimble",
        "Inquiring",
        "Attentive",
        "Sharp",
        "Prudent",
        "Vigilant",
        "Polite",
        "Calm",
        "Eager",
        "Spooky"
    ];

    private static readonly string[] Animals =
    [
        "Zebra",
        "Otter",
        "Capybara",
        "Falcon",
        "Fox",
        "Panda",
        "Koala",
        "Penguin",
        "Dolphin",
        "Badger",
        "Beaver",
        "Hedgehog",
        "Owl",
        "Lynx",
        "Giraffe",
        "Lemur",
        "Meerkat",
        "Platypus",
        "Quokka",
        "Wombat",
        "Niels",
        "Mikael",
        "Joel"
    ];

    public static string Generate(string? seed = null)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            var random = Random.Shared;
            var adj = Adjectives[random.Next(Adjectives.Length)];
            var animal = Animals[random.Next(Animals.Length)];
            return $"{adj} {animal}";
        }

        // Deterministic generation for stable connection/session IDs
        var hash = Math.Abs(seed.GetHashCode());
        var adjIndex = hash % Adjectives.Length;
        var animalIndex = (hash / Adjectives.Length) % Animals.Length;
        return $"{Adjectives[adjIndex]} {Animals[animalIndex]}";
    }
}
