namespace PejPass.Domain.Security;

public sealed class PasswordGeneratorOptions
{
    public int Length { get; set; } = 20;

    public bool IncludeLowercase { get; set; } = true;
    public bool IncludeUppercase { get; set; } = true;
    public bool IncludeDigits { get; set; } = true;
    public bool IncludeSymbols { get; set; } = true;

    /// <summary>Exclude ambiguous characters: 0 O o 1 l I | `</summary>
    public bool ExcludeAmbiguous { get; set; } = true;

    public const int MinLength = 8;
    public const int MaxLength = 128;
}
