using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BeatArenhax.CustomSongs
{
    public readonly record struct CustomScoreKey(int SongId, int PlayType, int SetIndex);

    public sealed class CustomScore
    {
        public int SongId { get; set; }
        public int PlayType { get; set; }
        public int SetIndex { get; set; }
        public int PlayCount { get; set; }
        public int Score { get; set; }
        public int[] Judges { get; set; }

        internal CustomScore Copy() => new()
        {
            SongId = SongId, PlayType = PlayType, SetIndex = SetIndex,
            PlayCount = PlayCount, Score = Score, Judges = (int[])Judges.Clone(),
        };
    }

    public sealed class CustomScoreStore
    {
        private const int CurrentVersion = 1;
        private readonly string _path;
        private readonly object _lock = new();
        private readonly Dictionary<CustomScoreKey, CustomScore> _scores = [];

        private sealed class ScoreFile
        {
            public int Version { get; set; }
            public List<CustomScore> Scores { get; set; }
        }

        public CustomScoreStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A score file path is required.", nameof(path));
            _path = Path.GetFullPath(path);
            string json;
            try { json = File.ReadAllText(_path); }
            catch (FileNotFoundException) { return; }
            ScoreFile file = JsonSerializer.Deserialize<ScoreFile>(json)
                ?? throw new InvalidDataException("Custom score file is empty.");
            if (file.Version != CurrentVersion || file.Scores == null)
                throw new InvalidDataException("Unsupported or invalid custom score file.");
            foreach (CustomScore score in file.Scores)
            {
                Validate(score);
                CustomScoreKey key = new(score.SongId, score.PlayType, score.SetIndex);
                if (!_scores.TryAdd(key, score.Copy()))
                    throw new InvalidDataException("Duplicate custom score: " + key);
            }
        }

        public CustomScore Get(CustomScoreKey key)
        {
            ValidateKey(key);
            lock (_lock) return _scores.TryGetValue(key, out CustomScore score) ? score.Copy() : null;
        }

        public IReadOnlyList<CustomScore> GetSongScores(int songId)
        {
            if (songId <= 0) throw new ArgumentOutOfRangeException(nameof(songId));
            lock (_lock) return _scores.Values.Where(score => score.SongId == songId).Select(score => score.Copy()).ToArray();
        }

        public bool Update(CustomScoreKey key, int score, int[] judges)
        {
            ValidateKey(key);
            if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));
            if (judges == null || judges.Length != 5 || judges.Any(value => value < 0))
                throw new ArgumentException("Five non-negative judge counts are required.", nameof(judges));
            lock (_lock)
            {
                CustomScore previous = _scores.TryGetValue(key, out CustomScore saved) ? saved : null;
                bool best = previous == null || previous.PlayCount == 0 || score > previous.Score;
                CustomScore next = new()
                {
                    SongId = key.SongId, PlayType = key.PlayType, SetIndex = key.SetIndex,
                    PlayCount = checked((previous?.PlayCount ?? 0) + 1),
                    Score = best ? score : previous.Score,
                    Judges = (int[])(best ? judges : previous.Judges).Clone(),
                };
                List<CustomScore> snapshot = [.. _scores.Where(entry => entry.Key != key).Select(entry => entry.Value.Copy()), next];
                Write(snapshot);
                _scores[key] = next;
                return best;
            }
        }

        private void Write(List<CustomScore> scores)
        {
            string directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                throw new DirectoryNotFoundException("Custom score directory does not exist: " + directory);
            string temporary = Path.Combine(directory, "." + Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, new ScoreFile { Version = CurrentVersion, Scores = scores });
                    stream.Flush(true);
                }
                if (File.Exists(_path)) File.Replace(temporary, _path, null);
                else File.Move(temporary, _path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void ValidateKey(CustomScoreKey key)
        {
            if (key.SongId <= 0 || key.PlayType < 0 || key.SetIndex <= 0)
                throw new ArgumentOutOfRangeException(nameof(key), "Invalid custom score key.");
        }

        private static void Validate(CustomScore score)
        {
            if (score == null) throw new InvalidDataException("Null custom score.");
            try { ValidateKey(new CustomScoreKey(score.SongId, score.PlayType, score.SetIndex)); }
            catch (ArgumentOutOfRangeException e) { throw new InvalidDataException("Invalid custom score key.", e); }
            if (score.PlayCount < 0 || score.Score < 0 || score.Judges == null
                || score.Judges.Length != 5 || score.Judges.Any(value => value < 0))
                throw new InvalidDataException("Invalid custom score data.");
        }
    }
}
