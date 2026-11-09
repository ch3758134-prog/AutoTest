using System.Security.Cryptography;
using System.Text;
using AutoTest.Projects.Project;
namespace AutoTest.Projects.Dbc;
public sealed record DbcSource(string Text, string Sha256, string EncodingName)
{
    public static DbcSource Load(ReadOnlySpan<byte> bytes)
    {
        string text;
        string encoding;
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            text = utf8.GetString(bytes); encoding = "utf-8";
        }
        catch (DecoderFallbackException)
        {
            throw new ProjectDataException("DBC_ENCODING", "DBC source is not valid UTF-8; transcode explicitly before import.");
        }
        return new(text, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), encoding);
    }
    public DbcDatabase Parse(string name) => DbcParserAdapter.Parse(Text, name);
}
