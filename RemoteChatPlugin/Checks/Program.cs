using System.Security.Cryptography;
using RemoteChatPlugin;

var plaintext = RandomNumberGenerator.GetBytes(32);
var entropy = RandomNumberGenerator.GetBytes(16);
var encrypted = WindowsDataProtection.Protect(plaintext, entropy);
var roundTrip = WindowsDataProtection.Unprotect(encrypted, entropy);

if (!CryptographicOperations.FixedTimeEquals(plaintext, roundTrip))
{
    Console.Error.WriteLine("Windows DPAPI round-trip failed.");
    return 1;
}

Console.WriteLine("RemoteChatPlugin checks passed.");
return 0;
