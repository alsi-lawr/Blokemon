using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blokemon.Web.Tests.Identity;

/// <summary>
/// A software authenticator for the server tests: one P-256 key, one discoverable credential,
/// self-attesting with the <c>none</c> format, answering the options the server issues the way
/// a browser's <c>PublicKeyCredential.toJSON()</c> would. Test assembly only.
/// </summary>
internal sealed class SoftwareAuthenticator(string origin, string rpId) : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _origin = origin;

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

    /// <summary>The user handle the credential was made with, once registered.</summary>
    public byte[]? UserHandle { get; private set; }

    public uint SignCount { get; set; }

    public string CredentialIdText => Base64Url.EncodeToString(CredentialId);

    /// <summary>Answers registration options with an attestation for a fresh credential.</summary>
    public JsonElement Register(JsonElement options)
    {
        var challenge = Base64Url.DecodeFromChars(options.GetProperty("challenge").GetString()!);
        UserHandle = Base64Url.DecodeFromChars(
            options.GetProperty("user").GetProperty("id").GetString()!
        );
        var parameters = _key.ExportParameters(false);
        var coseKey = new CborWriter(CborConformanceMode.Ctap2Canonical);
        coseKey.WriteStartMap(5);
        coseKey.WriteInt32(1);
        coseKey.WriteInt32(2);
        coseKey.WriteInt32(3);
        coseKey.WriteInt32(-7);
        coseKey.WriteInt32(-1);
        coseKey.WriteInt32(1);
        coseKey.WriteInt32(-2);
        coseKey.WriteByteString(parameters.Q.X!);
        coseKey.WriteInt32(-3);
        coseKey.WriteByteString(parameters.Q.Y!);
        coseKey.WriteEndMap();

        var attested = new MemoryStream();
        attested.Write(new byte[16]);
        attested.Write([(byte)(CredentialId.Length >> 8), (byte)CredentialId.Length]);
        attested.Write(CredentialId);
        attested.Write(coseKey.Encode());
        var authData = AuthData(0x45, SignCount, attested.ToArray());

        var attestation = new CborWriter(CborConformanceMode.Ctap2Canonical);
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt");
        attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt");
        attestation.WriteStartMap(0);
        attestation.WriteEndMap();
        attestation.WriteTextString("authData");
        attestation.WriteByteString(authData);
        attestation.WriteEndMap();

        var clientData = ClientData("webauthn.create", challenge, _origin);
        return Parse(
            new JsonObject
            {
                ["id"] = CredentialIdText,
                ["rawId"] = CredentialIdText,
                ["type"] = "public-key",
                ["authenticatorAttachment"] = "platform",
                ["response"] = new JsonObject
                {
                    ["attestationObject"] = Base64Url.EncodeToString(attestation.Encode()),
                    ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                    ["transports"] = new JsonArray("internal"),
                    ["publicKeyAlgorithm"] = -7,
                },
                ["clientExtensionResults"] = new JsonObject(),
            }
        );
    }

    /// <summary>
    /// Answers assertion options. The counter advances unless told otherwise; the user handle
    /// and origin default to the credential's own and the authenticator's.
    /// </summary>
    public JsonElement Assert(
        JsonElement options,
        byte[]? userHandle = null,
        bool advanceCounter = true,
        string? origin = null
    )
    {
        var challenge = Base64Url.DecodeFromChars(options.GetProperty("challenge").GetString()!);
        if (advanceCounter)
        {
            SignCount++;
        }

        var authData = AuthData(0x05, SignCount, []);
        var clientData = ClientData("webauthn.get", challenge, origin ?? _origin);
        var signature = _key.SignData(
            [.. authData, .. SHA256.HashData(clientData)],
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence
        );
        var handle = userHandle ?? UserHandle ?? [];
        return Parse(
            new JsonObject
            {
                ["id"] = CredentialIdText,
                ["rawId"] = CredentialIdText,
                ["type"] = "public-key",
                ["authenticatorAttachment"] = "platform",
                ["response"] = new JsonObject
                {
                    ["authenticatorData"] = Base64Url.EncodeToString(authData),
                    ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                    ["signature"] = Base64Url.EncodeToString(signature),
                    ["userHandle"] = handle.Length == 0 ? null : Base64Url.EncodeToString(handle),
                },
                ["clientExtensionResults"] = new JsonObject(),
            }
        );
    }

    public void Dispose() => _key.Dispose();

    private byte[] AuthData(byte flags, uint counter, byte[] attested)
    {
        var stream = new MemoryStream();
        stream.Write(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        stream.WriteByte(flags);
        var count = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(count);
        }
        stream.Write(count);
        stream.Write(attested);
        return stream.ToArray();
    }

    private static byte[] ClientData(string type, byte[] challenge, string origin) =>
        Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
                new
                {
                    type,
                    challenge = Base64Url.EncodeToString(challenge),
                    origin,
                    crossOrigin = false,
                }
            )
        );

    private static JsonElement Parse(JsonObject node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
