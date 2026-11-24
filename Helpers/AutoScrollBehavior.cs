using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace EncryptionMinerControl.Helpers;

public static class AutoScrollBehavior
{
    private sealed class CollectionChangedSubscription
    {
        public INotifyCollectionChanged Source { get; }
        public NotifyCollectionChangedEventHandler Handler { get; }

        public CollectionChangedSubscription(INotifyCollectionChanged source, NotifyCollectionChangedEventHandler handler)
        {
            Source = source;
            Handler = handler;
        }

        public void Detach()
        {
            Source.CollectionChanged -= Handler;
        }
    }

    private static readonly ConditionalWeakTable<System.Windows.Controls.TextBox, CollectionChangedSubscription> _textBoxSubscriptions = new();

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

    // Bindable collection source for read-only log TextBox / 읽기 전용 로그 TextBox용 컬렉션 바인딩
    public static readonly DependencyProperty LogItemsSourceProperty =
        DependencyProperty.RegisterAttached("LogItemsSource", typeof(IEnumerable), typeof(AutoScrollBehavior), new PropertyMetadata(null, OnLogItemsSourceChanged));

    public static IEnumerable? GetLogItemsSource(DependencyObject obj)
    {
        return (IEnumerable?)obj.GetValue(LogItemsSourceProperty);
    }

    public static void SetLogItemsSource(DependencyObject obj, IEnumerable? value)
    {
        obj.SetValue(LogItemsSourceProperty, value);
    }

    private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is System.Windows.Controls.ListBox listBox)
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
        var listBox = (System.Windows.Controls.ListBox)sender;
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

    private static void OnLogItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBox textBox) return;

        textBox.Loaded -= TextBox_Loaded;
        textBox.Unloaded -= TextBox_Unloaded;

        if (e.NewValue != null)
        {
            textBox.Loaded += TextBox_Loaded;
            textBox.Unloaded += TextBox_Unloaded;

            if (textBox.IsLoaded)
            {
                AttachLogSource(textBox);
            }
        }
        else
        {
            DetachLogSource(textBox);
            textBox.Clear();
        }
    }

    private static void TextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            AttachLogSource(textBox);
        }
    }

    private static void TextBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            DetachLogSource(textBox);
        }
    }

    // Sync initial text and subscribe to incremental updates / 초기 로그 동기화 및 증분 구독
    private static void AttachLogSource(System.Windows.Controls.TextBox textBox)
    {
        DetachLogSource(textBox);

        IEnumerable? items = GetLogItemsSource(textBox);
        if (items == null) return;

        var sb = new StringBuilder();
        foreach (var item in items)
        {
            if (item != null) sb.AppendLine(item.ToString());
        }

        textBox.Text = sb.ToString();
        textBox.ScrollToEnd();

        if (items is INotifyCollectionChanged notifyCollection)
        {
            NotifyCollectionChangedEventHandler handler = (_, args) => OnLogCollectionChanged(textBox, args);
            notifyCollection.CollectionChanged += handler;
            _textBoxSubscriptions.AddOrUpdate(textBox, new CollectionChangedSubscription(notifyCollection, handler));
        }
    }

    private static void DetachLogSource(System.Windows.Controls.TextBox textBox)
    {
        if (_textBoxSubscriptions.TryGetValue(textBox, out var subscription))
        {
            subscription.Detach();
            _textBoxSubscriptions.Remove(textBox);
        }
    }

    private static void OnLogCollectionChanged(System.Windows.Controls.TextBox textBox, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems != null)
        {
            // Check scroll position before appending so manual scroll/selection is preserved / 수동 스크롤·선택 보존을 위해 추가 전 하단 여부 확인
            bool isAtBottom = textBox.VerticalOffset + textBox.ViewportHeight >= textBox.ExtentHeight - 20.0;

            var sb = new StringBuilder();
            foreach (var item in args.NewItems)
            {
                if (item != null) sb.AppendLine(item.ToString());
            }

            if (sb.Length > 0)
            {
                textBox.AppendText(sb.ToString());
            }

            // Trim oldest lines to keep memory bounded when not selecting / 미선택 상태에서 메모리 제한을 위해 오래된 줄 정리
            if (textBox.SelectionLength == 0 && textBox.LineCount > 1200)
            {
                int cutIdx = textBox.GetCharacterIndexFromLineIndex(200);
                if (cutIdx > 0)
                {
                    textBox.Text = textBox.Text.Substring(cutIdx);
                }
            }

            if (isAtBottom && textBox.SelectionLength == 0)
            {
                textBox.ScrollToEnd();
            }
        }
        else if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            textBox.Clear();
        }
    }
}
