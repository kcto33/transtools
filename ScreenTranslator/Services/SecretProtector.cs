using System.Security.Cryptography;
using System.Text;

namespace ScreenTranslator.Services;

public static class SecretProtector
{
  public static string ProtectString(string secret)
  {
    var bytes = Encoding.UTF8.GetBytes(secret);
    var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
    return Convert.ToBase64String(protectedBytes);
  }

  public static string UnprotectString(string protectedBase64)
  {
    var protectedBytes = Convert.FromBase64String(protectedBase64);
    var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
    return Encoding.UTF8.GetString(bytes);
  }
}
