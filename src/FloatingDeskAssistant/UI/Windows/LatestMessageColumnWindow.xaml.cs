namespace FloatingDeskAssistant.UI.Windows;

public partial class LatestMessageColumnWindow : System.Windows.Window
{
    public LatestMessageColumnWindow()
    {
        InitializeComponent();
        FloatingDeskAssistant.Application.WindowCaptureProtection.Attach(this, 14, 8);
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
        var contentChanged = SegmentContentPresenter.Text != fullText
            || SegmentContentPresenter.SliceStart != sliceStart
            || SegmentContentPresenter.SliceLength != sliceLength;
        SegmentPanel.HorizontalAlignment = alignment;
        SegmentBubble.HorizontalAlignment = alignment;
        SegmentBubble.Background = bubbleBrush;
        SegmentContentPresenter.ApplySlice(fullText, sliceStart, sliceLength, textBrush, fontSize);
        WindowFrame.Background = windowBackdropBrush;
        if (contentChanged) SegmentScrollViewer.ScrollToTop();
    }

    public bool ScrollConversation(double pixels)
    {
        if (!IsVisible || SegmentScrollViewer.ScrollableHeight <= 1) return false;
        SegmentScrollViewer.ScrollToVerticalOffset(Math.Clamp(
            SegmentScrollViewer.VerticalOffset + pixels, 0, SegmentScrollViewer.ScrollableHeight));
        return true;
    }
}
