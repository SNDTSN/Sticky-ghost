using App.Core.Domain.Entities;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace App.UI.Views;

/// <summary>캐릭터팩 로더 검증용 임시 창. baseImage만 그린다 — 표정 렌더링은 별도 작업.</summary>
public partial class CharacterPreviewWindow : Window
{
    public CharacterPreviewWindow()
    {
        InitializeComponent();
    }

    public CharacterPreviewWindow(CharacterPack pack) : this()
    {
        Title = $"캐릭터 미리보기 - {pack.Name}";
        BaseImage.Source = new Bitmap(pack.Appearance.BaseImage);
    }
}
