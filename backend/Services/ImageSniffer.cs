namespace backend.Services
{
    /// <summary>Identifica JPEG/PNG/WebP pela assinatura dos primeiros bytes.</summary>
    public static class ImageSniffer
    {
        public static string? Detect(ReadOnlySpan<byte> d)
        {
            if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return "image/jpeg";
            if (d.Length >= 8 && d[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
            if (d.Length >= 12 && d[..4].SequenceEqual("RIFF"u8) && d[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
            return null;
        }
    }

}
