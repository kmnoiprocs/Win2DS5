namespace Win2DS5.DualSense;

public static class DualSenseCrc
{
    public static uint Compute(ReadOnlySpan<byte> reportWithoutCrc)
    {
        uint crc = 0xFFFFFFFFu;
        crc = Step(crc, 0xA2);
        foreach (byte value in reportWithoutCrc)
            crc = Step(crc, value);
        return ~crc;
    }

    private static uint Step(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        return crc;
    }
}

public static class DualSenseOutput
{
    public static byte[] BuildUsb(byte rightMotor, byte leftMotor, byte red, byte green, byte blue, bool lightbar, bool setup)
    {
        var buffer = new byte[63];
        buffer[0] = 0x02;
        WriteCommon(buffer.AsSpan(1), rightMotor, leftMotor, red, green, blue, lightbar, setup);
        return buffer;
    }

    public static byte[] BuildBluetooth(ref byte sequence, byte rightMotor, byte leftMotor, byte red, byte green, byte blue, bool lightbar, bool setup)
    {
        var buffer = new byte[78];
        buffer[0] = 0x31;
        buffer[1] = (byte)(sequence << 4);
        sequence = (byte)((sequence + 1) & 0x0F);
        buffer[2] = 0x10;
        WriteCommon(buffer.AsSpan(3), rightMotor, leftMotor, red, green, blue, lightbar, setup);
        uint crc = DualSenseCrc.Compute(buffer.AsSpan(0, 74));
        buffer[74] = (byte)crc;
        buffer[75] = (byte)(crc >> 8);
        buffer[76] = (byte)(crc >> 16);
        buffer[77] = (byte)(crc >> 24);
        return buffer;
    }

    private static void WriteCommon(Span<byte> common, byte rightMotor, byte leftMotor, byte red, byte green, byte blue, bool lightbar, bool setup)
    {
        if (rightMotor != 0 || leftMotor != 0)
        {
            common[0] |= 0x01 | 0x02;
            common[2] = rightMotor;
            common[3] = leftMotor;
        }

        if (lightbar)
        {
            common[1] |= 0x04;
            common[44] = red;
            common[45] = green;
            common[46] = blue;
        }

        if (setup)
        {
            common[38] |= 0x02;
            common[41] = 0x02;
        }
    }
}
