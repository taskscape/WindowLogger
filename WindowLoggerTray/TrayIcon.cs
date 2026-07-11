using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace WindowLoggerTray;

internal sealed class TrayIcon : IDisposable
{
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconModify = 0x00000001;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconMessage = 0x00000001;
    private const uint NotifyIconIcon = 0x00000002;
    private const uint NotifyIconTip = 0x00000004;
    private const uint NotifyIconInfo = 0x00000010;
    private const uint InfoIcon = 0x00000001;
    private const int WindowMessageApp = 0x8000;
    private const int CallbackMessage = WindowMessageApp + 1;
    private const int RightButtonUp = 0x0205;
    private const int ApplicationIcon = 32512;
    private static readonly int TaskbarCreatedMessage = unchecked((int)RegisterWindowMessage("TaskbarCreated"));

    private readonly HwndSource _messageSource;
    private readonly ContextMenu _contextMenu;
    private NotifyIconData _iconData;
    private bool _disposed;

    public TrayIcon(string toolTip, ContextMenu contextMenu)
    {
        _contextMenu = contextMenu;

        var parameters = new HwndSourceParameters("WindowLoggerTrayMessageWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3)
        };
        _messageSource = new HwndSource(parameters);
        _messageSource.AddHook(WindowProcedure);

        _iconData = CreateIconData();
        _iconData.WindowHandle = _messageSource.Handle;
        _iconData.Flags = NotifyIconMessage | NotifyIconIcon | NotifyIconTip;
        _iconData.CallbackMessage = CallbackMessage;
        _iconData.IconHandle = LoadIcon(IntPtr.Zero, new IntPtr(ApplicationIcon));
        _iconData.ToolTip = toolTip;

        if (!ShellNotifyIcon(NotifyIconAdd, ref _iconData))
        {
            _messageSource.Dispose();
            throw new InvalidOperationException("Windows could not create the notification-area icon.");
        }
    }

    public void ShowBalloon(string title, string message)
    {
        ThrowIfDisposed();

        NotifyIconData balloonData = _iconData;
        balloonData.Flags = NotifyIconInfo;
        balloonData.InfoTitle = title;
        balloonData.Info = message;
        balloonData.InfoFlags = InfoIcon;
        ShellNotifyIcon(NotifyIconModify, ref balloonData);
    }

    private IntPtr WindowProcedure(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == TaskbarCreatedMessage)
        {
            ShellNotifyIcon(NotifyIconAdd, ref _iconData);
            handled = true;
            return IntPtr.Zero;
        }

        if (message == CallbackMessage && lParam.ToInt32() == RightButtonUp)
        {
            SetForegroundWindow(windowHandle);
            _contextMenu.Placement = PlacementMode.MousePoint;
            _contextMenu.IsOpen = true;
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static NotifyIconData CreateIconData() => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        ToolTip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ShellNotifyIcon(NotifyIconDelete, ref _iconData);
        _messageSource.RemoveHook(WindowProcedure);
        _messageSource.Dispose();
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string messageName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ToolTip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIconHandle;
    }
}
