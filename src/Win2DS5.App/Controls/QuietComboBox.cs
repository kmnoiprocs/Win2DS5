using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Win2DS5.App.Controls;

public sealed class QuietComboBox : ComboBox
{
    protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        if (IsDropDownOpen)
            base.OnPointerWheelChanged(e);
    }
}
