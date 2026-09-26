namespace OpenKogama.World;

public static class QuaternionCompression
{
    const float DegreesToByte = 32f / 45f;
    const float ByteToDegrees = 1.40625f;
    const float RadiansToDegrees = 57.29578f;
    const float DegreesToRadians = MathF.PI / 180f;

    public static byte[] ToBytes(float x, float y, float z, float w)
    {
        (float ex, float ey, float ez) = ToEuler(x, y, z, w);
        return [Wrap(ex * DegreesToByte), Wrap(ey * DegreesToByte), Wrap(ez * DegreesToByte)];
    }

    public static (float X, float Y, float Z, float W) ToQuaternion(byte[] bytes) =>
        FromEuler(bytes[0] * ByteToDegrees, bytes[1] * ByteToDegrees, bytes[2] * ByteToDegrees);

    static byte Wrap(float value) => unchecked((byte)(int)value);

    static (float, float, float) ToEuler(float x, float y, float z, float w)
    {
        double ww = w * w;
        double xx = x * x;
        double yy = y * y;
        double zz = z * z;
        double unit = xx + yy + zz + ww;
        double test = x * y + z * w;

        if (test > 0.499 * unit)
            return ((float)(2.0 * Math.Atan2(x, w)), MathF.PI / 2f, 0f);
        if (test < -0.499 * unit)
            return ((float)(-2.0 * Math.Atan2(x, w)), -MathF.PI / 2f, 0f);

        float ex = (float)Math.Atan2(2.0 * y * w - 2.0 * x * z, xx - yy - zz + ww);
        float ey = (float)Math.Asin(2.0 * test / unit);
        float ez = (float)Math.Atan2(2.0 * x * w - (double)(2f * y * z), 0.0 - xx + yy - zz + ww);
        return (ex * RadiansToDegrees, ey * RadiansToDegrees, ez * RadiansToDegrees);
    }

    static (float, float, float, float) FromEuler(float ex, float ey, float ez)
    {
        double heading = ex * DegreesToRadians;
        double attitude = ey * DegreesToRadians;
        double bank = ez * DegreesToRadians;
        double c1 = Math.Cos(heading / 2.0);
        double s1 = Math.Sin(heading / 2.0);
        double c2 = Math.Cos(attitude / 2.0);
        double s2 = Math.Sin(attitude / 2.0);
        double c3 = Math.Cos(bank / 2.0);
        double s3 = Math.Sin(bank / 2.0);
        double c1c2 = c1 * c2;
        double s1s2 = s1 * s2;
        float w = (float)(c1c2 * c3 - s1s2 * s3);
        float x = (float)(c1c2 * s3 + s1s2 * c3);
        float y = (float)(s1 * c2 * c3 + c1 * s2 * s3);
        float z = (float)(c1 * s2 * c3 - s1 * c2 * s3);
        return (x, y, z, w);
    }
}
