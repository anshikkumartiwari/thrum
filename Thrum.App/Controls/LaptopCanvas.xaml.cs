using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Thrum.App.ViewModels;

namespace Thrum.App.Controls;

public partial class LaptopCanvas : UserControl
{
    private bool _isDragging;
    private Point _dragStartPoint;
    private ZoneViewModel? _draggedZone;

    public LaptopCanvas()
    {
        InitializeComponent();
    }

    private void OnMarkerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ZoneViewModel zone)
        {
            if (DataContext is MainViewModel mainVm)
            {
                mainVm.SelectedZone = zone;
            }

            _isDragging = true;
            _draggedZone = zone;
            _dragStartPoint = e.GetPosition(ChassisCanvas);
            element.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMarkerMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && _draggedZone != null && sender is FrameworkElement element)
        {
            Point currentPoint = e.GetPosition(ChassisCanvas);
            double canvasWidth = ChassisCanvas.ActualWidth;
            double canvasHeight = ChassisCanvas.ActualHeight;

            if (canvasWidth > 50 && canvasHeight > 50)
            {
                // Allow dragging beyond laptop outline onto surrounding desk area
                double newRelX = Math.Clamp(currentPoint.X / canvasWidth, 0.02, 0.98);
                double newRelY = Math.Clamp(currentPoint.Y / canvasHeight, 0.04, 0.96);

                _draggedZone.X = newRelX;
                _draggedZone.Y = newRelY;
            }
        }
    }

    private void OnMarkerMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging && sender is FrameworkElement element)
        {
            _isDragging = false;
            _draggedZone = null;
            element.ReleaseMouseCapture();
            e.Handled = true;
        }
    }
}
