namespace SlimClip.Core.Models;

public record LanguageInfo(string Code, string NativeName, string EnglishName)
{
    public override string ToString() => NativeName;
}
