using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveCaptionsTranslator.utils
{
    /// <summary>
    /// Protects secrets at rest with Windows DPAPI (per user, per machine).
    /// Encrypted values are stored as "dpapi:" followed by Base64 so that
    /// legacy plaintext values in setting.json keep working and get encrypted
    /// on the next save.
    /// </summary>
    public static class SecretProtector
    {
        private const string PREFIX = "dpapi:";
        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        public static string Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText) || plainText.StartsWith(PREFIX))
                return plainText ?? string.Empty;

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plainText);
                DATA_BLOB input = CreateBlob(bytes);
                try
                {
                    if (!CryptProtectData(input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                            CRYPTPROTECT_UI_FORBIDDEN, out DATA_BLOB output))
                        return plainText;

                    byte[] encrypted = ToArray(output);
                    LocalFree(output.pbData);
                    return PREFIX + Convert.ToBase64String(encrypted);
                }
                finally
                {
                    LocalFree(input.pbData);
                }
            }
            catch
            {
                return plainText;
            }
        }

        public static string Unprotect(string? storedValue)
        {
            if (string.IsNullOrEmpty(storedValue) || !storedValue.StartsWith(PREFIX))
                return storedValue ?? string.Empty;

            try
            {
                byte[] encrypted = Convert.FromBase64String(storedValue[PREFIX.Length..]);
                DATA_BLOB input = CreateBlob(encrypted);
                try
                {
                    if (!CryptUnprotectData(input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                            CRYPTPROTECT_UI_FORBIDDEN, out DATA_BLOB output))
                    {
                        // The value was encrypted by another user or machine.
                        FileLogger.Warn("Failed to decrypt a stored secret (DPAPI), please re-enter the key.");
                        return string.Empty;
                    }

                    byte[] bytes = ToArray(output);
                    LocalFree(output.pbData);
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    LocalFree(input.pbData);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static DATA_BLOB CreateBlob(byte[] bytes)
        {
            var blob = new DATA_BLOB
            {
                cbData = bytes.Length,
                pbData = Marshal.AllocHGlobal(bytes.Length)
            };
            Marshal.Copy(bytes, 0, blob.pbData, bytes.Length);
            return blob;
        }

        private static byte[] ToArray(DATA_BLOB blob)
        {
            var bytes = new byte[blob.cbData];
            Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
            return bytes;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptProtectData(DATA_BLOB pDataIn, string? szDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(DATA_BLOB pDataIn, StringBuilder? szDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }
    }

    /// <summary>
    /// Serializes secret-bearing config properties encrypted with DPAPI.
    /// Apply to a string property via [JsonConverter(typeof(SecretJsonConverter))].
    /// </summary>
    public class SecretJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return SecretProtector.Unprotect(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(SecretProtector.Protect(value));
        }
    }
}
