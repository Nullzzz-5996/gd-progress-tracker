using System.Text;
using FluentAssertions;
using GdTracker.GameSync;

namespace GdTracker.Tests;

public class SaveFileCodecTests
{
    private const string SampleXml =
        "<?xml version=\"1.0\"?><plist version=\"1.0\" gjver=\"2.0\"><dict>" +
        "<k>GLM_03</k><d><k>128</k><d><k>k1</k><i>128</i><k>k19</k><i>45</i></d></d>" +
        "</dict></plist>";

    [Fact]
    public void Decode_reverses_Encode()
    {
        var encoded = SaveFileCodec.Encode(SampleXml);
        var decoded = SaveFileCodec.Decode(encoded);
        decoded.Should().Be(SampleXml);
    }

    [Fact]
    public void Encoded_bytes_are_obfuscated_not_plaintext()
    {
        var encoded = SaveFileCodec.Encode(SampleXml);
        Encoding.UTF8.GetString(encoded).Should().NotContain("plist");
        Encoding.ASCII.GetString(encoded).Should().NotContain("GLM_03");
    }

    [Fact]
    public void Decode_handles_unpadded_url_safe_base64()
    {
        // Кодируем, затем убираем '=' padding (как делает GD) — Decode должен всё равно справиться.
        var encoded = SaveFileCodec.Encode(SampleXml);
        // снять XOR, убрать padding, снова наложить XOR
        var ascii = new byte[encoded.Length];
        for (var i = 0; i < encoded.Length; i++) ascii[i] = (byte)(encoded[i] ^ 0x0B);
        var b64 = Encoding.ASCII.GetString(ascii).TrimEnd('=');
        var stripped = Encoding.ASCII.GetBytes(b64);
        for (var i = 0; i < stripped.Length; i++) stripped[i] ^= 0x0B;

        SaveFileCodec.Decode(stripped).Should().Be(SampleXml);
    }
}
