using System.Security.Cryptography;
using System.Text;

namespace EduSync.Utils
{
    public static class HashHelper
    {
        public static string Compute(string value)
        {
            using var sha = SHA256.Create();

            var bytes = sha.ComputeHash(
                Encoding.UTF8.GetBytes(value));

            return Convert.ToHexString(bytes);
        }
    }
}
