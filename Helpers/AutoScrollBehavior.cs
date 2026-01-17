using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace EncryptionMinerControl.Helpers;

public static class AutoScrollBehavior
{
    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.RegisterAttached("AutoScroll", typeof(bool), typeof(AutoScrollBehavior), new PropertyMetadata(false, OnAutoScrollChanged));

    public static bool GetAutoScroll(DependencyObject obj)
    {
        return (bool)obj.GetValue(AutoScrollProperty);
    }

    public static void SetAutoScroll(DependencyObject obj, bool value)
    {
        obj.SetValue(AutoScrollProperty, value);
    }

    private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListBox listBox)
        {
            if ((bool)e.NewValue)
            {
                listBox.Loaded += ListBox_Loaded;
                listBox.Unloaded += ListBox_Unloaded;
            }
            else
            {
                listBox.Loaded -= ListBox_Loaded;
                listBox.Unloaded -= ListBox_Unloaded;
            }
        }
    }

    private static void ListBox_Loaded(object sender, RoutedEventArgs e)
    {
        var listBox = (ListBox)sender;
        ((INotifyCollectionChanged)listBox.Items).CollectionChanged += (s, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
            {
                if (listBox.Items.Count > 0)
                {
                    listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
                }
            }
        };
    }
    
    private static void ListBox_Unloaded(object sender, RoutedEventArgs e)
    {
         // Cleanup if needed, though strictly lambda capture might keep ref. 
         // In a simple app lifecycle, this is acceptable.
    }
}
