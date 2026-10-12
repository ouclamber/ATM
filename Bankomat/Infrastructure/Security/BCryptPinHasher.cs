using Bankomat.Application.Abstractions;

namespace Bankomat.Infrastructure.Security;

public class BCryptPinHasher : IPinHasher
{
    public string Hash(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
            throw new ArgumentException("PIN не может быть пустым", nameof(pin));

        return BCrypt.Net.BCrypt.HashPassword(pin);
    }

    public bool Verify(string pin, string hash)
    {
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(hash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(pin, hash);
        }
        catch (Exception)
        {
            return false;
        }
    }
}