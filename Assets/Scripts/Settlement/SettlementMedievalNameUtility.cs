using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>Rolls medieval English-style place names for settlements.</summary>
public static class SettlementMedievalNameUtility
{
    static readonly string[] Prefixes =
    {
        "Ash", "Black", "Bright", "Cold", "Dun", "East", "Fair", "Green",
        "High", "Hollow", "Kings", "Little", "Long", "Mid", "North", "Oak",
        "Raven", "Red", "River", "Stone", "Thorn", "West", "White", "Wind",
        "Wood", "Gold", "Grey", "Silver", "Wolf", "Fox", "Hart", "Iron"
    };

    static readonly string[] Suffixes =
    {
        "bury", "cliffe", "dale", "field", "ford", "gate", "grove", "hall",
        "ham", "haven", "heath", "hill", "holm", "ley", "marsh", "mere",
        "moor", "mouth", "ness", "ridge", "shaw", "stead", "stock", "thorpe",
        "ton", "vale", "wick", "wood", "worth", "bridge", "chester", "minster"
    };

    /// <summary>Deterministic name for a settlement index under a world seed.</summary>
    public static string Generate(int worldSeed, int settlementId)
    {
        uint seed = math.asuint(worldSeed) ^ ((uint)(settlementId + 1) * 2654435761u);
        var rng = new Unity.Mathematics.Random(math.max(1u, seed));
        return Generate(ref rng);
    }

    /// <summary>Fills <paramref name="results"/> with unique names for a batch of settlements.</summary>
    public static void GenerateUnique(int worldSeed, int count, List<string> results)
    {
        results.Clear();
        if (count <= 0)
            return;

        var used = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            string name = null;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                uint seed = math.asuint(worldSeed)
                    ^ ((uint)(i + 1) * 2654435761u)
                    ^ ((uint)(attempt + 1) * 2246822519u);
                var rng = new Unity.Mathematics.Random(math.max(1u, seed));
                string candidate = Generate(ref rng);
                if (used.Add(candidate))
                {
                    name = candidate;
                    break;
                }
            }

            if (name == null)
            {
                name = $"{Prefixes[i % Prefixes.Length]}{Suffixes[(i * 7) % Suffixes.Length]}";
                int suffix = 2;
                while (!used.Add(name))
                    name = $"{Prefixes[i % Prefixes.Length]}{Suffixes[(i * 7) % Suffixes.Length]}{suffix++}";
            }

            results.Add(name);
        }
    }

    public static string Generate(ref Unity.Mathematics.Random rng)
    {
        string prefix = Prefixes[rng.NextInt(0, Prefixes.Length)];
        string suffix = Suffixes[rng.NextInt(0, Suffixes.Length)];
        return prefix + suffix;
    }
}
