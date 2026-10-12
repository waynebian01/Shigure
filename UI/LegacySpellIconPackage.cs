using System.Drawing;

namespace Shigure;

internal sealed class LegacySpellIconPackage : IDisposable, ISpellIconPackageCatalog
{
    private static readonly byte[] Magic = "SHGICN1\0"u8.ToArray();
    private static readonly byte[] ItemFooterMagic = "SHGITM1\0"u8.ToArray();
    private const int Version = 1;
    private const int HeaderSize = 56;
    private const int RecordSize = 12;
    private const int ItemFooterSize = 48;

    private readonly FileStream _stream;
    private readonly long[] _spellIds;
    private readonly int[] _spellIconIndices;
    private readonly long[] _itemIds;
    private readonly int[] _itemIconIndices;
    private readonly long[] _iconOffsets;
    private readonly int[] _iconLengths;

    private LegacySpellIconPackage(string path)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            using var reader = new BinaryReader(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic)
                || reader.ReadInt32() != Version)
            {
                throw new InvalidDataException("Unsupported spell icon package.");
            }

            var spellCount = reader.ReadInt32();
            var iconCount = reader.ReadInt32();
            var nameCount = reader.ReadInt32();
            var spellMapOffset = reader.ReadInt64();
            var iconIndexOffset = reader.ReadInt64();
            var nameIndexOffset = reader.ReadInt64();
            var dataOffset = reader.ReadInt64();
            if (spellCount is < 1 or > 2_000_000
                || iconCount is < 1 or > 100_000
                || nameCount is < 0 or > 2_000_000
                || spellMapOffset != HeaderSize
                || iconIndexOffset != spellMapOffset + (long)spellCount * RecordSize
                || nameIndexOffset != iconIndexOffset + (long)iconCount * RecordSize
                || dataOffset < nameIndexOffset
                || dataOffset > _stream.Length)
            {
                throw new InvalidDataException("Invalid spell icon package header.");
            }

            _spellIds = new long[spellCount];
            _spellIconIndices = new int[spellCount];
            _stream.Position = spellMapOffset;
            for (var index = 0; index < spellCount; index++)
            {
                var spellId = reader.ReadInt64();
                var iconIndex = reader.ReadInt32();
                if (spellId <= 0
                    || index > 0 && spellId <= _spellIds[index - 1]
                    || iconIndex < 0
                    || iconIndex >= iconCount)
                {
                    throw new InvalidDataException("Invalid spell map in icon package.");
                }

                _spellIds[index] = spellId;
                _spellIconIndices[index] = iconIndex;
            }

            _iconOffsets = new long[iconCount];
            _iconLengths = new int[iconCount];
            _stream.Position = iconIndexOffset;
            for (var index = 0; index < iconCount; index++)
            {
                var offset = reader.ReadInt64();
                var length = reader.ReadInt32();
                if (offset < dataOffset
                    || length is < 512 or > 10 * 1024 * 1024
                    || offset > _stream.Length - length)
                {
                    throw new InvalidDataException("Invalid image index in icon package.");
                }

                _iconOffsets[index] = offset;
                _iconLengths[index] = length;
            }

            SpellIdsByName = new Dictionary<string, long>(StringComparer.Ordinal);
            SpellNamesById = new Dictionary<long, string>();
            _stream.Position = nameIndexOffset;
            for (var index = 0; index < nameCount; index++)
            {
                var spellId = reader.ReadInt64();
                var byteLength = reader.ReadInt32();
                if (spellId <= 0
                    || byteLength is < 1 or > 4096
                    || _stream.Position > dataOffset - byteLength)
                {
                    throw new InvalidDataException("Invalid name index in icon package.");
                }

                var name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(byteLength));
                if (!string.IsNullOrWhiteSpace(name))
                {
                    SpellIdsByName.TryAdd(name, spellId);
                    SpellNamesById.TryAdd(spellId, name);
                }
            }

            if (_stream.Position != dataOffset)
            {
                throw new InvalidDataException("Spell icon package index size mismatch.");
            }

            ItemIdsByName = new Dictionary<string, long>(StringComparer.Ordinal);
            ItemNamesById = new Dictionary<long, string>();
            if (!TryReadItemExtension(reader, dataOffset, iconCount, out _itemIds, out _itemIconIndices))
            {
                _itemIds = [];
                _itemIconIndices = [];
            }
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    public Dictionary<string, long> SpellIdsByName { get; }
    public Dictionary<long, string> SpellNamesById { get; }
    public Dictionary<string, long> ItemIdsByName { get; }
    public Dictionary<long, string> ItemNamesById { get; }
    public IReadOnlyList<long> SpellIds => _spellIds;
    public IReadOnlyList<long> ItemIds => _itemIds;
    public bool HasItemDatabase => _itemIds.Length > 0;

    public static LegacySpellIconPackage Open(string path) => new(path);

    public static LegacySpellIconPackage? TryOpen(string path)
    {
        try
        {
            return File.Exists(path) ? Open(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    public Image? LoadSpellIcon(long spellId)
    {
        var spellIndex = Array.BinarySearch(_spellIds, spellId);
        return spellIndex < 0 ? null : LoadIconBlob(_spellIconIndices[spellIndex]);
    }

    public Image? LoadItemIcon(long itemId)
    {
        var itemIndex = Array.BinarySearch(_itemIds, itemId);
        return itemIndex < 0 ? null : LoadIconBlob(_itemIconIndices[itemIndex]);
    }

    public void Dispose() => _stream.Dispose();

    private bool TryReadItemExtension(
        BinaryReader reader,
        long dataOffset,
        int iconCount,
        out long[] itemIds,
        out int[] itemIconIndices)
    {
        itemIds = [];
        itemIconIndices = [];
        if (_stream.Length < dataOffset + ItemFooterSize)
        {
            return false;
        }

        var footerOffset = _stream.Length - ItemFooterSize;
        if (footerOffset < dataOffset)
        {
            return false;
        }

        _stream.Position = footerOffset;
        var magic = reader.ReadBytes(ItemFooterMagic.Length);
        if (!magic.SequenceEqual(ItemFooterMagic))
        {
            return false;
        }

        var itemCount = reader.ReadInt32();
        var itemNameCount = reader.ReadInt32();
        var itemMapOffset = reader.ReadInt64();
        var itemNameOffset = reader.ReadInt64();
        _ = reader.ReadInt64();
        _ = reader.ReadInt64();
        if (itemCount is < 1 or > 2_000_000
            || itemNameCount is < 0 or > 2_000_000
            || itemMapOffset < dataOffset
            || itemNameOffset != itemMapOffset + (long)itemCount * RecordSize
            || itemNameOffset > footerOffset)
        {
            throw new InvalidDataException("Invalid item extension footer in icon package.");
        }

        itemIds = new long[itemCount];
        itemIconIndices = new int[itemCount];
        _stream.Position = itemMapOffset;
        for (var index = 0; index < itemCount; index++)
        {
            var itemId = reader.ReadInt64();
            var iconIndex = reader.ReadInt32();
            if (itemId <= 0
                || index > 0 && itemId <= itemIds[index - 1]
                || iconIndex < 0
                || iconIndex >= iconCount)
            {
                throw new InvalidDataException("Invalid item map in icon package.");
            }

            itemIds[index] = itemId;
            itemIconIndices[index] = iconIndex;
        }

        _stream.Position = itemNameOffset;
        for (var index = 0; index < itemNameCount; index++)
        {
            var itemId = reader.ReadInt64();
            var byteLength = reader.ReadInt32();
            if (itemId <= 0
                || byteLength is < 1 or > 4096
                || _stream.Position > footerOffset - byteLength)
            {
                throw new InvalidDataException("Invalid item name index in icon package.");
            }

            var name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(byteLength));
            if (!string.IsNullOrWhiteSpace(name))
            {
                ItemIdsByName.TryAdd(name, itemId);
                ItemNamesById.TryAdd(itemId, name);
            }
        }

        if (_stream.Position != footerOffset)
        {
            throw new InvalidDataException("Item name table size mismatch in icon package.");
        }

        return true;
    }

    private Image? LoadIconBlob(int iconIndex)
    {
        var bytes = new byte[_iconLengths[iconIndex]];
        try
        {
            _stream.Position = _iconOffsets[iconIndex];
            _stream.ReadExactly(bytes);
            using var memory = new MemoryStream(bytes, writable: false);
            using var source = Image.FromStream(memory);
            return new Bitmap(source);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ObjectDisposedException)
        {
            return null;
        }
    }
}
