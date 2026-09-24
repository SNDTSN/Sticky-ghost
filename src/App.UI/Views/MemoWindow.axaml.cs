using Avalonia.Controls;
using Avalonia.Input;
using App.UI.Services;
using App.UI.ViewModels;

namespace App.UI.Views;

public partial class MemoWindow : Window
{
    private MemoNoteViewModel? _viewModel;
    private WindowPlacementTracker? _placementTracker;

    public MemoWindow()
    {
        InitializeComponent();
    }

    public MemoWindow(MemoNoteViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        // 위치/크기 저장은 메인/캐릭터 창과 같은 트래커가 한다(KNOWN_ISSUES #20-1). 헤더가 화면 밖으로 잘린 메모는
        // 잡을 수 없으므로 기본 위치로 모으지 않고 가장 가까운 화면 안으로 옮긴다.
        _placementTracker = new WindowPlacementTracker(this, new MemoPlacementStore(viewModel), trackSize: true);
        _placementTracker.RestoreClampedToNearest();

        viewModel.CloseRequested += Close;
        viewModel.ConfirmDeleteRequested = () => ConfirmDialog.ShowAsync(this, "이 메모를 삭제하시겠습니까?");

        // 위치 저장 타이머는 트래커가 Closed에서 스스로 멈춘다. 뷰모델의 내용 저장 타이머만 여기서 멈춘다.
        Closed += (_, _) => _viewModel?.Dispose();
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

    /// <summary>메인 창 종료 등으로 강제 종료되기 전에, 디바운스 중이던 위치/크기/내용 저장을 즉시 실행한다.
    /// 최소화 상태로 종료하면 위치/크기 저장만 건너뛰고(트래커의 가드) 내용은 그대로 저장된다.</summary>
    public void FlushPendingSave()
    {
        _placementTracker?.Flush();
        _viewModel?.FlushContentSave();
    }
}
