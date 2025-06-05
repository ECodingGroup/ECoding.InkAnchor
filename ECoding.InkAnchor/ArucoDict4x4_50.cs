public static class ArucoDict4x4_50
{
    // Each entry is 16 bits, each bit represents a cell (row-major order)
    public static readonly ushort[] Markers = new ushort[]
    {
        0x12BC, 0x3DC1, 0xC31E, 0xEC83, 0xA967, 0x56C8, 0x8E3A, 0x61C5,
        0x97B6, 0x6E49, 0x49DB, 0xB246, 0xDA31, 0x25CE, 0x4EC3, 0xD13C,
        0xF324, 0x0CDB, 0x38E1, 0xC71E, 0xE49C, 0x1B63, 0x6379, 0x9C86,
        0x84F3, 0x7B0C, 0xA1D2, 0x5E2D, 0x2F54, 0xD0AB, 0xF46B, 0x0B94,
        0x31E8, 0xCE17, 0xB50C, 0x4AF3, 0x69D7, 0x9638, 0xAD25, 0x52DA,
        0x7E1A, 0x81E5, 0xC8B7, 0x3728, 0x48CC, 0xB733, 0x93F3, 0x6C0C,
        0x0AF2, 0xF50D
    };
    public static byte[,] GetMarkerBits(int markerId)
    {
        // Aruco 4x4 marker, 16 bits
        if (markerId < 0 || markerId >= Markers.Length)
            throw new ArgumentOutOfRangeException(nameof(markerId));
        ushort bits = Markers[markerId];
        byte[,] matrix = new byte[4, 4];
        for (int i = 0; i < 16; i++)
        {
            int row = i / 4, col = i % 4;
            matrix[row, col] = (byte)((bits & (1 << (15 - i))) != 0 ? 1 : 0);
        }
        return matrix;
    }

}

