using System.Security.Cryptography;

namespace SecureFileHub.Services
{
    public class EncryptionService
    {
        private const int NonceSize = 12;
        private const int TagSize = 16;

        private readonly byte[] _key;

        public EncryptionService()
        {
            var keyString = Environment.GetEnvironmentVariable("ENCRYPTION_KEY")
                ?? throw new InvalidOperationException(
                    "ENCRYPTION_KEY environment variable is not set. " +
                    "Copy .env.example to .env and set a 32-byte key.");

            _key = keyString.Length == 32
                ? System.Text.Encoding.UTF8.GetBytes(keyString)
                : Convert.FromBase64String(keyString);

            if (_key.Length != 32)
                throw new InvalidOperationException(
                    "ENCRYPTION_KEY must be exactly 32 bytes (256 bits).");
        }

        // Stored format: [12-byte nonce][16-byte auth tag][ciphertext]
        public byte[] Encrypt(byte[] plaintext)
        {
            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];

            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            var result = new byte[NonceSize + TagSize + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
            Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);
            return result;
        }

        public byte[] Decrypt(byte[] encryptedBlob)
        {
            if (encryptedBlob.Length < NonceSize + TagSize)
                throw new ArgumentException("Encrypted data is too short to be valid.");

            var nonce = encryptedBlob[..NonceSize];
            var tag = encryptedBlob[NonceSize..(NonceSize + TagSize)];
            var ciphertext = encryptedBlob[(NonceSize + TagSize)..];
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return plaintext;
        }

        public async Task<byte[]> EncryptStreamAsync(Stream inputStream)
        {
            using var ms = new MemoryStream();
            await inputStream.CopyToAsync(ms);
            return Encrypt(ms.ToArray());
        }

        public byte[] DecryptFile(string filePath)
        {
            return Decrypt(File.ReadAllBytes(filePath));
        }
    }
}
