using Unity.Collections;
using Unity.Mathematics;

namespace Medieval.Npcs
{
    /// <summary>Rolls medieval-sounding first + last names for DOTS NPCs.</summary>
    public static class NpcMedievalNameUtility
    {
        static readonly string[] FirstNames =
        {
            "Aldric", "Alaric", "Anselm", "Baldwin", "Benedict", "Cedric", "Clement",
            "Conrad", "Dominic", "Duncan", "Edgar", "Edmund", "Everard", "Florian",
            "Gareth", "Geoffrey", "Godric", "Harold", "Hector", "Humphrey", "Isidore",
            "Ivan", "Jasper", "Julian", "Kenneth", "Leofric", "Magnus", "Nigel",
            "Osric", "Percival", "Quentin", "Reginald", "Roland", "Simon", "Theodore",
            "Tristan", "Ulrich", "Victor", "Walter", "Wilfred", "Adela", "Beatrice",
            "Cecily", "Eleanor", "Giselle", "Isolde", "Matilda", "Rosalind", "Sigrid",
            "Winifred"
        };

        static readonly string[] LastNames =
        {
            "Ashford", "Blackwood", "Bracken", "Crestwell", "Dunmore", "Fairchild",
            "Greycloak", "Holloway", "Ironwood", "Kingsley", "Lancaster", "Marshwood",
            "Northridge", "Oakenshield", "Pemberly", "Ravenhurst", "Stonehaven",
            "Stormridge", "Thornwick", "Underhill", "Whitaker", "Woolsey", "Yorke",
            "Barlowe", "Crowley", "Drake", "Eastwick", "Fletcher", "Grimsby",
            "Hartwell", "Lockwood", "Merrick", "Rowan", "Shepard", "Talbot",
            "Vance", "Wainwright", "Wycliffe", "Yardley"
        };

        public static FixedString64Bytes Generate(ref Unity.Mathematics.Random rng)
        {
            string first = FirstNames[rng.NextInt(0, FirstNames.Length)];
            string last = LastNames[rng.NextInt(0, LastNames.Length)];
            return new FixedString64Bytes($"{first} {last}");
        }

        public static FixedString64Bytes Generate(uint seed)
        {
            var rng = new Unity.Mathematics.Random(math.max(1u, seed));
            return Generate(ref rng);
        }
    }
}
