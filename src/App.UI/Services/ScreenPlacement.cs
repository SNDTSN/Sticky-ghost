using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace App.UI.Services;

/// <summary>
/// 창 위치의 화면 안 검증/보정과 기본 위치 계산. 좌표는 전부 물리 픽셀(Avalonia PixelRect/PixelPoint) —
/// 창 크기(DIP)를 넘길 때는 호출부가 화면 배율을 곱해서 PixelSize로 변환해 넘긴다.
/// 계산 함수는 작업 영역 목록만 받는 순수 함수라 Screens 없이도 검증할 수 있다.
/// </summary>
public static class ScreenPlacement
{
    public const int Margin = 16;

    // 최소한 이만큼은 화면 안에 보여야 잡아서 옮길 수 있다고 본다.
    private const int MinVisiblePx = 64;

    public static IReadOnlyList<PixelRect> WorkAreas(Screens screens) =>
        screens.All.Select(s => s.WorkingArea).ToList();

    /// <summary>
    /// 창을 사용자가 다시 잡을 수 있는 위치인지. "조금이라도 겹치면 통과"로 하면 메모 헤더처럼 창 상단에 있는
    /// 드래그 핸들이 화면 위로 잘린 채 복구 불가능해지므로, 상단 모서리가 작업 영역 안에 있고
    /// 가로로 MinVisiblePx 이상 겹치는 경우만 통과시킨다.
    /// </summary>
    public static bool IsReachable(PixelRect rect, IReadOnlyList<PixelRect> workAreas) =>
        workAreas.Any(wa =>
            rect.Y >= wa.Y
            && rect.Y <= wa.Bottom - MinVisiblePx
            && Math.Min(rect.Right, wa.Right) - Math.Max(rect.X, wa.X) >= MinVisiblePx);

    /// <summary>그대로 쓸 수 있으면 원래 위치를, 화면 밖으로 잘렸으면 가장 가까운 작업 영역 안으로 clamp한 위치를 반환한다.</summary>
    public static PixelPoint RestoreOrClamp(PixelRect rect, IReadOnlyList<PixelRect> workAreas)
    {
        if (workAreas.Count == 0 || IsReachable(rect, workAreas))
            return rect.Position;

        var nearest = workAreas.MinBy(wa => DistanceSquared(rect.Center, wa.Center));
        return ClampInto(rect, nearest);
    }

    /// <summary>
    /// DIP 크기의 창을 저장된 위치로 복원할 때 쓰는 편의 오버로드. 저장 위치가 속한 화면의 배율로 물리 픽셀 크기를
    /// 계산한다. 화면 정보를 얻을 수 없으면 검증 없이 저장 위치를 그대로 반환한다.
    /// </summary>
    public static PixelPoint RestoreOrClamp(Screens? screens, PixelPoint saved, double widthDip, double heightDip)
    {
        if (screens is null)
            return saved;

        var scaling = (screens.ScreenFromPoint(saved) ?? screens.Primary)?.Scaling ?? 1.0;
        var rect = new PixelRect(saved.X, saved.Y, (int)Math.Round(widthDip * scaling), (int)Math.Round(heightDip * scaling));
        return RestoreOrClamp(rect, WorkAreas(screens));
    }

    /// <summary>작업 영역의 우하단 모서리에서 Margin만큼 띄운 위치 (본가 우카가카식 기본 위치).</summary>
    public static PixelPoint BottomRight(PixelRect workArea, PixelSize size) =>
        ClampInto(new PixelRect(workArea.Right - size.Width - Margin, workArea.Bottom - size.Height - Margin, size.Width, size.Height), workArea);

    /// <summary>작업 영역의 우상단 모서리에서 Margin만큼 띄운 위치.</summary>
    public static PixelPoint TopRight(PixelRect workArea, PixelSize size) =>
        ClampInto(new PixelRect(workArea.Right - size.Width - Margin, workArea.Y + Margin, size.Width, size.Height), workArea);

    /// <summary>rect가 workArea 안에 완전히 들어가는지 (새 메모를 메인 창 옆에 놓을 수 있는지 판단할 때).</summary>
    public static bool Fits(PixelRect rect, PixelRect workArea) =>
        rect.X >= workArea.X && rect.Y >= workArea.Y && rect.Right <= workArea.Right && rect.Bottom <= workArea.Bottom;

    // 창이 작업 영역보다 크면 좌상단을 맞춘다(오른쪽/아래가 잘리는 쪽이 헤더를 잡기 쉽다).
    private static PixelPoint ClampInto(PixelRect rect, PixelRect workArea)
    {
        var x = Math.Max(workArea.X, Math.Min(rect.X, workArea.Right - rect.Width));
        var y = Math.Max(workArea.Y, Math.Min(rect.Y, workArea.Bottom - rect.Height));
        return new PixelPoint(x, y);
    }

    private static long DistanceSquared(PixelPoint a, PixelPoint b)
    {
        long dx = a.X - b.X;
        long dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
