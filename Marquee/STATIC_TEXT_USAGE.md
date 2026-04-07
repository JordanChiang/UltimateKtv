# Static Text Marquee Usage

The Marquee API now supports displaying static text with countdown timers (no scrolling animation).

## Basic Usage

### Simple Static Text
```csharp
// Display text indefinitely (no timeout)
MarqueeAPI.ShowStaticText("Welcome to Ultimate KTV!", 0);

// Display text for 10 seconds then auto-hide
MarqueeAPI.ShowStaticText("Song will start in 10 seconds", 10);
```

### Static Announcements
```csharp
// Display announcement for 15 seconds
MarqueeAPI.ShowStaticAnnouncement("⚠ System maintenance in 5 minutes", 15);

// Display indefinitely
MarqueeAPI.ShowStaticAnnouncement("Now Playing: Your Favorite Song", 0);
```

### Custom Static Text
```csharp
// Full customization with timeout
MarqueeAPI.ShowCustomStaticText(
    "Custom Message",
    Brushes.Cyan,
    new FontFamily("Arial"),
    28,
    MarqueePosition.Center,
    30, // 30 seconds timeout
    0   // main window
);
```

## Parameters

- **text**: The text to display
- **timeoutSeconds**: Timer duration in seconds
  - `0` = No timeout (display indefinitely)
  - `> 0` = Auto-hide after specified seconds
- **displayDevice**: Target display (0 = main window, 1+ = secondary displays)

## Positioning Options

- `MarqueePosition.Top` - Top center
- `MarqueePosition.TopLeft` - Top left corner
- `MarqueePosition.TopRight` - Top right corner
- `MarqueePosition.Center` - Screen center
- `MarqueePosition.Bottom` - Bottom center
- `MarqueePosition.BottomLeft` - Bottom left corner
- `MarqueePosition.BottomRight` - Bottom right corner

## Examples by Use Case

### Song Information (No Animation)
```csharp
MarqueeAPI.ShowCustomStaticText(
    "♪ Now Playing: Amazing Grace - John Newton ♪",
    Brushes.LightBlue,
    new FontFamily("Microsoft JhengHei"),
    24,
    MarqueePosition.Top,
    0, // Display until manually stopped
    0
);
```

### Countdown Timer
```csharp
MarqueeAPI.ShowStaticText("Next singer in 30 seconds", 30);
```

### System Messages
```csharp
MarqueeAPI.ShowStaticAnnouncement("Microphone volume adjusted", 5);
```

### Stop Static Text
```csharp
// Stop on specific device
MarqueeAPI.Stop(0);

// Stop all displays
MarqueeAPI.StopAll();
```

## Key Features

1. **No Animation**: Text stays in fixed position
2. **Countdown Timer**: Auto-hide after specified duration
3. **Indefinite Display**: Set timeout to 0 for permanent display
4. **Multiple Positioning**: Choose exact screen position
5. **Custom Styling**: Full control over fonts, colors, and sizes
6. **Multi-Display Support**: Target main window or secondary displays