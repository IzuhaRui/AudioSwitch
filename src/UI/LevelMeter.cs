using System.Windows;

namespace AudioSwitch.UI
{
    /// <summary>
    /// Attached property used by the volume slider template: the width (in device
    /// independent pixels) of the live level meter bar that is integrated into the
    /// slider track.
    /// </summary>
    public static class LevelMeter
    {
        public static readonly DependencyProperty LevelProperty = DependencyProperty.RegisterAttached(
            "Level", typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0));

        public static readonly DependencyProperty FillWidthProperty = DependencyProperty.RegisterAttached(
            "FillWidth", typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0));

        public static double GetLevel(DependencyObject element)
        {
            return (double)element.GetValue(LevelProperty);
        }

        public static void SetLevel(DependencyObject element, double value)
        {
            element.SetValue(LevelProperty, value);
        }

        public static double GetFillWidth(DependencyObject element)
        {
            return (double)element.GetValue(FillWidthProperty);
        }

        public static void SetFillWidth(DependencyObject element, double value)
        {
            element.SetValue(FillWidthProperty, value);
        }
    }
}
