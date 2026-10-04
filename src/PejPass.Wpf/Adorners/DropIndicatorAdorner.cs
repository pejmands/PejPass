using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PejPass.Wpf.Adorners;

public sealed class DropIndicatorAdorner : Adorner
{
    private readonly Pen _pen;
    private double _y;
    private bool _isVisible;

    public DropIndicatorAdorner(UIElement adornedElement, Brush brush)
        : base(adornedElement)
    {
        IsHitTestVisible = false;

        _pen = new Pen(brush, 2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
    }

    public void ShowAt(double y)
    {
        _y = y;
        _isVisible = true;
        InvalidateVisual();
    }

    public void Hide()
    {
        _isVisible = false;
        _y = 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (!_isVisible)
            return;

        var width = AdornedElement.RenderSize.Width;

        drawingContext.DrawLine(
            _pen,
            new Point(8, _y),
            new Point(width - 8, _y));
    }
}
