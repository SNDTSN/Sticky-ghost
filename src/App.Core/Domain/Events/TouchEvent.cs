namespace App.Core.Domain.Events;

/// <summary>매니페스트가 아니라 런타임(포인터 궤적)이 판정하는 이벤트 — 찌르기 vs 쓰다듬기.</summary>
public enum TouchKind
{
    Poke,
    Stroke,
}

public sealed record TouchEvent(string RegionId, TouchKind Kind);
