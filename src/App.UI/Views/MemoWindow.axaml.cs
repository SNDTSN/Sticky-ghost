using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using App.UI.Services;
using App.UI.ViewModels;

namespace App.UI.Views;

public partial class MemoWindow : Window
{
    private readonly DispatcherTimer _saveTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };
    private MemoNoteViewModel? _viewModel;

    public MemoWindow()
    {
        InitializeComponent();
    }

    public MemoWindow(MemoNoteViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Width = viewModel.Width;
        Height = viewModel.Height;
        // 모니터가 빠지거나 해상도가 바뀌어 헤더가 화면 밖으로 잘린 메모는 잡을 수 없으므로 가장 가까운 화면 안으로 옮긴다.
        // 보정된 위치는 PositionChanged → 디바운스 저장 경로로 DB에도 반영된다.
        Position = ScreenPlacement.RestoreOrClamp(
            Screens, new PixelPoint((int)viewModel.PositionX, (int)viewModel.PositionY), Width, Height);

        viewModel.CloseRequested += Close;
        viewModel.ConfirmDeleteRequested = () => ConfirmDialog.ShowAsync(this, "이 메모를 삭제하시겠습니까?");

        _saveTimer.Tick += (_, _) => SaveGeometryNow();
        PositionChanged += (_, _) => RestartSaveTimer();
        SizeChanged += (_, _) => RestartSaveTimer();

        Opened += (_, _) => viewModel.AttachWindowHandle(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        Closed += OnWindowClosed;
    }

    // 타이머를 Stop()하지 않으면 디스패처 타이머 목록에 계속 등록된 채로 남아
    // Tick 람다가 캡처한 this(창/뷰모델)가 GC되지 못하고 500ms마다 영원히 저장을 시도한다.
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        _viewModel?.Dispose();
    }

    // SystemDecorations="None"이라 OS가 제공하던 타이틀바 드래그 이동이 없다 — 헤더 영역 클릭 시 직접 이동을 시작한다.
    // 헤더 안의 버튼(📌/색상/✕)이 눌리면 해당 컨트롤이 PointerPressed를 먼저 처리해서 여기까지 버블링되지 않는다.
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    // OS 리사이즈 테두리가 없으므로 우측 하단 그립으로 대체한다.
    private void OnResizeGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginResizeDrag(WindowEdge.SouthEast, e);
    }

    private void RestartSaveTimer()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // 최소화된 창의 Position은 Windows에서 (-32000,-32000)이고, 최대화 상태의 크기도 사용자가 정한 값이 아니다.
    // WindowPlacementTracker.SaveNow와 같은 규칙 — 메모 창만 자체 저장 경로를 갖고 있어 이 가드가 빠져 있었다.
    private void SaveGeometryNow()
    {
        _saveTimer.Stop();
        if (WindowState != WindowState.Normal)
            return;

        _viewModel?.UpdatePosition(Position.X, Position.Y);
        _viewModel?.UpdateSize(Width, Height);
    }

    /// <summary>메인 창 종료 등으로 강제 종료되기 전에, 디바운스 중이던 위치/크기/내용 저장을 즉시 실행한다.
    /// 최소화 상태로 종료하면 위치/크기 저장만 건너뛰고 내용은 그대로 저장된다.</summary>
    public void FlushPendingSave()
    {
        SaveGeometryNow();
        _viewModel?.FlushContentSave();
    }
}
