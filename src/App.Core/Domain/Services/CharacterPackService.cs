using App.Core.Domain.Entities;
using App.Core.Domain.Repositories;

namespace App.Core.Domain.Services;

/// <summary>캐릭터팩 로딩 폴백 정책 + "현재 로드된 팩" 상태를 담당. 매니페스트 파싱/검증 자체는 <see cref="ICharacterPackLoader"/> 책임.</summary>
public sealed class CharacterPackService
{
    private readonly ICharacterPackLoader _loader;
    private readonly string _builtInPackPath;

    public CharacterPack? CurrentPack { get; private set; }

    public CharacterPackService(ICharacterPackLoader loader, string builtInPackPath)
    {
        _loader = loader;
        _builtInPackPath = builtInPackPath;
    }

    public CharacterPackLoadOutcome LoadPack(string packFolderPath)
    {
        var result = _loader.Load(packFolderPath);
        if (result.IsSuccess)
        {
            CurrentPack = result.Pack;
            return new CharacterPackLoadOutcome(result.Pack!, UsedFallback: false, OriginalErrors: []);
        }

        var fallback = _loader.Load(_builtInPackPath);
        if (fallback.IsSuccess)
        {
            CurrentPack = fallback.Pack;
            return new CharacterPackLoadOutcome(fallback.Pack!, UsedFallback: true, OriginalErrors: result.Errors);
        }

        // 내장 팩까지 깨졌다면 사용자 입력 문제가 아니라 배포 버그.
        throw new InvalidOperationException(
            "내장 기본 캐릭터팩 로딩 실패: " + string.Join(", ", fallback.Errors));
    }
}

/// <summary>호출부(App.UI)가 "기본 캐릭터로 전환됨" 알림을 띄울지 판단하는 용도.</summary>
public sealed record CharacterPackLoadOutcome(CharacterPack Pack, bool UsedFallback, List<string> OriginalErrors);
