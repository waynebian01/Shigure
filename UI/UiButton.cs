using System.ComponentModel;

namespace Shigure;

/// <summary>避免应用级深色模式覆盖现有按钮的自绘样式。</summary>
internal class UiButton : Button
{
    internal bool DisplayFocusCue => ShowFocusCues;
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowExternalLinkIcon { get; set; }

    protected override CreateParams CreateParams
    {
        get
        {
            // WinForms 需在读取 CreateParams 前关闭按钮的隐式主题绘制。
            SetStyle(ControlStyles.ApplyThemingImplicitly, false);
            return base.CreateParams;
        }
    }
}
