using System;
using System.IO;

namespace BeatArenhax.CustomSongs
{
    public enum FolderSelectionKind { All, Custom, Original, Folder }

    public sealed class FolderSelection
    {
        public FolderSelectionKind Kind;
        public FolderInfo Folder;

        public string Name => Kind switch
        {
            FolderSelectionKind.All => "ALL",
            FolderSelectionKind.Custom => "CUSTOM",
            FolderSelectionKind.Original => "BEAT ARENA",
            FolderSelectionKind.Folder => Folder.Name,
            _ => throw new InvalidOperationException("Unknown folder selection."),
        };

        public bool Matches(int id, SongLibrary library) => Kind switch
        {
            FolderSelectionKind.All => true,
            FolderSelectionKind.Custom => library.ById.ContainsKey(id),
            FolderSelectionKind.Original => !library.ById.ContainsKey(id),
            FolderSelectionKind.Folder => Folder.SongIds.Contains(id),
            _ => throw new InvalidOperationException("Unknown folder selection."),
        };
    }

    public static class SongSearch
    {
        public static bool Matches(string query, string title, string artist, string songFolder)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            query = query.Trim();
            string folderName = string.IsNullOrEmpty(songFolder) ? null
                : Path.GetFileName(Path.TrimEndingDirectorySeparator(songFolder));
            return Contains(title, query) || Contains(artist, query) || Contains(folderName, query);
        }

        private static bool Contains(string value, string query) =>
            value != null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static class SongSearchKeyboardLayout
    {
        public const float Scale = 1.6f;
        public const float HeadOffsetY = -0.30f;
        public const float HeadOffsetZ = 0.48f;
        public const float Width = 620f;
        public const float Height = 482f;
        public const float KeyWidth = 58f;
        public const float KeyHeight = 46f;
        public const float KeyPitch = 60f;
        public const float RowPitch = 53f;
        public const float FirstRowY = 126f;
        public const float LauncherSize = 100f;
    }

    public static class FolderGrid
    {
        public const int Columns = 3;
        public const float TileSize = 100f;
        public const float Gap = 8f;
        public const float Pitch = TileSize + Gap;
        public const float Width = Columns * Pitch - Gap;
        public const float Height = 4 * Pitch + TileSize * 0.5f;
        public const float DragThreshold = 20f;

        public static float MaxScroll(int count) =>
            Math.Max(0f, ((count + Columns - 1) / Columns) * Pitch - Gap - Height);

        public static int Hit(float x, float y, float scroll, int count)
        {
            if (x < 0f || x >= Width || y < 0f || y >= Height) return -1;
            y += scroll;
            int column = (int)(x / Pitch), row = (int)(y / Pitch);
            int index = row * Columns + column;
            return x % Pitch < TileSize && y % Pitch < TileSize && index < count ? index : -1;
        }
    }

    public sealed class FolderTouch
    {
        private int _tile = -1;
        private float _x, _y, _scroll;
        private bool _dragged;
        public bool Dragged => _dragged;

        public bool Start(float x, float y, float scroll, int count)
        {
            _tile = FolderGrid.Hit(x, y, scroll, count);
            _x = x; _y = y; _scroll = scroll; _dragged = false;
            return _tile >= 0;
        }

        public float Move(float x, float y, int count)
        {
            float dx = x - _x, dy = y - _y;
            if (dx * dx + dy * dy >= FolderGrid.DragThreshold * FolderGrid.DragThreshold) _dragged = true;
            return _dragged ? Math.Clamp(_scroll - dy, 0f, FolderGrid.MaxScroll(count)) : _scroll;
        }

        public int Release()
        {
            int result = _dragged ? -1 : _tile;
            _tile = -1;
            return result;
        }
    }
}
