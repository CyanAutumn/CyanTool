using System;
using System.Security.Cryptography;
using System.Text;

namespace CyanTools.Utils
{
    internal static class WindowsDataProtector
    {
        private static readonly byte[] Entropy = SHA256.HashData(
            Encoding.UTF8.GetBytes("CyanTools.ProxySettings.v1"));

        public static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var plaintext = Encoding.UTF8.GetBytes(value);
            try
            {
                var protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(protectedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        public static string Unprotect(string protectedValue)
        {
            if (string.IsNullOrWhiteSpace(protectedValue))
            {
                return string.Empty;
            }

            var protectedBytes = Convert.FromBase64String(protectedValue);
            var plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }
}
