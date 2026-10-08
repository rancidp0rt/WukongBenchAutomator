using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace WukongBenchAutomator.Vision;

internal static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Save(CapturedImage source, string path, int maxWidth = 1920)
    {
        var factor = Math.Max(1, (int)Math.Ceiling(source.Width / (double)maxWidth));
        var image = factor == 1 ? source : source.Downscale(factor);
        var width = image.Width;
        var height = image.Height;

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + width * 3];
            for (var y = 0; y < height; y++)
            {
                row[0] = 1; // фильтр Sub: разница с соседним пикселем слева - хорошо жмётся
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 4;
                    var o = 1 + x * 3;
                    row[o] = image.Bgra[i + 2];
                    row[o + 1] = image.Bgra[i + 1];
                    row[o + 2] = image.Bgra[i];
                }

                for (var i = row.Length - 1; i >= 4; i--)
                {
                    row[i] = (byte)(row[i] - row[i - 3]);
                }

                zlib.Write(row);
            }
        }

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // бит на канал
        header[9] = 2;  // RGB
        WriteChunk(file, "IHDR", header);
        WriteChunk(file, "IDAT", raw.ToArray());
        WriteChunk(file, "IEND", []);
    }

    internal static uint Crc32(ReadOnlySpan<byte> data, uint crc = 0xFFFFFFFF)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(data, Crc32(typeBytes)) ^ 0xFFFFFFFF);
        stream.Write(crc);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
