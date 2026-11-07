using System.Text.RegularExpressions;

namespace BeatArenhax
{
    public static class Colors
    {
        public const string CasualDifficulty = "#33B5FF";
        public const string BeginnerDifficulty = "#33FF33";
        public const string AdvancedDifficulty = "#FF9900";
        public const string ExtremeDifficulty = "#FF3333";
        public const string MasterDifficulty = "#CC66FF";

        private static readonly Regex DifficultyLabel = new(
            @"(?<![\p{L}\p{N}])(casual|beginner|basic|bsc|advanced|adv|extreme|ext|master|mas|mst)(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string DifficultyColor(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;
            Match match = DifficultyLabel.Match(label);
            if (!match.Success) return null;

            return match.Value.ToUpperInvariant() switch
            {
                "CASUAL" => CasualDifficulty,
                "BEGINNER" or "BASIC" or "BSC" => BeginnerDifficulty,
                "ADVANCED" or "ADV" => AdvancedDifficulty,
                "EXTREME" or "EXT" => ExtremeDifficulty,
                "MASTER" or "MAS" or "MST" => MasterDifficulty,
                _ => null,
            };
        }
    }
}
