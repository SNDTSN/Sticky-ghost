using System;
using System.Data.Common;
using System.Threading.Tasks;
using App.Core.Diagnostics;
using App.Core.Domain.Entities;
using App.Core.Domain.Repositories;
using App.Core.Infrastructure.FileSystem;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaColor = Avalonia.Media.Color;

namespace App.UI.ViewModels;

public partial class MemoNoteViewModel : ViewModelBase, IDisposable
{
    private readonly MemoNote _memo;
    private readonly IMemoRepository _repository;
    private readonly DispatcherTimer _contentSaveTimer;

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private string _colorHex;

    [ObservableProperty]
    private bool _isPinned;

    /// <summary>마지막 저장이 실패해 아직 파일에 반영되지 않은 변경이 있다. 메모 헤더의 "⚠ 저장 안 됨" 표시와 묶인다 —
    /// 모른 채 앱을 꺼서 메모를 잃지 않게(KNOWN_ISSUES #28 A-5).</summary>
    [ObservableProperty]
    private bool _hasSaveError;

    // 메모 행에 아직 저장하지 못한 변경(내용/색상/📌)이 있는지. 저장이 실패해도 그대로 남아 다음 저장(입력·색·📌·종료)에서 재시도된다.
    // 종료 시 flush가 이 값을 보고 할 일이 없으면 아무것도 쓰지 않는다 — 전에는 종료마다 모든 메모의 UpdatedAt이 바뀌었다(#28 A-2).
    private bool _dirty;

    public double PositionX => _memo.PositionX;
    public double PositionY => _memo.PositionY;
    public double Width => _memo.Width;
    public double Height => _memo.Height;

    public Action? CloseRequested;
    public Func<Task<bool>>? ConfirmDeleteRequested;

    public MemoNoteViewModel(MemoNote memo, IMemoRepository repository)
    {
        _memo = memo;
        _repository = repository;
        _content = memo.Content;
        _colorHex = memo.Color;
        _isPinned = memo.IsPinned;

        _contentSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _contentSaveTimer.Tick += (_, _) => SaveNow();
    }

    public MediaColor ColorValue
    {
        get => MediaColor.Parse(ColorHex);
        set => ColorHex = value.ToString();
    }

    /// <summary>
    /// 창 위치/크기 저장(MemoPlacementStore 경유). 메모리의 MemoNote도 같이 갱신한다 — 내용/색상 저장이 행 전체를 덮어쓰므로
    /// 여기서 DB만 바꾸면 다음 내용 저장이 옛 위치로 되돌린다. DB 쪽은 UPDATE만 하므로 삭제된 메모가 되살아나지 않는다.
    /// </summary>
    public void UpdateGeometry(WindowPlacement placement)
    {
        _memo.PositionX = placement.X;
        _memo.PositionY = placement.Y;
        if (placement is { Width: { } width, Height: { } height })
        {
            _memo.Width = width;
            _memo.Height = height;
        }
        _repository.UpdateGeometry(_memo.Id, _memo.PositionX, _memo.PositionY, _memo.Width, _memo.Height);
    }

    partial void OnContentChanged(string value)
    {
        _dirty = true;
        _contentSaveTimer.Stop();
        _contentSaveTimer.Start();
    }

    /// <summary>
    /// 밀린 변경을 행 전체로 저장한다. 행 전체를 쓰므로 한 번 성공하면 그때까지 실패했던 변경도 모두 반영된다.
    /// 실패는 기록하고 표시만 켠다 — 입력 중에 대화상자를 띄우면 오히려 방해가 되고, 타이머 Tick/바인딩 setter에서 예외가 새면
    /// 아무도 잡지 않는다.
    /// </summary>
    private void SaveNow()
    {
        _contentSaveTimer.Stop();
        if (!_dirty)
            return;

        // UpdatedAt은 내용이 실제로 바뀌었을 때만 갱신한다(색·📌만 바꾼 저장은 "내용 수정"이 아니다).
        if (_memo.Content != Content)
        {
            _memo.Content = Content;
            _memo.UpdatedAt = DateTime.Now;
        }

        try
        {
            _repository.Save(_memo);
            _dirty = false;
            HasSaveError = false;
        }
        catch (DbException ex)
        {
            AppLog.Write("memo", ex);
            HasSaveError = true;
        }
    }

    /// <summary>메인 창 종료 등으로 디바운스를 기다릴 수 없을 때 밀린 저장을 즉시 실행한다. 밀린 게 없으면 아무것도 쓰지 않는다.</summary>
    public void FlushPendingSave() => SaveNow();

    /// <summary>창이 닫힐 때 호출. Stop()으로 디스패처 타이머 목록에서 빠지지 않으면
    /// Tick 람다가 캡처한 this가 계속 살아남아 창/뷰모델이 GC되지 못한다.</summary>
    public void Dispose()
    {
        _contentSaveTimer.Stop();
    }

    partial void OnColorHexChanged(string value)
    {
        _memo.Color = value;
        _dirty = true;
        SaveNow();
        OnPropertyChanged(nameof(ColorValue));
    }

    // 창을 실제로 위로 올리는 것은 MemoWindow.axaml의 Topmost="{Binding IsPinned}" 바인딩이 한다.
    // 네이티브 SetWindowPos로 직접 올리면 Avalonia가 이 창이 topmost인 줄 모르게 되어,
    // 확인 대화상자가 소유자의 topmost를 물려받지 못하고 메모 뒤로 숨는다(KNOWN_ISSUES #25).
    partial void OnIsPinnedChanged(bool value)
    {
        _memo.IsPinned = value;
        _dirty = true;
        SaveNow();
    }

    [RelayCommand]
    private async Task RequestCloseAsync()
    {
        if (string.IsNullOrWhiteSpace(Content))
        {
            _repository.Delete(_memo.Id);
            CloseRequested?.Invoke();
            return;
        }

        var confirmed = ConfirmDeleteRequested is not null && await ConfirmDeleteRequested.Invoke();
        if (confirmed)
        {
            _repository.Delete(_memo.Id);
            CloseRequested?.Invoke();
        }
    }
}
