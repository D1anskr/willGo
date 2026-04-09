namespace FloatingDeskAssistant.UI.Windows;

public partial class LatestMessageColumnWindow : System.Windows.Window
{
    public LatestMessageColumnWindow()
    {
        InitializeComponent();
    }

    public void ApplySegment(
        System.Windows.HorizontalAlignment alignment,
        System.Windows.Media.Brush bubbleBrush,
        System.Windows.Media.Brush textBrush,
        System.Windows.Media.Brush windowBackdropBrush,
        double fontSize,
        string fullText,
        int sliceStart,
        int sliceLength)
    {
        SegmentPanel.HorizontalAlignment = alignment;
        SegmentBubble.HorizontalAlignment = alignment;
        SegmentBubble.Background = bubbleBrush;
        SegmentContentPresenter.ApplySlice(fullText, sliceStart, sliceLength, textBrush, fontSize);
        WindowFrame.Background = windowBackdropBrush;
        SegmentScrollViewer.ScrollToTop();
    }
}
