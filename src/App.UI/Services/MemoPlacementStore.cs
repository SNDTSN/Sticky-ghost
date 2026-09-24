using App.Core.Infrastructure.FileSystem;
using App.UI.ViewModels;

namespace App.UI.Services;

/// <summary>
/// 메모 창의 위치/크기를 MemoNote 행에 저장한다. 뷰모델을 거치는 이유: 뷰모델이 들고 있는 MemoNote는 내용/색상 저장 때
/// 행 전체를 덮어쓰므로(IMemoRepository.Save), 메모리 값도 같이 갱신하지 않으면 다음 내용 저장이 옛 위치로 되돌린다.
/// </summary>
public sealed class MemoPlacementStore : IWindowPlacementStore
{
    private readonly MemoNoteViewModel _viewModel;

    public MemoPlacementStore(MemoNoteViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public WindowPlacement? Load() =>
        new((int)_viewModel.PositionX, (int)_viewModel.PositionY, _viewModel.Width, _viewModel.Height);

    public void Save(WindowPlacement placement) => _viewModel.UpdateGeometry(placement);
}
