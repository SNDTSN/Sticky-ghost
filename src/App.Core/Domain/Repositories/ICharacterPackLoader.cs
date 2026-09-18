using App.Core.Domain.Entities;

namespace App.Core.Domain.Repositories;

public interface ICharacterPackLoader
{
    CharacterPackLoadResult Load(string packFolderPath);
}

public sealed class CharacterPackLoadResult
{
    public required bool IsSuccess { get; init; }
    public CharacterPack? Pack { get; init; }
    public List<string> Errors { get; init; } = new();

    public static CharacterPackLoadResult Success(CharacterPack pack) =>
        new() { IsSuccess = true, Pack = pack };

    public static CharacterPackLoadResult Fail(List<string> errors) =>
        new() { IsSuccess = false, Errors = errors };
}
