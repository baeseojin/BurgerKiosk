
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BurgerKiosk.Behaviors
{
    public static class ScrollViewerDragBehavior
    {
        public static readonly DependencyProperty EnableMouseDragProperty =
            DependencyProperty.RegisterAttached(
                "EnableMouseDrag",
                typeof(bool),
                typeof(ScrollViewerDragBehavior),
                new PropertyMetadata(false, OnEnableChanged));

        public static void SetEnableMouseDrag(
            DependencyObject element, bool value)
        {
            element.SetValue(EnableMouseDragProperty, value);
        }

        public static bool GetEnableMouseDrag(
            DependencyObject element)
        {
            return (bool)element.GetValue(EnableMouseDragProperty);
        }

        private static void OnEnableChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer viewer)
                return;

            if ((bool)e.NewValue)
            {
                viewer.PreviewMouseLeftButtonDown += MouseDown;
                viewer.PreviewMouseMove += MouseMove;
                viewer.PreviewMouseLeftButtonUp += MouseUp;
                viewer.LostMouseCapture += LostCapture;
            }
            else
            {
                viewer.PreviewMouseLeftButtonDown -= MouseDown;
                viewer.PreviewMouseMove -= MouseMove;
                viewer.PreviewMouseLeftButtonUp -= MouseUp;
                viewer.LostMouseCapture -= LostCapture;
            }
        }

        private static Point startPoint;
        private static double startOffset;
        private static bool isDragging;
        private static ScrollViewer? activeViewer;

        private static void MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ScrollViewer viewer)
                return;

            activeViewer = viewer;
            startPoint = e.GetPosition(viewer);
            startOffset = viewer.VerticalOffset;
            isDragging = false;
        }

        private static void MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not ScrollViewer viewer ||
                activeViewer != viewer ||
                e.LeftButton != MouseButtonState.Pressed)
                return;

            Point current = e.GetPosition(viewer);
            double distance = current.Y - startPoint.Y;

            if (!isDragging && System.Math.Abs(distance) > 8)
            {
                isDragging = true;
                viewer.CaptureMouse();
            }

            if (isDragging)
            {
                viewer.ScrollToVerticalOffset(
                    startOffset - distance);

                e.Handled = true;
            }
        }

        private static void MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ScrollViewer viewer &&
                activeViewer == viewer)
            {
                if (isDragging)
                {
                    e.Handled = true;
                    viewer.ReleaseMouseCapture();
                }

                isDragging = false;
                activeViewer = null;
            }
        }

        private static void LostCapture(object sender, MouseEventArgs e)
        {
            isDragging = false;
            activeViewer = null;
        }
    }
}
