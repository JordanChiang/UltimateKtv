# Marquee System Usage Guide

This document explains how to use the cleaned up marquee system in the Ultimate KTV application.

## Overview

The marquee system consists of three main components:
- `MarqueeAPI`: Simple API for common marquee operations
- `MarqueeManager`: Manages marquee displays across windows
- `MarqueeControl`: The actual marquee user control

## Quick Start

### Basic Text Marquee
```csharp
// Show simple text on main window (device 0)
MarqueeAPI.ShowText("Hello World!", 0);

// Show on secondary display (device 1)
MarqueeAPI.ShowText("Hello World!", 1);
```

### Song Information Display
```csharp
// Show song info with special formatting
MarqueeAPI.ShowSongInfo("Song Name", "Artist Name", 0);
```

### Other Message Types
```csharp
// Show prominent announcement
MarqueeAPI.ShowAnnouncement("Important Message!", 0);

// Show welcome message
MarqueeAPI.ShowWelcome("Welcome to Ultimate KTV!", 0);

// Show alert with warning styling
MarqueeAPI.ShowAlert("Warning Message!", 0);
```

## Advanced Usage

### Custom Marquee
```csharp
MarqueeAPI.ShowCustom(
    text: "Custom Message",
    color: Brushes.Yellow,
    fontFamily: new FontFamily("Microsoft JhengHei"),
    fontSize: 24,
    repeatCount: 3,
    position: MarqueePosition.Top,
    speed: 50,
    displayDevice: 0
);
```

### Control Operations
```csharp
// Stop marquee on specific device
MarqueeAPI.Stop(0);

// Stop all marquees
MarqueeAPI.StopAll();

// Check if marquee is active
bool isActive = MarqueeAPI.IsActive(0);
```

## Automatic Integration with Media Player

The marquee system automatically integrates with the media player:

### Song Info Display (MediaOpened)
When media starts playing, song information is automatically displayed using the song name and artist from the database.

### Marquee Cleanup (MediaEnded)
When media ends, all marquees are automatically stopped to prepare for the next song.

## Display Devices

- **Device 0**: Main window
- **Device 1+**: Secondary displays (video output windows)

## Marquee Positions

- `MarqueePosition.Top`: Top of the screen
- `MarqueePosition.Bottom`: Bottom of the screen
- `MarqueePosition.Center`: Center of the screen

## Testing

Use the marquee test button in the application to test functionality:
- Tests basic text marquee
- Tests song info display with sample data

## Best Practices

1. **Use appropriate message types**: `ShowSongInfo()` for songs, `ShowAnnouncement()` for important messages
2. **Specify correct display device**: 0 for main window, 1+ for secondary displays
3. **Let the system handle cleanup**: Marquees are automatically stopped when media ends
4. **Consider text length and speed**: Longer text may need slower speed for readability

## Example Usage

```csharp
// Show song info when a song starts
MarqueeAPI.ShowSongInfo("愛你", "王心凌", 0);

// Show announcement
MarqueeAPI.ShowAnnouncement("歡迎來到 Ultimate KTV！", 0);

// Stop all marquees manually if needed
MarqueeAPI.StopAll();
```

## Parameters

### MarqueePosition Options
- `MarqueePosition.Top` - Top of the screen
- `MarqueePosition.Bottom` - Bottom of the screen  
- `MarqueePosition.Center` - Center of the screen

### Display Device Values
- `0` - Main window (primary display)
- `1` - First secondary display
- `2+` - Additional secondary displays (if available)

## Error Handling

The marquee system includes built-in error handling and will gracefully fail without crashing the application if issues occur.